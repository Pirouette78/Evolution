using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Jobs;
using Unity.Collections;
using Unity.Burst;

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

    [Header("Flow Field Performance")]
    [Tooltip("Définit la réduction de résolution pour le Dijkstra. 4 = 16x plus rapide. 1 = natif.")]
    [Range(1, 128)]
    public int flowFieldDownscale = 4;

    // ── Runtime lists ────────────────────────────────────────────────
    private readonly List<WaypointData> waypointList  = new List<WaypointData>();
    private readonly List<string>       waypointNames = new List<string>();
    private readonly List<HiveData>     hiveList      = new List<HiveData>();
    private readonly float[]            wpStocksCache = new float[SlimeMapRenderer.MaxSlots];

    // Shared flow field texture (MaxSlots slices, reused across recomputations)
    private Texture2DArray flowFieldTexture;
    private Texture2D      tempSliceTexture; // used for zero-copy slice uploads
    private bool           flowFieldReady = false;

    // Burst Job types and tracking
    public struct DijkstraNode
    {
        public float cost;
        public int idx;
    }

    private struct PendingFlowJob
    {
        public int waypointIndex;
        public JobHandle handle;
        public NativeArray<bool> walkable;
        public NativeArray<float> dist;
        public NativeArray<int> parent;
        public NativeArray<DijkstraNode> heap;
        public NativeArray<Vector2> outputSimSlice;
    }

    private Queue<PendingFlowJob> pendingJobs = new Queue<PendingFlowJob>();

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

        // ── Phase 0 : Update Async Flow Fields ────────────────────────────
        bool textureApplied = false;
        while (pendingJobs.Count > 0)
        {
            var pJob = pendingJobs.Peek();
            if (pJob.handle.IsCompleted)
            {
                pJob.handle.Complete();
                
                // Zero-conversion copy on GPU side directly ! Overcomes the 0.3s freeze caused by ToArray() and Color[] conversion
                var rawTexData = tempSliceTexture.GetPixelData<Vector2>(0);
                rawTexData.CopyFrom(pJob.outputSimSlice);
                
                // N'upload que l'unique slice calculée au lieu des 176 Mo du tableau complet !
                tempSliceTexture.Apply(false, false);
                Graphics.CopyTexture(tempSliceTexture, 0, 0, flowFieldTexture, pJob.waypointIndex, 0);
                
                textureApplied = true;

                pJob.outputSimSlice.Dispose();
                pJob.heap.Dispose();
                pJob.parent.Dispose();
                pJob.dist.Dispose();
                pJob.walkable.Dispose();

                pendingJobs.Dequeue();
            }
            else
            {
                break;
            }
        }
        if (textureApplied)
        {
            flowFieldReady = true;
        }

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
        System.Array.Clear(wpStocksCache, 0, SlimeMapRenderer.MaxSlots);
        foreach (var hive in hiveList)
        {
            if (!hive.processResources) continue;
            int wi = hive.waypointIndex;
            if (wi >= 0 && wi < SlimeMapRenderer.MaxSlots) wpStocksCache[wi] = hive.localStock;
        }
        smr.SetWaypointStocks(wpStocksCache);

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

            if (def != null)
            {
                var terrain = TerrainMapRenderer.Instance;
                var smr = SlimeMapRenderer.Instance;
                if (terrain != null && smr != null && def.blockTilesW > 0f)
                {
                    float scaleX = terrain.Width  / (float)smr.Width;
                    float scaleY = terrain.Height / (float)smr.Height;
                    int cx = (int)(wp.position.x * scaleX);
                    int cy = (int)(wp.position.y * scaleY);

                    int offX  = Mathf.RoundToInt(def.blockOffsetX);
                    int offY  = Mathf.RoundToInt(def.blockOffsetY);
                    int rectW = Mathf.Max(1, Mathf.RoundToInt(def.blockTilesW));
                    int rectH = Mathf.Max(1, Mathf.RoundToInt(def.blockTilesH));

                    terrain.SetUnitBlockRect(cx, cy, offX, offY, rectW, rectH, true);
                    RebuildAllFlowFields();
                }

                if (def.spriteTilesW > 0 && UnitSpriteRenderer.Instance != null)
                {
                    UnitSpriteRenderer.Instance.RegisterBuilding(wp.position, def);
                }
            }

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

    /// <summary>
    /// Recalcule tous les flow fields (ex: après un changement de marchabilité dû aux unités).
    /// Sans effet si les flow fields ne sont pas encore initialisés.
    /// </summary>
    public void RebuildAllFlowFields()
    {
        if (!flowFieldReady) return;
        RecomputeAllFlowFields();
        UploadToGPU();
        Debug.Log("[WAYPOINTS] Flow fields rebuilt (walkability changed).");
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

        // Texture en dimensions SIMULATION — le shader échantillonne aux positions des agents (espace sim)
        int W = SlimeMapRenderer.Instance.Width;
        int H = SlimeMapRenderer.Instance.Height;

        flowFieldTexture = new Texture2DArray(W, H, SlimeMapRenderer.MaxSlots, TextureFormat.RGFloat, false)
        {
            filterMode = FilterMode.Point,
            wrapMode   = TextureWrapMode.Clamp
        };
        Color[] empty = new Color[W * H];
        for (int s = 0; s < SlimeMapRenderer.MaxSlots; s++) flowFieldTexture.SetPixels(empty, s);
        flowFieldTexture.Apply();

        tempSliceTexture = new Texture2D(W, H, TextureFormat.RGFloat, false);
    }

    private void RecomputeAllFlowFields()
    {
        int count = Mathf.Min(waypointList.Count, SlimeMapRenderer.MaxSlots);
        for (int i = 0; i < count; i++) ComputeFlowFieldForIndex(i);
        // Do not call Apply here, texture is applied asynchronously in Update
    }

    /// <summary>Queue an async Burst job to compute Dijkstra flow field.</summary>
    private void ComputeFlowFieldForIndex(int wi)
    {
        if (wi < 0 || wi >= waypointList.Count) return;
        var terrain = TerrainMapRenderer.Instance;
        int trueTerrW = terrain.Width;
        int trueTerrH = terrain.Height;
        int simW  = SlimeMapRenderer.Instance.Width;
        int simH  = SlimeMapRenderer.Instance.Height;

        // SCALE DOWN FOR SPEED
        int div = Mathf.Max(1, flowFieldDownscale);
        int terrW = Mathf.Max(1, trueTerrW / div);
        int terrH = Mathf.Max(1, trueTerrH / div);

        Vector2 target = waypointList[wi].position;
        int tx = Mathf.Clamp((int)(target.x * terrW / (float)simW), 0, terrW - 1);
        int ty = Mathf.Clamp((int)(target.y * terrH / (float)simH), 0, terrH - 1);

        // Prep data for Burst
        NativeArray<bool> walkableNative = new NativeArray<bool>(terrW * terrH, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        bool[,] wGrid = terrain.WalkabilityGrid;
        
        // Flatten et sous-échantillonnage de la grille sur le Main Thread (très rapide)
        // On check le centre de chaque bloc 4x4
        for (int y = 0; y < terrH; y++)
        {
            for (int x = 0; x < terrW; x++)
            {
                // Vérifier si le centre du bloc div*div est walkable
                int sampleX = Mathf.Clamp(x * div + div / 2, 0, trueTerrW - 1);
                int sampleY = Mathf.Clamp(y * div + div / 2, 0, trueTerrH - 1);
                walkableNative[y * terrW + x] = wGrid[sampleX, sampleY];
            }
        }

        var job = new DijkstraJob
        {
            W = terrW,
            H = terrH,
            targetX = tx,
            targetY = ty,
            simW = simW,
            simH = simH,
            walkable = walkableNative,
            dist = new NativeArray<float>(terrW * terrH, Allocator.Persistent, NativeArrayOptions.UninitializedMemory),
            parent = new NativeArray<int>(terrW * terrH, Allocator.Persistent, NativeArrayOptions.UninitializedMemory),
            heap = new NativeArray<DijkstraNode>(terrW * terrH, Allocator.Persistent, NativeArrayOptions.UninitializedMemory),
            outputSimSlice = new NativeArray<Vector2>(simW * simH, Allocator.Persistent, NativeArrayOptions.UninitializedMemory)
        };

        JobHandle handle = job.Schedule();

        pendingJobs.Enqueue(new PendingFlowJob
        {
            waypointIndex = wi,
            handle = handle,
            walkable = job.walkable,
            dist = job.dist,
            parent = job.parent,
            heap = job.heap,
            outputSimSlice = job.outputSimSlice
        });
    }

    private void UploadToGPU()
    {
        SlimeMapRenderer.Instance.SetFlowFields(flowFieldTexture);
        SlimeMapRenderer.Instance.SetWaypoints(waypointList.ToArray());
    }

    // ── Burst Dijkstra Flow Field ────────────────────────────────────

    [BurstCompile]
    private struct DijkstraJob : IJob
    {
        public int W;
        public int H;
        public int targetX;
        public int targetY;
        public int simW;
        public int simH;

        [ReadOnly] public NativeArray<bool> walkable;
        
        // Scratchpads from main thread (prevents Temp allocation overflow)
        public NativeArray<float> dist;
        public NativeArray<int> parent;
        public NativeArray<DijkstraNode> heap;

        // Output
        public NativeArray<Vector2> outputSimSlice;

        public void Execute()
        {
            for (int i = 0; i < dist.Length; i++) { dist[i] = float.MaxValue; parent[i] = -1; }

            int targetIdx = targetY * W + targetX;
            dist[targetIdx] = 0f;
            parent[targetIdx] = targetIdx;

            int heapCount = 1;
            heap[0] = new DijkstraNode { cost = 0f, idx = targetIdx };

            NativeArray<int> dx = new NativeArray<int>(8, Allocator.Temp);
            dx[0] = 1; dx[1] = -1; dx[2] = 0; dx[3] = 0; dx[4] = 1; dx[5] = -1; dx[6] = 1; dx[7] = -1;

            NativeArray<int> dy = new NativeArray<int>(8, Allocator.Temp);
            dy[0] = 0; dy[1] = 0; dy[2] = 1; dy[3] = -1; dy[4] = 1; dy[5] = -1; dy[6] = -1; dy[7] = 1;

            NativeArray<float> dcost = new NativeArray<float>(8, Allocator.Temp);
            dcost[0] = 1f; dcost[1] = 1f; dcost[2] = 1f; dcost[3] = 1f; 
            dcost[4] = 1.41421356f; dcost[5] = 1.41421356f; dcost[6] = 1.41421356f; dcost[7] = 1.41421356f;

            while (heapCount > 0)
            {
                var cur = heap[0];
                heapCount--;
                heap[0] = heap[heapCount];
                int idx = 0;
                while (true)
                {
                    int s = idx, l = 2 * idx + 1, r = 2 * idx + 2;
                    if (l < heapCount && heap[l].cost < heap[s].cost) s = l;
                    if (r < heapCount && heap[r].cost < heap[s].cost) s = r;
                    if (s == idx) break;
                    var tmp = heap[idx]; heap[idx] = heap[s]; heap[s] = tmp;
                    idx = s;
                }

                if (cur.cost > dist[cur.idx]) continue;

                int cx = cur.idx % W, cy = cur.idx / W;
                for (int d = 0; d < 8; d++)
                {
                    int nx = cx + dx[d], ny = cy + dy[d];
                    if (nx < 0 || nx >= W || ny < 0 || ny >= H) continue;
                    
                    int ni = ny * W + nx;
                    if (!walkable[ni]) continue;

                    float newCost = cur.cost + dcost[d];
                    if (newCost >= dist[ni]) continue;

                    dist[ni] = newCost;
                    parent[ni] = cur.idx;

                    var n = new DijkstraNode { cost = newCost, idx = ni };
                    heap[heapCount] = n;
                    int i = heapCount;
                    heapCount++;
                    while (i > 0)
                    {
                        int p = (i - 1) / 2;
                        if (heap[p].cost <= heap[i].cost) break;
                        var tmp = heap[p]; heap[p] = heap[i]; heap[i] = tmp;
                        i = p;
                    }
                }
            }
            
            dx.Dispose(); dy.Dispose(); dcost.Dispose();

            // Direct mapping to sim slice (Nearest Neighbour)
            for (int sy = 0; sy < simH; sy++)
            {
                for (int sx = 0; sx < simW; sx++)
                {
                    int terrX = (int)(sx * (float)W / simW);
                    if (terrX >= W) terrX = W - 1; else if (terrX < 0) terrX = 0;
                    
                    int terrY = (int)(sy * (float)H / simH);
                    if (terrY >= H) terrY = H - 1; else if (terrY < 0) terrY = 0;

                    int terrIdx = terrY * W + terrX;
                    int pIdx = parent[terrIdx];

                    if (pIdx == -1) 
                    { 
                        outputSimSlice[sy * simW + sx] = Vector2.zero; 
                    }
                    else 
                    {
                        int px = pIdx % W;
                        int py = pIdx / W;
                        if (px == terrX && py == terrY) 
                        {
                            outputSimSlice[sy * simW + sx] = Vector2.zero;
                        }
                        else
                        {
                            float dirX = px - terrX, dirY = py - terrY;
                            float len = Mathf.Sqrt(dirX * dirX + dirY * dirY);
                            if (len == 0f)
                                outputSimSlice[sy * simW + sx] = Vector2.zero;
                            else
                                outputSimSlice[sy * simW + sx] = new Vector2(dirX / len, dirY / len);
                        }
                    }
                }
            }
        }
    }

    private void OnDestroy()
    {
        if (flowFieldTexture != null) Destroy(flowFieldTexture);

        // Abort and cleanup pending jobs so we don't leak NativeArrays
        while (pendingJobs.Count > 0)
        {
            var pJob = pendingJobs.Dequeue();
            pJob.handle.Complete();
            pJob.outputSimSlice.Dispose();
            pJob.heap.Dispose();
            pJob.parent.Dispose();
            pJob.dist.Dispose();
            pJob.walkable.Dispose();
        }
    }
}
