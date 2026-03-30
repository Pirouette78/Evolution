using UnityEngine;
using System.IO;

public class AgentTacticalLayer : MonoBehaviour, ITacticalLayer
{
    [Header("Rendering")]
    public Mesh agentMesh;
    public Material agentMaterial;

    private MaterialPropertyBlock _mpb;
    private Texture2DArray _spriteArray;
    private Vector4[] _spriteData = new Vector4[32]; // cols, rows, uScale, vScale

    private void Start()
    {
        if (ZoomLevelController.Instance != null)
        {
            ZoomLevelController.Instance.RegisterLayer(this);
        }

        if (agentMesh == null) agentMesh = CreateQuad();
        if (agentMaterial == null)
        {
            Shader shader = Shader.Find("Evolution/AgentTactical");
            if (shader != null) 
            {
                agentMaterial = new Material(shader);
                agentMaterial.enableInstancing = true; // REQUIS.
            }
            else Debug.LogError("[AgentTacticalLayer] Shader Evolution/AgentTactical introuvable !");
        }
        else
        {
            agentMaterial.enableInstancing = true; // REQUIS.
        }

        _mpb = new MaterialPropertyBlock();

        BuildSpriteArray();
    }

    private static Mesh CreateQuad()
    {
        var mesh = new Mesh { name = "AgentSpriteQuad" };
        mesh.vertices  = new Vector3[] { new(-0.5f, -0.5f, 0f), new(0.5f, -0.5f, 0f), new(0.5f, 0.5f, 0f), new(-0.5f, 0.5f, 0f) };
        mesh.uv        = new Vector2[] { new(0,0), new(1,0), new(1,1), new(0,1) };
        mesh.triangles = new int[]     { 0, 1, 2, 0, 2, 3 };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private void BuildSpriteArray()
    {
        int size = 1024;
        _spriteArray = new Texture2DArray(size, size, SlimeMapRenderer.MaxSlots, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };

        // Clear to transparent
        Color32[] clearColors = new Color32[size * size];
        for (int i = 0; i < SlimeMapRenderer.MaxSlots; i++)
        {
            _spriteArray.SetPixels32(clearColors, i);
            _spriteData[i] = new Vector4(1, 1, 0, 0); // fallback invisible
        }

        var smr = SlimeMapRenderer.Instance;
        var lib = SpeciesLibrary.Instance;
        if (smr == null || lib == null)
        {
            Debug.LogWarning($"[AgentTacticalLayer] BuildSpriteArray: smr={smr != null}, lib={lib != null} → early exit");
            return;
        }

        Debug.Log($"[AgentTacticalLayer] BuildSpriteArray: {smr.numActiveSlots} slots");
        for (int i = 0; i < smr.numActiveSlots; i++)
        {
            string id = smr.speciesIds[i];
            var def = lib.Get(id);
            if (def == null) { Debug.LogWarning($"[AgentTacticalLayer] Slot {i} ({id}): def null"); continue; }

            // Espèces avec sprite grande-taille (arbres, bâtiments) → gérées par UnitSpriteRenderer
            if (def.spriteTilesW > 0)
            {
                _spriteData[i] = new Vector4(0, 0, 0, 0); // cols=0 → shader clip(-1)
                Debug.Log($"[AgentTacticalLayer] Slot {i} ({id}): tileSprite, skipped (UnitSpriteRenderer)");
                continue;
            }

            if (string.IsNullOrEmpty(def.spriteName))
            {
                Debug.Log($"[AgentTacticalLayer] Slot {i} ({id}): no spriteName");
                continue;
            }

            string path = Path.Combine(Application.streamingAssetsPath, "Sprites", def.spriteName + ".png");
            if (!File.Exists(path))
            {
                Debug.LogWarning($"[AgentTacticalLayer] Slot {i} ({id}): FILE NOT FOUND: {path}");
                continue;
            }

            byte[] bytes = File.ReadAllBytes(path);
            Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.LoadImage(bytes);

            // Copy texture to array slice (bottom-left aligned)
            Color32[] pixels = tex.GetPixels32();
            Color32[] slicePixels = new Color32[size * size];
            int w = Mathf.Min(tex.width, size);
            int h = Mathf.Min(tex.height, size);

            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    slicePixels[y * size + x] = pixels[y * tex.width + x];

            _spriteArray.SetPixels32(slicePixels, i);

            int cols = Mathf.Max(1, def.spriteFramesW);
            int rows = Mathf.Max(1, def.spriteFramesH);
            _spriteData[i] = new Vector4(cols, rows, w / (float)size, h / (float)size);

            Debug.Log($"[AgentTacticalLayer] Slot {i} ({id}): loaded {def.spriteName} {tex.width}x{tex.height}px → cols={cols} rows={rows} uScale={w/(float)size:F3} vScale={h/(float)size:F3}");

            Destroy(tex);
        }

        _spriteArray.Apply();
    }

    public void OnEnterTactical(Vector4 cameraBounds)
    {
        // Re-check sprites in case species changed
        BuildSpriteArray();
    }

    private float _camBoundsLogTimer = 0f;
    public void OnCameraBoundsChanged(Vector4 cameraBounds)
    {
        var renderer = SlimeMapRenderer.Instance;
        if (renderer == null) { Debug.LogWarning("[AgentTacticalLayer] OnCameraBoundsChanged: renderer null"); return; }

        if (renderer.VisibleArgsBuffer == null || renderer.VisibleAgentIdsBuffer == null)
        {
            Debug.LogWarning($"[AgentTacticalLayer] OnCameraBoundsChanged: argsBuffer={renderer.VisibleArgsBuffer != null} idsBuffer={renderer.VisibleAgentIdsBuffer != null}");
            return;
        }
        if (agentMesh == null || agentMaterial == null)
        {
            Debug.LogWarning($"[AgentTacticalLayer] OnCameraBoundsChanged: mesh={agentMesh != null} mat={agentMaterial != null}");
            return;
        }

        // Assigner les buffers et textures au MaterialPropertyBlock
        float spriteAlpha = ZoomLevelController.Instance != null ? ZoomLevelController.Instance.SpriteAlpha : 1f;
        _mpb.SetFloat("_GlobalAlpha", spriteAlpha);

        _camBoundsLogTimer += Time.deltaTime;
        if (_camBoundsLogTimer >= 2f)
        {
            _camBoundsLogTimer = 0f;
            uint[] args = new uint[5];
            renderer.VisibleArgsBuffer.GetData(args);
            Debug.Log($"[AgentTacticalLayer] Draw: alpha={spriteAlpha:F2} instances={args[1]} spriteArray={_spriteArray != null} mesh={agentMesh != null} mat={agentMaterial != null} spriteData[1]={_spriteData[1]}");
        }

        _mpb.SetBuffer("_VisibleAgentIds", renderer.VisibleAgentIdsBuffer);
        _mpb.SetBuffer("_AgentBuffer", renderer.AgentBuffer);
        
        if (_spriteArray != null)
        {
            _mpb.SetTexture("_SpriteArray", _spriteArray);
            _mpb.SetVectorArray("_SpriteData", _spriteData);
        }

        // Paramètres pour convertir a.position (0..1024) vers coords monde du Quad (DisplayTarget)
        Bounds drawBounds = new Bounds();
        if (renderer.DisplayTarget != null)
        {
            Bounds db = renderer.DisplayTarget.bounds;
            // On s'assure que db a de l'épaisseur pour ne pas qu'Unity culle la geometry plate
            db.Expand(new Vector3(500f, 500f, 1000f)); 
            drawBounds = db; // Unity world bounds for culling
            
            Bounds org = renderer.DisplayTarget.bounds;
            _mpb.SetVector("_MapWorldBounds", new Vector4(org.min.x, org.min.y, org.size.x, org.size.y));
            _mpb.SetVector("_MapSimParams", new Vector4(renderer.Width, renderer.Height, org.center.z - 0.2f, 0));
        }
        else
        {
            float mw = renderer.Width;
            float mh = renderer.Height;
            drawBounds = new Bounds(new Vector3(mw/2f, mh/2f, 0f), new Vector3(mw, mh, 1000f));
            _mpb.SetVector("_MapWorldBounds", new Vector4(0, 0, mw, mh));
            _mpb.SetVector("_MapSimParams", new Vector4(mw, mh, 0, 0));
        }

        Graphics.DrawMeshInstancedIndirect(
            agentMesh,
            0,
            agentMaterial,
            drawBounds,
            renderer.VisibleArgsBuffer,
            0,
            _mpb
        );
    }

    public void OnExitTactical()
    {
        // Appelé quand le zoom recule
    }
    
    private float _debugTimer = 0f;
    private uint[] _debugArgs = new uint[5];
    private void Update()
    {
        if (ZoomLevelController.Instance != null && ZoomLevelController.Instance.IsInTacticalMode)
        {
            _debugTimer += Time.deltaTime;
            if (_debugTimer >= 2f)
            {
                _debugTimer = 0f;
                var smr = SlimeMapRenderer.Instance;
                if (smr != null && smr.VisibleArgsBuffer != null)
                {
                    smr.VisibleArgsBuffer.GetData(_debugArgs);
                    
                    Vector4 simBounds = ZoomLevelController.Instance.CameraSimBounds;
                    Bounds db = smr.DisplayTarget != null ? smr.DisplayTarget.bounds : new Bounds();
                    
                    Debug.Log($"[AgentTacticalLayer] Args: {_debugArgs[1]} agents. " +
                              $"CamSim={simBounds}. " + 
                              $"DisplayBounds: Min={db.min}, Max={db.max}. " +
                              $"MapW={smr.Width}, MapH={smr.Height}");
                }
            }
        }
    }

    private void OnDestroy()
    {
        if (ZoomLevelController.Instance != null)
        {
            ZoomLevelController.Instance.UnregisterLayer(this);
        }
        if (_spriteArray != null) Destroy(_spriteArray);
    }
}
