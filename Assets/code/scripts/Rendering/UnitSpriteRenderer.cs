using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Rend les sprites des unités statiques (ex: arbres) dans l'espace monde.
/// Rendu via Graphics.DrawMeshInstanced() — un draw call par batch de 1023 instances.
/// Auto-créé au démarrage, DontDestroyOnLoad.
/// </summary>
public class UnitSpriteRenderer : MonoBehaviour
{
    public static UnitSpriteRenderer Instance { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoCreate()
    {
        if (Instance != null) return;
        var go = new GameObject("[UnitSpriteRenderer]");
        go.AddComponent<UnitSpriteRenderer>();
        DontDestroyOnLoad(go);
    }

    private struct SpriteEntry
    {
        public Vector2           pos;
        public SpeciesDefinition def;
    }

    private struct BuildingSpriteEntry
    {
        public Vector2            pos;
        public BuildingDefinition def;
    }

    // Matériaux par spriteName (chargés lazily à la première registration)
    private readonly Dictionary<string, Material>                    materials      = new();
    // Instances à rendre, groupées par spriteName
    private readonly Dictionary<string, List<SpriteEntry>>           entries        = new();
    private readonly Dictionary<string, List<BuildingSpriteEntry>>   buildingEntries = new();

    private BuildingSpriteEntry? previewBuilding   = null;
    private bool                 previewIsWalkable = false;

    private Mesh                 quadMesh;
    private MaterialPropertyBlock _mpb;

    // Cache de matrices pré-alloué pour DrawMeshInstanced (max 1023 par batch, zéro alloc par frame)
    private const int BATCH_SIZE = 1023;
    private readonly Matrix4x4[] _batchMatrices = new Matrix4x4[BATCH_SIZE];

    // ── Unity lifecycle ─────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        quadMesh = CreateQuad();
        _mpb     = new MaterialPropertyBlock();
    }

    /// <summary>
    /// Attend la fin du spawn initial de SlimeMapRenderer, puis re-scanne
    /// tous les agents bloquants avec sprite pour les enregistrer.
    /// </summary>
    private IEnumerator Start()
    {
        while (SlimeMapRenderer.Instance == null || !SlimeMapRenderer.Instance.InitialSpawnDone)
            yield return null;
        yield return null;

        var smr = SlimeMapRenderer.Instance;
        var lib = SpeciesLibrary.Instance;
        if (smr == null || lib == null) yield break;

        entries.Clear();

        int total = 0;
        for (int s = 0; s < smr.numActiveSlots; s++)
        {
            if (!smr.speciesBlocksMovement[s]) continue;
            var def = lib.Get(smr.speciesIds[s]);
            if (def == null || def.spriteTilesW <= 0) continue;

            foreach (Vector2 pos in smr.GetBlockingPositions(s))
            {
                Register(pos, def);
                total++;
            }
        }
        Debug.Log($"[UnitSpriteRenderer] Re-scanné {total} agent(s) bloquants avec sprite.");
    }

