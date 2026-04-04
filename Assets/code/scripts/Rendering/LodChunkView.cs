using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Manages the terrain preview quads for LOD 1 and LOD 2 chunks surrounding the active simulation chunk.
/// Quads are placed adjacent to SlimeMapRenderer.DisplayTarget in world space and show low-res terrain textures.
/// Created and driven exclusively by WorldChunkRegistry — do not add to scene manually.
///
/// Layout around the active chunk (LOD 0 = centre, not managed here):
///   LOD2 LOD2 LOD2 LOD2 LOD2
///   LOD2 LOD1 LOD1 LOD1 LOD2
///   LOD2 LOD1  [0]  LOD1 LOD2
///   LOD2 LOD1 LOD1 LOD1 LOD2
///   LOD2 LOD2 LOD2 LOD2 LOD2
/// </summary>
public class LodChunkView : MonoBehaviour
{
    // ── Config ───────────────────────────────────────────────────────
    private int lod1Radius;
    private int lod2Radius;

    // ── Per-quad state ───────────────────────────────────────────────
    private struct QuadEntry
    {
        public GameObject    go;
        public MeshRenderer  mr;
    }

    /// <summary>Key = chunk offset relative to active chunk (dx, dy).</summary>
    private readonly Dictionary<Vector2Int, QuadEntry> quads = new();
    private MaterialPropertyBlock mpb;

    private Material lodMaterial;

    /// <summary>
    /// When false (during a transition), ApplyTexture calls will not show quads.
    /// Prevents a quad from flickering on during the transition frame.
    /// </summary>
    private bool showQuads;

    // ── Shared quad mesh (created once) ─────────────────────────────
    private static Mesh sharedQuadMesh;

    private static Mesh GetQuadMesh()
    {
        if (sharedQuadMesh != null) return sharedQuadMesh;
        sharedQuadMesh = new Mesh { name = "LodChunkQuad" };
        sharedQuadMesh.SetVertices(new[]
        {
            new Vector3(-0.5f, -0.5f, 0f),
            new Vector3( 0.5f, -0.5f, 0f),
            new Vector3( 0.5f,  0.5f, 0f),
            new Vector3(-0.5f,  0.5f, 0f)
        });
        sharedQuadMesh.SetUVs(0, new[]
        {
            new Vector2(0f, 0f), new Vector2(1f, 0f),
            new Vector2(1f, 1f), new Vector2(0f, 1f)
        });
        sharedQuadMesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
        sharedQuadMesh.RecalculateNormals();
        return sharedQuadMesh;
    }

    // ── Public API ───────────────────────────────────────────────────

    /// <summary>Called once by WorldChunkRegistry.Init(). Creates the quad pool.</summary>
    public void Init(int lod1Radius, int lod2Radius)
    {
        this.lod1Radius = lod1Radius;
        this.lod2Radius = lod2Radius;

        mpb         = new MaterialPropertyBlock();
        lodMaterial = BuildMaterial();

        for (int dy = -lod2Radius; dy <= lod2Radius; dy++)
        for (int dx = -lod2Radius; dx <= lod2Radius; dx++)
        {
            if (dx == 0 && dy == 0) continue;
            CreateQuad(new Vector2Int(dx, dy));
        }
    }

    /// <summary>
    /// Refreshes all quad positions and textures after the active chunk has changed.
    /// Called by WorldChunkRegistry at the end of every transition.
    /// </summary>
    public void RefreshAll(Vector2Int activeChunk, WorldChunkRegistry registry)
    {
        showQuads = true;
        RepositionAll();

        foreach (var kv in quads)
        {
            Vector2Int offset     = kv.Key;
            Vector2Int worldCoord = activeChunk + offset;

            if (!registry.IsValidChunk(worldCoord))
            {
                kv.Value.go.SetActive(false);
                continue;
            }

            int       dist = ChebyshevDist(offset);
            var       (lod1Tex, lod2Tex) = registry.GetLodTextures(worldCoord);
            Texture2D tex = dist <= lod1Radius ? lod1Tex : lod2Tex;
            SetQuadTexture(kv.Value, tex);
        }
    }

