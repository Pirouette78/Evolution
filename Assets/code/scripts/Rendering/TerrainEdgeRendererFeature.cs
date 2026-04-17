using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;

public class TerrainEdgeRendererFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        [Header("Edge")]
        [Range(0f, 1f)] public float threshold = 0.05f;
        [Range(0f, 1f)] public float blend     = 1.0f;
        public Color color = new Color(0.15f, 0.10f, 0.05f, 1f);

        [Header("Biomes actifs")]
        public bool water  = false;
        public bool sand   = false;
        public bool grass  = false;
        public bool forest = false;
        public bool rock   = true;
        public bool snow   = true;
    }

    // ── Inner pass ────────────────────────────────────────────────────────────
    class TerrainEdgePass : ScriptableRenderPass
    {
        static readonly int ThresholdId   = Shader.PropertyToID("_EdgeThreshold");
        static readonly int BlendId       = Shader.PropertyToID("_EdgeBlend");
        static readonly int ColorId       = Shader.PropertyToID("_EdgeColor");
        static readonly int BiomeMaskId   = Shader.PropertyToID("_BiomeMask");
        static readonly int SunShadeTexId = Shader.PropertyToID("_SunShadeTex");

        readonly Material _edgeMat;
        readonly Material _sunShadeMat;
        readonly Settings _settings;
        RenderTexture     _sunShadeRT;

        class PassData
        {
            public TextureHandle  src;
            public Material       edgeMat;
            public Material       sunShadeMat;
            public float          threshold;
            public float          blend;
            public Color          color;
            public int            biomeMask;
            public RenderTexture  sunShadeRT;
            public Renderer       terrainRenderer;
        }

        public TerrainEdgePass(Settings settings)
        {
            _settings = settings;
            requiresIntermediateTexture = true;

            var edgeShader = Shader.Find("Evolution/TerrainEdge");
            if (edgeShader != null) _edgeMat = CoreUtils.CreateEngineMaterial(edgeShader);
            else Debug.LogError("[TerrainEdgePass] Shader 'Evolution/TerrainEdge' introuvable.");

            var sunShadeShader = Shader.Find("Evolution/TerrainSunShade");
            if (sunShadeShader != null) _sunShadeMat = CoreUtils.CreateEngineMaterial(sunShadeShader);
            else Debug.LogError("[TerrainEdgePass] Shader 'Evolution/TerrainSunShade' introuvable.");
        }

        int BiomeMask()
        {
            int m = 0;
            if (_settings.water)  m |= 1 << 0;
            if (_settings.sand)   m |= 1 << 1;
            if (_settings.grass)  m |= 1 << 2;
            if (_settings.forest) m |= 1 << 3;
            if (_settings.rock)   m |= 1 << 4;
            if (_settings.snow)   m |= 1 << 5;
            return m;
        }

        void EnsureRT(int w, int h)
        {
            if (_sunShadeRT != null && _sunShadeRT.width == w && _sunShadeRT.height == h) return;
            _sunShadeRT?.Release();
            _sunShadeRT = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Bilinear };
            _sunShadeRT.Create();
        }

        void SyncSunShadeMaterial(Material terrainOverlayMat)
        {
            if (_sunShadeMat == null || terrainOverlayMat == null) return;
            var t = TerrainMapRenderer.Instance;
            _sunShadeMat.SetTexture("_MainTex",         terrainOverlayMat.GetTexture("_MainTex"));
            _sunShadeMat.SetVector("_MapSize",          terrainOverlayMat.GetVector("_MapSize"));
            _sunShadeMat.SetFloat("_SandThreshold",     terrainOverlayMat.GetFloat("_SandThreshold"));
            _sunShadeMat.SetFloat("_GrassThreshold",    terrainOverlayMat.GetFloat("_GrassThreshold"));
            _sunShadeMat.SetFloat("_ForestThreshold",   terrainOverlayMat.GetFloat("_ForestThreshold"));
            _sunShadeMat.SetFloat("_RockThreshold",     terrainOverlayMat.GetFloat("_RockThreshold"));
            _sunShadeMat.SetFloat("_SlopeScaleWater",   terrainOverlayMat.GetFloat("_SlopeScaleWater"));
            _sunShadeMat.SetFloat("_SlopeScaleSand",    terrainOverlayMat.GetFloat("_SlopeScaleSand"));
            _sunShadeMat.SetFloat("_SlopeScaleGrass",   terrainOverlayMat.GetFloat("_SlopeScaleGrass"));
            _sunShadeMat.SetFloat("_SlopeScaleForest",  terrainOverlayMat.GetFloat("_SlopeScaleForest"));
            _sunShadeMat.SetFloat("_SlopeScaleRock",    terrainOverlayMat.GetFloat("_SlopeScaleRock"));
            _sunShadeMat.SetFloat("_SlopeScaleSnow",    terrainOverlayMat.GetFloat("_SlopeScaleSnow"));
            _sunShadeMat.SetFloat("_SunShadowStrength", terrainOverlayMat.GetFloat("_SunShadowStrength"));
            _sunShadeMat.SetFloat("_GlobalSunPosition", Shader.GetGlobalFloat("_GlobalSunPosition"));
            if (t != null) _sunShadeMat.SetFloat("_WaterThreshold", t.WaterThreshold);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (_edgeMat == null) return;

            var resourceData = frameData.Get<UniversalResourceData>();
            if (resourceData.isActiveTargetBackBuffer) return;

            var cameraData = frameData.Get<UniversalCameraData>();
            EnsureRT(cameraData.cameraTargetDescriptor.width, cameraData.cameraTargetDescriptor.height);

            SyncSunShadeMaterial(ZoomLevelController.Instance?.TerrainOverlayMaterial);

            var src = resourceData.activeColorTexture;
            var desc = renderGraph.GetTextureDesc(src);
            desc.name = "_TerrainEdgeTemp";
            desc.clearBuffer = false;
            var dst = renderGraph.CreateTexture(desc);

            using (var builder = renderGraph.AddUnsafePass<PassData>("TerrainEdge", out var passData))
            {
                passData.src             = src;
                passData.edgeMat         = _edgeMat;
                passData.sunShadeMat     = _sunShadeMat;
                passData.threshold       = _settings.threshold;
                float tacticalBlend      = ZoomLevelController.Instance?.TacticalBlend ?? 1f;
                passData.blend           = _settings.blend * tacticalBlend;
                passData.color           = _settings.color;
                passData.biomeMask       = BiomeMask();
                passData.sunShadeRT      = _sunShadeRT;
                passData.terrainRenderer = TerrainMapRenderer.Instance?.DisplayTarget;

                builder.UseTexture(src, AccessFlags.ReadWrite);
                builder.UseTexture(dst, AccessFlags.Write);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc((PassData d, UnsafeGraphContext ctx) =>
                {
                    var cmd = CommandBufferHelpers.GetNativeCommandBuffer(ctx.cmd);

                    if (d.terrainRenderer != null && d.sunShadeRT != null && d.sunShadeMat != null)
                    {
                        cmd.SetRenderTarget(d.sunShadeRT);
                        cmd.ClearRenderTarget(false, true, Color.clear);
                        cmd.DrawRenderer(d.terrainRenderer, d.sunShadeMat, 0, 0);
                    }

                    d.edgeMat.SetFloat(ThresholdId,     d.threshold);
                    d.edgeMat.SetFloat(BlendId,         d.blend);
                    d.edgeMat.SetColor(ColorId,         d.color);
                    d.edgeMat.SetInt(BiomeMaskId,       d.biomeMask);
                    d.edgeMat.SetTexture(SunShadeTexId, d.sunShadeRT);

                    Blitter.BlitCameraTexture(cmd, d.src, dst, d.edgeMat, 0);
                    Blitter.BlitCameraTexture(cmd, dst, d.src);
                });
            }
        }

        public void Dispose()
        {
            _sunShadeRT?.Release();
            CoreUtils.Destroy(_edgeMat);
            CoreUtils.Destroy(_sunShadeMat);
        }
    }

    // ── Feature ───────────────────────────────────────────────────────────────
    public Settings settings = new Settings();
    TerrainEdgePass _pass;

    public override void Create()
    {
        _pass = new TerrainEdgePass(settings);
        _pass.renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (renderingData.cameraData.cameraType != CameraType.Game) return;
        renderer.EnqueuePass(_pass);
    }

    protected override void Dispose(bool disposing) => _pass?.Dispose();
}
