using System.Collections.Generic;
using System.IO;
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

    // Matériaux par spriteName (chargés lazily à la première registration)
    private readonly Dictionary<string, Material>        materials = new Dictionary<string, Material>();
    // Instances à rendre, groupées par spriteName (une DrawMesh par instance)
    private readonly Dictionary<string, List<SpriteEntry>> entries = new Dictionary<string, List<SpriteEntry>>();

    private Mesh quadMesh;

    // ── Unity lifecycle ─────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        quadMesh = CreateQuad();
    }

    private void LateUpdate()
    {
        var smr = SlimeMapRenderer.Instance;
        if (smr == null || smr.DisplayTarget == null || quadMesh == null) return;

        Bounds b    = smr.DisplayTarget.bounds;
        float  mapW = smr.Width;
        float  mapH = smr.Height;
        float  sz   = b.center.z - 0.1f; // légèrement devant le quad slime map

        foreach (var kv in entries)
        {
            if (!materials.TryGetValue(kv.Key, out Material mat) || mat == null) continue;

            foreach (var e in kv.Value)
            {
                SpeciesDefinition def = e.def;

                // Centre du sprite en pixels sim (en tenant compte de l'ancre)
                float csx = e.pos.x + (0.5f - def.spriteAnchorX) * def.spriteTilesW;
                float csy = e.pos.y + (0.5f - def.spriteAnchorY) * def.spriteTilesH;

                // Conversion pixels sim → coordonnées monde
                float wx = b.min.x + (csx / mapW) * b.size.x;
                float wy = b.min.y + (csy / mapH) * b.size.y;

                // Taille monde = tiles × (taille monde par pixel sim)
                float scaleX = def.spriteTilesW * b.size.x / mapW;
                float scaleY = def.spriteTilesH * b.size.y / mapH;

                Matrix4x4 matrix = Matrix4x4.TRS(
                    new Vector3(wx, wy, sz),
                    Quaternion.identity,
                    new Vector3(scaleX, scaleY, 1f));

                Graphics.DrawMesh(quadMesh, matrix, mat, 0);
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
        EnsureMaterial(def.spriteName);
    }

    /// <summary>Retire une unité morte du rendu.</summary>
    public void Unregister(Vector2 pos, SpeciesDefinition def)
    {
        if (def == null || !entries.TryGetValue(def.spriteName, out var list)) return;
        for (int i = list.Count - 1; i >= 0; i--)
            if (list[i].pos == pos) { list.RemoveAt(i); break; }
    }

    // ── Chargement texture ───────────────────────────────────────────

    private void EnsureMaterial(string spriteName)
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
        tex.LoadImage(bytes); // redimensionne automatiquement

        var mat = new Material(Shader.Find("Unlit/Transparent"))
        {
            mainTexture = tex
        };
        materials[spriteName] = mat;
        Debug.Log($"[SPRITES] Chargé : {spriteName} ({tex.width}×{tex.height}px)");
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
