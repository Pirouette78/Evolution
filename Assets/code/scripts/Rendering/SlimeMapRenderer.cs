using UnityEngine;
using System.Collections;
using Unity.Entities;

/// <summary>
/// Core GPU simulation manager. Owns the agent buffer and drives
/// UpdateAgents → DrawMap → Diffuse every frame entirely on the GPU.
/// No ECS dependency for movement.
/// </summary>
public enum SpeciesType { Plante, Animal, Champignon, Insecte, Bacterie, Algue, GlobuleRouge, GlobuleBlanc, Virus, Plaquette }

[System.Serializable]
public struct WaypointData
{
    public Vector2 position;     // pixel space (0..map size)
    public int     type;         // 0 = Source, 1 = Destination
    public int     speciesIndex; // owner species slot
}

public class SlimeMapRenderer : MonoBehaviour
{
    public static SlimeMapRenderer Instance { get; private set; }

    public const int MaxSlots = 32;

    // ── Inspector ──────────────────────────────────────────────────
    [Header("Compute Shader (assign in Inspector)")]
    public ComputeShader SlimeShader;

    [Header("Simulation Settings")]
    public int Width = 512;
    public int Height = 512;
    [Range(1, 8)]       public int   StepsPerFrame     = 1;
    
    [Header("Initial Spawn")]
    public int InitialAgentCount = 5000;

    [Header("Output")]
    public MeshRenderer DisplayTarget;

    // ── Public state ────────────────────────────────────────────────
    public RenderTexture TrailMap    { get; private set; }
    public RenderTexture DiffusedMap { get; private set; }
    public RenderTexture DisplayMap  { get; private set; }
    public RenderTexture AgentMap    { get; private set; }
    public ComputeBuffer AgentBuffer => agentBuffer;
    public bool IsReady              => isInitialized;
    public int  AgentCount           => currentAgentCount;

    public SpeciesSettings[] speciesSettings = new SpeciesSettings[MaxSlots];
    public uint[]   AliveSpeciesCounts = new uint[MaxSlots];
    public SpeciesType[] speciesTypes  = new SpeciesType[MaxSlots];
    /// <summary>ID d'espèce pour chaque slot GPU. Géré par PlayerLibrary.</summary>
    public string[] speciesIds         = new string[MaxSlots];
    /// <summary>Nombre de slots GPU actifs (configuré par PlayerLibrary).</summary>
    public int numActiveSlots = 16;
    /// <summary>Couleur de rendu par slot GPU (envoyée au shader via slotColorsBuffer).</summary>
    public Vector4[] slotColors = new Vector4[MaxSlots];

    // ── Private ─────────────────────────────────────────────────────
    private ComputeBuffer agentBuffer;
    private int maxAgents = 600000;
    private int currentAgentCount = 0;
    private int nextSpawnIndex = 0;   // index circulaire pour réutiliser les slots morts
    private bool isInitialized = false;
    private bool initialSpawnDone = false;
    private int playerVisibilityMask = ~0; // all 32 slots visible by default

    private int updateKernel, drawKernel, diffuseKernel, clearKernel, composeKernel, clearCountsKernel, countAliveKernel, clearDeliveryKernel, clearAgentMapKernel;
    public  int[] DeliveryCounts = new int[MaxSlots];
    private ComputeBuffer waypointStockBuffer;
    private ComputeBuffer deliveryCounterBuffer;

    // Cached terrain data for CPU-side spawn validation
    private float[,] heightMapCache;
    private float waterThresholdCache;

    [System.Serializable]
    public struct SpeciesSettings
    {
        public float moveSpeed;
        public float turnSpeed;
        public float sensorAngleRad;
        public float sensorOffsetDst;
        public int   sensorSize;
        public float maxAge;
        public float trailWeight;
        public float decayRate;
        public float diffuseRate;

        public float warDamageRate;         // damage rate from enemy trails
        public int   warMask;               // bitmask of enemy species
        public int   behaviorType;          // 0=default,1=Bacterie,2=GlobRouge,3=GlobBlanc,4=Virus,5=Plaquette
        public float energyConsumptionRate; // energy drained per second (Bacterie)
        public float energyReward;          // energy released via trail (GlobuleRouge)
        public float startingEnergy;        // initial agent.hunger value at spawn
        public float arrivalRadius;         // distance seuil pour considérer un waypoint "atteint"
        public float loadingTime;           // time to wait at Source waypoint
        public float unloadingTime;         // time to wait at Destination waypoint
        public float waitForStock;          // 1 = attend le stock disponible avant de partir
        public float trailErasePower;       // unités de traînée ennemie effacées/sec à la position de l'agent
        public float maxHealth;             // Points de vie max combat (indépendant de maxAge) → 84 bytes total
    }

    public enum DiplomaticState { Neutral, Ally, Peace, War }
    private ComputeBuffer speciesSettingsBuffer;
    private ComputeBuffer interactionMatrixBuffer;
    public  float[]       interactionMatrixData      = new float[MaxSlots * MaxSlots];
    private ComputeBuffer agentInteractionMatrixBuffer;
    public  float[]       agentInteractionMatrixData = new float[MaxSlots * MaxSlots];
    private ComputeBuffer speciesCountsBuffer;
    private ComputeBuffer slotColorsBuffer;
    private ComputeBuffer waypointBuffer;
    private ComputeBuffer smoothedPathBuffer;
    private ComputeBuffer smoothedPathMetaBuffer;
    private Texture2DArray flowFieldMap;
    private bool flowFieldMapIsOwned = false;

