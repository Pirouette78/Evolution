using UnityEngine;
using System.Collections;

/// <summary>
/// Core GPU simulation manager. Owns the agent buffer and drives
/// UpdateAgents → DrawMap → Diffuse every frame entirely on the GPU.
/// No ECS dependency for movement.
/// </summary>
public class SlimeMapRenderer : MonoBehaviour
{
    public static SlimeMapRenderer Instance { get; private set; }

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
    public ComputeBuffer AgentBuffer => agentBuffer;
    public bool IsReady              => isInitialized;
    public int  AgentCount           => currentAgentCount;

    public int SelectedPlayerIndex = 0;
    public SpeciesSettings[] speciesSettings = new SpeciesSettings[6];

    // ── Private ─────────────────────────────────────────────────────
    private ComputeBuffer agentBuffer;
    private int maxAgents = 600000;
    private int currentAgentCount = 0;
    private bool isInitialized = false;
    private int playerVisibilityMask = 63; // 111111 in binary (all 6 visible by default)

    private int updateKernel, drawKernel, diffuseKernel, clearKernel, composeKernel;

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
    }
    private ComputeBuffer speciesSettingsBuffer;

    struct TypeOfWorker
    {
        public int fighter;
        public int worker;
        public int builder;
        public int queen;
        public int scout;
        public int foodCollector;
    }

    struct Agent
    {
        public Vector2 position;   // 8
        public float   angle;      // 4
        public int     speciesIndex;// 4
        public float   age;         // 4
        public float   hunger;      // 4
        public TypeOfWorker typeOfWorker;// 24
    }

    // ================== Unity lifecycle ============================

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        agentBuffer = new ComputeBuffer(maxAgents, sizeof(float)*5 + sizeof(int)*7);
        // struct size (48 bytes) = 5 floats (20) + 7 ints (28)
        
        speciesSettingsBuffer = new ComputeBuffer(6, 36);

        for (int i = 0; i < 6; i++) {
            speciesSettings[i] = new SpeciesSettings {
                moveSpeed = 75f,
                turnSpeed = 10f,
                sensorAngleRad = 30f * Mathf.Deg2Rad,
                sensorOffsetDst = 20f,
                sensorSize = 2,
                maxAge = 100f,
                trailWeight = 5f,
                decayRate = 1f,
                diffuseRate = 2f
            };
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
        TrailMap = CreateRTArray("TrailMap");
        DiffusedMap = CreateRTArray("DiffusedMap");

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
                volumeDepth = 6,
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

        // Bind textures (persistent across frames)
        SlimeShader.SetTexture(updateKernel,  "TrailMap",         TrailMap);
        SlimeShader.SetTexture(drawKernel,    "TrailMap",         TrailMap);
        SlimeShader.SetTexture(diffuseKernel, "TrailMap",         TrailMap);
        SlimeShader.SetTexture(diffuseKernel, "DiffusedTrailMap", DiffusedMap);
        SlimeShader.SetTexture(clearKernel,   "TrailMap",         TrailMap);
        SlimeShader.SetTexture(clearKernel,   "DiffusedTrailMap", DiffusedMap);
        SlimeShader.SetTexture(composeKernel, "DiffusedTrailMap", DiffusedMap);
        SlimeShader.SetTexture(composeKernel, "DisplayMap",       DisplayMap);
        SlimeShader.SetTexture(composeKernel, "DisplayMap",       DisplayMap);
        SlimeShader.SetInt("width",  Width);
        SlimeShader.SetInt("height", Height);

        SlimeShader.SetBuffer(updateKernel, "speciesSettings", speciesSettingsBuffer);
        SlimeShader.SetBuffer(drawKernel, "speciesSettings", speciesSettingsBuffer);
        SlimeShader.SetBuffer(diffuseKernel, "speciesSettings", speciesSettingsBuffer);

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
        if (index >= 0 && index < 6)
        {
            if (isVisible) playerVisibilityMask |= (1 << index);
            else           playerVisibilityMask &= ~(1 << index);
        }
    }

    public bool GetPlayerVisibility(int index)
    {
        if (index >= 0 && index < 6)
            return (playerVisibilityMask & (1 << index)) != 0;
        return false;
    }

    /// <summary>Append count new agents to the GPU buffer.</summary>
    public void AddAgents(int count, int forceSpecies = -1)
    {
        if (!isInitialized) { Debug.LogWarning("[RENDERER] Not initialized."); return; }
        
        count = Mathf.Min(count, maxAgents - currentAgentCount);
        if (count <= 0) { Debug.LogWarning("[RENDERER] Max agents reached."); return; }

        // Build only the NEW agents on the CPU
        Agent[] newAgents = new Agent[count];
        for (int i = 0; i < count; i++)
        {
            Vector2 pos = GetSpawnPosition();
            float   angle = Random.value * Mathf.PI * 2f;
            int     pid   = forceSpecies >= 0 ? forceSpecies : Random.Range(0, 6);

            newAgents[i] = new Agent
            {
                position    = pos,
                angle       = angle,
                speciesIndex= pid,
                age         = 0f,
                hunger      = 0f,
                typeOfWorker= new TypeOfWorker()
            };
        }

        // Upload only the new slice (no full-buffer stall)
        agentBuffer.SetData(newAgents, 0, currentAgentCount, count);
        currentAgentCount += count;

        Debug.Log($"[RENDERER] Added {count} agents. Total: {currentAgentCount}");
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

    private void Update()
    {
        if (!isInitialized || currentAgentCount == 0) return;

        // Re-bind terrain every frame in case it became available after initialization
        if (TerrainMapRenderer.Instance != null &&
            TerrainMapRenderer.Instance.GetTexture() != null &&
            heightMapCache == null)
        {
            CacheTerrainData(TerrainMapRenderer.Instance);
        }

        float dt = Time.deltaTime;

        // Per-frame global uniforms
        SlimeShader.SetFloat("time",             Time.time);
        SlimeShader.SetFloat("deltaTime",        dt);
        SlimeShader.SetInt  ("numAgents",        currentAgentCount);
        
        // Push per-species settings
        speciesSettingsBuffer.SetData(speciesSettings);

        SlimeShader.SetInt ("playerVisibilityMask", playerVisibilityMask);

        int agentGroups  = Mathf.CeilToInt(currentAgentCount / 16f);
        int texGroupsX   = Mathf.CeilToInt(Width  / 8f);
        int texGroupsY   = Mathf.CeilToInt(Height / 8f);

        for (int step = 0; step < StepsPerFrame; step++)
        {
            // 1. Move agents
            SlimeShader.SetBuffer(updateKernel, "agents", agentBuffer);
            SlimeShader.Dispatch(updateKernel, agentGroups, 1, 1);

            // 2. Draw agent positions into trail map
            SlimeShader.SetBuffer(drawKernel, "agents", agentBuffer);
            SlimeShader.Dispatch(drawKernel, agentGroups, 1, 1);

            // 3. Diffuse + decay
            SlimeShader.Dispatch(diffuseKernel, texGroupsX, texGroupsY, 1);

            // 4. Copy diffused result back as the new trail source
            Graphics.CopyTexture(DiffusedMap, TrailMap);
        }

        // 5. Compose the final display map
        SlimeShader.Dispatch(composeKernel, texGroupsX, texGroupsY, 1);
    }

    // ================== Cleanup ====================================

    private void OnDestroy()
    {
        agentBuffer?.Release();
        speciesSettingsBuffer?.Release();
        TrailMap?.Release();
        DiffusedMap?.Release();
        DisplayMap?.Release();
    }
}
