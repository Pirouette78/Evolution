using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manages waypoints (Sources and Destinations) for species navigation,
/// computes CPU-side BFS flow fields for pathfinding, and handles hive spawning.
/// Waypoints are added at runtime via AddWaypoint() (called from BuildingPlacementController).
/// </summary>
public class WaypointManager : MonoBehaviour
{
    public static WaypointManager Instance { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoCreate()
    {
        if (Instance != null) return;
        var go = new GameObject("[WaypointManager]");
        go.AddComponent<WaypointManager>();
        DontDestroyOnLoad(go);
    }

    [System.Serializable]
    public class HiveData
    {
        public int   waypointIndex;
        public int   speciesSlot;
        public float spawnsPerSecond;
        public int   maxPopulation;
        [HideInInspector] public float accumulator;
    }

    [Header("Waypoints initiaux (optionnel, pré-configurés)")]
    public WaypointData[] InitialWaypoints = new WaypointData[0];

    [Header("Ruches initiales (optionnel)")]
    public HiveData[] InitialHives = new HiveData[0];

    // ── Runtime lists ────────────────────────────────────────────────
    private readonly List<WaypointData> waypointList  = new List<WaypointData>();
    private readonly List<string>       waypointNames = new List<string>();
    private readonly List<HiveData>     hiveList      = new List<HiveData>();

    // Shared flow field texture (16 slices, reused across recomputations)
    private Texture2DArray flowFieldTexture;
    private bool           flowFieldReady = false;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private IEnumerator Start()
    {
        // Wait for terrain and renderer to be ready
        while (TerrainMapRenderer.Instance == null ||
               TerrainMapRenderer.Instance.WalkabilityGrid == null ||
               SlimeMapRenderer.Instance == null ||
               !SlimeMapRenderer.Instance.IsReady)
        {
            yield return null;
        }

        // Load initial waypoints / hives
        foreach (var wp in InitialWaypoints) waypointList.Add(wp);
        foreach (var h  in InitialHives)     hiveList.Add(h);

        // Create the flow field texture (always 16 slices regardless of current count)
        AllocFlowFieldTexture();

        if (waypointList.Count > 0)
        {
            RecomputeAllFlowFields();
            UploadToGPU();
        }
        else
        {
            SlimeMapRenderer.Instance.SetFlowFields(flowFieldTexture);
            SlimeMapRenderer.Instance.SetWaypoints(new WaypointData[0]);
        }

        flowFieldReady = true;
    }

    private void Update()
    {
        if (SlimeMapRenderer.Instance == null || !SlimeMapRenderer.Instance.IsReady) return;

        foreach (var hive in hiveList)
        {
            if (hive.speciesSlot < 0 || hive.speciesSlot >= 6) continue;
            if (SlimeMapRenderer.Instance.AliveSpeciesCounts[hive.speciesSlot] >= (uint)hive.maxPopulation) continue;

            hive.accumulator += Time.deltaTime;
            float interval = hive.spawnsPerSecond > 0f ? 1f / hive.spawnsPerSecond : float.MaxValue;
            if (hive.accumulator >= interval)
            {
                hive.accumulator -= interval;
                int  wi  = hive.waypointIndex;
                Vector2 pos = (wi >= 0 && wi < waypointList.Count)
                    ? waypointList[wi].position
                    : new Vector2(SlimeMapRenderer.Instance.Width * 0.5f, SlimeMapRenderer.Instance.Height * 0.5f);
                SlimeMapRenderer.Instance.AddAgentsAt(1, hive.speciesSlot, pos);
            }
        }
    }

    // ── Public API ──────────────────────────────────────────────────

    /// <summary>
    /// Add a waypoint at runtime. If it's a Source (type=0), optionally auto-creates a hive.
    /// Recomputes only the new flow field slice — incremental update.
    /// </summary>
    public void AddWaypoint(WaypointData wp, string buildingName = "", bool autoHive = true)
    {
        if (waypointList.Count >= 16)
        {
            Debug.LogWarning("[WAYPOINTS] Max 16 waypoints reached.");
            return;
        }

        waypointList.Add(wp);
        waypointNames.Add(buildingName);
        int newIndex = waypointList.Count - 1;

        if (!flowFieldReady)
        {
            Debug.LogWarning("[WAYPOINTS] Flow fields not ready yet — waypoint queued for next init.");
            return;
        }

        // Incremental BFS for just the new slice
        ComputeFlowFieldForIndex(newIndex);
        UploadToGPU();

        // Auto-create hive for Source waypoints
        if (wp.type == 0 && autoHive)
        {
            hiveList.Add(new HiveData
            {
                waypointIndex  = newIndex,
                speciesSlot    = wp.speciesIndex,
                spawnsPerSecond = 2f,
                maxPopulation  = 5000
            });
        }

        Debug.Log($"[WAYPOINTS] Added waypoint #{newIndex} type={wp.type} species={wp.speciesIndex} at {wp.position}");
    }

    public WaypointData[] GetWaypoints() => waypointList.ToArray();

    public string GetWaypointName(int index)
        => (index >= 0 && index < waypointNames.Count) ? waypointNames[index] : "";

    // ── Flow field computation ───────────────────────────────────────

    private void AllocFlowFieldTexture()
    {
        if (flowFieldTexture != null) Destroy(flowFieldTexture);

        int W = TerrainMapRenderer.Instance.Width;
        int H = TerrainMapRenderer.Instance.Height;

        flowFieldTexture = new Texture2DArray(W, H, 16, TextureFormat.RGHalf, false)
        {
            filterMode = FilterMode.Point,
            wrapMode   = TextureWrapMode.Clamp
        };
        Color[] empty = new Color[W * H];
        for (int s = 0; s < 16; s++) flowFieldTexture.SetPixels(empty, s);
        flowFieldTexture.Apply();
    }

    private void RecomputeAllFlowFields()
    {
        int count = Mathf.Min(waypointList.Count, 16);
        for (int i = 0; i < count; i++) ComputeFlowFieldForIndex(i, applyTexture: false);
        flowFieldTexture.Apply();
    }

    /// <summary>Compute BFS flow field for a single waypoint slice.</summary>
    private void ComputeFlowFieldForIndex(int wi, bool applyTexture = true)
    {
        if (wi < 0 || wi >= waypointList.Count) return;
        var terrain = TerrainMapRenderer.Instance;
        int W = terrain.Width;
        int H = terrain.Height;

        Vector2 target = waypointList[wi].position;
        int tx = Mathf.Clamp((int)target.x, 0, W - 1);
        int ty = Mathf.Clamp((int)target.y, 0, H - 1);

        Color[] slice = BFSFlowField(terrain.WalkabilityGrid, W, H, tx, ty);
        flowFieldTexture.SetPixels(slice, wi);
        if (applyTexture) flowFieldTexture.Apply();
    }

    private void UploadToGPU()
    {
        SlimeMapRenderer.Instance.SetFlowFields(flowFieldTexture);
        SlimeMapRenderer.Instance.SetWaypoints(waypointList.ToArray());
    }

    // ── BFS ─────────────────────────────────────────────────────────

    private static Color[] BFSFlowField(bool[,] walkable, int W, int H, int targetX, int targetY)
    {
        int[] parent = new int[W * H];
        for (int i = 0; i < parent.Length; i++) parent[i] = -1;

        int targetIdx = targetY * W + targetX;
        parent[targetIdx] = targetIdx;

        Queue<int> queue = new Queue<int>();
        queue.Enqueue(targetIdx);

        int[] dx = { 1, -1, 0,  0, 1, -1,  1, -1 };
        int[] dy = { 0,  0, 1, -1, 1, -1, -1,  1 };

        while (queue.Count > 0)
        {
            int cur = queue.Dequeue();
            int cx = cur % W;
            int cy = cur / W;
            for (int d = 0; d < 8; d++)
            {
                int nx = cx + dx[d]; int ny = cy + dy[d];
                if (nx < 0 || nx >= W || ny < 0 || ny >= H) continue;
                if (!walkable[nx, ny]) continue;
                int ni = ny * W + nx;
                if (parent[ni] != -1) continue;
                parent[ni] = cur;
                queue.Enqueue(ni);
            }
        }

        Color[] slice = new Color[W * H];
        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W; x++)
            {
                int idx = y * W + x;
                if (parent[idx] == -1) { slice[idx] = Color.black; continue; }
                int px = parent[idx] % W;
                int py = parent[idx] / W;
                if (px == x && py == y) { slice[idx] = Color.black; continue; }
                float dirX = px - x; float dirY = py - y;
                float len  = Mathf.Sqrt(dirX * dirX + dirY * dirY);
                slice[idx] = new Color(dirX / len, dirY / len, 0f, 0f);
            }
        }
        return slice;
    }

    private void OnDestroy()
    {
        if (flowFieldTexture != null) Destroy(flowFieldTexture);
    }
}