    private float _dbgTimer;
    private void LateUpdate()
    {
        var smr = SlimeMapRenderer.Instance;
        if (smr == null || smr.DisplayTarget == null || quadMesh == null) return;

        _dbgTimer += Time.deltaTime;
        if (_dbgTimer >= 3f)
        {
            _dbgTimer = 0f;
            int total = 0;
            foreach (var kv in entries) total += kv.Value.Count;
            var terrain2 = TerrainMapRenderer.Instance;
            float tw = terrain2 != null ? terrain2.Width  : smr.Width;
            float th = terrain2 != null ? terrain2.Height : smr.Height;
            Bounds b2 = smr.DisplayTarget.bounds;
            foreach (var kv in entries)
            {
                if (!materials.TryGetValue(kv.Key, out var m2) || m2 == null) break;
                if (kv.Value.Count == 0) break;
                var e0 = kv.Value[0];
                int tcx0 = (int)(e0.pos.x * tw / smr.Width);
                int tcy0 = (int)(e0.pos.y * th / smr.Height);
                float wx0 = b2.min.x + ((tcx0 + (0.5f - e0.def.spriteAnchorX)*e0.def.spriteTilesW) / tw) * b2.size.x;
                float wy0 = b2.min.y + ((tcy0 + (0.5f - e0.def.spriteAnchorY)*e0.def.spriteTilesH) / th) * b2.size.y;
                float sx0 = e0.def.spriteTilesW * b2.size.x / tw;
                float sy0 = e0.def.spriteTilesH * b2.size.y / th;
                float sz0 = b2.center.z - 0.1f;
                Debug.Log($"[USR] entries={total} pos0=({wx0:F1},{wy0:F1},{sz0:F2}) scale=({sx0:F1},{sy0:F1}) bounds={b2.min}..{b2.max} mapW={smr.Width} terrW={tw}");
                break;
            }
        }

        float spriteAlpha = ZoomLevelController.Instance != null ? ZoomLevelController.Instance.SpriteAlpha : 0f;
        if (spriteAlpha <= 0f) return;
        _mpb.SetColor("_Color", new Color(1f, 1f, 1f, spriteAlpha));

        Bounds b     = smr.DisplayTarget.bounds;
        float  mapW  = smr.Width;
        float  mapH  = smr.Height;
        float  zBase = b.center.z;

        var   terrain = TerrainMapRenderer.Instance;
        float terrW   = terrain != null ? terrain.Width  : mapW;
        float terrH   = terrain != null ? terrain.Height : mapH;

        // Précalculs communs à toutes les entrées
        float invTerrW   = 1f / terrW;
        float invTerrH   = 1f / terrH;
        float bSizeX     = b.size.x;
        float bSizeY     = b.size.y;

        float bMinX      = b.min.x;
        float bMinY      = b.min.y;
        float invBSizeY  = 1f / Mathf.Max(0.0001f, bSizeY);
        float zBaseM1    = zBase - 1.0f;

        // ── Unités (species) ──────────────────────────────────────────
        foreach (var kv in entries)
        {
            if (!materials.TryGetValue(kv.Key, out Material mat) || mat == null) continue;
            var list = kv.Value;
            int count = list.Count;
            int batchStart = 0;

            while (batchStart < count)
            {
                int batchCount = Mathf.Min(BATCH_SIZE, count - batchStart);
                for (int i = 0; i < batchCount; i++)
                {
                    var e   = list[batchStart + i];
                    var def = e.def;

                    int   tcx  = (int)(e.pos.x * terrW / mapW);
                    int   tcy  = (int)(e.pos.y * terrH / mapH);
                    float stW  = def.spriteTilesW;
                    float stH  = def.spriteTilesH;

                    float anchorWorldY = bMinY + (tcy * invTerrH) * bSizeY;
                    float csxT = tcx + (0.5f - def.spriteAnchorX) * stW;
                    float csyT = tcy + (0.5f - def.spriteAnchorY) * stH;

                    float wx = bMinX + (csxT * invTerrW) * bSizeX;
                    float wy = bMinY + (csyT * invTerrH) * bSizeY;

                    float normY  = Mathf.Clamp01((anchorWorldY - bMinY) * invBSizeY);
                    float sz     = zBaseM1 + normY * 0.9f;
                    float scaleX = stW * bSizeX * invTerrW;
                    float scaleY = stH * bSizeY * invTerrH;

                    _batchMatrices[i] = Matrix4x4.TRS(
                        new Vector3(wx, wy, sz),
                        Quaternion.identity,
                        new Vector3(scaleX, scaleY, 1f));
                }
                Graphics.DrawMeshInstanced(quadMesh, 0, mat, _batchMatrices, batchCount, _mpb,
                    UnityEngine.Rendering.ShadowCastingMode.Off, false, 0, null);
                batchStart += batchCount;
            }
        }

        // ── Bâtiments ─────────────────────────────────────────────────
        foreach (var kv in buildingEntries)
        {
            if (!materials.TryGetValue(kv.Key, out Material mat) || mat == null) continue;
            var list  = kv.Value;
            int count = list.Count;
            int batchStart = 0;

            while (batchStart < count)
            {
                int batchCount = Mathf.Min(BATCH_SIZE, count - batchStart);
                for (int i = 0; i < batchCount; i++)
                {
                    var e   = list[batchStart + i];
                    var def = e.def;

                    int   tcx  = (int)(e.pos.x * terrW / mapW);
                    int   tcy  = (int)(e.pos.y * terrH / mapH);
                    float stW  = def.spriteTilesW;
                    float stH  = def.spriteTilesH;

                    float anchorWorldY = bMinY + (tcy * invTerrH) * bSizeY;
                    float csxT = tcx + (0.5f - def.spriteAnchorX) * stW;
                    float csyT = tcy + (0.5f - def.spriteAnchorY) * stH;

                    float wx = bMinX + (csxT * invTerrW) * bSizeX;
                    float wy = bMinY + (csyT * invTerrH) * bSizeY;

                    float normY  = Mathf.Clamp01((anchorWorldY - bMinY) * invBSizeY);
                    float sz     = zBaseM1 + normY * 0.9f;
                    float scaleX = stW * bSizeX * invTerrW;
                    float scaleY = stH * bSizeY * invTerrH;

                    _batchMatrices[i] = Matrix4x4.TRS(
                        new Vector3(wx, wy, sz),
                        Quaternion.identity,
                        new Vector3(scaleX, scaleY, 1f));
                }
                Graphics.DrawMeshInstanced(quadMesh, 0, mat, _batchMatrices, batchCount, _mpb,
                    UnityEngine.Rendering.ShadowCastingMode.Off, false, 0, null);
                batchStart += batchCount;
            }
        }

        // ── Preview bâtiment (placement) — toujours DrawMesh car instance unique ──
        if (previewBuilding.HasValue)
        {
            var e   = previewBuilding.Value;
            var def = e.def;

            if (materials.TryGetValue(def.spriteName, out Material mat) && mat != null)
            {
                int   tcx  = (int)(e.pos.x * terrW / mapW);
                int   tcy  = (int)(e.pos.y * terrH / mapH);
                float stW  = def.spriteTilesW;
                float stH  = def.spriteTilesH;

                float anchorWorldY = bMinY + (tcy * invTerrH) * bSizeY;
                float csxT = tcx + (0.5f - def.spriteAnchorX) * stW;
                float csyT = tcy + (0.5f - def.spriteAnchorY) * stH;

                float wx = bMinX + (csxT * invTerrW) * bSizeX;
                float wy = bMinY + (csyT * invTerrH) * bSizeY;

                float normY  = Mathf.Clamp01((anchorWorldY - bMinY) * invBSizeY);
                float sz     = zBaseM1 + normY * 0.9f - 0.05f;
                float scaleX = stW * bSizeX * invTerrW;
                float scaleY = stH * bSizeY * invTerrH;

                var previewMpb = new MaterialPropertyBlock();
                previewMpb.SetColor("_Color", previewIsWalkable
                    ? new Color(0.7f, 1f, 0.7f, spriteAlpha * 0.8f)
                    : new Color(1f, 0.3f, 0.3f, spriteAlpha * 0.8f));

                Graphics.DrawMesh(quadMesh,
                    Matrix4x4.TRS(new Vector3(wx, wy, sz), Quaternion.identity, new Vector3(scaleX, scaleY, 1f)),
                    mat, 0, null, 0, previewMpb);
            }
        }
    }

