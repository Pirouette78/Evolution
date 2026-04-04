using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manages waypoints (Sources and Destinations) for species navigation,
/// computes CPU-side Dijkstra flow fields for pathfinding, and handles hive spawning.
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

    // Fine flow field (sim space, chunk actif)
    private Texture2DArray flowFieldTexture;
    private bool           flowFieldReady = false;

    // Coarse flow field (world tile space, monde entier — Phase 2)
    private Texture2DArray coarseFlowFieldTexture;

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
            yield return StartCoroutine(RecomputeAllFlowFieldsAsync(UploadToGPU));
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
                    ? WorldTileToSimPixel(waypointList[wpi].position)
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
        ComputeCoarseFlowFieldForIndex(newIndex);
        UploadToGPU();
        UploadCoarseToGPU();

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
                        ComputeCoarseFlowFieldForIndex(wpIndex);
                        UploadToGPU();
                        UploadCoarseToGPU();
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

    /// <summary>
    /// Recalcule tous les flow fields (ex: après un changement de marchabilité dû aux unités).
    /// Sans effet si les flow fields ne sont pas encore initialisés.
    /// </summary>
    public void RebuildAllFlowFields()
    {
        if (!flowFieldReady) return;
        StartCoroutine(RecomputeAllFlowFieldsAsync(() => {
            UploadToGPU();
            Debug.Log("[WAYPOINTS] Flow fields rebuilt (walkability changed).");
        }));
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
    /// Retourne le placementId du waypoint Source le plus proche de simPos (sim pixel space).
    /// Retourne -1 si aucun dans le rayon maxDist (sim pixels).
    /// </summary>
    public int GetPlacementAt(Vector2 simPos, float maxDist = 25f)
    {
        float best = maxDist * maxDist;
        int   found = -1;
        for (int i = 0; i < waypointList.Count; i++)
        {
            if (waypointList[i].type != 0) continue; // Sources uniquement
            // Convertir la position world tile du waypoint en sim pixel pour comparer
            Vector2 wpSim = WorldTileToSimPixel(waypointList[i].position);
            float d = (wpSim - simPos).sqrMagnitude;
            if (d < best)
            {
                best = d;
                foreach (var h in hiveList)
                    if (h.waypointIndex == i) { found = h.placementId; break; }
            }
        }
        return found;
    }

    /// <summary>
    /// Appelé par WorldChunkRegistry après un changement de chunk.
    /// Recompute tous les flow fields sur le nouveau terrain et uploade vers le GPU.
    /// </summary>
    public void OnChunkActivated()
    {
        if (!flowFieldReady) return;
        StartCoroutine(RecomputeAllFlowFieldsAsync(() => {
            UploadToGPU();
            UploadCoarseToGPU();
            Debug.Log("[WAYPOINTS] Flow fields recalculés pour le nouveau chunk.");
        }));
    }

    // ── Helpers coord spaces ─────────────────────────────────────────

    /// <summary>
    /// Convertit une position en world tile space vers sim pixel space du chunk actif.
    /// Si WorldChunkRegistry n'est pas encore initialisé, traite la position comme déjà en sim space.
    /// </summary>
    private Vector2 WorldTileToSimPixel(Vector2 worldTile)
    {
        var wcr = WorldChunkRegistry.Instance;
        if (wcr == null) return worldTile;  // pas encore de chunk system → position déjà en sim space
        Vector2 chunkOrigin = wcr.ChunkOriginTile(wcr.ActiveChunk.x, wcr.ActiveChunk.y);
        float ratio = SlimeMapRenderer.Instance != null ? SlimeMapRenderer.Instance.SimRatio : 1f;
        return (worldTile - chunkOrigin) * ratio;
    }

    /// <summary>Projette un tableau de waypoints de world tile space vers sim pixel space.</summary>
    private WaypointData[] ProjectToSimSpace(WaypointData[] worldWps)
    {
        var wcr = WorldChunkRegistry.Instance;
        if (wcr == null) return worldWps;
        Vector2 chunkOrigin = wcr.ChunkOriginTile(wcr.ActiveChunk.x, wcr.ActiveChunk.y);
        float ratio = SlimeMapRenderer.Instance != null ? SlimeMapRenderer.Instance.SimRatio : 1f;
        var result = new WaypointData[worldWps.Length];
        for (int i = 0; i < worldWps.Length; i++)
        {
            result[i]          = worldWps[i];
            result[i].position = (worldWps[i].position - chunkOrigin) * ratio;
        }
        return result;
    }

    // ── Flow field computation ───────────────────────────────────────

    private void AllocFlowFieldTexture()
    {
        if (flowFieldTexture != null) Destroy(flowFieldTexture);

        // Texture en dimensions SIMULATION — le shader échantillonne aux positions des agents (espace sim)
        int W = SlimeMapRenderer.Instance.Width;
        int H = SlimeMapRenderer.Instance.Height;

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

    /// <summary>
    /// Version async : exécute tous les BFS en parallèle sur des background threads,
    /// puis applique la texture et appelle onDone sur le main thread.
    /// Utilisé lors des transitions de chunk pour éviter de freezer.
    /// </summary>
    private IEnumerator RecomputeAllFlowFieldsAsync(System.Action onDone = null)
    {
        var terrain = TerrainMapRenderer.Instance;
        if (terrain?.WalkabilityGrid == null) { onDone?.Invoke(); yield break; }

        // Snapshot de tout ce qui est nécessaire au BFS — capturé sur le main thread
        bool[,]  walkable    = terrain.WalkabilityGrid;
        int      terrW       = terrain.Width;
        int      terrH       = terrain.Height;
        int      simW        = SlimeMapRenderer.Instance.Width;
        int      simH        = SlimeMapRenderer.Instance.Height;
        var      wcr         = WorldChunkRegistry.Instance;
        Vector2  chunkOrigin = wcr != null
            ? wcr.ChunkOriginTile(wcr.ActiveChunk.x, wcr.ActiveChunk.y)
            : Vector2.zero;

        int      count   = Mathf.Min(waypointList.Count, SlimeMapRenderer.MaxSlots);
        Color[][] results = new Color[SlimeMapRenderer.MaxSlots][];
        int      empty   = simW * simH;
        for (int i = 0; i < SlimeMapRenderer.MaxSlots; i++) results[i] = new Color[empty];

        if (count > 0)
        {
            // Snapshot des positions (la liste peut changer pendant les yields)
            var localTiles = new Vector2[count];
            for (int i = 0; i < count; i++)
                localTiles[i] = waypointList[i].position - chunkOrigin;

            // Lancer un Task par waypoint — DijkstraFlowField n'utilise pas l'API Unity
            var tasks = new System.Threading.Tasks.Task[count];
            for (int i = 0; i < count; i++)
            {
                int wi  = i;
                var loc = localTiles[wi];
                tasks[wi] = System.Threading.Tasks.Task.Run(() =>
                    results[wi] = ComputeFlowFieldSlice(walkable, terrW, terrH, simW, simH, loc));
            }

            var all = System.Threading.Tasks.Task.WhenAll(tasks);
            while (!all.IsCompleted) yield return null;

            if (all.IsFaulted)
                Debug.LogError($"[WAYPOINTS] BFS async error: {all.Exception?.InnerException}");
        }

        // Retour sur le main thread — SetPixels et Apply doivent s'y faire
        for (int i = 0; i < SlimeMapRenderer.MaxSlots; i++)
            flowFieldTexture.SetPixels(results[i], i);
        flowFieldTexture.Apply();

        onDone?.Invoke();
    }

    /// <summary>Compute Dijkstra flow field for a single waypoint slice (synchronous, for AddWaypoint).</summary>
    private void ComputeFlowFieldForIndex(int wi, bool applyTexture = true)
    {
        if (wi < 0 || wi >= waypointList.Count) return;
        var terrain = TerrainMapRenderer.Instance;
        var wcr = WorldChunkRegistry.Instance;
        Vector2 chunkOrigin = wcr != null
            ? wcr.ChunkOriginTile(wcr.ActiveChunk.x, wcr.ActiveChunk.y)
            : Vector2.zero;
        Vector2 localTile = waypointList[wi].position - chunkOrigin;

        Color[] slice = ComputeFlowFieldSlice(
            terrain.WalkabilityGrid, terrain.Width, terrain.Height,
            SlimeMapRenderer.Instance.Width, SlimeMapRenderer.Instance.Height,
            localTile);

        flowFieldTexture.SetPixels(slice, wi);
        if (applyTexture) flowFieldTexture.Apply();
    }

    /// <summary>
    /// Calcule un slice de flow field à partir d'une localTile.
    /// Thread-safe : n'utilise que des tableaux, pas l'API Unity.
    /// </summary>
    private static Color[] ComputeFlowFieldSlice(
        bool[,] walkable, int terrW, int terrH, int simW, int simH, Vector2 localTile)
    {
        if (localTile.x < 0 || localTile.x >= terrW || localTile.y < 0 || localTile.y >= terrH)
            return new Color[simW * simH]; // zéro = pas de direction → coarse field prend le relais

        int tx = Mathf.Clamp((int)localTile.x, 0, terrW - 1);
        int ty = Mathf.Clamp((int)localTile.y, 0, terrH - 1);

        Color[] terrSlice = DijkstraFlowField(walkable, terrW, terrH, tx, ty);
        if (terrW == simW && terrH == simH) return terrSlice;

        // Nearest-neighbour resize vers l'espace sim
        var sim = new Color[simW * simH];
        for (int sy = 0; sy < simH; sy++)
        for (int sx = 0; sx < simW; sx++)
        {
            int terrX = Mathf.Clamp((int)(sx * terrW / (float)simW), 0, terrW - 1);
            int terrY = Mathf.Clamp((int)(sy * terrH / (float)simH), 0, terrH - 1);
            sim[sy * simW + sx] = terrSlice[terrY * terrW + terrX];
        }
        return sim;
    }

    private void UploadToGPU()
    {
        SlimeMapRenderer.Instance.SetFlowFields(flowFieldTexture);
        // Projeter les positions world tile → sim pixel avant l'upload GPU
        SlimeMapRenderer.Instance.SetWaypoints(ProjectToSimSpace(waypointList.ToArray()));
    }

    // ── Coarse flow field (Phase 2 — monde entier) ───────────────────

    /// <summary>
    /// Construit tous les coarse flow fields sur la grille mondiale grossière.
    /// Appelé une fois par WorldChunkRegistry après BuildCoarseWalkability().
    /// </summary>
    public void RebuildCoarseFlowFields()
    {
        var wcr = WorldChunkRegistry.Instance;
        if (wcr?.CoarseWalkability == null) return;

        AllocCoarseFlowFieldTexture(wcr.CoarseRes);

        int count = Mathf.Min(waypointList.Count, SlimeMapRenderer.MaxSlots);
        for (int i = 0; i < count; i++) ComputeCoarseFlowFieldForIndex(i, applyTexture: false);
        coarseFlowFieldTexture.Apply();

        UploadCoarseToGPU();
        Debug.Log($"[WAYPOINTS] Coarse flow fields built ({wcr.CoarseRes}×{wcr.CoarseRes}).");
    }

    private void AllocCoarseFlowFieldTexture(int coarseRes)
    {
        if (coarseFlowFieldTexture != null) Destroy(coarseFlowFieldTexture);
        coarseFlowFieldTexture = new Texture2DArray(coarseRes, coarseRes, SlimeMapRenderer.MaxSlots,
                                                    TextureFormat.RGHalf, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode   = TextureWrapMode.Clamp
        };
        Color[] empty = new Color[coarseRes * coarseRes];
        for (int s = 0; s < SlimeMapRenderer.MaxSlots; s++) coarseFlowFieldTexture.SetPixels(empty, s);
        coarseFlowFieldTexture.Apply();
    }

    /// <summary>
    /// Calcule le coarse flow field pour un waypoint sur la grille mondiale grossière (Dijkstra).
    /// Si la texture coarse n'est pas encore allouée, no-op.
    /// </summary>
    private void ComputeCoarseFlowFieldForIndex(int wi, bool applyTexture = true)
    {
        if (wi < 0 || wi >= waypointList.Count) return;
        var wcr = WorldChunkRegistry.Instance;
        if (wcr?.CoarseWalkability == null || coarseFlowFieldTexture == null) return;

        int        coarseRes  = wcr.CoarseRes;
        bool[,]    coarseWalk = wcr.CoarseWalkability;

        // world tile → coarse cell
        float scaleX = (float)coarseRes / wcr.WorldWidth;
        float scaleY = (float)coarseRes / wcr.WorldHeight;
        int cx = Mathf.Clamp((int)(waypointList[wi].position.x * scaleX), 0, coarseRes - 1);
        int cy = Mathf.Clamp((int)(waypointList[wi].position.y * scaleY), 0, coarseRes - 1);

        Color[] slice = DijkstraFlowField(coarseWalk, coarseRes, coarseRes, cx, cy);
        coarseFlowFieldTexture.SetPixels(slice, wi);
        if (applyTexture) coarseFlowFieldTexture.Apply();
    }

    /// <summary>
    /// Uploade la texture coarse + uniforms (chunkWorldOrigin, worldSize, simToWorldScale) vers le GPU.
    /// Appelé lors de l'init, à chaque changement de chunk, et à chaque ajout de waypoint.
    /// </summary>
    private void UploadCoarseToGPU()
    {
        var smr = SlimeMapRenderer.Instance;
        var wcr = WorldChunkRegistry.Instance;
        if (smr == null || wcr == null || coarseFlowFieldTexture == null) return;

        smr.SetCoarseFlowField(coarseFlowFieldTexture);

        Vector2 origin = wcr.ChunkOriginTile(wcr.ActiveChunk.x, wcr.ActiveChunk.y);
        smr.SetChunkWorldOrigin(origin);

        var terrain = TerrainMapRenderer.Instance;
        float simToWorld = (terrain != null && smr.Width > 0)
            ? (float)terrain.Width / smr.Width
            : 1f;
        smr.SetWorldDimensions(wcr.WorldWidth, wcr.WorldHeight, simToWorld);
    }

    // ── Dijkstra flow field ──────────────────────────────────────────

    /// <summary>
    /// Calcule le flow field Dijkstra depuis targetX/Y.
    /// Coût cardinaux = 1.0, diagonaux = √2 — élimine le biais BFS vers les diagonales.
    /// </summary>
    private static Color[] DijkstraFlowField(bool[,] walkable, int W, int H, int targetX, int targetY)
    {
        float[] dist   = new float[W * H];
        int[]   parent = new int[W * H];
        for (int i = 0; i < dist.Length; i++) { dist[i] = float.MaxValue; parent[i] = -1; }

        int targetIdx = targetY * W + targetX;
        dist[targetIdx]   = 0f;
        parent[targetIdx] = targetIdx;

        // Min-heap : (cost, cellIndex)
        var heap = new List<(float cost, int idx)>(W * H / 4);
        heap.Add((0f, targetIdx));

        int[]   dx    = { 1, -1,  0,  0,  1, -1,  1, -1 };
        int[]   dy    = { 0,  0,  1, -1,  1, -1, -1,  1 };
        float[] dcost = { 1f, 1f, 1f, 1f, 1.41421356f, 1.41421356f, 1.41421356f, 1.41421356f };

        while (heap.Count > 0)
        {
            var (curCost, cur) = heap[0];
            HeapPop(heap);

            if (curCost > dist[cur]) continue; // entrée périmée

            int cx = cur % W, cy = cur / W;
            for (int d = 0; d < 8; d++)
            {
                int nx = cx + dx[d], ny = cy + dy[d];
                if (nx < 0 || nx >= W || ny < 0 || ny >= H) continue;
                if (!walkable[nx, ny]) continue;
                int   ni      = ny * W + nx;
                float newCost = curCost + dcost[d];
                if (newCost >= dist[ni]) continue;
                dist[ni]   = newCost;
                parent[ni] = cur;
                HeapPush(heap, (newCost, ni));
            }
        }

        Color[] slice = new Color[W * H];
        for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
        {
            int idx = y * W + x;
            if (parent[idx] == -1) { slice[idx] = Color.black; continue; }
            int px = parent[idx] % W, py = parent[idx] / W;
            if (px == x && py == y) { slice[idx] = Color.black; continue; }
            float dirX = px - x, dirY = py - y;
            float len  = Mathf.Sqrt(dirX * dirX + dirY * dirY);
            slice[idx] = new Color(dirX / len, dirY / len, 0f, 0f);
        }
        return slice;
    }

    private static void HeapPush(List<(float, int)> heap, (float, int) item)
    {
        heap.Add(item);
        int i = heap.Count - 1;
        while (i > 0)
        {
            int p = (i - 1) / 2;
            if (heap[p].Item1 <= heap[i].Item1) break;
            (heap[p], heap[i]) = (heap[i], heap[p]);
            i = p;
        }
    }

    private static void HeapPop(List<(float, int)> heap)
    {
        int last = heap.Count - 1;
        heap[0] = heap[last];
        heap.RemoveAt(last);
        int i = 0, n = heap.Count;
        while (true)
        {
            int s = i, l = 2*i+1, r = 2*i+2;
            if (l < n && heap[l].Item1 < heap[s].Item1) s = l;
            if (r < n && heap[r].Item1 < heap[s].Item1) s = r;
            if (s == i) break;
            (heap[i], heap[s]) = (heap[s], heap[i]);
            i = s;
        }
    }

    private void OnDestroy()
    {
        if (flowFieldTexture != null)        Destroy(flowFieldTexture);
        if (coarseFlowFieldTexture != null)  Destroy(coarseFlowFieldTexture);
    }
}
