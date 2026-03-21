using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Manages in-game building placement mode.
/// Auto-creates itself at runtime — no need to add it to the scene manually.
/// Uses the new Input System (Mouse.current / Keyboard.current).
/// </summary>
public class BuildingPlacementController : MonoBehaviour
{
    public static BuildingPlacementController Instance { get; private set; }

    // Auto-create at scene load — no manual scene setup required
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoCreate()
    {
        if (Instance != null) return;
        var go = new GameObject("[BuildingPlacementController]");
        go.AddComponent<BuildingPlacementController>();
        DontDestroyOnLoad(go);
    }

    // ── Building catalogue ──────────────────────────────────────────

    public struct BuildingInfo
    {
        public string Name;
        public int    WaypointType; // 0 = Source, 1 = Destination

        public BuildingInfo(string name, int waypointType)
        { Name = name; WaypointType = waypointType; }
    }

    private static readonly Dictionary<SpeciesType, BuildingInfo[]> BuildingsBySpecies =
        new Dictionary<SpeciesType, BuildingInfo[]>
    {
        { SpeciesType.GlobuleRouge,  new[] { new BuildingInfo("Poumon",            0), new BuildingInfo("Rate",           1) } },
        { SpeciesType.GlobuleBlanc,  new[] { new BuildingInfo("Rate",              0) } },
        { SpeciesType.Bacterie,      new[] { new BuildingInfo("Source Nutriments", 0), new BuildingInfo("Zone Infection", 1) } },
        { SpeciesType.Virus,         new[] { new BuildingInfo("Nœud Viral",        0), new BuildingInfo("Cellule Hôte",   1) } },
        { SpeciesType.Plaquette,     new[] { new BuildingInfo("Moelle",            0), new BuildingInfo("Lésion",         1) } },
    };

    public static BuildingInfo[] GetBuildings(SpeciesType type)
        => BuildingsBySpecies.TryGetValue(type, out var b) ? b : System.Array.Empty<BuildingInfo>();

    /// <summary>
    /// Retourne la BuildingDefinition complète pour un nom de bâtiment (lookup insensible à la casse).
    /// Null si BuildingLibrary n'est pas encore chargé ou si le nom est inconnu.
    /// </summary>
    public static BuildingDefinition GetDefinition(string buildingName)
        => BuildingLibrary.Instance != null ? BuildingLibrary.Instance.Get(buildingName) : null;

    // ── Events ──────────────────────────────────────────────────────

    public event System.Action               OnPlacementStarted;
    public event System.Action               OnPlacementCancelled;
    public event System.Action<WaypointData> OnPlacementConfirmed;

    // ── State ───────────────────────────────────────────────────────

    public bool   IsPlacing          { get; private set; }
    public string PendingBuildingName { get; private set; }

    private int pendingWaypointType;
    private int pendingSpeciesIndex;
    private int placementStartFrame = -1; // skip the frame the button was clicked

    // Palette matches SlimeTrailRender.compute
    private static readonly Color[] SpeciesPalette =
    {
        new Color(1f, 0f, 0f), new Color(0f, 1f, 0f), new Color(0f, 0f, 1f),
        new Color(0f, 1f, 1f), new Color(1f, 0f, 1f), new Color(1f, 1f, 0f),
    };

    // GL preview
    private Material glMaterial;
    private Vector2? currentMousePixel;
    private bool     mouseIsWalkable;