    private void OnDestroy()
    {
        foreach (var mat in materials.Values)
            if (mat != null) Destroy(mat);
        if (quadMesh != null) Destroy(quadMesh);
    }

    // ── API publique ─────────────────────────────────────────────────

    /// <summary>Enregistre une unité avec sprite pour le rendu.</summary>
    public void Register(Vector2 pos, SpeciesDefinition def)
    {
        if (def == null || def.spriteTilesW <= 0 || string.IsNullOrEmpty(def.spriteName)) return;

        if (!entries.ContainsKey(def.spriteName))
            entries[def.spriteName] = new List<SpriteEntry>();

        entries[def.spriteName].Add(new SpriteEntry { pos = pos, def = def });
        EnsureMaterial(def.spriteName, def.spriteFramePixelW, def.spriteFramePixelH);
    }

    /// <summary>Retire une unité morte du rendu.</summary>
    public void Unregister(Vector2 pos, SpeciesDefinition def)
    {
        if (def == null || !entries.TryGetValue(def.spriteName, out var list)) return;
        for (int i = list.Count - 1; i >= 0; i--)
            if (list[i].pos == pos) { list.RemoveAt(i); break; }
    }

    /// <summary>Enregistre un bâtiment avec sprite pour le rendu.</summary>
    public void RegisterBuilding(Vector2 pos, BuildingDefinition def)
    {
        if (def == null || def.spriteTilesW <= 0 || string.IsNullOrEmpty(def.spriteName)) return;

        if (!buildingEntries.ContainsKey(def.spriteName))
            buildingEntries[def.spriteName] = new List<BuildingSpriteEntry>();

        buildingEntries[def.spriteName].Add(new BuildingSpriteEntry { pos = pos, def = def });
        EnsureMaterial(def.spriteName, def.spriteFramePixelW, def.spriteFramePixelH);
    }

