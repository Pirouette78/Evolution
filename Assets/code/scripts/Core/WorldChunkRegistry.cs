using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;

/// <summary>
/// Manages the large world (WorldWidth × WorldHeight terrain tiles) divided into LOD chunks.
///
/// LOD system (radius-based, inspired by LeoWorld2's DynamicTileLoader):
///   LOD 0 (active chunk)    — full GPU simulation, fine BFS flow field
///   LOD 1 (Lod1Radius ring) — low-res terrain preview, coarse flow field pre-computed
///   LOD 2 (Lod2Radius ring) — very low-res terrain preview only
///   Beyond Lod2Radius       — unloaded (only agent counts kept in memory)
///
/// Pre-loading: when the camera approaches a chunk boundary (> PreloadThreshold),
/// the adjacent chunk's terrain + walkability data is computed on a background thread
/// so the LOD 0 transition is nearly instantaneous.
/// </summary>
public class WorldChunkRegistry : MonoBehaviour
{
    public static WorldChunkRegistry Instance { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoCreate()
    {
        if (Instance != null) return;
        var go = new GameObject("WorldChunkRegistry");
        go.AddComponent<WorldChunkRegistry>();
        DontDestroyOnLoad(go);
    }

    // ── Inspector ────────────────────────────────────────────────────
    [Header("World Dimensions (terrain tiles)")]
    public int WorldWidth  = 4096;
    public int WorldHeight = 4096;

    [Header("LOD 1 — Adjacent Chunks (terrain preview + coarse flow)")]
    [Tooltip("Chebyshev radius of chunks shown at LOD 1 around the active chunk.")]
    public int Lod1Radius = 1;
    [Tooltip("Texture resolution (pixels) for LOD 1 terrain preview quads.")]
    public int Lod1TexResolution = 128;

    [Header("LOD 2 — Outer Ring (terrain preview only)")]
    [Tooltip("Chebyshev radius of chunks shown at LOD 2 around the active chunk.")]
    public int Lod2Radius = 2;
    [Tooltip("Texture resolution (pixels) for LOD 2 terrain preview quads.")]
    public int Lod2TexResolution = 32;

    [Header("Pre-loading")]
    [Range(0.5f, 0.99f)]
    [Tooltip("Fraction of chunk width/height at which adjacent chunks start pre-computing in the background.")]
    public float PreloadThreshold = 0.75f;

    [Header("Coarse Flow Field")]
    [Tooltip("Resolution of the world-wide coarse walkability grid (CoarseRes × CoarseRes cells).")]
    public int CoarseRes = 128;

    [Header("Simulation Defaults")]
    [Tooltip("Agents spawned per active species slot when entering an unvisited chunk.")]
    public int DefaultAgentsPerSlot = 200;

    // ── Public state ─────────────────────────────────────────────────
    public Vector2Int ActiveChunk     { get; private set; }
    public bool       IsTransitioning { get; private set; }

    /// <summary>World-scale coarse walkability grid (CoarseRes × CoarseRes). Used by WaypointManager.</summary>
    public bool[,] CoarseWalkability { get; private set; }

    // ── Per-chunk data ───────────────────────────────────────────────
    private enum ChunkLOD { Unloaded, LOD2, LOD1, LOD0 }

    private class ChunkData
    {
        // Persistent across all LOD levels
        public readonly int[] agentCounts = new int[SlimeMapRenderer.MaxSlots];
        public bool   visited;
        public ChunkLOD lod = ChunkLOD.Unloaded;

        // Background pre-computation results
        public float[,]  preloadHeightMap;
        public bool[,]   preloadWalkability;
        public bool      preloadReady;
        public bool      preloadInProgress;

        // Visual preview textures (built on main thread once preload is done)
        public Texture2D lod1Texture;
        public Texture2D lod2Texture;
    }

    private readonly Dictionary<Vector2Int, ChunkData> chunkMap = new();
    private int  chunkCountX, chunkCountY;
    private bool initialized;
    private LodChunkView lodView;

    // ── Unity lifecycle ──────────────────────────────────────────────
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start() => StartCoroutine(InitWhenReady());

    private IEnumerator InitWhenReady()
    {
        while (TerrainMapRenderer.Instance == null ||
               SlimeMapRenderer.Instance    == null ||
               TerrainMapRenderer.Instance.WalkabilityGrid == null)
            yield return null;

        Init();
    }

    private void Init()
    {
        var terrain = TerrainMapRenderer.Instance;
        int chunkW  = terrain.Width;
        int chunkH  = terrain.Height;

        chunkCountX = Mathf.Max(1, WorldWidth  / chunkW);
        chunkCountY = Mathf.Max(1, WorldHeight / chunkH);

        // Chunk (0,0) is already active — mark it accordingly
        ActiveChunk = Vector2Int.zero;
        var data00  = GetOrCreate(Vector2Int.zero);
        data00.visited = true;
        data00.lod     = ChunkLOD.LOD0;
        for (int s = 0; s < SlimeMapRenderer.MaxSlots; s++)
            data00.agentCounts[s] = (int)SlimeMapRenderer.Instance.AliveSpeciesCounts[s];

        // Build world-scale coarse walkability (once, never changes)
        BuildCoarseWalkability();
        WaypointManager.Instance?.RebuildCoarseFlowFields();

        // Create the LOD visual manager
        lodView = gameObject.AddComponent<LodChunkView>();
        lodView.Init(Lod1Radius, Lod2Radius);

        // Kick off background preloads for all chunks in the LOD radius
        UpdateLODsAround(ActiveChunk);

        initialized = true;
        Debug.Log($"[WORLD] Initialized {chunkCountX}×{chunkCountY} chunks " +
                  $"({WorldWidth}×{WorldHeight} tiles, chunk size {chunkW}×{chunkH}).");
    }

    // ── Update ───────────────────────────────────────────────────────
    private void Update()
    {
        if (!initialized || IsTransitioning) return;
        if (SlimeMapRenderer.Instance == null || Camera.main == null) return;

        // LOD 0 transition: camera moved into a different chunk
        Vector2Int desired = WorldPosToChunk(Camera.main.transform.position);
        if (desired != ActiveChunk && IsValidChunk(desired))
        {
            StartCoroutine(TransitionToChunk(desired));
            return;
        }

        // Pre-load adjacent chunks as camera approaches boundaries
        CheckPreloads();
    }

    // ── Pre-loading ──────────────────────────────────────────────────

    private void CheckPreloads()
    {
        var smr = SlimeMapRenderer.Instance;
        if (smr?.DisplayTarget == null) return;

        Bounds b = smr.DisplayTarget.bounds;
        if (b.size.x <= 0f || b.size.y <= 0f) return;

        float nx = (Camera.main.transform.position.x - b.min.x) / b.size.x;
        float ny = (Camera.main.transform.position.y - b.min.y) / b.size.y;
        float t  = PreloadThreshold;
        float ti = 1f - t;

        // Cardinal directions
        if (nx > t)  TryPreload(ActiveChunk + new Vector2Int( 1,  0));
        if (nx < ti) TryPreload(ActiveChunk + new Vector2Int(-1,  0));
        if (ny > t)  TryPreload(ActiveChunk + new Vector2Int( 0,  1));
        if (ny < ti) TryPreload(ActiveChunk + new Vector2Int( 0, -1));

        // Diagonal corners
        if (nx > t  && ny > t)  TryPreload(ActiveChunk + new Vector2Int( 1,  1));
        if (nx < ti && ny > t)  TryPreload(ActiveChunk + new Vector2Int(-1,  1));
        if (nx > t  && ny < ti) TryPreload(ActiveChunk + new Vector2Int( 1, -1));
        if (nx < ti && ny < ti) TryPreload(ActiveChunk + new Vector2Int(-1, -1));
    }

    private void TryPreload(Vector2Int coord)
    {
        if (!IsValidChunk(coord)) return;
        var data = GetOrCreate(coord);
        if (data.preloadReady || data.preloadInProgress || data.lod == ChunkLOD.LOD0) return;
        data.preloadInProgress = true;
        StartCoroutine(PreloadAsync(coord, data));
    }

    private IEnumerator PreloadAsync(Vector2Int coord, ChunkData data)
    {
        var terrain = TerrainMapRenderer.Instance;

        // Snapshot all settings on the main thread before launching the background task
        int   w  = terrain.Width;
        int   h  = terrain.Height;
        float wt = terrain.WaterThreshold;
        var   ns = new NoiseSettings                     // detached copy — thread-safe
        {
            Scale       = terrain.Noise.Scale,
            Octaves     = terrain.Noise.Octaves,
            Persistance = terrain.Noise.Persistance,
            Lacunarity  = terrain.Noise.Lacunarity,
            Seed        = terrain.Noise.Seed,
            Offset      = terrain.Noise.Offset
        };
        ns.Validate();

        float[,] heightMap   = null;
        bool[,]  walkability = null;

        var task = Task.Run(() =>
            (heightMap, walkability) = TerrainMapRenderer.GenerateChunkDataRaw(coord.x, coord.y, w, h, ns, wt));

        while (!task.IsCompleted) yield return null;

        if (task.IsFaulted)
        {
            Debug.LogError($"[WORLD] Preload chunk {coord} failed: {task.Exception?.InnerException?.Message}");
            data.preloadInProgress = false;
            yield break;
        }

        data.preloadHeightMap   = heightMap;
        data.preloadWalkability = walkability;
        data.preloadReady       = true;
        data.preloadInProgress  = false;

        // Build LOD textures on the main thread now that we have the data
        BuildLodTextures(coord, data);
        Debug.Log($"[WORLD] Chunk {coord} pre-loaded.");
    }

    private void BuildLodTextures(Vector2Int coord, ChunkData data)
    {
        if (!data.preloadReady) return;
        var terrain = TerrainMapRenderer.Instance;
        int w = terrain.Width, h = terrain.Height;

        if (data.lod1Texture != null) Destroy(data.lod1Texture);
        if (data.lod2Texture != null) Destroy(data.lod2Texture);

        data.lod1Texture = terrain.BuildLowResTexture(data.preloadHeightMap, w, h, Lod1TexResolution);
        data.lod2Texture = terrain.BuildLowResTexture(data.preloadHeightMap, w, h, Lod2TexResolution);

        // Tell the LOD view about the new textures (offset = coord - current active chunk)
        lodView?.SetChunkTexture(coord - ActiveChunk, data.lod1Texture, data.lod2Texture);
    }

    // ── LOD zone management ──────────────────────────────────────────

    /// <summary>
    /// Triggers background preloads for all chunks in the LOD 1/2 radius around center,
    /// unloads textures for chunks that fell out of range, and refreshes the LOD view.
    /// Mirrors LeoWorld2's UpdateTiles() logic.
    /// </summary>
    private void UpdateLODsAround(Vector2Int center)
    {
        int maxR = Lod2Radius;

        for (int dy = -maxR; dy <= maxR; dy++)
        for (int dx = -maxR; dx <= maxR; dx++)
        {
            if (dx == 0 && dy == 0) continue;
            var coord = center + new Vector2Int(dx, dy);
            if (!IsValidChunk(coord)) continue;

            var data = GetOrCreate(coord);
            int dist = ChebyshevDist(dx, dy);
            data.lod = dist <= Lod1Radius ? ChunkLOD.LOD1 : ChunkLOD.LOD2;

            if (!data.preloadReady && !data.preloadInProgress)
            {
                data.preloadInProgress = true;
                StartCoroutine(PreloadAsync(coord, data));
            }
            else if (data.preloadReady && (data.lod1Texture == null || data.lod2Texture == null))
            {
                BuildLodTextures(coord, data);
            }
        }

        // Unload textures for chunks that fell outside the LOD 2 radius (free memory)
        var toUnload = new List<Vector2Int>();
        foreach (var kv in chunkMap)
        {
            if (kv.Key == center) continue;
            var diff = kv.Key - center;
            if (ChebyshevDist(diff.x, diff.y) > maxR)
                toUnload.Add(kv.Key);
        }
        foreach (var coord in toUnload)
            FreeLodTextures(chunkMap[coord]);

        // Refresh all visual quads with current textures
        lodView?.RefreshAll(center, this);
    }

    private void FreeLodTextures(ChunkData data)
    {
        if (data.lod1Texture != null) { Destroy(data.lod1Texture); data.lod1Texture = null; }
        if (data.lod2Texture != null) { Destroy(data.lod2Texture); data.lod2Texture = null; }
        if (data.lod != ChunkLOD.LOD0) data.lod = ChunkLOD.Unloaded;
    }

    // ── Chunk transition ─────────────────────────────────────────────

    private IEnumerator TransitionToChunk(Vector2Int newChunk)
    {
        IsTransitioning = true;
        Debug.Log($"[WORLD] Transitioning → chunk {newChunk}");

        lodView?.HideAll();     // hide LOD quads during the transition frame
        yield return null;      // (future: fade-out here)

        // 1. Save agent counts for the current LOD 0 chunk; shut down simulation
        SaveAndDeactivate();

        // 2. Reposition camera to the equivalent point in the new chunk
        SnapCamera(ActiveChunk, newChunk);

        // 3. Activate the new chunk as LOD 0
        ActiveChunk = newChunk;
        yield return StartCoroutine(ActivateChunkCoroutine(newChunk));

        // 4. Refresh surrounding LOD 1/2 quads (kicks off any new preloads too)
        UpdateLODsAround(newChunk);

        yield return null;      // (future: fade-in here)
        IsTransitioning = false;
        Debug.Log($"[WORLD] Chunk {newChunk} active.");
    }

    private IEnumerator ActivateChunkCoroutine(Vector2Int coord)
    {
        var terrain = TerrainMapRenderer.Instance;
        var smr     = SlimeMapRenderer.Instance;
        var wpm     = WaypointManager.Instance;
        var data    = GetOrCreate(coord);

        // 1. If preload is still running on the background thread, wait for it.
        //    This keeps the main thread alive (no freeze) while the Task.Run completes.
        if (data.preloadInProgress)
        {
            Debug.Log($"[WORLD] Chunk {coord} preload in progress — waiting (no freeze).");
            float elapsed = 0f;
            while (data.preloadInProgress && elapsed < 10f)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
            if (!data.preloadReady)
                Debug.LogWarning($"[WORLD] Chunk {coord} preload timed out after {elapsed:F1}s.");
        }

        // 2. If preload never started, kick it off now and wait.
        if (!data.preloadReady && !data.preloadInProgress)
        {
            Debug.Log($"[WORLD] Chunk {coord} was not pre-loaded — starting now.");
            data.preloadInProgress = true;
            yield return StartCoroutine(PreloadAsync(coord, data));
        }

        // 3. Apply terrain — fast path (textures already built by PreloadAsync)
        if (data.preloadReady)
        {
            terrain.ApplyPrecomputedChunkData(
                coord.x, coord.y, data.preloadHeightMap, data.preloadWalkability);
        }
        else
        {
            // Absolute last resort: synchronous generation.
            // Should only happen if the preload crashed or timed out.
            Debug.LogWarning($"[WORLD] Chunk {coord} — using synchronous fallback (may cause brief stall).");
            terrain.GenerateMapForChunk(coord.x, coord.y);
        }

        smr.RefreshTerrainCache();
        yield return null;

        // 2. Recompute flow fields asynchronously — does not block the main thread
        wpm?.OnChunkActivated();
        yield return null;

        // 3. Spawn agents
        if (data.visited)
        {
            for (int s = 0; s < SlimeMapRenderer.MaxSlots; s++)
                if (data.agentCounts[s] > 0)
                    smr.SpawnAgentsForSlot(s, data.agentCounts[s]);
        }
        else
        {
            data.visited = true;
            for (int s = 0; s < smr.numActiveSlots; s++)
                smr.SpawnAgentsForSlot(s, DefaultAgentsPerSlot);
        }

        data.lod = ChunkLOD.LOD0;
        Debug.Log($"[WORLD] Chunk {coord} activated.");
    }

    // ── Public API ───────────────────────────────────────────────────

    /// <summary>Saves agent counts for the active chunk and shuts down the GPU simulation.</summary>
    public void SaveAndDeactivate()
    {
        var smr = SlimeMapRenderer.Instance;
        if (smr == null) return;

        var data = GetOrCreate(ActiveChunk);
        for (int s = 0; s < SlimeMapRenderer.MaxSlots; s++)
            data.agentCounts[s] = (int)smr.AliveSpeciesCounts[s];

        smr.KillAllAgents();
        smr.ClearTrails();
        Debug.Log($"[WORLD] Chunk {ActiveChunk} deactivated, counts saved.");
    }

    /// <summary>World tile corner (bottom-left) of chunk (cx, cy).</summary>
    public Vector2 ChunkOriginTile(int cx, int cy)
    {
        int w = TerrainMapRenderer.Instance != null ? TerrainMapRenderer.Instance.Width  : 512;
        int h = TerrainMapRenderer.Instance != null ? TerrainMapRenderer.Instance.Height : 512;
        return new Vector2(cx * w, cy * h);
    }

    /// <summary>Returns the chunk index that contains world tile position (wx, wy).</summary>
    public Vector2Int WorldTileToChunk(float wx, float wy)
    {
        int w = TerrainMapRenderer.Instance != null ? TerrainMapRenderer.Instance.Width  : 512;
        int h = TerrainMapRenderer.Instance != null ? TerrainMapRenderer.Instance.Height : 512;
        return new Vector2Int(
            Mathf.Clamp(Mathf.FloorToInt(wx / w), 0, chunkCountX - 1),
            Mathf.Clamp(Mathf.FloorToInt(wy / h), 0, chunkCountY - 1));
    }

    public bool IsValidChunk(Vector2Int c)
        => initialized && c.x >= 0 && c.x < chunkCountX && c.y >= 0 && c.y < chunkCountY;

    public bool IsValidChunk(int cx, int cy) => IsValidChunk(new Vector2Int(cx, cy));

    /// <summary>Force-transitions to chunk (cx, cy). No-op if already transitioning.</summary>
    public void ActivateChunk(int cx, int cy)
    {
        if (!IsValidChunk(cx, cy) || IsTransitioning) return;
        StartCoroutine(TransitionToChunk(new Vector2Int(cx, cy)));
    }

    /// <summary>Returns the LOD textures for a chunk. Called by LodChunkView.RefreshAll().</summary>
    public (Texture2D lod1, Texture2D lod2) GetLodTextures(Vector2Int coord)
    {
        if (chunkMap.TryGetValue(coord, out var d)) return (d.lod1Texture, d.lod2Texture);
        return (null, null);
    }

    // ── Private helpers ──────────────────────────────────────────────

    private ChunkData GetOrCreate(Vector2Int coord)
    {
        if (!chunkMap.TryGetValue(coord, out var data))
        {
            data = new ChunkData();
            chunkMap[coord] = data;
        }
        return data;
    }

    /// <summary>Converts a Unity world-space camera position to a chunk index.</summary>
    private Vector2Int WorldPosToChunk(Vector2 worldPos)
    {
        var smr     = SlimeMapRenderer.Instance;
        var terrain = TerrainMapRenderer.Instance;
        if (smr?.DisplayTarget == null || terrain == null) return ActiveChunk;

        Bounds b = smr.DisplayTarget.bounds;
        if (b.size.x <= 0f || b.size.y <= 0f) return ActiveChunk;

        // Normalised position within the current chunk quad → world tile → chunk index
        float   nx     = (worldPos.x - b.min.x) / b.size.x;
        float   ny     = (worldPos.y - b.min.y) / b.size.y;
        Vector2 origin = ChunkOriginTile(ActiveChunk.x, ActiveChunk.y);
        return WorldTileToChunk(origin.x + nx * terrain.Width, origin.y + ny * terrain.Height);
    }

    /// <summary>
    /// Repositions the camera so that its normalised position in the new chunk
    /// mirrors its position in the old chunk. Prevents infinite transition cascades.
    /// </summary>
    private void SnapCamera(Vector2Int oldChunk, Vector2Int newChunk)
    {
        var smr     = SlimeMapRenderer.Instance;
        var terrain = TerrainMapRenderer.Instance;
        if (Camera.main == null || smr?.DisplayTarget == null || terrain == null) return;

        Bounds  b   = smr.DisplayTarget.bounds;
        var     cam = Camera.main;

        // Camera's normalised position in the old chunk quad
        float nx = (cam.transform.position.x - b.min.x) / Mathf.Max(0.0001f, b.size.x);
        float ny = (cam.transform.position.y - b.min.y) / Mathf.Max(0.0001f, b.size.y);

        // Convert to world tile coordinates in the old chunk
        Vector2 oldOrigin = ChunkOriginTile(oldChunk.x, oldChunk.y);
        float   wx        = oldOrigin.x + nx * terrain.Width;
        float   wy        = oldOrigin.y + ny * terrain.Height;

        // Re-express as normalised position within the new chunk
        Vector2 newOrigin = ChunkOriginTile(newChunk.x, newChunk.y);
        float   nxNew     = (wx - newOrigin.x) / terrain.Width;
        float   nyNew     = (wy - newOrigin.y) / terrain.Height;

        // Apply — clamp to [0,1] so the camera starts inside the new chunk
        Vector3 pos = cam.transform.position;
        pos.x = b.min.x + Mathf.Clamp01(nxNew) * b.size.x;
        pos.y = b.min.y + Mathf.Clamp01(nyNew) * b.size.y;
        cam.transform.position = pos;
    }

    private void BuildCoarseWalkability()
    {
        var terrain = TerrainMapRenderer.Instance;
        if (terrain == null) return;

        var s = new NoiseSettings
        {
            Scale       = terrain.Noise.Scale * ((float)WorldWidth / CoarseRes),
            Octaves     = terrain.Noise.Octaves,
            Persistance = terrain.Noise.Persistance,
            Lacunarity  = terrain.Noise.Lacunarity,
            Seed        = terrain.Noise.Seed,
            Offset      = terrain.Noise.Offset
        };
        float[,] noise = NoiseMapGenerator.Generate(CoarseRes, CoarseRes, s);
        float    wt    = terrain.WaterThreshold;

        CoarseWalkability = new bool[CoarseRes, CoarseRes];
        for (int y = 0; y < CoarseRes; y++)
        for (int x = 0; x < CoarseRes; x++)
            CoarseWalkability[x, y] = noise[x, y] >= wt;

        Debug.Log($"[WORLD] Coarse walkability built ({CoarseRes}×{CoarseRes} over {WorldWidth}×{WorldHeight}).");
    }

    private static int ChebyshevDist(int dx, int dy) => Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy));

    private void OnDestroy()
    {
        foreach (var data in chunkMap.Values)
            FreeLodTextures(data);
    }
}