    struct Agent
    {
        public Vector2 position;     // 8
        public float   angle;        // 4
        public int     speciesIndex; // 4
        public float   age;          // 4 : vieillissement naturel
        public float   health;       // 4 : points de vie combat
        public float   hunger;       // 4
        public int     navState;     // 4 : 0=cherche Source, 1→Source, 2=chargement, 3→Dest, 4=déchargement
        public int     targetWp;     // 4 : index waypoint cible (-1=aucun)
        public int     pathIdx;      // 4 : index chemin lissé (-1=flow field)
        public int     cargo;        // 4 : 0=vide, 1=chargé
    } // total 44 bytes

    // ================== Unity lifecycle ============================

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        // Réinitialise les tableaux à la bonne taille (les données sérialisées Unity
        // peuvent avoir l'ancienne taille 6 si la scène n'a pas été re-sauvegardée)
        speciesSettings        = new SpeciesSettings[MaxSlots];
        speciesIds             = new string[MaxSlots];
        speciesTypes           = new SpeciesType[MaxSlots];
        slotColors             = new Vector4[MaxSlots];
        AliveSpeciesCounts     = new uint[MaxSlots];
        interactionMatrixData      = new float[MaxSlots * MaxSlots];
        agentInteractionMatrixData = new float[MaxSlots * MaxSlots];
        DeliveryCounts         = new int[MaxSlots];

        agentBuffer = new ComputeBuffer(maxAgents, sizeof(float)*6 + sizeof(int)*5);
        // struct size (44 bytes) = 6 floats (24) + 5 ints (20)
        speciesSettingsBuffer = new ComputeBuffer(MaxSlots, 84);
        speciesCountsBuffer   = new ComputeBuffer(MaxSlots, sizeof(uint));
        slotColorsBuffer      = new ComputeBuffer(MaxSlots, sizeof(float) * 4);
        waypointStockBuffer   = new ComputeBuffer(MaxSlots, sizeof(float));
        deliveryCounterBuffer = new ComputeBuffer(MaxSlots, sizeof(int));
        waypointStockBuffer.SetData(new float[MaxSlots]);
        deliveryCounterBuffer.SetData(new int[MaxSlots]);

        // Default colors: spread across hue for all slots
        for (int i = 0; i < MaxSlots; i++) {
            float hue = i / (float)MaxSlots;
            Color c = Color.HSVToRGB(hue, 1f, 1f);
            slotColors[i] = new Vector4(c.r, c.g, c.b, 1f);
        }

        for (int i = 0; i < MaxSlots; i++) {
            speciesSettings[i] = new SpeciesSettings {
                moveSpeed = 75f,
                turnSpeed = 10f,
                sensorAngleRad = 30f * Mathf.Deg2Rad,
                sensorOffsetDst = 20f,
                sensorSize = 2,
                maxAge = 100f,
                trailWeight = 5f,
                decayRate = 1f,
                diffuseRate = 2f,
                warDamageRate = 1f
            };
            speciesIds[i] = speciesTypes[i].ToString().ToLowerInvariant();
            interactionMatrixData[i * MaxSlots + i] = 1f; // chaque espèce suit sa propre traînée par défaut
        }

