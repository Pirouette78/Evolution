using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Draws a per-species GL overlay showing waypoint positions and paths.
/// Attach to any active GameObject. Requires a camera to trigger OnRenderObject.
/// </summary>
public class WaypointOverlayRenderer : MonoBehaviour
{
    public static WaypointOverlayRenderer Instance { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoCreate()
    {
        if (Instance != null) return;
        var go = new GameObject("[WaypointOverlayRenderer]");
        go.AddComponent<WaypointOverlayRenderer>();
        DontDestroyOnLoad(go);
    }

    public bool[] ShowSpeciesOverlay = new bool[SlimeMapRenderer.MaxSlots];
    public bool   ShowPOI = true; // toggle global — contrôlé par le bouton "Afficher POI"

    // Images POI : id du bâtiment (JSON) → texture chargée depuis Resources/
    private readonly Dictionary<string, Texture2D> poiImages = new Dictionary<string, Texture2D>();

    private Material glMaterial;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        for (int i = 0; i < SlimeMapRenderer.MaxSlots; i++) ShowSpeciesOverlay[i] = true;

        LoadPoiImages();

        // Create an unlit, blended material for GL drawing
        var shader = Shader.Find("Hidden/Internal-Colored");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        glMaterial = new Material(shader)
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        glMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        glMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        glMaterial.SetInt("_Cull",     (int)UnityEngine.Rendering.CullMode.Off);
        glMaterial.SetInt("_ZWrite",   0);
        glMaterial.SetInt("_ZTest",    (int)UnityEngine.Rendering.CompareFunction.Always);
    }

    private void LoadPoiImages()
    {
        if (BuildingLibrary.Instance == null) return;
        foreach (var def in BuildingLibrary.Instance.GetAll())
        {
            if (string.IsNullOrEmpty(def.poiImagePath)) continue;
            var tex = Resources.Load<Texture2D>(def.poiImagePath);
            if (tex != null) poiImages[def.id] = tex;
            else Debug.LogWarning($"[POI] Texture introuvable : Resources/{def.poiImagePath}");
        }
    }

    private void OnGUI()
    {
        if (!ShowPOI) return;
        if (WaypointManager.Instance == null || SlimeMapRenderer.Instance == null) return;

        WaypointData[] waypoints = WaypointManager.Instance.GetWaypoints();
        if (waypoints == null || waypoints.Length == 0) return;

        float size = 48f;

        for (int i = 0; i < waypoints.Length; i++)
        {
            var wp = waypoints[i];
            if (wp.speciesIndex < 0 || wp.speciesIndex >= SlimeMapRenderer.MaxSlots) continue;

            string name = WaypointManager.Instance.GetWaypointName(i);
            if (!poiImages.TryGetValue(name, out Texture2D tex) || tex == null) continue;

            // Passe par l'espace monde pour suivre les mouvements de caméra
            Vector3 worldPos = MapToWorld(wp.position);
            Vector3 vp       = Camera.main.WorldToViewportPoint(worldPos);
            if (vp.z < 0f) continue;

            float guiX = vp.x * Screen.width  - size * 0.5f;
            float guiY = (1f - vp.y) * Screen.height - size * 0.5f;
            GUI.DrawTexture(new Rect(guiX, guiY, size, size), tex, ScaleMode.ScaleToFit, true);
        }
    }

