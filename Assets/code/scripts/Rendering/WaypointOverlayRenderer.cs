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

    public bool[] ShowSpeciesOverlay = new bool[6] { true, true, true, true, true, true };

    // Images POI : nom du bâtiment → texture chargée depuis Resources/POI/
    private readonly Dictionary<string, Texture2D> poiImages = new Dictionary<string, Texture2D>();

    // Palette matches the compute shader palette
    private static readonly Color[] Palette = new Color[]
    {
        new Color(1f, 0f, 0f, 1f),
        new Color(0f, 1f, 0f, 1f),
        new Color(0f, 0f, 1f, 1f),
        new Color(0f, 1f, 1f, 1f),
        new Color(1f, 0f, 1f, 1f),
        new Color(1f, 1f, 0f, 1f),
    };

    private Material glMaterial;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
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
        var entries = new[] { ("Poumon", "POI/poumon"), ("Rate", "POI/rate") };
        foreach (var (name, path) in entries)
        {
            var tex = Resources.Load<Texture2D>(path);
            if (tex != null) poiImages[name] = tex;
            else Debug.LogWarning($"[POI] Texture introuvable : Resources/{path}");
        }
    }

    private void OnGUI()
    {
        if (WaypointManager.Instance == null || SlimeMapRenderer.Instance == null) return;

        WaypointData[] waypoints = WaypointManager.Instance.GetWaypoints();
        if (waypoints == null || waypoints.Length == 0) return;

        float size = 48f;

        for (int i = 0; i < waypoints.Length; i++)
        {
            var wp = waypoints[i];
            if (wp.speciesIndex < 0 || wp.speciesIndex >= 6) continue;
            if (!ShowSpeciesOverlay[wp.speciesIndex]) continue;

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
            if (wp.speciesIndex < 0 || wp.speciesIndex >= 6) continue;
            if (!ShowSpeciesOverlay[wp.speciesIndex]) continue;

            Color col = Palette[wp.speciesIndex];
            // Source = full color, Destination = 50% alpha
            col.a = wp.type == 0 ? 1f : 0.5f;
            float radius = wp.type == 0 ? 8f : 12f;

            Vector3 worldPos = MapToWorld(wp.position);
            DrawCircle(worldPos, radius, col, 12);
        }

        // Draw animated dashed lines along the smoothed path (or direct line as fallback)
        float dashOffset = Time.time % 1f;
        for (int s = 0; s < 6; s++)
        {
            if (!ShowSpeciesOverlay[s]) continue;

            Color lineCol = Palette[s];
            lineCol.a = 0.7f;

            Vector2[] smoothPath = WaypointManager.Instance.GetSmoothedPath(s);
            if (smoothPath != null && smoothPath.Length >= 2)
            {
                // Dessine le chemin lissé segment par segment
                for (int p = 0; p < smoothPath.Length - 1; p++)
                    DrawDashedLine(MapToWorld(smoothPath[p]), MapToWorld(smoothPath[p + 1]),
                                   lineCol, dashOffset, 0.4f);
            }
            else
            {
                // Fallback : ligne droite Source → Destination
                Vector2? src = null, dst = null;
                for (int i = 0; i < waypoints.Length; i++)
                {
                    if (waypoints[i].speciesIndex != s) continue;
                    if (waypoints[i].type == 0 && src == null) src = waypoints[i].position;
                    if (waypoints[i].type == 1 && dst == null) dst = waypoints[i].position;
                }
                if (src == null || dst == null) continue;
                DrawDashedLine(MapToWorld(src.Value), MapToWorld(dst.Value), lineCol, dashOffset, 0.05f);
            }
        }

        GL.PopMatrix();
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