        if (DisplayTarget == null) DisplayTarget = GetComponent<MeshRenderer>();
    }

    private void Start()
    {
        // Try to load shader from Resources if not assigned in Inspector
        if (SlimeShader == null)
        {
            SlimeShader = Resources.Load<ComputeShader>("SlimeTrailRender");
#if UNITY_EDITOR
            if (SlimeShader == null)
                SlimeShader = UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>(
                    "Assets/Art/Shaders/SlimeTrailRender.compute");
#endif
        }

        if (SlimeShader == null)
        {
            Debug.LogError("[RENDERER] SlimeTrailRender.compute not found! Assign it in the Inspector.");
            return;
        }

        InitTextures();
        InitKernels();

        isInitialized = true;
        Debug.Log("[RENDERER] Initialized successfully.");

        // Wait for terrain then auto-spawn
        StartCoroutine(WaitForTerrainAndSpawn());
    }

    private void InitTextures()
    {
        TrailMap    = CreateRTArray("TrailMap");
        DiffusedMap = CreateRTArray("DiffusedMap");
        AgentMap    = CreateRTArray("AgentMap");

        DisplayMap = new RenderTexture(Width, Height, 0, RenderTextureFormat.ARGBFloat)
        { 
            enableRandomWrite = true, 
            name = "DisplayMap",
            filterMode = FilterMode.Bilinear // Apply bilinear filtering for smooth rendering
        };
        DisplayMap.Create();

        RenderTexture CreateRTArray(string name)
        {
            var rt = new RenderTexture(Width, Height, 0, UnityEngine.Experimental.Rendering.GraphicsFormat.R16_SFloat)
            { 
                dimension = UnityEngine.Rendering.TextureDimension.Tex2DArray,
                volumeDepth = MaxSlots,
                enableRandomWrite = true, 
                name = name
            };
            rt.Create();
            return rt;
        }
    }

    private void InitKernels()
    {
        updateKernel  = SlimeShader.FindKernel("UpdateAgents");
        drawKernel    = SlimeShader.FindKernel("DrawMap");
        diffuseKernel = SlimeShader.FindKernel("Diffuse");
        clearKernel   = SlimeShader.FindKernel("ResetMap");
        composeKernel = SlimeShader.FindKernel("ComposeDisplay");
        clearCountsKernel    = SlimeShader.FindKernel("ClearCounts");
        countAliveKernel     = SlimeShader.FindKernel("CountAlive");
        clearDeliveryKernel  = SlimeShader.FindKernel("ClearDeliveryCounters");
        clearAgentMapKernel  = SlimeShader.FindKernel("ClearAgentMap");

        // Bind textures (persistent across frames)
        SlimeShader.SetTexture(updateKernel,       "TrailMap",         TrailMap);
        SlimeShader.SetTexture(updateKernel,       "AgentMap",         AgentMap);
        SlimeShader.SetTexture(drawKernel,         "TrailMap",         TrailMap);
        SlimeShader.SetTexture(drawKernel,         "AgentMap",         AgentMap);
        SlimeShader.SetTexture(diffuseKernel,      "TrailMap",         TrailMap);
        SlimeShader.SetTexture(diffuseKernel,      "DiffusedTrailMap", DiffusedMap);
        SlimeShader.SetTexture(clearKernel,        "TrailMap",         TrailMap);
        SlimeShader.SetTexture(clearKernel,        "DiffusedTrailMap", DiffusedMap);
        SlimeShader.SetTexture(clearKernel,        "AgentMap",         AgentMap);
        SlimeShader.SetTexture(clearAgentMapKernel,"AgentMap",         AgentMap);
        SlimeShader.SetTexture(composeKernel, "DiffusedTrailMap", DiffusedMap);
        SlimeShader.SetTexture(composeKernel, "DisplayMap",       DisplayMap);
        SlimeShader.SetTexture(composeKernel, "AgentMap",         AgentMap);
        SlimeShader.SetTexture(composeKernel, "DisplayMap",       DisplayMap);
        SlimeShader.SetInt("width",  Width);
        SlimeShader.SetInt("height", Height);

        SlimeShader.SetBuffer(updateKernel, "speciesSettings", speciesSettingsBuffer);
        SlimeShader.SetBuffer(drawKernel, "speciesSettings", speciesSettingsBuffer);
        SlimeShader.SetBuffer(diffuseKernel, "speciesSettings", speciesSettingsBuffer);
        SlimeShader.SetBuffer(countAliveKernel, "speciesSettings", speciesSettingsBuffer);
        SlimeShader.SetBuffer(clearCountsKernel, "speciesCounts", speciesCountsBuffer);
        SlimeShader.SetBuffer(countAliveKernel, "speciesCounts", speciesCountsBuffer);
        slotColorsBuffer.SetData(slotColors);
        SlimeShader.SetBuffer(composeKernel, "slotColors", slotColorsBuffer);
        SlimeShader.SetBuffer(updateKernel, "waypointStockIn",  waypointStockBuffer);
        SlimeShader.SetBuffer(updateKernel, "deliveryCounters", deliveryCounterBuffer);
        SlimeShader.SetBuffer(clearDeliveryKernel, "deliveryCounters", deliveryCounterBuffer);

        // Interaction matrix (MaxSlots×MaxSlots) pour le Particle Life sur traînées
        interactionMatrixBuffer = new ComputeBuffer(MaxSlots * MaxSlots, sizeof(float));
        interactionMatrixBuffer.SetData(interactionMatrixData);
        SlimeShader.SetBuffer(updateKernel, "interactionMatrix", interactionMatrixBuffer);

        // Agent interaction matrix (MaxSlots×MaxSlots) pour le sensing direct de présence d'agents
        agentInteractionMatrixBuffer = new ComputeBuffer(MaxSlots * MaxSlots, sizeof(float));
        agentInteractionMatrixBuffer.SetData(agentInteractionMatrixData);
        SlimeShader.SetBuffer(updateKernel, "agentInteractionMatrix", agentInteractionMatrixBuffer);

        // Waypoints buffer (max MaxSlots × 16 bytes)
        waypointBuffer = new ComputeBuffer(MaxSlots, 16);
        waypointBuffer.SetData(new WaypointData[MaxSlots]);
        SlimeShader.SetBuffer(updateKernel, "waypoints", waypointBuffer);
        SlimeShader.SetInt("numWaypoints", 0);

        // Smooth path buffers (string pulling) — indexés par waypoint, pas par espèce
        smoothedPathBuffer = new ComputeBuffer(MaxSlots * 64, sizeof(float) * 2);
        smoothedPathBuffer.SetData(new Vector2[MaxSlots * 64]);
        SlimeShader.SetBuffer(updateKernel, "smoothedPaths", smoothedPathBuffer);

        smoothedPathMetaBuffer = new ComputeBuffer(MaxSlots, sizeof(int) * 2);
        smoothedPathMetaBuffer.SetData(new Vector2Int[MaxSlots]);
        SlimeShader.SetBuffer(updateKernel, "smoothedPathMeta", smoothedPathMetaBuffer);

        // Flow field texture array (MaxSlots slices, one per waypoint, RG = direction)
        flowFieldMap = new Texture2DArray(Width, Height, MaxSlots, TextureFormat.RGHalf, false)
        {
            filterMode = FilterMode.Point,
            wrapMode   = TextureWrapMode.Clamp
        };
        Color[] emptySlice = new Color[Width * Height];
        for (int s = 0; s < MaxSlots; s++) flowFieldMap.SetPixels(emptySlice, s);
        flowFieldMap.Apply();
        flowFieldMapIsOwned = true;
        SlimeShader.SetTexture(updateKernel, "FlowFieldMap", flowFieldMap);

        // Fallback for TerrainWalkabilityMap in case terrain isn't ready when UpdateAgents runs
        SlimeShader.SetTexture(updateKernel, "TerrainWalkabilityMap", Texture2D.whiteTexture);
        SlimeShader.SetInt("useTerrainCollision", 0);

        // Clear to black
        int gx = Mathf.CeilToInt(Width  / 8f);
        int gy = Mathf.CeilToInt(Height / 8f);
        SlimeShader.Dispatch(clearKernel, gx, gy, 1);

        // Bind output to display quad
        if (DisplayTarget != null)
        {
            DisplayTarget.sharedMaterial.mainTexture = DisplayMap;
            if (DisplayTarget.sharedMaterial.HasProperty("_BaseMap"))
                DisplayTarget.sharedMaterial.SetTexture("_BaseMap", DisplayMap);
        }
    }

    // ── Wait until terrain texture is ready, then spawn ─────────────
    private IEnumerator WaitForTerrainAndSpawn()
    {
        // Wait up to 10 seconds for terrain
        float waited = 0f;
        while (waited < 10f)
        {
            var terrain = TerrainMapRenderer.Instance;
            if (terrain != null && terrain.GetTexture() != null)
            {
                CacheTerrainData(terrain);
                break;
            }
            waited += Time.deltaTime;
            yield return null;
        }

        if (waited >= 10f)
            Debug.LogWarning("[RENDERER] Terrain not found after 10s — spawning without terrain check.");

        AddAgents(InitialAgentCount);
        initialSpawnDone = true;
    }

    private void CacheTerrainData(TerrainMapRenderer terrain)
    {
        heightMapCache      = terrain.HeightMap;
        waterThresholdCache = terrain.WaterThreshold;

        // Bind the BINARY walkability texture (white=land, black=water)
        var walkTex = terrain.GetWalkabilityTexture();
        if (walkTex != null)
        {
            SlimeShader.SetTexture(updateKernel, "TerrainWalkabilityMap", walkTex);
            SlimeShader.SetInt("useTerrainCollision", 1);
            Debug.Log("[RENDERER] Binary walkability texture bound to shader.");
        }
        else
        {
            SlimeShader.SetInt("useTerrainCollision", 0);
            Debug.LogWarning("[RENDERER] Walkability texture not ready — terrain collision disabled.");
        }
    }

    // ================== Public API =================================

    public void SetPlayerVisibility(int index, bool isVisible)
    {
        if (index >= 0 && index < MaxSlots)
        {
            if (isVisible) playerVisibilityMask |= (1 << index);
            else           playerVisibilityMask &= ~(1 << index);
        }
    }

    public bool GetPlayerVisibility(int index)
    {
        if (index >= 0 && index < MaxSlots)
            return (playerVisibilityMask & (1 << index)) != 0;
        return false;
    }

    /// <summary>Définit la couleur de rendu d'un slot GPU et l'envoie au shader.</summary>
    public void SetSlotColor(int slot, Vector4 color)
    {
        if (slot < 0 || slot >= MaxSlots) return;
        slotColors[slot] = color;
        slotColorsBuffer?.SetData(slotColors);
    }

    public static SpeciesSettings GetPreset(SpeciesType type)
    {
        switch (type)
        {
            case SpeciesType.Plante:     return new SpeciesSettings { moveSpeed=20,  turnSpeed=5,  sensorAngleRad=45*Mathf.Deg2Rad, sensorOffsetDst=10, sensorSize=3, maxAge=200, trailWeight=8,  decayRate=0.5f, diffuseRate=3f,   warDamageRate=0.5f };
            case SpeciesType.Animal:     return new SpeciesSettings { moveSpeed=100, turnSpeed=15, sensorAngleRad=30*Mathf.Deg2Rad, sensorOffsetDst=25, sensorSize=2, maxAge=80,  trailWeight=3,  decayRate=1.5f, diffuseRate=1f,   warDamageRate=2f   };
            case SpeciesType.Champignon: return new SpeciesSettings { moveSpeed=15,  turnSpeed=3,  sensorAngleRad=60*Mathf.Deg2Rad, sensorOffsetDst=8,  sensorSize=4, maxAge=300, trailWeight=10, decayRate=0.3f, diffuseRate=5f,   warDamageRate=0.3f };
            case SpeciesType.Insecte:    return new SpeciesSettings { moveSpeed=150, turnSpeed=25, sensorAngleRad=20*Mathf.Deg2Rad, sensorOffsetDst=30, sensorSize=2, maxAge=40,  trailWeight=2,  decayRate=2f,   diffuseRate=0.5f, warDamageRate=3f   };
            case SpeciesType.Bacterie:   return new SpeciesSettings { moveSpeed=50,  turnSpeed=20, sensorAngleRad=45*Mathf.Deg2Rad, sensorOffsetDst=15, sensorSize=2, maxAge=30,  trailWeight=6,  decayRate=3f,   diffuseRate=4f,   warDamageRate=4f,   behaviorType=1, energyConsumptionRate=5f, startingEnergy=100f };
            case SpeciesType.Algue:        return new SpeciesSettings { moveSpeed=10,  turnSpeed=2,  sensorAngleRad=90*Mathf.Deg2Rad, sensorOffsetDst=5,  sensorSize=5, maxAge=500, trailWeight=15, decayRate=0.2f, diffuseRate=6f,   warDamageRate=0.1f, behaviorType=0 };
            case SpeciesType.GlobuleRouge: return new SpeciesSettings { moveSpeed=60,  turnSpeed=20, sensorAngleRad=30*Mathf.Deg2Rad, sensorOffsetDst=15, sensorSize=2, maxAge=400, trailWeight=8, decayRate=0.3f, diffuseRate=3f, warDamageRate=0.1f, behaviorType=2, energyReward=5f, arrivalRadius=20f, loadingTime=2f, unloadingTime=1f };
            case SpeciesType.GlobuleBlanc: return new SpeciesSettings { moveSpeed=120, turnSpeed=20, sensorAngleRad=30*Mathf.Deg2Rad, sensorOffsetDst=25, sensorSize=2, maxAge=60,  trailWeight=1,  decayRate=3f,   diffuseRate=0.5f, warDamageRate=3f,   behaviorType=3 };
            case SpeciesType.Virus:        return new SpeciesSettings { moveSpeed=80,  turnSpeed=30, sensorAngleRad=20*Mathf.Deg2Rad, sensorOffsetDst=20, sensorSize=2, maxAge=20,  trailWeight=1,  decayRate=5f,   diffuseRate=0.3f, warDamageRate=5f,   behaviorType=4 };
            case SpeciesType.Plaquette:    return new SpeciesSettings { moveSpeed=25,  turnSpeed=5,  sensorAngleRad=45*Mathf.Deg2Rad, sensorOffsetDst=8,  sensorSize=4, maxAge=300, trailWeight=20, decayRate=0.1f, diffuseRate=6f,   warDamageRate=0f,   behaviorType=0 };
            default:                       return new SpeciesSettings { moveSpeed=75,  turnSpeed=10, sensorAngleRad=30*Mathf.Deg2Rad, sensorOffsetDst=20, sensorSize=2, maxAge=100, trailWeight=5,  decayRate=1f,   diffuseRate=2f,   warDamageRate=1f,   behaviorType=0 };
        }
    }

    public void SetSpeciesType(int index, SpeciesType type)
    {
        if (index < 0 || index >= MaxSlots) return;
        speciesTypes[index] = type;
        string id = type.ToString().ToLowerInvariant();
        speciesIds[index] = id; // toujours synchronisé avec speciesTypes

        // Paramètres : SpeciesLibrary (JSON) en priorité, sinon preset hardcodé
        SpeciesDefinition jsonDef = SpeciesLibrary.Instance?.Get(id);
        SpeciesSettings settings = jsonDef != null ? jsonDef.ToSpeciesSettings() : GetPreset(type);
        settings.warMask = speciesSettings[index].warMask; // préserver l'état de guerre
        speciesSettings[index] = settings;
    }

    public void SetWar(int a, int b, bool atWar)
    {
        if (a < 0 || a >= MaxSlots || b < 0 || b >= MaxSlots || a == b) return;
        var sa = speciesSettings[a];
        var sb = speciesSettings[b];
        if (atWar) { sa.warMask |= (1 << b); sb.warMask |= (1 << a); }
        else        { sa.warMask &= ~(1 << b); sb.warMask &= ~(1 << a); }
        speciesSettings[a] = sa;
        speciesSettings[b] = sb;
    }

    public bool IsAtWar(int a, int b)
    {
        if (a < 0 || a >= MaxSlots || b < 0 || b >= MaxSlots) return false;
        return (speciesSettings[a].warMask & (1 << b)) != 0;
    }

    public DiplomaticState GetDiplomaticState(int slotA, int slotB)
    {
        if (slotA < 0 || slotA >= MaxSlots || slotB < 0 || slotB >= MaxSlots) return DiplomaticState.Neutral;
        if (IsAtWar(slotA, slotB)) return DiplomaticState.War;
        float w = interactionMatrixData[slotA * MaxSlots + slotB];
        if (w >  0.01f) return DiplomaticState.Ally;
        if (w < -0.01f) return DiplomaticState.Peace;
        return DiplomaticState.Neutral;
    }

    public void SetInteraction(int slotA, int slotB, float weight)
    {
        if (slotA < 0 || slotA >= MaxSlots || slotB < 0 || slotB >= MaxSlots) return;
        interactionMatrixData[slotA * MaxSlots + slotB] = weight;
    }

    /// <summary>Configure l'état diplomatique entre deux slots GPU et met à jour la matrice d'interaction.</summary>
    public void SetDiplomaticState(int slotA, int slotB, DiplomaticState state)
    {
        if (slotA < 0 || slotA >= MaxSlots || slotB < 0 || slotB >= MaxSlots || slotA == slotB) return;

        float ab = 0f, ba = 0f;
        switch (state)
        {
            case DiplomaticState.Ally:  ab =  0.5f; ba =  0.5f; break;
            case DiplomaticState.Peace: ab = -1.5f; ba = -1.5f; break;
            case DiplomaticState.War:   ab =  2.5f; ba =  2.5f; break;
        }
        SetInteraction(slotA, slotB, ab);
        SetInteraction(slotB, slotA, ba);

        // warMask : mis à jour pour que warDamageRate et trailErasePower fonctionnent correctement
        bool atWar = (state == DiplomaticState.War);
        var sa = speciesSettings[slotA];
        var sb = speciesSettings[slotB];
        if (atWar) { sa.warMask |= (1 << slotB); sb.warMask |= (1 << slotA); }
        else       { sa.warMask &= ~(1 << slotB); sb.warMask &= ~(1 << slotA); }
        speciesSettings[slotA] = sa;
        speciesSettings[slotB] = sb;
    }

    /// <summary>Change uniquement la direction fromSlot→toSlot, sans toucher à l'inverse.</summary>
    public void SetInteractionOneWay(int fromSlot, int toSlot, DiplomaticState state)
    {
        if (fromSlot < 0 || fromSlot >= MaxSlots || toSlot < 0 || toSlot >= MaxSlots) return;
        float weight = 0f;
        switch (state) {
            case DiplomaticState.Ally:  weight =  0.5f; break;
            case DiplomaticState.Peace: weight = -1.5f; break;
            case DiplomaticState.War:   weight =  2.5f; break;
        }
        SetInteraction(fromSlot, toSlot, weight);
        if (fromSlot == toSlot) return; // diagonal : pas de warMask sur soi-même
        var s = speciesSettings[toSlot];                               // la CIBLE reçoit le warMask
        if (state == DiplomaticState.War) s.warMask |=  (1 << fromSlot); // cible vulnérable à l'attaquant
        else                               s.warMask &= ~(1 << fromSlot);
        speciesSettings[toSlot] = s;
    }

    // ── API data-driven (DiplomacyLibrary) ───────────────────────────

    /// <summary>Setter asymétrique prenant un DiplomacyLevelDefinition (data-driven).</summary>
    public void SetInteractionOneWay(int fromSlot, int toSlot, DiplomacyLevelDefinition level)
    {
        if (fromSlot < 0 || fromSlot >= MaxSlots || toSlot < 0 || toSlot >= MaxSlots || level == null) return;
        SetInteraction(fromSlot, toSlot, level.value);
        // Agent sensing matrix
        if (fromSlot != toSlot)
            agentInteractionMatrixData[fromSlot * MaxSlots + toSlot] = level.agentSenseWeight;
        if (fromSlot == toSlot) return;
        var s = speciesSettings[toSlot];                    // la CIBLE reçoit le warMask
        if (level.isWar) s.warMask |=  (1 << fromSlot);    // cible vulnérable à l'attaquant
        else             s.warMask &= ~(1 << fromSlot);
        speciesSettings[toSlot] = s;
    }

    /// <summary>Setter symétrique (pour init PlayerLibrary).</summary>
    public void SetInteractionSymmetric(int slotA, int slotB, DiplomacyLevelDefinition level)
    {
        if (slotA < 0 || slotA >= MaxSlots || slotB < 0 || slotB >= MaxSlots || slotA == slotB || level == null) return;
        SetInteraction(slotA, slotB, level.value);
        SetInteraction(slotB, slotA, level.value);
        agentInteractionMatrixData[slotA * MaxSlots + slotB] = level.agentSenseWeight;
        agentInteractionMatrixData[slotB * MaxSlots + slotA] = level.agentSenseWeight;
        var sa = speciesSettings[slotA];
        var sb = speciesSettings[slotB];
        if (level.isWar) { sa.warMask |= (1 << slotB); sb.warMask |= (1 << slotA); }
        else             { sa.warMask &=~(1 << slotB); sb.warMask &=~(1 << slotA); }
        speciesSettings[slotA] = sa;
        speciesSettings[slotB] = sb;
    }

    /// <summary>Retourne le niveau diplomatique correspondant à la direction slotA→slotB.</summary>
    public DiplomacyLevelDefinition GetDiplomacyLevel(int slotA, int slotB)
    {
        var lib = DiplomacyLibrary.Instance;
        if (lib == null) return null;
        float w = (slotA >= 0 && slotB >= 0) ? interactionMatrixData[slotA * MaxSlots + slotB] : 0f;
        return lib.GetByValue(w);
    }

    public void SetWaypoints(WaypointData[] data)
    {
        if (waypointBuffer == null) return;
        int count = Mathf.Min(data.Length, MaxSlots);
        WaypointData[] padded = new WaypointData[MaxSlots];
        System.Array.Copy(data, padded, count);
        waypointBuffer.SetData(padded);
        SlimeShader.SetInt("numWaypoints", count);
    }

    public void SetFlowFields(Texture2DArray tex)
    {
        if (flowFieldMapIsOwned && flowFieldMap != null) Destroy(flowFieldMap);
        flowFieldMap = tex;
        flowFieldMapIsOwned = false;
        SlimeShader.SetTexture(updateKernel, "FlowFieldMap", flowFieldMap);
    }

    public void SetSmoothedPaths(Vector2[] flatPaths, int[] starts, int[] counts)
    {
        if (smoothedPathBuffer == null || smoothedPathMetaBuffer == null) return;
        smoothedPathBuffer.SetData(flatPaths);
        var meta = new Vector2Int[MaxSlots];
        for (int i = 0; i < MaxSlots; i++) meta[i] = new Vector2Int(starts[i], counts[i]);
        smoothedPathMetaBuffer.SetData(meta);
    }

    public void AddAgentsAt(int count, int speciesIndex, Vector2 position)
    {
        if (!isInitialized) return;
        if (count <= 0) return;

        Agent[] newAgents = new Agent[count];
        for (int i = 0; i < count; i++)
        {
            float r     = Random.value * 5f;
            float theta = Random.value * Mathf.PI * 2f;
            Vector2 pos = position + new Vector2(Mathf.Cos(theta) * r, Mathf.Sin(theta) * r);
            pos.x = Mathf.Clamp(pos.x, 1f, Width  - 2f);
            pos.y = Mathf.Clamp(pos.y, 1f, Height - 2f);
            newAgents[i] = new Agent
            {
                position     = pos,
                angle        = Random.value * Mathf.PI * 2f,
                speciesIndex = speciesIndex,
                age          = 0f,
                health       = speciesSettings[speciesIndex].maxHealth,
                hunger       = speciesSettings[speciesIndex].startingEnergy,
            };
        }
        int writePos;
        if (currentAgentCount + count <= maxAgents)
        {
            writePos = currentAgentCount;
            currentAgentCount += count;
        }
        else
        {
            // Buffer plein : réutilisation circulaire des slots morts
            currentAgentCount = maxAgents;
            writePos = nextSpawnIndex;
            nextSpawnIndex = (nextSpawnIndex + count) % maxAgents;
        }
        agentBuffer.SetData(newAgents, 0, writePos, count);
    }

    /// <summary>Append count new agents to the GPU buffer.</summary>
    public void AddAgents(int count, int forceSpecies = -1)
    {
        if (!isInitialized) { Debug.LogWarning("[RENDERER] Not initialized."); return; }
        if (count <= 0) return;

        // Build only the NEW agents on the CPU
        Agent[] newAgents = new Agent[count];
        for (int i = 0; i < count; i++)
        {
            Vector2 pos = GetSpawnPosition();
            float   angle = Random.value * Mathf.PI * 2f;
            int     pid   = forceSpecies >= 0 ? forceSpecies : Random.Range(0, Mathf.Max(1, numActiveSlots));

            newAgents[i] = new Agent
            {
                position    = pos,
                angle       = angle,
                speciesIndex= pid,
                age         = 0f,
                health      = speciesSettings[pid].maxHealth,
                hunger      = speciesSettings[pid].startingEnergy,
            };
        }

        // Upload only the new slice (no full-buffer stall)
        int writePos;
        if (currentAgentCount + count <= maxAgents)
        {
            writePos = currentAgentCount;
            currentAgentCount += count;
        }
        else
        {
            currentAgentCount = maxAgents;
            writePos = nextSpawnIndex;
            nextSpawnIndex = (nextSpawnIndex + count) % maxAgents;
        }
        agentBuffer.SetData(newAgents, 0, writePos, count);

        Debug.Log($"[RENDERER] Added {count} agents. Total: {currentAgentCount} (writePos={writePos})");
    }

    private Vector2 GetSpawnPosition()
    {
        if (heightMapCache == null)
            return new Vector2(Random.Range(5f, Width - 5f), Random.Range(5f, Height - 5f));

        for (int attempt = 0; attempt < 100; attempt++)
        {
            var p = new Vector2(Random.Range(5f, Width - 5f), Random.Range(5f, Height - 5f));
            int ix = Mathf.Clamp((int)p.x, 0, Width  - 1);
            int iy = Mathf.Clamp((int)p.y, 0, Height - 1);
            if (heightMapCache[ix, iy] >= waterThresholdCache) return p;
        }
        return new Vector2(Width / 2f, Height / 2f);
    }

    // ================== Simulation loop ============================

    private float GetScaledDeltaTime()
    {
        var world = World.DefaultGameObjectInjectionWorld;
        if (world == null) return Time.deltaTime;
        var em = world.EntityManager;
        var q  = em.CreateEntityQuery(typeof(GameTime));
        if (q.IsEmpty) { q.Dispose(); return Time.deltaTime; }
        float sdt = em.GetComponentData<GameTime>(q.GetSingletonEntity()).ScaledDeltaTime;
        q.Dispose();
        return sdt;
    }

    private int countFrameAccum = 0;
    private void Update()
    {
        try {
            if (!isInitialized || !initialSpawnDone) return;
            if (speciesSettingsBuffer == null || slotColorsBuffer == null || agentBuffer == null) return;

            // Re-bind terrain every frame in case it became available after initialization
            if (TerrainMapRenderer.Instance != null &&
                TerrainMapRenderer.Instance.GetTexture() != null &&
                heightMapCache == null)
            {
                CacheTerrainData(TerrainMapRenderer.Instance);
            }

            float dt = GetScaledDeltaTime();

            // Per-frame global uniforms
            SlimeShader.SetFloat("time",             Time.time);
            SlimeShader.SetFloat("deltaTime",        dt);
            SlimeShader.SetInt  ("numAgents",        currentAgentCount);
            
            // Push per-species settings and colors
            speciesSettingsBuffer.SetData(speciesSettings);
            slotColorsBuffer.SetData(slotColors);

            SlimeShader.SetInt("numActiveSlots",     numActiveSlots);
            SlimeShader.SetInt("playerVisibilityMask", playerVisibilityMask);

            int agentGroups  = Mathf.CeilToInt(currentAgentCount / 16f);
            int texGroupsX   = Mathf.CeilToInt(Width  / 8f);
            int texGroupsY   = Mathf.CeilToInt(Height / 8f);

            // Rebind persistent resources every frame (guards against shader hot-reload in editor)
            SlimeShader.SetBuffer(updateKernel,  "speciesSettings", speciesSettingsBuffer);
            SlimeShader.SetBuffer(drawKernel,    "speciesSettings", speciesSettingsBuffer);
            SlimeShader.SetBuffer(diffuseKernel, "speciesSettings", speciesSettingsBuffer);
            SlimeShader.SetTexture(composeKernel, "DiffusedTrailMap", DiffusedMap);
            SlimeShader.SetBuffer(composeKernel, "slotColors", slotColorsBuffer);
            SlimeShader.SetBuffer(updateKernel, "waypoints", waypointBuffer);
            SlimeShader.SetTexture(updateKernel, "FlowFieldMap", flowFieldMap);
            SlimeShader.SetBuffer(updateKernel, "smoothedPaths",    smoothedPathBuffer);
            SlimeShader.SetBuffer(updateKernel, "smoothedPathMeta", smoothedPathMetaBuffer);
            interactionMatrixBuffer.SetData(interactionMatrixData);
            SlimeShader.SetBuffer(updateKernel, "interactionMatrix", interactionMatrixBuffer);
            agentInteractionMatrixBuffer.SetData(agentInteractionMatrixData);
            SlimeShader.SetBuffer(updateKernel, "agentInteractionMatrix", agentInteractionMatrixBuffer);

            for (int step = 0; step < StepsPerFrame; step++)
            {
                // 0. Clear AgentMap (présence réelle d'agents du step précédent)
                SlimeShader.Dispatch(clearAgentMapKernel, texGroupsX, texGroupsY, 1);

                // 1. Move agents (lit TrailMap + AgentMap du step précédent)
                SlimeShader.SetBuffer(updateKernel, "agents", agentBuffer);
                SlimeShader.Dispatch(updateKernel, agentGroups, 1, 1);

                // 2. Draw agent positions into trail map + AgentMap
                SlimeShader.SetBuffer(drawKernel, "agents", agentBuffer);
                SlimeShader.Dispatch(drawKernel, agentGroups, 1, 1);

                // 3. Diffuse + decay
                SlimeShader.Dispatch(diffuseKernel, texGroupsX, texGroupsY, 1);

                // 4. Copy diffused result back as the new trail source
                Graphics.CopyTexture(DiffusedMap, TrailMap);
            }

            // Count living agents (throttled — GPU readback stalls the pipeline)
            countFrameAccum++;
            if (countFrameAccum >= 15)
            {
                countFrameAccum = 0;
                int countGroups = Mathf.CeilToInt(currentAgentCount / 64f);
                SlimeShader.SetBuffer(clearCountsKernel, "speciesCounts", speciesCountsBuffer);
                SlimeShader.Dispatch(clearCountsKernel, 1, 1, 1);
                SlimeShader.SetBuffer(countAliveKernel, "agents", agentBuffer);
                SlimeShader.SetBuffer(countAliveKernel, "speciesSettings", speciesSettingsBuffer);
                SlimeShader.SetBuffer(countAliveKernel, "speciesCounts", speciesCountsBuffer);
                SlimeShader.Dispatch(countAliveKernel, countGroups, 1, 1);
                speciesCountsBuffer.GetData(AliveSpeciesCounts);

                // Lire et réinitialiser les compteurs de livraison GPU
                deliveryCounterBuffer.GetData(DeliveryCounts);
                SlimeShader.Dispatch(clearDeliveryKernel, 1, 1, 1);
            }

            // 5. Compose the final display map
            SlimeShader.Dispatch(composeKernel, texGroupsX, texGroupsY, 1);
            
        } catch (System.Exception e) {
            Debug.LogError($"[RENDERER] Update crash: {e}");
        }
    }

    // ================== Cleanup ====================================

    private void OnDestroy()
    {
        agentBuffer?.Release();
        speciesSettingsBuffer?.Release();
        interactionMatrixBuffer?.Release();
        speciesCountsBuffer?.Release();
        slotColorsBuffer?.Release();
        waypointBuffer?.Release();
        smoothedPathBuffer?.Release();
        smoothedPathMetaBuffer?.Release();
        waypointStockBuffer?.Release();
        deliveryCounterBuffer?.Release();
        if (flowFieldMapIsOwned && flowFieldMap != null) Destroy(flowFieldMap);
        TrailMap?.Release();
        DiffusedMap?.Release();
        DisplayMap?.Release();
    }

    /// <summary>Upload les stocks actuels des waypoints vers le GPU (appelé par WaypointManager chaque frame).</summary>
    public void SetWaypointStocks(float[] stocks) => waypointStockBuffer.SetData(stocks);
}