    /// <summary>Retire un bâtiment du rendu.</summary>
    public void UnregisterBuilding(Vector2 pos, BuildingDefinition def)
    {
        if (def == null || !buildingEntries.TryGetValue(def.spriteName, out var list)) return;
        for (int i = list.Count - 1; i >= 0; i--)
            if (list[i].pos == pos) { list.RemoveAt(i); break; }
    }

    /// <summary>Définit le bâtiment en cours de prévisualisation (placement).</summary>
    public void SetPreviewBuilding(Vector2 pos, BuildingDefinition def, bool isWalkable)
    {
        if (def == null || def.spriteTilesW <= 0 || string.IsNullOrEmpty(def.spriteName))
        {
            previewBuilding = null;
            return;
        }

        previewBuilding   = new BuildingSpriteEntry { pos = pos, def = def };
        previewIsWalkable = isWalkable;
        EnsureMaterial(def.spriteName, def.spriteFramePixelW, def.spriteFramePixelH);
    }

    /// <summary>Efface la prévisualisation.</summary>
    public void ClearPreviewBuilding() => previewBuilding = null;

    // ── Chargement texture ───────────────────────────────────────────

    private void EnsureMaterial(string spriteName, int spriteFramePixelW, int spriteFramePixelH)
    {
        if (materials.ContainsKey(spriteName)) return;

        string path = Path.Combine(Application.streamingAssetsPath, "Sprites", spriteName + ".png");
        if (!File.Exists(path))
        {
            Debug.LogWarning($"[SPRITES] Fichier introuvable : {path}");
            materials[spriteName] = null;
            return;
        }

        byte[]    bytes = File.ReadAllBytes(path);
        Texture2D tex   = new Texture2D(2, 2, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode   = TextureWrapMode.Clamp
        };
        tex.LoadImage(bytes);

        Texture2D useTex = tex;
        if (spriteFramePixelW > 0 && spriteFramePixelH > 0
            && spriteFramePixelW <= tex.width && spriteFramePixelH <= tex.height)
        {
            int fw   = spriteFramePixelW;
            int fh   = spriteFramePixelH;
            int srcY = tex.height - fh;
            Color32[] all     = tex.GetPixels32();
            Color32[] cropped = new Color32[fw * fh];
            for (int row = 0; row < fh; row++)
            for (int col = 0; col < fw; col++)
                cropped[row * fw + col] = all[(srcY + row) * tex.width + col];

            int keyed = 0;
            for (int i = 0; i < cropped.Length; i++)
            {
                Color32 px = cropped[i];
                if (px.r > 240 && px.g > 240 && px.b > 240 && px.a > 240)
                { cropped[i] = new Color32(0, 0, 0, 0); keyed++; }
            }
            if (keyed > 0)
                Debug.Log($"[SPRITES] {spriteName} : color-key blanc appliqué à {keyed} pixels");

            useTex = new Texture2D(fw, fh, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode   = TextureWrapMode.Clamp
            };
            useTex.SetPixels32(cropped);
            useTex.Apply();
            Destroy(tex);
            Debug.Log($"[SPRITES] Chargé : {spriteName}, frame extraite {fw}×{fh}px");
        }
        else
        {
            Debug.Log($"[SPRITES] Chargé : {spriteName} ({tex.width}×{tex.height}px) texture complète");
        }

        // Le shader doit supporter GPU instancing (INSTANCING_ON) pour DrawMeshInstanced
        var shader = Shader.Find("Evolution/UnitSprite") ?? Shader.Find("Sprites/Default");
        var mat    = new Material(shader) { mainTexture = useTex };
        mat.enableInstancing = true;

        materials[spriteName] = mat;
    }

    // ── Mesh quad centré (−0.5…+0.5) ────────────────────────────────

    private static Mesh CreateQuad()
    {
        var mesh = new Mesh { name = "UnitSpriteQuad" };
        mesh.vertices  = new Vector3[]
        {
            new(-0.5f, -0.5f, 0f),
            new( 0.5f, -0.5f, 0f),
            new( 0.5f,  0.5f, 0f),
            new(-0.5f,  0.5f, 0f),
        };
        mesh.uv        = new Vector2[] { new(0,0), new(1,0), new(1,1), new(0,1) };
        mesh.triangles = new int[]     { 0, 1, 2, 0, 2, 3 };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }
}
