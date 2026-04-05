using UnityEngine;
using System.Collections;
using System.IO;

public class AgentTacticalLayer : MonoBehaviour, ITacticalLayer
{
    [Header("Rendering")]
    public Mesh agentMesh;
    public Material agentMaterial;

    private MaterialPropertyBlock _mpb;
    private Texture2DArray _spriteArray;
    private Vector4[] _spriteData = new Vector4[32]; // cols, rows, uScale, vScale
    private Vector4[] _spriteScaleAnchor = new Vector4[32]; // stW, stH, ancX, ancY

    // Nombre de slots pour lesquels le tableau a été construit (-1 = jamais)
    private int _builtForSlots = -1;

    private void Start()
    {
        if (ZoomLevelController.Instance != null)
            ZoomLevelController.Instance.RegisterLayer(this);

        if (agentMesh == null) agentMesh = CreateQuad();
        if (agentMaterial == null)
        {
            Shader shader = Shader.Find("Evolution/AgentTactical");
            if (shader != null)
            {
                agentMaterial = new Material(shader);
                agentMaterial.enableInstancing = true;
            }
            else Debug.LogError("[AgentTacticalLayer] Shader Evolution/AgentTactical introuvable !");
        }
        else
        {
            agentMaterial.enableInstancing = true;
        }

        _mpb = new MaterialPropertyBlock();

        // Construction différée : attendre que SlimeMapRenderer ait fini son spawn initial
        StartCoroutine(BuildSpriteArrayWhenReady());
    }

    private IEnumerator BuildSpriteArrayWhenReady()
    {
        while (SlimeMapRenderer.Instance == null || !SlimeMapRenderer.Instance.InitialSpawnDone)
            yield return null;
        yield return null; // une frame supplémentaire

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
        var smr = SlimeMapRenderer.Instance;
        var lib = SpeciesLibrary.Instance;
        if (smr == null || lib == null) return;

        int numSlots = smr.numActiveSlots;

        // Détruire l'ancienne texture avant d'en créer une nouvelle
        if (_spriteArray != null) Destroy(_spriteArray);

        int size = 1024;
        _spriteArray = new Texture2DArray(size, size, SlimeMapRenderer.MaxSlots, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };

        Color32[] clearColors = new Color32[size * size];
        for (int i = 0; i < SlimeMapRenderer.MaxSlots; i++)
        {
            _spriteArray.SetPixels32(clearColors, i);
            _spriteData[i] = new Vector4(1, 1, 0, 0); // fallback = cercle rouge debug
        }

        for (int i = 0; i < numSlots; i++)
        {
            string id = smr.speciesIds[i];
            var def = lib.Get(id);
            if (def == null) continue;

            if (def.spriteTilesW > 0 && def.blocksMovement)
            {
                _spriteData[i] = new Vector4(0, 0, 0, 0); // géré par UnitSpriteRenderer
                _spriteScaleAnchor[i] = new Vector4(1, 1, 0.5f, 0.5f);
                continue;
            }

            if (string.IsNullOrEmpty(def.spriteName)) continue;

            string path = Path.Combine(Application.streamingAssetsPath, "Sprites", def.spriteName + ".png");
            if (!File.Exists(path))
            {
                Debug.LogWarning($"[AgentTacticalLayer] Sprite introuvable : {path}");
                continue;
            }

            byte[] bytes = File.ReadAllBytes(path);
            Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.LoadImage(bytes);

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

            float stW = def.spriteTilesW > 0 ? def.spriteTilesW : 8.0f;
            float stH = def.spriteTilesH > 0 ? def.spriteTilesH : 8.0f;
            _spriteScaleAnchor[i] = new Vector4(stW, stH, def.spriteAnchorX, def.spriteAnchorY);

            Debug.Log($"[AgentTacticalLayer] Slot {i} ({id}): {def.spriteName} {tex.width}x{tex.height}px cols={cols} rows={rows}");
            Destroy(tex);
        }

        _spriteArray.Apply();
        _builtForSlots = numSlots;
        Debug.Log($"[AgentTacticalLayer] SpriteArray construit pour {numSlots} slots.");
    }

    public void OnEnterTactical(Vector4 cameraBounds)
    {
        // Ne rebuilder que si les espèces ont changé depuis la dernière construction
        var smr = SlimeMapRenderer.Instance;
        if (smr != null && smr.numActiveSlots != _builtForSlots)
            BuildSpriteArray();
    }

    public void OnCameraBoundsChanged(Vector4 cameraBounds)
    {
        var renderer = SlimeMapRenderer.Instance;
        if (renderer == null) return;
        if (renderer.VisibleArgsBuffer == null || renderer.VisibleAgentIdsBuffer == null) return;
        if (agentMesh == null || agentMaterial == null) return;

        float spriteAlpha = ZoomLevelController.Instance != null ? ZoomLevelController.Instance.SpriteAlpha : 1f;
        _mpb.SetFloat("_GlobalAlpha", spriteAlpha);

        _mpb.SetBuffer("_VisibleAgentIds", renderer.VisibleAgentIdsBuffer);
        _mpb.SetBuffer("_AgentBuffer", renderer.AgentBuffer);

        if (_spriteArray != null)
        {
            _mpb.SetTexture("_SpriteArray", _spriteArray);
            _mpb.SetVectorArray("_SpriteData", _spriteData);
            _mpb.SetVectorArray("_SpriteScaleAnchor", _spriteScaleAnchor);
        }

        Bounds drawBounds;
        if (renderer.DisplayTarget != null)
        {
            Bounds db = renderer.DisplayTarget.bounds;
            db.Expand(new Vector3(500f, 500f, 1000f));
            drawBounds = db;

            Bounds org = renderer.DisplayTarget.bounds;
            _mpb.SetVector("_MapWorldBounds", new Vector4(org.min.x, org.min.y, org.size.x, org.size.y));
            _mpb.SetVector("_MapSimParams", new Vector4(renderer.Width, renderer.Height, org.center.z, 0));
            
            float tw = TerrainMapRenderer.Instance != null ? TerrainMapRenderer.Instance.Width : renderer.Width;
            float th = TerrainMapRenderer.Instance != null ? TerrainMapRenderer.Instance.Height : renderer.Height;
            _mpb.SetVector("_MapTerrainParams", new Vector4(tw, th, 0, 0));
        }
        else
        {
            float mw = renderer.Width;
            float mh = renderer.Height;
            drawBounds = new Bounds(new Vector3(mw/2f, mh/2f, 0f), new Vector3(mw, mh, 1000f));
            _mpb.SetVector("_MapWorldBounds", new Vector4(0, 0, mw, mh));
            _mpb.SetVector("_MapSimParams", new Vector4(mw, mh, 0, 0));
            _mpb.SetVector("_MapTerrainParams", new Vector4(mw, mh, 0, 0));
        }

        Graphics.DrawMeshInstancedIndirect(agentMesh, 0, agentMaterial, drawBounds,
            renderer.VisibleArgsBuffer, 0, _mpb);
    }

    public void OnExitTactical() { }

    private void OnDestroy()
    {
        if (ZoomLevelController.Instance != null)
            ZoomLevelController.Instance.UnregisterLayer(this);
        if (_spriteArray != null) Destroy(_spriteArray);
    }
}
