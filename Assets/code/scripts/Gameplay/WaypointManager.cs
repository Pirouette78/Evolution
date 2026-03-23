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
        [System.NonSerialized] public BuildingDefinition definition;
        [System.NonSerialized] public bool processResources;

        // ── Stock local (ressources transportées par agents) ───────
        [System.NonSerialized] public float localStock;             // oxygène, glucose… reçu par livraison
        [System.NonSerialized] public float maxLocalStock;        // plafond (défini au placement)
        [System.NonSerialized] public float cachedEfficiency = 1f;// calculé chaque frame par la hive primaire, lu par les hives secondaires

        // ── Stats ──────────────────────────────────────────────────
        [System.NonSerialized] public int   placementId;
        [System.NonSerialized] public float totalAgentsSpawned;
        [System.NonSerialized] public float totalResourceProduced;
        [System.NonSerialized] public float totalResourceConsumed;
        [System.NonSerialized] public float lifetimeSeconds;
        [System.NonSerialized] public int   level;
    }

    private static int nextPlacementId = 0;

    [Header("Waypoints initiaux (optionnel, pré-configurés)")]
    public WaypointData[] InitialWaypoints = new WaypointData[0];

    [Header("Ruches initiales (optionnel)")]
    public HiveData[] InitialHives = new HiveData[0];

    // ── Runtime lists ────────────────────────────────────────────────
    private readonly List<WaypointData> waypointList  = new List<WaypointData>();
    private readonly List<string>       waypointNames = new List<string>();
    private readonly List<HiveData>     hiveList      = new List<HiveData>();

    // Shared flow field texture (MaxSlots slices, reused across recomputations)
    private Texture2DArray flowFieldTexture;
    private bool           flowFieldReady = false;

    // Smoothed paths (string pulling) — indexés par waypoint de destination
    private Vector2[][] smoothPaths = new Vector2[SlimeMapRenderer.MaxSlots][];
    private const int   MaxSmoothedWaypoints = 64;

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

        // Create the flow field texture (MaxSlots slices)
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
        var smr = SlimeMapRenderer.Instance;
        if (smr == null || !smr.IsReady) return;

        float dt = Time.deltaTime;

        // ── Phase A : Production locale ───────────────────────────────────
        // Les bâtiments producteurs (Poumon…) remplissent leur propre stock local.
        foreach (var hive in hiveList)
        {
            if (!hive.processResources) continue;
            var def = hive.definition;
            if (def?.produces == null) continue;
            foreach (var prod in def.produces)
            {
                float added = prod.amount * dt;
                hive.localStock = Mathf.Min(hive.localStock + added, hive.maxLocalStock);
                hive.totalResourceProduced += added;
            }
        }

        // ── Phase B : Livraison (lecture des compteurs GPU) ───────────────
        // Upload des stocks actuels vers le GPU (lu par les agents au chargement)
        float[] wpStocks = new float[SlimeMapRenderer.MaxSlots];
        foreach (var hive in hiveList)
        {
            if (!hive.processResources) continue;
            int wi = hive.waypointIndex;
            if (wi >= 0 && wi < SlimeMapRenderer.MaxSlots) wpStocks[wi] = hive.localStock;
        }
        smr.SetWaypointStocks(wpStocks);

        // Appliquer les livraisons lues depuis le GPU (frame précédente)
        foreach (var dest in hiveList)
        {
            int dWi = dest.waypointIndex;
            if (dWi < 0 || dWi >= SlimeMapRenderer.MaxSlots) continue;
            if (dWi >= waypointList.Count) continue;
            if (waypointList[dWi].type != 1) continue;

            int deliveries = smr.DeliveryCounts[dWi];
            if (deliveries <= 0) continue;

            var carrierDef = SpeciesLibrary.Instance?.GetBySlot(dest.speciesSlot);
            float delivered = deliveries * (carrierDef?.payloadCapacity ?? 1f);

            // Drainer le stock de la Source correspondante
            foreach (var src in hiveList)
            {
                if (!src.processResources) continue;
                int srcWi = src.waypointIndex;
                if (srcWi < 0 || srcWi >= waypointList.Count) continue;
                if (waypointList[srcWi].type != 0 || waypointList[srcWi].speciesIndex != dest.speciesSlot) continue;
                float taken = Mathf.Min(delivered, src.localStock);
                src.localStock -= taken;
                delivered = taken; // n'ajouter que ce qui a réellement été prélevé
                break;
            }

            var consumer = GetPrimaryHive(dest.placementId);
            if (consumer != null && consumer != dest)
                consumer.localStock = Mathf.Min(consumer.localStock + delivered, consumer.maxLocalStock);
        }

        // ── Phase C : Consommation locale + spawn ─────────────────────────
        // Passe 1 : calcul de l'efficacité par la hive primaire de chaque placement.
        // Résultat stocké dans cachedEfficiency pour être partagé avec les hives secondaires.
        foreach (var hive in hiveList)
        {
            if (!hive.processResources) continue;
            var   def = hive.definition;
            float eff = 1f;

            string scaleRes    = def?.ResolvedScaleResource;
            float  scaleAmount = def?.ResolvedScaleAmount ?? 0f;

            if (!string.IsNullOrEmpty(scaleRes) && scaleAmount > 0f)
            {
                float needed   = scaleAmount * dt;
                float consumed = Mathf.Min(needed, hive.localStock);
                hive.localStock            -= consumed;
                hive.totalResourceConsumed += consumed;
                eff = needed > 0f ? consumed / needed : 1f;
            }
            else if (def?.consumes != null)
            {
                foreach (var req in def.consumes)
                {
                    float needed   = req.amount * dt;
                    float consumed = Mathf.Min(needed, hive.localStock);
                    hive.localStock            -= consumed;
                    hive.totalResourceConsumed += consumed;
                    eff = Mathf.Min(eff, needed > 0f ? consumed / needed : 1f);
                }
            }

            hive.cachedEfficiency = eff;
        }

        // Passe 2 : toutes les hives de production utilisent l'efficacité
        // de la hive primaire de leur placement (primaire ET secondaires partagent la même).
        foreach (var hive in hiveList)
        {
            if (hive.speciesSlot < 0 || hive.speciesSlot >= 6) continue;
            if (smr.AliveSpeciesCounts[hive.speciesSlot] >= (uint)hive.maxPopulation) continue;

            hive.lifetimeSeconds += dt;

            float efficiency    = GetPrimaryHive(hive.placementId)?.cachedEfficiency ?? 1f;
            float effectiveRate = hive.spawnsPerSecond * efficiency;
            if (effectiveRate <= 0f) continue;

            hive.accumulator += dt;
            float interval = 1f / effectiveRate;
            if (hive.accumulator >= interval)
            {
                hive.accumulator -= interval;
                int     wpi = hive.waypointIndex;
                Vector2 pos = (wpi >= 0 && wpi < waypointList.Count)
                    ? waypointList[wpi].position
                    : new Vector2(smr.MapWidth * 0.5f, smr.MapHeight * 0.5f);
                smr.AddAgentsAt(1, hive.speciesSlot, pos);
                hive.totalAgentsSpawned++;
                hive.level = Mathf.Min(1 + (int)(hive.totalAgentsSpawned / 500f), 10);
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
        if (waypointList.Count >= SlimeMapRenderer.MaxSlots)
        {
            Debug.LogWarning($"[WAYPOINTS] Max {SlimeMapRenderer.MaxSlots} waypoints reached.");
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

        // Auto-create hive(s) for Source waypoints
        if (wp.type == 0 && autoHive)
        {
            BuildingDefinition def = BuildingLibrary.Instance != null
                ? BuildingLibrary.Instance.Get(buildingName)
                : null;

            string playerId = PlayerLibrary.Instance?.GetPlayerIdForSlot(wp.speciesIndex);
            var outputs = def?.ResolvedOutputs();

            int pid = -1;
            if (outputs != null && outputs.Length > 0)
            {
                pid = nextPlacementId++;
                bool isFirst = true;
                foreach (var output in outputs)
                {
                    int slot = (!string.IsNullOrEmpty(playerId) && PlayerLibrary.Instance != null)
                        ? PlayerLibrary.Instance.GetSlotIndex(playerId, output.speciesId)
                        : -1;
                    if (slot < 0) { isFirst = false; continue; }

                    int wpIndex;
                    if (isFirst)
                    {
                        wpIndex = newIndex;
                    }
                    else
                    {
                        if (waypointList.Count >= SlimeMapRenderer.MaxSlots) { Debug.LogWarning($"[WAYPOINTS] Max {SlimeMapRenderer.MaxSlots} waypoints reached."); break; }
                        waypointList.Add(new WaypointData { position = wp.position, type = 0, speciesIndex = slot });
                        waypointNames.Add(buildingName);
                        wpIndex = waypointList.Count - 1;
                        ComputeFlowFieldForIndex(wpIndex);
                        UploadToGPU();
                    }

                    hiveList.Add(new HiveData
                    {
                        waypointIndex    = wpIndex,
                        speciesSlot      = slot,
                        spawnsPerSecond  = output.spawnsPerSecond,
                        maxPopulation    = output.maxPopulation,
                        definition       = def,
                        processResources = isFirst,
                        placementId      = pid,
                        level            = 1,
                        maxLocalStock    = 500f,
                    });

                    isFirst = false;
                }
            }
            // Si le bâtiment a une espèce liée, créer automatiquement un waypoint Destination pour elle
            // + une HiveData "point de livraison" pour le mécanisme de transport de ressources.
            if (def != null && !string.IsNullOrEmpty(def.linkedSpeciesId))
            {
                int linkedSlot = (!string.IsNullOrEmpty(playerId) && PlayerLibrary.Instance != null)
                    ? PlayerLibrary.Instance.GetSlotIndex(playerId, def.linkedSpeciesId)
                    : -1;
                if (linkedSlot >= 0)
                {
                    AddWaypoint(new WaypointData { position = wp.position, type = 1, speciesIndex = linkedSlot },
                                buildingName, autoHive: false);
                    // Créer la HiveData associée au waypoint Destination pour tracer les livraisons
                    if (pid >= 0)
                    {
                        hiveList.Add(new HiveData
                        {
                            waypointIndex    = waypointList.Count - 1,
                            speciesSlot      = linkedSlot,
                            spawnsPerSecond  = 0f,
                            maxPopulation    = int.MaxValue,
                            definition       = def,
                            processResources = false,
                            placementId      = pid,
                            level            = 0,
                            maxLocalStock    = 0f,
                        });
                    }
                }
            }
        }

        Debug.Log($"[WAYPOINTS] Added waypoint #{newIndex} type={wp.type} species={wp.speciesIndex} at {wp.position}");
    }

    public WaypointData[] GetWaypoints() => waypointList.ToArray();

    public string GetWaypointName(int index)
        => (index >= 0 && index < waypointNames.Count) ? waypointNames[index] : "";

    /// <summary>Retourne la Hive primaire (processResources=true) d'un placement.</summary>
    public HiveData GetPrimaryHive(int placementId)
    {
        foreach (var h in hiveList)
            if (h.placementId == placementId && h.processResources) return h;
        // Fallback : première hive du placement
        foreach (var h in hiveList)
            if (h.placementId == placementId) return h;
        return null;
    }

    /// <summary>Retourne toutes les Hives d'un placement (multi-output).</summary>
    public List<HiveData> GetHivesForPlacement(int placementId)
    {
        var result = new List<HiveData>();
        foreach (var h in hiveList)
            if (h.placementId == placementId) result.Add(h);
        return result;
    }

    /// <summary>
    /// Retourne le placementId du waypoint Source le plus proche de worldPos.
    /// Retourne -1 si aucun dans le rayon maxDist (pixels).
    /// </summary>
    public int GetPlacementAt(Vector2 worldPos, float maxDist = 25f)
    {
        float best = maxDist * maxDist;
        int   found = -1;
        for (int i = 0; i < waypointList.Count; i++)
        {
            if (waypointList[i].type != 0) continue; // Sources uniquement
            float d = (waypointList[i].position - worldPos).sqrMagnitude;
            if (d < best)
            {
                best = d;
                // Trouver le placementId associé à ce waypoint index
                foreach (var h in hiveList)
                    if (h.waypointIndex == i) { found = h.placementId; break; }
            }
        }
        return found;
    }

    // ── Flow field computation ───────────────────────────────────────

    private void AllocFlowFieldTexture()
    {
        if (flowFieldTexture != null) Destroy(flowFieldTexture);

        int W = TerrainMapRenderer.Instance.Width;
        int H = TerrainMapRenderer.Instance.Height;

        flowFieldTexture = new Texture2DArray(W, H, SlimeMapRenderer.MaxSlots, TextureFormat.RGHalf, false)
        {
            filterMode = FilterMode.Point,
            wrapMode   = TextureWrapMode.Clamp
        };
        Color[] empty = new Color[W * H];
        for (int s = 0; s < SlimeMapRenderer.MaxSlots; s++) flowFieldTexture.SetPixels(empty, s);
        flowFieldTexture.Apply();
    }

    private void RecomputeAllFlowFields()
    {
        int count = Mathf.Min(waypointList.Count, SlimeMapRenderer.MaxSlots);
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
        ComputeSmoothedPaths();
    }

    /// <summary>Retourne tous les chemins lissés (vers chaque destination) d'une espèce.</summary>
    public List<Vector2[]> GetSmoothedPathsForSpecies(int speciesIndex)
    {
        var result = new List<Vector2[]>();
        for (int wi = 0; wi < Mathf.Min(waypointList.Count, SlimeMapRenderer.MaxSlots); wi++)
        {
            if (waypointList[wi].speciesIndex != speciesIndex) continue;
            if (waypointList[wi].type != 1) continue;
            if (smoothPaths[wi] != null) result.Add(smoothPaths[wi]);
        }
        return result;
    }

    // ── BFS ─────────────────────────────────────────────────────────

    private static Color[] BFSFlowField(bool[,] walkable, int W, int H, int targetX, int targetY)
        => BFSFlowField(walkable, W, H, targetX, targetY, out _);

    private static Color[] BFSFlowField(bool[,] walkable, int W, int H,
        int targetX, int targetY, out int[] parent)
    {
        parent = new int[W * H];
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

    // ── String Pulling ───────────────────────────────────────────────

    /// <summary>Trace le chemin brut de source vers destination via le tableau parent[] du BFS.</summary>
    private static List<Vector2Int> ExtractRawPath(int[] parent, int W, int srcIdx, int dstIdx)
    {
        var path  = new List<Vector2Int>();
        int cur   = srcIdx;
        int limit = parent.Length;
        while (cur != dstIdx && limit-- > 0)
        {
            if (parent[cur] == -1) return null; // source inaccessible
            path.Add(new Vector2Int(cur % W, cur / W));
            cur = parent[cur];
        }
        path.Add(new Vector2Int(cur % W, cur / W)); // ajoute la destination
        return path;
    }

    /// <summary>Bresenham — retourne true si la ligne droite entre deux pixels est entièrement praticable.</summary>
    private static bool HasLineOfSight(bool[,] walkable, int W, int H,
        int x0, int y0, int x1, int y1)
    {
        int dx = Mathf.Abs(x1 - x0), dy = Mathf.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
        int err = dx - dy, x = x0, y = y0;
        while (true)
        {
            if (x < 0 || x >= W || y < 0 || y >= H || !walkable[x, y]) return false;
            if (x == x1 && y == y1) return true;
            int e2 = 2 * err;
            if (e2 > -dy) { err -= dy; x += sx; }
            if (e2 <  dx) { err += dx; y += sy; }
        }
    }

    /// <summary>Réduit rawPath en supprimant les points intermédiaires visibles en ligne droite.</summary>
    private static List<Vector2Int> StringPull(List<Vector2Int> raw, bool[,] walkable, int W, int H)
    {
        if (raw == null || raw.Count <= 2) return raw;
        var result = new List<Vector2Int> { raw[0] };
        int anchor = 0;
        for (int i = 2; i < raw.Count; i++)
        {
            var a = raw[anchor]; var b = raw[i];
            if (!HasLineOfSight(walkable, W, H, a.x, a.y, b.x, b.y))
            {
                result.Add(raw[i - 1]);
                anchor = i - 1;
            }
        }
        result.Add(raw[raw.Count - 1]);
        return result;
    }

    /// <summary>
    /// Pour chaque waypoint de DESTINATION, calcule le chemin lissé depuis la Source de la même espèce.
    /// Indexé par index de waypoint (0-15), pas par espèce — supporte plusieurs destinations par espèce.
    /// </summary>
    private void ComputeSmoothedPaths()
    {
        var terrain = TerrainMapRenderer.Instance;
        if (terrain == null) return;
        int W = terrain.Width, H = terrain.Height;
        bool[,] walkable = terrain.WalkabilityGrid;

        var flatBuffer = new Vector2[SlimeMapRenderer.MaxSlots * MaxSmoothedWaypoints];
        var starts     = new int[SlimeMapRenderer.MaxSlots];
        var counts     = new int[SlimeMapRenderer.MaxSlots];

        for (int i = 0; i < SlimeMapRenderer.MaxSlots; i++)
        {
            smoothPaths[i] = null;
            starts[i]      = i * MaxSmoothedWaypoints;
            counts[i]      = 0;
        }

        // Un chemin lissé par waypoint de destination
        for (int wi = 0; wi < Mathf.Min(waypointList.Count, SlimeMapRenderer.MaxSlots); wi++)
        {
            if (waypointList[wi].type != 1) continue; // destinations seulement

            int s = waypointList[wi].speciesIndex;

            // Trouver la source de la même espèce
            int srcWp = -1;
            for (int w = 0; w < waypointList.Count; w++)
                if (waypointList[w].speciesIndex == s && waypointList[w].type == 0) { srcWp = w; break; }
            if (srcWp < 0) continue;

            Vector2 srcPos = waypointList[srcWp].position;
            Vector2 dstPos = waypointList[wi].position;
            int srcX = Mathf.Clamp((int)srcPos.x, 0, W - 1);
            int srcY = Mathf.Clamp((int)srcPos.y, 0, H - 1);
            int dstX = Mathf.Clamp((int)dstPos.x, 0, W - 1);
            int dstY = Mathf.Clamp((int)dstPos.y, 0, H - 1);

            // BFS depuis cette destination (flow field enraciné à la cible)
            BFSFlowField(walkable, W, H, dstX, dstY, out int[] parent);

            var raw      = ExtractRawPath(parent, W, srcY * W + srcX, dstY * W + dstX);
            var smoothed = StringPull(raw, walkable, W, H);
            if (smoothed == null || smoothed.Count == 0) continue;

            int count = Mathf.Min(smoothed.Count, MaxSmoothedWaypoints);
            smoothPaths[wi] = new Vector2[count];
            for (int i = 0; i < count; i++)
            {
                var v = new Vector2(smoothed[i].x, smoothed[i].y);
                smoothPaths[wi][i]              = v;
                flatBuffer[starts[wi] + i] = v;
            }
            counts[wi] = count;
        }

        SlimeMapRenderer.Instance.SetSmoothedPaths(flatBuffer, starts, counts);
    }

    private void OnDestroy()
    {
        if (flowFieldTexture != null) Destroy(flowFieldTexture);
    }
}
