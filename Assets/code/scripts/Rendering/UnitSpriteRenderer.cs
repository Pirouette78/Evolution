using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

/// <summary>
/// Rend les sprites des unités statiques (ex: arbres) dans l'espace monde.
/// Les unités avec spriteTilesW > 0 s'enregistrent via Register() / Unregister()
/// (appelés depuis SlimeMapRenderer à l'apparition/mort de l'unité).
/// Rendu via Graphics.DrawMesh() en LateUpdate — zéro allocation par frame.
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
        public Vector2          pos;
        public SpeciesDefinition def;
    }

    private struct BuildingSpriteEntry
    {
        public Vector2          pos;
        public BuildingDefinition def;
    }

    // Matériaux par spriteName (chargés lazily à la première registration)
    private readonly Dictionary<string, Material>        materials = new Dictionary<string, Material>();
    // Instances à rendre, groupées par spriteName (une DrawMesh par instance)
    private readonly Dictionary<string, List<SpriteEntry>> entries = new Dictionary<string, List<SpriteEntry>>();
    private readonly Dictionary<string, List<BuildingSpriteEntry>> buildingEntries = new Dictionary<string, List<BuildingSpriteEntry>>();

    private BuildingSpriteEntry? previewBuilding = null;
    private bool previewIsWalkable = false;

    private Mesh quadMesh;
    private MaterialPropertyBlock _mpb;

    // ── Unity lifecycle ─────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        quadMesh = CreateQuad();
        _mpb = new MaterialPropertyBlock();
    }

    /// <summary>
    /// Re-scanne les agents bloquants déjà présents dans le buffer GPU,
    /// au cas où ils auraient été spawnés avant que Instance soit créé.
    /// </summary>
    /// <summary>
    /// Attend la fin du spawn initial de SlimeMapRenderer, puis re-scanne
    /// tous les agents bloquants avec sprite pour les enregistrer.
    /// Nécessaire car le spawn se fait en coroutine et peut dépasser Start().
    /// </summary>
    private IEnumerator Start()
    {
        // Attendre que SlimeMapRenderer ait fini son spawn initial
        while (SlimeMapRenderer.Instance == null || !SlimeMapRenderer.Instance.InitialSpawnDone)
            yield return null;
        yield return null; // une frame supplémentaire pour que RegisterBlockingAgents finisse

        var smr = SlimeMapRenderer.Instance;
        var lib = SpeciesLibrary.Instance;
        if (smr == null || lib == null) yield break;

        // Vider avant de re-scanner : RegisterBlockingAgents a déjà enregistré
        // les arbres initiaux (Instance existait), donc sans ce clear ils seraient en double.
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
            // Log first entry
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

        Bounds b    = smr.DisplayTarget.bounds;
        float  mapW = smr.Width;
        float  mapH = smr.Height;
        float  zBase = b.center.z; // Z de référence de la carte

        // Résolution de la grille terrain (même snap entier que le système de blocage)
        var   terrain = TerrainMapRenderer.Instance;
        float terrW   = terrain != null ? terrain.Width  : mapW;
        float terrH   = terrain != null ? terrain.Height : mapH;

        foreach (var kv in entries)
        {
            if (!materials.TryGetValue(kv.Key, out Material mat) || mat == null) continue;

            foreach (var e in kv.Value)
            {
                SpeciesDefinition def = e.def;

                // Position d'ancre en pixels terrain (entiers, identique au blocage)
                int   tcx   = (int)(e.pos.x * terrW / mapW);
                int   tcy   = (int)(e.pos.y * terrH / mapH);

                // spriteTilesW/H = dimensions en pixels terrain (échelle 1:1 avec la grille terrain)
                float stW   = def.spriteTilesW;
                float stH   = def.spriteTilesH;

                // L'ancre Y bas (spriteAnchorY) représente le pied de l'objet → c'est ce Y qu'on utilise pour le sorting
                float anchorWorldY = b.min.y + (tcy / terrH) * b.size.y;

                // Centre du sprite en pixels terrain (ancre appliquée)
                float csxT  = tcx + (0.5f - def.spriteAnchorX) * stW;
                float csyT  = tcy + (0.5f - def.spriteAnchorY) * stH;

                // Conversion pixels terrain → coordonnées monde
                float wx = b.min.x + (csxT / terrW) * b.size.x;
                float wy = b.min.y + (csyT / terrH) * b.size.y;

                // Y-Sorting : bas de l'écran (anchorWorldY petit) → Z petit → devant
                float normY = Mathf.Clamp01((anchorWorldY - b.min.y) / Mathf.Max(0.0001f, b.size.y));
                float sz    = (zBase - 1.0f) + normY * 0.9f;

                // Taille monde
                float scaleX = stW * b.size.x / terrW;
                float scaleY = stH * b.size.y / terrH;

                Matrix4x4 matrix = Matrix4x4.TRS(
                    new Vector3(wx, wy, sz),
                    Quaternion.identity,
                    new Vector3(scaleX, scaleY, 1f));

                Graphics.DrawMesh(quadMesh, matrix, mat, 0, null, 0, _mpb);
            }
        }

        foreach (var kv in buildingEntries)
        {
            if (!materials.TryGetValue(kv.Key, out Material mat) || mat == null) continue;

            foreach (var e in kv.Value)
            {
                BuildingDefinition def = e.def;

                int   tcx   = (int)(e.pos.x * terrW / mapW);
                int   tcy   = (int)(e.pos.y * terrH / mapH);

                float stW   = def.spriteTilesW;
                float stH   = def.spriteTilesH;

                float anchorWorldY = b.min.y + (tcy / terrH) * b.size.y;
                float csxT  = tcx + (0.5f - def.spriteAnchorX) * stW;
                float csyT  = tcy + (0.5f - def.spriteAnchorY) * stH;

                float wx = b.min.x + (csxT / terrW) * b.size.x;
                float wy = b.min.y + (csyT / terrH) * b.size.y;

                float normY = Mathf.Clamp01((anchorWorldY - b.min.y) / Mathf.Max(0.0001f, b.size.y));
                float sz    = (zBase - 1.0f) + normY * 0.9f;

                float scaleX = stW * b.size.x / terrW;
                float scaleY = stH * b.size.y / terrH;

                Matrix4x4 matrix = Matrix4x4.TRS(
                    new Vector3(wx, wy, sz),
                    Quaternion.identity,
                    new Vector3(scaleX, scaleY, 1f));

                Graphics.DrawMesh(quadMesh, matrix, mat, 0, null, 0, _mpb);
            }
        }

        if (previewBuilding.HasValue)
        {
            var e = previewBuilding.Value;
            var def = e.def;

            if (materials.TryGetValue(def.spriteName, out Material mat) && mat != null)
            {
                int   tcx   = (int)(e.pos.x * terrW / mapW);
                int   tcy   = (int)(e.pos.y * terrH / mapH);

                float stW   = def.spriteTilesW;
                float stH   = def.spriteTilesH;

                float anchorWorldY = b.min.y + (tcy / terrH) * b.size.y;
                float csxT  = tcx + (0.5f - def.spriteAnchorX) * stW;
                float csyT  = tcy + (0.5f - def.spriteAnchorY) * stH;

                float wx = b.min.x + (csxT / terrW) * b.size.x;
                float wy = b.min.y + (csyT / terrH) * b.size.y;

                float normY = Mathf.Clamp01((anchorWorldY - b.min.y) / Mathf.Max(0.0001f, b.size.y));
                float sz    = (zBase - 1.0f) + normY * 0.9f - 0.05f; // Slightly in front

                float scaleX = stW * b.size.x / terrW;
                float scaleY = stH * b.size.y / terrH;

                Matrix4x4 matrix = Matrix4x4.TRS(
                    new Vector3(wx, wy, sz),
                    Quaternion.identity,
                    new Vector3(scaleX, scaleY, 1f));

                _mpb.SetColor("_Color", previewIsWalkable 
                    ? new Color(0.7f, 1f, 0.7f, spriteAlpha * 0.8f) 
                    : new Color(1f, 0.3f, 0.3f, spriteAlpha * 0.8f));
                Graphics.DrawMesh(quadMesh, matrix, mat, 0, null, 0, _mpb);
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

        previewBuilding = new BuildingSpriteEntry { pos = pos, def = def };
        previewIsWalkable = isWalkable;
        EnsureMaterial(def.spriteName, def.spriteFramePixelW, def.spriteFramePixelH);
    }

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

        // Si spriteFramePixelW/H est défini, extraire la première frame en nouveaux pixels
        Texture2D useTex = tex;
        if (spriteFramePixelW > 0 && spriteFramePixelH > 0
            && spriteFramePixelW <= tex.width && spriteFramePixelH <= tex.height)
        {
            int fw = spriteFramePixelW;
            int fh = spriteFramePixelH;
            // Unity Texture2D : y=0 en BAS → première frame PNG (haut) = srcY = tex.height - fh
            int srcY = tex.height - fh;
            Color32[] all    = tex.GetPixels32();
            Color32[] cropped = new Color32[fw * fh];
            for (int row = 0; row < fh; row++)
            for (int col = 0; col < fw; col++)
                cropped[row * fw + col] = all[(srcY + row) * tex.width + col];

            // Color-key blanc : pixels presque-blancs et (presque-)opaques → transparent
            // Nécessaire quand le fond du PNG est blanc opaque (même si le sprite a un alpha partiel)
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

        // Evolution/UnitSprite : ZTest Always, alpha correct, Y-sorting gèré par le Z calculé
        var mat = new Material(Shader.Find("Evolution/UnitSprite") ?? Shader.Find("Sprites/Default"))
        {
            mainTexture = useTex
        };

        materials[spriteName] = mat;
    }

    // ── Mesh quad centré (−0.5…+0.5) ────────────────────────────────

    private static Mesh CreateQuad()
    {
        var mesh = new Mesh { name = "UnitSpriteQuad" };
        mesh.vertices  = new Vector3[]
        {
            new Vector3(-0.5f, -0.5f, 0f),
            new Vector3( 0.5f, -0.5f, 0f),
            new Vector3( 0.5f,  0.5f, 0f),
            new Vector3(-0.5f,  0.5f, 0f),
        };
        mesh.uv        = new Vector2[] { new(0,0), new(1,0), new(1,1), new(0,1) };
        mesh.triangles = new int[]     { 0, 1, 2, 0, 2, 3 };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }
}