    private void OnRenderObject()
    {
        if (!ShowPOI) return;
        if (WaypointManager.Instance == null || SlimeMapRenderer.Instance == null) return;

        WaypointData[] waypoints = WaypointManager.Instance.GetWaypoints();
        if (waypoints == null || waypoints.Length == 0) return;

        if (Camera.current == null) return;
        glMaterial.SetPass(0);
        GL.PushMatrix();
        GL.LoadProjectionMatrix(Camera.current.projectionMatrix);
        GL.modelview = Camera.current.worldToCameraMatrix;

        // Draw circles at each waypoint
        for (int i = 0; i < waypoints.Length; i++)
        {
            var wp = waypoints[i];
            if (wp.speciesIndex < 0 || wp.speciesIndex >= SlimeMapRenderer.MaxSlots) continue;

            Color col = GetSlotColor(wp.speciesIndex);
            // Source = full color, Destination = 50% alpha
            col.a = wp.type == 0 ? 1f : 0.5f;
            float radius = wp.type == 0 ? 8f : 12f;

            Vector3 worldPos = MapToWorld(wp.position);
            DrawCircle(worldPos, radius, col, 12);
        }

        // Draw animated dashed lines along the smoothed path (or direct line as fallback)
        float dashOffset = Time.time % 1f;
        int numSlots = SlimeMapRenderer.Instance?.numActiveSlots ?? 16;
        for (int s = 0; s < numSlots; s++)
        {

            Color lineCol = GetSlotColor(s);
            lineCol.a = 0.7f;

            // Dessine un chemin lissé par destination (supporte plusieurs destinations)
            var smoothPaths = WaypointManager.Instance.GetSmoothedPathsForSpecies(s);
            if (smoothPaths != null && smoothPaths.Count > 0)
            {
                foreach (var path in smoothPaths)
                    for (int p = 0; p < path.Length - 1; p++)
                        DrawDashedLine(MapToWorld(path[p]), MapToWorld(path[p + 1]),
                                       lineCol, dashOffset, 0.4f);
            }
            else
            {
                // Fallback : ligne droite Source → chaque Destination
                Vector2? src = null;
                for (int i = 0; i < waypoints.Length; i++)
                    if (waypoints[i].speciesIndex == s && waypoints[i].type == 0) { src = waypoints[i].position; break; }
                if (src == null) continue;

                for (int i = 0; i < waypoints.Length; i++)
                {
                    if (waypoints[i].speciesIndex != s || waypoints[i].type != 1) continue;
                    DrawDashedLine(MapToWorld(src.Value), MapToWorld(waypoints[i].position),
                                   lineCol, dashOffset, 0.05f);
                }
            }
        }

        GL.PopMatrix();
    }

    private static Color GetSlotColor(int slot)
    {
        var smr = SlimeMapRenderer.Instance;
        if (smr != null && slot >= 0 && slot < smr.slotColors.Length)
        {
            var v = smr.slotColors[slot];
            return new Color(v.x, v.y, v.z, 1f);
        }
        float hue = (slot % SlimeMapRenderer.MaxSlots) / (float)SlimeMapRenderer.MaxSlots;
        return Color.HSVToRGB(hue, 1f, 1f);
    }

    private Vector3 MapToWorld(Vector2 pixelPos)
    {
        var renderer = SlimeMapRenderer.Instance.DisplayTarget;
        if (renderer == null) return Vector3.zero;

        Bounds b = renderer.bounds;
        int mapW = SlimeMapRenderer.Instance.Width;
        int mapH = SlimeMapRenderer.Instance.Height;

        float wx = b.min.x + (pixelPos.x / mapW) * b.size.x;
        float wy = b.min.y + (pixelPos.y / mapH) * b.size.y;
        return new Vector3(wx, wy, b.center.z - 0.01f);
    }

    private void DrawCircle(Vector3 center, float pixelRadius, Color color, int segments)
    {
        var renderer = SlimeMapRenderer.Instance.DisplayTarget;
        if (renderer == null) return;
        float worldRadius = pixelRadius / SlimeMapRenderer.Instance.Width * renderer.bounds.size.x;

        GL.Begin(GL.LINE_STRIP);
        GL.Color(color);
        for (int i = 0; i <= segments; i++)
        {
            float angle = i * Mathf.PI * 2f / segments;
            GL.Vertex(center + new Vector3(Mathf.Cos(angle) * worldRadius, Mathf.Sin(angle) * worldRadius, 0));
        }
        GL.End();
    }

    private void DrawDashedLine(Vector3 from, Vector3 to, Color color, float offset, float dashFraction)
    {
        float totalLen = Vector3.Distance(from, to);
        if (totalLen < 0.001f) return;

        int dashCount   = Mathf.Max(4, Mathf.RoundToInt(totalLen * 20f));
        float dashLen   = dashFraction;
        float gapLen    = 1f - dashLen;

        GL.Begin(GL.LINES);
        GL.Color(color);

        for (int d = 0; d < dashCount; d++)
        {
            float t0 = ((float)d / dashCount + offset) % 1f;
            float t1 = t0 + dashLen / dashCount;
            t1 = Mathf.Min(t1, ((float)(d + 1)) / dashCount);

            GL.Vertex(Vector3.Lerp(from, to, t0));
            GL.Vertex(Vector3.Lerp(from, to, t1));
        }
        GL.End();
    }

    private void OnDestroy()
    {
        if (glMaterial != null) Destroy(glMaterial);
    }
}
