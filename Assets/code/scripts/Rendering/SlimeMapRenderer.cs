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
    [Range(0.1f, 200f)] public float MoveSpeed = 75f;
    [Range(0f, 50f)] public float TrailWeight = 5f;
    [Range(0f, 5f)] public float DecayRate = 1f;
    [Range(0f, 5f)] public float DiffuseRate = 2f;
    [Range(1, 8)] public int StepsPerFrame = 1;
    [Header("Initial Spawn")]
    public int InitialAgentCount = 5000;

    [Header("Output")]
    public MeshRenderer DisplayTarget;

    // ── Public state ────────────────────────────────────────────────
    public RenderTexture TrailMap    { get; private set; }
    public RenderTexture DiffusedMap { get; private set; }
    public ComputeBuffer AgentBuffer => agentBuffer;
    public bool IsReady              => isInitialized;
    public int  AgentCount           => currentAgentCount;

    // ── Private ─────────────────────────────────────────────────────
    private ComputeBuffer agentBuffer;
    private int maxAgents = 600000;
    private int currentAgentCount = 0;
    private bool isInitialized = false;

    private int updateKernel, drawKernel, diffuseKernel, clearKernel;

    // Cached terrain data for CPU-side spawn validation
    private float[,] heightMapCache;
    private float waterThresholdCache;

    // ── Struct must match the compute shader exactly (32 bytes) ─────
    struct Agent
    {
        public Vector2 position;   // 8
        public float   angle;      // 4
        public Vector4 speciesMask;// 16
        public int     speciesIndex;// 4
    }

    // ================== Unity lifecycle ============================

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        agentBuffer = new ComputeBuffer(maxAgents, sizeof(float)*7 + sizeof(int));
        // sizeof(float)*7 = pos(2) + angle(1) + mask(4) = 7 floats = 28 bytes
        // + sizeof(int) = 4 bytes  → total = 32 bytes ✓

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
        TrailMap = CreateRT("TrailMap");
        DiffusedMap = CreateRT("DiffusedMap");

        RenderTexture CreateRT(string name)
        {
            var rt = new RenderTexture(Width, Height, 0, RenderTextureFormat.ARGBFloat)
            { enableRandomWrite = true, name = name };
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

        // Bind textures (persistent across frames)
        SlimeShader.SetTexture(updateKernel,  "TrailMap",         TrailMap);
        SlimeShader.SetTexture(drawKernel,    "TrailMap",         TrailMap);
        SlimeShader.SetTexture(diffuseKernel, "TrailMap",         TrailMap);
        SlimeShader.SetTexture(diffuseKernel, "DiffusedTrailMap", DiffusedMap);
        SlimeShader.SetTexture(clearKernel,   "TrailMap",         TrailMap);
        SlimeShader.SetTexture(clearKernel,   "DiffusedTrailMap", DiffusedMap);
        SlimeShader.SetInt("width",  Width);
        SlimeShader.SetInt("height", Height);

        // Clear to black
        int gx = Mathf.CeilToInt(Width  / 8f);
        int gy = Mathf.CeilToInt(Height / 8f);
        SlimeShader.Dispatch(clearKernel, gx, gy, 1);

        // Bind output to display quad
        if (DisplayTarget != null)
        {
            DisplayTarget.sharedMaterial.mainTexture = DiffusedMap;
            if (DisplayTarget.sharedMaterial.HasProperty("_BaseMap"))
                DisplayTarget.sharedMaterial.SetTexture("_BaseMap", DiffusedMap);
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
        heightMapCache    = terrain.HeightMap;
        waterThresholdCache = terrain.WaterThreshold;

        // Bind terrain texture to UpdateAgents kernel
        SlimeShader.SetTexture(updateKernel, "TerrainWalkabilityMap", terrain.GetTexture());
        SlimeShader.SetInt("useTerrainCollision", 1);
        Debug.Log("[RENDERER] Terrain texture bound to shader.");
    }

    // ================== Public API =================================

    /// <summary>Append count new agents to the GPU buffer.</summary>
    public void AddAgents(int count)
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
            int     pid   = Random.Range(0, 3);

            Vector4 mask = Vector4.zero;
            mask[pid] = 1f;

            newAgents[i] = new Agent
            {
                position    = pos,
                angle       = angle,
                speciesMask = mask,
                speciesIndex= pid
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
        SlimeShader.SetFloat("time",        Time.time);
        SlimeShader.SetFloat("deltaTime",   dt);
        SlimeShader.SetFloat("moveSpeed",   MoveSpeed);
        SlimeShader.SetFloat("trailWeight", TrailWeight);
        SlimeShader.SetFloat("decayRate",   DecayRate);
        SlimeShader.SetFloat("diffuseRate", DiffuseRate);
        SlimeShader.SetInt  ("numAgents",   currentAgentCount);

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
            Graphics.Blit(DiffusedMap, TrailMap);
        }
    }

    // ================== Cleanup ====================================

    private void OnDestroy()
    {
        agentBuffer?.Release();
        TrailMap?.Release();
        DiffusedMap?.Release();
    }
}
