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

    // ── Building catalogue (data-driven) ───────────────────────────

    /// <summary>
    /// Retourne les bâtiments disponibles pour une espèce (par string id : "globulerouge"…).
    /// Données issues de BuildingLibrary (JSON). Aucune liste hardcodée.
    /// </summary>
    public static List<BuildingDefinition> GetBuildings(string speciesId)
        => BuildingLibrary.Instance != null
            ? BuildingLibrary.Instance.GetFor(speciesId)
            : new List<BuildingDefinition>();

    /// <summary>Retourne la BuildingDefinition par id. Null si inconnu.</summary>
    public static BuildingDefinition GetDefinition(string buildingId)
        => BuildingLibrary.Instance?.Get(buildingId);

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

    // Couleur de prévisualisation : issue de PlayerLibrary/SlimeMapRenderer (slot GPU)
    private static Color GetSlotColor(int slotIndex)
    {
        var smr = SlimeMapRenderer.Instance;
        if (smr != null && slotIndex >= 0 && slotIndex < smr.slotColors.Length)
        {
            var c = smr.slotColors[slotIndex];
            return new Color(c.x, c.y, c.z, 0.85f);
        }
        // Fallback HSV
        float hue = (slotIndex % SlimeMapRenderer.MaxSlots) / (float)SlimeMapRenderer.MaxSlots;
        return Color.HSVToRGB(hue, 1f, 1f);
    }

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
        
        var def = GetDefinition(PendingBuildingName);
        if (currentMousePixel.HasValue)
        {
            UnitSpriteRenderer.Instance?.SetPreviewBuilding(currentMousePixel.Value, def, mouseIsWalkable);
        }
        else
        {
            UnitSpriteRenderer.Instance?.SetPreviewBuilding(Vector2.zero, null, false);
        }

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
            UnitSpriteRenderer.Instance?.SetPreviewBuilding(Vector2.zero, null, false);
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
            ? GetSlotColor(pendingSpeciesIndex)
            : new Color(1f, 0.15f, 0.15f, 0.85f);

        float pulse  = 1f + 0.15f * Mathf.Sin(Time.time * 6f);
        float radius = 20f * pulse;

        var def = GetDefinition(PendingBuildingName);
        if (def != null && def.blockTilesW > 0f)
        {
            var smr = SlimeMapRenderer.Instance;
            var terrain = TerrainMapRenderer.Instance;
            if (smr != null && terrain != null && smr.DisplayTarget != null && terrain.WalkabilityGrid != null)
            {
                float cellWorldW = smr.DisplayTarget.bounds.size.x / terrain.WalkabilityGrid.GetLength(0);
                float cellWorldH = smr.DisplayTarget.bounds.size.y / terrain.WalkabilityGrid.GetLength(1);
                
                float w = def.blockTilesW * cellWorldW;
                float h = def.blockTilesH * cellWorldH;

                float centerX = worldPos.x + (def.blockOffsetX + def.blockTilesW * 0.5f - 0.5f) * cellWorldW;
                float centerY = worldPos.y + (def.blockOffsetY + def.blockTilesH * 0.5f - 0.5f) * cellWorldH;

                DrawRect(new Vector3(centerX, centerY, worldPos.z), w, h, col);
            }
            else
            {
                DrawCircle(worldPos, radius, col, 16);
            }
        }
        else
        {
            DrawCircle(worldPos, radius, col, 16);
        }

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
        UnitSpriteRenderer.Instance?.SetPreviewBuilding(Vector2.zero, null, false);
        OnPlacementCancelled?.Invoke();
    }

    // ── Coordinate helpers ──────────────────────────────────────────

    private Vector2? GetMouseMapPosition(Mouse mouse, out bool walkable)
    {
        walkable = false;
        var smr = SlimeMapRenderer.Instance;
        if (smr == null || Camera.main == null || smr.DisplayTarget == null) return null;

        // ScreenToWorldPoint → position monde au plan z=0 (suit la caméra si elle bouge)
        // Puis inverse de PixelToWorld via les bounds du quad pour obtenir les coordonnées pixel
        float depth = -Camera.main.transform.position.z;
        Vector3 worldPos = Camera.main.ScreenToWorldPoint(
            new Vector3(mouse.position.ReadValue().x, mouse.position.ReadValue().y, depth));

        Bounds b = smr.DisplayTarget.bounds;
        if (b.size.x <= 0 || b.size.y <= 0) return null;
        int px = (int)((worldPos.x - b.min.x) / b.size.x * smr.Width);
        int py = (int)((worldPos.y - b.min.y) / b.size.y * smr.Height);

        if (px < 0 || px >= smr.Width || py < 0 || py >= smr.Height) return null;

        var terrain = TerrainMapRenderer.Instance;
        var def = GetDefinition(PendingBuildingName);
        if (terrain != null && terrain.WalkabilityGrid != null)
        {
            int gridW = terrain.WalkabilityGrid.GetLength(0);
            int gridH = terrain.WalkabilityGrid.GetLength(1);
            int gx = Mathf.Clamp((int)(px * gridW / (float)smr.Width),  0, gridW - 1);
            int gy = Mathf.Clamp((int)(py * gridH / (float)smr.Height), 0, gridH - 1);
            
            walkable = true;
            if (def != null && def.blockTilesW > 0f)
            {
                int offX = Mathf.RoundToInt(def.blockOffsetX);
                int offY = Mathf.RoundToInt(def.blockOffsetY);
                int bw = Mathf.Max(1, Mathf.RoundToInt(def.blockTilesW));
                int bh = Mathf.Max(1, Mathf.RoundToInt(def.blockTilesH));

                for (int y = gy + offY; y < gy + offY + bh; y++)
                {
                    for (int x = gx + offX; x < gx + offX + bw; x++)
                    {
                        if (x < 0 || x >= gridW || y < 0 || y >= gridH || !terrain.WalkabilityGrid[x, y])
                        {
                            walkable = false;
                            break;
                        }
                    }
                    if (!walkable) break;
                }
            }
            else
            {
                walkable = terrain.WalkabilityGrid[gx, gy];
            }

            px = (int)((gx + 0.5f) * smr.Width / (float)gridW);
            py = (int)((gy + 0.5f) * smr.Height / (float)gridH);
        }
        else
        {
            walkable = true;
        }

        return new Vector2(px, py);
    }

    private Vector3 PixelToWorld(Vector2 pixelPos)
    {
        var smr = SlimeMapRenderer.Instance;
        if (smr == null) return Vector3.zero;

        if (smr.DisplayTarget != null)
        {
            Bounds b = smr.DisplayTarget.bounds;
            return new Vector3(
                b.min.x + (pixelPos.x / smr.Width)  * b.size.x,
                b.min.y + (pixelPos.y / smr.Height) * b.size.y,
                b.center.z - 0.02f);
        }
        // Fallback : le monde == espace pixel (map à [0,Width]×[0,Height])
        return new Vector3(pixelPos.x, pixelPos.y, -0.02f);
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

    private void DrawRect(Vector3 center, float worldW, float worldH, Color color)
    {
        float halfW = worldW * 0.5f;
        float halfH = worldH * 0.5f;
        GL.Begin(GL.LINE_STRIP);
        GL.Color(color);
        GL.Vertex3(center.x - halfW, center.y - halfH, 0f);
        GL.Vertex3(center.x + halfW, center.y - halfH, 0f);
        GL.Vertex3(center.x + halfW, center.y + halfH, 0f);
        GL.Vertex3(center.x - halfW, center.y + halfH, 0f);
        GL.Vertex3(center.x - halfW, center.y - halfH, 0f); // Close
        GL.End();
    }

    private void OnDestroy()
    {
        if (glMaterial != null) Destroy(glMaterial);
    }
}