    /// <summary>
    /// Updates a single quad's texture when its chunk finishes pre-loading.
    /// offset = worldCoord - activeChunk.
    /// </summary>
    public void SetChunkTexture(Vector2Int offset, Texture2D lod1Tex, Texture2D lod2Tex)
    {
        if (!quads.TryGetValue(offset, out var entry)) return;
        int       dist = ChebyshevDist(offset);
        Texture2D tex  = dist <= lod1Radius ? lod1Tex : lod2Tex;
        SetQuadTexture(entry, tex);
    }

    /// <summary>Hides all LOD quads (called at the start of a chunk transition).</summary>
    public void HideAll()
    {
        showQuads = false;
        foreach (var kv in quads) kv.Value.go.SetActive(false);
    }

    // ── Private helpers ──────────────────────────────────────────────

    private void CreateQuad(Vector2Int offset)
    {
        var go = new GameObject($"LodChunk [{offset.x:+0;-0},{offset.y:+0;-0}]")
        {
            hideFlags = HideFlags.DontSave
        };
        go.transform.SetParent(transform, worldPositionStays: false);
        go.SetActive(false);

        var mf = go.AddComponent<MeshFilter>();
        mf.sharedMesh = GetQuadMesh();

        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial      = lodMaterial;
        mr.shadowCastingMode   = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows      = false;
        mr.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;

        quads[offset] = new QuadEntry { go = go, mr = mr };
    }

    private void SetQuadTexture(in QuadEntry entry, Texture2D tex)
    {
        if (!showQuads || tex == null)
        {
            entry.go.SetActive(false);
            return;
        }
        mpb.Clear();
        mpb.SetTexture("_MainTex", tex);    // BiRP / Unlit
        mpb.SetTexture("_BaseMap", tex);    // URP Lit/Unlit
        entry.mr.SetPropertyBlock(mpb);
        entry.go.SetActive(true);
    }

    /// <summary>Repositions every quad relative to the main simulation quad's current bounds.</summary>
    private void RepositionAll()
    {
        var smr = SlimeMapRenderer.Instance;
        if (smr?.DisplayTarget == null) return;

        Bounds b = smr.DisplayTarget.bounds;
        if (b.size.x <= 0f || b.size.y <= 0f) return;

        foreach (var kv in quads)
        {
            Vector2Int offset = kv.Key;
            // Place slightly behind the main quad (higher z = further from camera in typical setup)
            var pos = new Vector3(
                b.center.x + offset.x * b.size.x,
                b.center.y + offset.y * b.size.y,
                b.center.z + 0.01f
            );
            kv.Value.go.transform.position   = pos;
            kv.Value.go.transform.localScale  = new Vector3(b.size.x, b.size.y, 1f);
        }
    }

    private Material BuildMaterial()
    {
        // Prefer to clone the terrain display material so the colour ramp matches exactly.
        var terrainMat = TerrainMapRenderer.Instance?.DisplayTarget?.sharedMaterial;
        Material mat;
        if (terrainMat != null)
        {
            mat = new Material(terrainMat) { hideFlags = HideFlags.HideAndDontSave };
            // Force full opacity if the material has an alpha property
            if (mat.HasProperty("_Alpha"))     mat.SetFloat("_Alpha",     1f);
            if (mat.HasProperty("_AlphaClip")) mat.SetFloat("_AlphaClip", 0f);
        }
        else
        {
            // Fallback: simple unlit shader that works in both BiRP and URP
            var shader = Shader.Find("Sprites/Default")
                      ?? Shader.Find("Universal Render Pipeline/Unlit")
                      ?? Shader.Find("Unlit/Texture");
            mat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        }
        return mat;
    }

    private static int ChebyshevDist(Vector2Int v)
        => Mathf.Max(Mathf.Abs(v.x), Mathf.Abs(v.y));

    private void OnDestroy()
    {
        if (lodMaterial != null) Destroy(lodMaterial);
        // Child GameObjects are destroyed automatically
    }
}