    // ── Unity lifecycle ─────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        var shader = Shader.Find("Hidden/Internal-Colored");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        glMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        glMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        glMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        glMaterial.SetInt("_Cull",    (int)UnityEngine.Rendering.CullMode.Off);
        glMaterial.SetInt("_ZWrite",  0);
        glMaterial.SetInt("_ZTest",   (int)UnityEngine.Rendering.CompareFunction.Always);
    }

    private void Update()
    {
        if (!IsPlacing) return;

        var mouse    = Mouse.current;
        var keyboard = Keyboard.current;
        if (mouse == null) return;

        // Update cursor position every frame
        currentMousePixel = GetMouseMapPosition(mouse, out mouseIsWalkable);

        // Skip the frame the palette button was clicked (avoids immediate accidental placement)
        if (Time.frameCount <= placementStartFrame + 1) return;

        // Cancel: Escape or right-click
        if ((keyboard != null && keyboard.escapeKey.wasPressedThisFrame) ||
            mouse.rightButton.wasPressedThisFrame)
        {
            CancelPlacement();
            return;
        }

        // Confirm: left click on a walkable map pixel
        if (mouse.leftButton.wasPressedThisFrame && currentMousePixel.HasValue && mouseIsWalkable)
        {
            var wp = new WaypointData
            {
                position     = currentMousePixel.Value,
                type         = pendingWaypointType,
                speciesIndex = pendingSpeciesIndex
            };
            WaypointManager.Instance?.AddWaypoint(wp, PendingBuildingName);
            IsPlacing = false;
            OnPlacementConfirmed?.Invoke(wp);
        }
    }

    private void OnRenderObject()
    {
        if (!IsPlacing || !currentMousePixel.HasValue || Camera.current == null) return;

        glMaterial.SetPass(0);
        GL.PushMatrix();
        GL.LoadProjectionMatrix(Camera.current.projectionMatrix);
        GL.modelview = Camera.current.worldToCameraMatrix;

        Vector3 worldPos = PixelToWorld(currentMousePixel.Value);
        Color   col      = mouseIsWalkable
            ? SpeciesPalette[Mathf.Clamp(pendingSpeciesIndex, 0, 5)]
            : new Color(1f, 0.15f, 0.15f, 0.85f);

        float pulse  = 1f + 0.15f * Mathf.Sin(Time.time * 6f);
        float radius = 20f * pulse;

        DrawCircle(worldPos, radius, col, 16);

        // Inner dot: green for Source, orange for Destination
        Color innerCol = pendingWaypointType == 0
            ? new Color(0.5f, 1f, 0.5f, 0.9f)
            : new Color(1f, 0.6f, 0.2f, 0.9f);
        DrawCircle(worldPos, radius * 0.3f, innerCol, 8);

        GL.PopMatrix();
    }

    // ── Public API ──────────────────────────────────────────────────

    public void StartPlacement(int waypointType, int speciesIndex, string buildingName)
    {
        pendingWaypointType  = waypointType;
        pendingSpeciesIndex  = speciesIndex;
        PendingBuildingName  = buildingName;
        placementStartFrame  = Time.frameCount;
        currentMousePixel    = null;
        IsPlacing            = true;
        OnPlacementStarted?.Invoke();
    }

    public void CancelPlacement()
    {
        IsPlacing = false;
        OnPlacementCancelled?.Invoke();
    }

    // ── Coordinate helpers ──────────────────────────────────────────

    private Vector2? GetMouseMapPosition(Mouse mouse, out bool walkable)
    {
        walkable = false;
        var smr = SlimeMapRenderer.Instance;
        if (smr == null || Camera.main == null) return null;

        Vector2 screenPos = mouse.position.ReadValue();
        // Profondeur : distance caméra → plan z=0 de la carte
        float depth = -Camera.main.transform.position.z;
        Vector3 worldPos = Camera.main.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, depth));

        int px = (int)worldPos.x;
        int py = (int)worldPos.y;

        if (px < 0 || px >= smr.Width || py < 0 || py >= smr.Height) return null;

        var terrain = TerrainMapRenderer.Instance;
        walkable = (terrain != null && terrain.WalkabilityGrid != null)
            ? terrain.WalkabilityGrid[px, py]
            : true;

        return new Vector2(px, py);
    }

    private Vector3 PixelToWorld(Vector2 pixelPos)
    {
        var smr = SlimeMapRenderer.Instance;
        if (smr == null || Camera.main == null) return Vector3.zero;

        if (smr.DisplayTarget != null)
        {
            Bounds b = smr.DisplayTarget.bounds;
            return new Vector3(
                b.min.x + (pixelPos.x / smr.Width)  * b.size.x,
                b.min.y + (pixelPos.y / smr.Height) * b.size.y,
                b.center.z - 0.02f);
        }
        float vx = pixelPos.x / smr.Width;
        float vy = pixelPos.y / smr.Height;
        Vector3 world = Camera.main.ViewportToWorldPoint(
            new Vector3(vx, vy, -Camera.main.transform.position.z));
        world.z = -0.02f;
        return world;
    }

    private void DrawCircle(Vector3 center, float pixelRadius, Color color, int segments)
    {
        var smr = SlimeMapRenderer.Instance;
        if (smr == null || smr.DisplayTarget == null) return;
        float worldR = pixelRadius / smr.Width * smr.DisplayTarget.bounds.size.x;
        GL.Begin(GL.LINE_STRIP);
        GL.Color(color);
        for (int i = 0; i <= segments; i++)
        {
            float a = i * Mathf.PI * 2f / segments;
            GL.Vertex(center + new Vector3(Mathf.Cos(a) * worldR, Mathf.Sin(a) * worldR, 0f));
        }
        GL.End();
    }

    private void OnDestroy()
    {
        if (glMaterial != null) Destroy(glMaterial);
    }
}
