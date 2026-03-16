using UnityEngine;
using UnityEngine.UI;

public class SlimeMapRenderer : MonoBehaviour
{
    public static SlimeMapRenderer Instance { get; private set; }

    [Header("Compute Shader")]
    public ComputeShader SlimeShader;

    [Header("Simulation Settings")]
    public int Width = 512;
    public int Height = 512;
    [Range(0, 100)] public float TrailWeight = 50f;
    [Range(0, 5)] public float DecayRate = 0.5f;
    [Range(0, 5)] public float DiffuseRate = 2f;

    [Header("Output")]
    public MeshRenderer DisplayTarget;

    public RenderTexture TrailMap { get; private set; }
    public RenderTexture DiffusedMap { get; private set; }
    private ComputeBuffer agentBuffer; // Changed from public property to private field
    public ComputeBuffer AgentBuffer => agentBuffer; 

    private int drawKernel;
    private int diffuseKernel;
    private int clearKernel;
    private int maxAgents = 10000;
    private bool hasLoggedAgentCount = false; 
    private bool isInitialized = false; 

    private void Start()
    {
        Initialize();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // 32 bytes: 2 floats (pos) + 1 float (angle) + 4 floats (mask) + 1 int (index) -> 8 + 4 + 16 + 4 = 32
        agentBuffer = new ComputeBuffer(maxAgents, 32); // Use private field

        if (DisplayTarget == null) DisplayTarget = GetComponent<MeshRenderer>();
    }

    private void Initialize()
    {
        if (isInitialized) return;
        if (SlimeShader == null) {
            Debug.LogError("[RENDERER] SlimeShader is NULL! Cannot initialize.");
            return;
        }

        try {
            drawKernel = SlimeShader.FindKernel("DrawMap");
        } catch { drawKernel = 0; Debug.LogWarning("[RENDERER] DrawMap kernel fallback to 0"); }

        try {
            diffuseKernel = SlimeShader.FindKernel("Diffuse");
        } catch { diffuseKernel = 1; Debug.LogWarning("[RENDERER] Diffuse kernel fallback to 1"); }

        try {
            clearKernel = SlimeShader.FindKernel("ResetMap");
        } catch { clearKernel = 2; Debug.LogWarning("[RENDERER] ResetMap kernel fallback to 2"); }

        if (TrailMap == null) {
            TrailMap = new RenderTexture(Width, Height, 0, RenderTextureFormat.ARGB32);
            TrailMap.enableRandomWrite = true;
            TrailMap.name = "TrailMap";
            TrailMap.Create();
        }

        if (DiffusedMap == null) {
            DiffusedMap = new RenderTexture(Width, Height, 0, RenderTextureFormat.ARGB32);
            DiffusedMap.enableRandomWrite = true;
            DiffusedMap.name = "DiffusedMap";
            DiffusedMap.Create();
        }

        SlimeShader.SetInt("width", Width);
        SlimeShader.SetInt("height", Height);
        
        SlimeShader.SetTexture(drawKernel, "TrailMap", TrailMap);
        SlimeShader.SetTexture(diffuseKernel, "TrailMap", TrailMap);
        SlimeShader.SetTexture(diffuseKernel, "DiffusedTrailMap", DiffusedMap);
        
        SlimeShader.SetTexture(clearKernel, "TrailMap", TrailMap);
        SlimeShader.SetTexture(clearKernel, "DiffusedTrailMap", DiffusedMap);

        // Clear textures to Black via shader
        int groupsX = Mathf.CeilToInt(Width / 8f);
        int groupsY = Mathf.CeilToInt(Height / 8f);
        SlimeShader.Dispatch(clearKernel, groupsX, groupsY, 1);

        if (DisplayTarget != null) {
            DisplayTarget.sharedMaterial.mainTexture = DiffusedMap;
            Debug.LogWarning($"[RENDERER] Assigned DiffusedMap to {DisplayTarget.name} material.");
        } else {
             Debug.LogWarning("[RENDERER] DisplayTarget is STILL NULL during Initialize.");
        }

        isInitialized = true;
        Debug.LogWarning("[RENDERER] Initialized successfully.");
    }

    // Called by ECS Dispatcher
    public void DispatchCompute(int currentAgentCount, float deltaTime)
    {
        if (!isInitialized) Initialize();
        if (!isInitialized) return;

        if (currentAgentCount == 0) return;

        // Ensure buffer is large enough
        if (currentAgentCount > maxAgents)
        {
            agentBuffer.Release(); // Use private field
            maxAgents = currentAgentCount * 2;
            agentBuffer = new ComputeBuffer(maxAgents, 32); // Use private field
        }

        // Log agent count once
        if (!hasLoggedAgentCount && currentAgentCount > 0)
        {
            Debug.LogWarning($"[RENDERER] First dispatch with {currentAgentCount} agents.");
            hasLoggedAgentCount = true; 
        }

        if (agentBuffer == null) return;

        // Draw Map
        SlimeShader.SetFloat("trailWeight", TrailWeight);
        SlimeShader.SetFloat("decayRate", DecayRate);
        SlimeShader.SetFloat("diffuseRate", DiffuseRate);
        SlimeShader.SetFloat("deltaTime", deltaTime);
        
        SlimeShader.SetInt("numAgents", currentAgentCount);
        SlimeShader.SetBuffer(drawKernel, "agents", agentBuffer); 
        SlimeShader.SetTexture(drawKernel, "TrailMap", TrailMap);
        
        int threadGroupsX = Mathf.CeilToInt(currentAgentCount / 16f);
        SlimeShader.Dispatch(drawKernel, threadGroupsX, 1, 1);

        // Diffuse Map
        SlimeShader.SetTexture(diffuseKernel, "TrailMap", TrailMap);
        SlimeShader.SetTexture(diffuseKernel, "DiffusedTrailMap", DiffusedMap);
        
        int diffuseGroupsX = Mathf.CeilToInt(Width / 8f);
        int diffuseGroupsY = Mathf.CeilToInt(Height / 8f);
        SlimeShader.Dispatch(diffuseKernel, diffuseGroupsX, diffuseGroupsY, 1);

        // Copy back to Trail for next frame
        Graphics.Blit(DiffusedMap, TrailMap);

        // DIAGNOSTIC 
        if (Time.frameCount % 300 == 0) {
             // Periodically ensure material link
             if (DisplayTarget != null && DisplayTarget.sharedMaterial != null)
                 DisplayTarget.sharedMaterial.mainTexture = DiffusedMap;
             
             Debug.Log($"[RENDERER] Active - Agents: {currentAgentCount}");
        }
    }

    private void OnDestroy()
    {
        if (AgentBuffer != null) AgentBuffer.Release();
        if (TrailMap != null) TrailMap.Release();
        if (DiffusedMap != null) DiffusedMap.Release();
    }
}
