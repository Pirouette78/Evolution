using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Civ-style camera controller: scroll to zoom, right-click drag to pan.
/// Also supports WASD / arrow keys and edge-of-screen panning.
/// Uses the new Input System.
/// </summary>
public class CameraController : MonoBehaviour
{
    [Header("Zoom")]
    [Tooltip("Facteur multiplicatif par cran de molette (ex: 1.15 = +15% par cran). Zoom logarithmique.")]
    public float ZoomFactor = 1.15f;
    public float MinOrthoSize = 10f;
    public float MaxOrthoSize = 300f;

    [Header("Pan")]
    public float PanSpeed = 500f;
    public float EdgePanThreshold = 15f;

    [Header("Bounds (map limits)")]
    public float MapMinX = 0f;
    public float MapMaxX = 512f;
    public float MapMinY = 0f;
    public float MapMaxY = 512f;

    /// <summary>Caméra gérée par ce controller (accès pour ZoomLevelController et autres systèmes).</summary>
    public Camera Cam => cam;

    /// <summary>Niveau de zoom discret actuel (0 = max zoom, 4 = dézoom max). Mis à jour à chaque changement de zoom.</summary>
    public static int CurrentZoomLevel    { get; private set; } = 0;
    /// <summary>Multiplicateur total de vitesse de simulation lié au zoom.</summary>
    public static int ZoomTimeMultiplier   { get; private set; } = 1;
    /// <summary>Portion du multiplicateur appliquée via GameTime.TimeScale (accélère tout : agents, spawns, timers).</summary>
    public static int ZoomTimeScaleFactor  { get; private set; } = 1;
    /// <summary>Portion appliquée via StepsPerFrame (précision GPU).</summary>
    public static int ZoomStepsPerFrame    { get; private set; } = 1;

    [Header("Zoom → Vitesse de simulation")]
    [Tooltip("Activer l'accélération automatique de la simulation quand on dézoomme.")]
    public bool  zoomDrivesSimSpeed  = true;
    [Tooltip("Nombre de niveaux : 4 → ×1, ×2, ×4, ×8, ×16.")]
    public int   zoomSpeedLevels     = 4;
    [Tooltip("OrthoSize à partir duquel la sim tourne à ×1 (zoom max).")]
    public float minSpeedOrthoSize   = 20f;
    [Tooltip("OrthoSize à partir duquel la sim tourne à ×(2^zoomSpeedLevels) (dézoom max).")]
    public float maxSpeedOrthoSize   = 300f;
    [Tooltip("Portion maximale du multiplicateur gérée par GameTime.TimeScale (accélère tout : agents + spawns + timers CPU).")]
    public int   maxTimeScaleFromZoom = 4;
    [Tooltip("Portion maximale gérée par StepsPerFrame (précision GPU uniquement). Total = TimeScale * Steps.")]
    public int   maxStepsFromZoom     = 4;

    private Camera cam;
    private Vector3 dragOrigin;
    private bool isDragging;

    private Mouse mouse;
    private Keyboard keyboard;

    private void Awake()
    {
        cam = GetComponent<Camera>();
        if (cam == null) cam = Camera.main;

        // Removed hardcoded centering: The camera will now start exactly where it is placed in the scene.
    }

    private void Update()
    {
        mouse = Mouse.current;
        keyboard = Keyboard.current;
        if (mouse == null) return;

        HandleZoom();
        HandleDrag();
        HandleKeyboardPan();
        // HandleEdgePan();
        ClampPosition();
    }

    private void HandleZoom()
    {
        float scroll = mouse.scroll.ReadValue().y;
        if (Mathf.Abs(scroll) < 0.01f) return;

        // Zoom logarithmique : chaque cran multiplie l'orthoSize par ZoomFactor.
        float notches = scroll * 50f / 120f;
        float newSize = cam.orthographicSize * Mathf.Pow(ZoomFactor, -notches);
        cam.orthographicSize = Mathf.Clamp(newSize, MinOrthoSize, MaxOrthoSize);

        UpdateZoomSimSpeed();
    }

    private void UpdateZoomSimSpeed()
    {
        if (!zoomDrivesSimSpeed) return;

        // zoomRatio = 0 → orthoSize <= minSpeedOrthoSize → ×1
        // zoomRatio = 1 → orthoSize >= maxSpeedOrthoSize → ×(2^zoomSpeedLevels)
        float lo  = Mathf.Log(Mathf.Max(1f, minSpeedOrthoSize));
        float hi  = Mathf.Log(Mathf.Max(lo + 0.001f, maxSpeedOrthoSize));
        float cur = Mathf.Log(Mathf.Max(1f, cam.orthographicSize));
        float zoomRatio = Mathf.Clamp01((cur - lo) / (hi - lo));

        int level = Mathf.Clamp(Mathf.RoundToInt(zoomRatio * zoomSpeedLevels), 0, zoomSpeedLevels);
        int mult  = 1 << level; // total multiplier : 1, 2, 4, 8, 16...

        if (level == CurrentZoomLevel) return;
        CurrentZoomLevel = level;
        ZoomTimeMultiplier = mult;

        // Hybride : Time portion jusqu'à maxTimeScaleFromZoom, steps pour le reste (précision)
        int tsComponent    = Mathf.Clamp(mult, 1, Mathf.Max(1, maxTimeScaleFromZoom));
        int stepsComponent = Mathf.Clamp(mult / tsComponent, 1, Mathf.Max(1, maxStepsFromZoom));
        ZoomTimeScaleFactor = tsComponent;
        ZoomStepsPerFrame   = stepsComponent;
        // UIController.ApplyZoomSpeed() lit ces valeurs chaque frame dans Update() et les applique.
    }

    private void HandleDrag()
    {
        // Start drag on right-click or middle-click
        if (mouse.rightButton.wasPressedThisFrame || mouse.middleButton.wasPressedThisFrame)
        {
            dragOrigin = cam.ScreenToWorldPoint(mouse.position.ReadValue());
            isDragging = true;
        }

        if (mouse.rightButton.wasReleasedThisFrame || mouse.middleButton.wasReleasedThisFrame)
        {
            isDragging = false;
        }

        if (isDragging)
        {
            Vector3 currentPos = cam.ScreenToWorldPoint(mouse.position.ReadValue());
            Vector3 diff = dragOrigin - currentPos;
            cam.transform.position += diff;
        }
    }

    private void HandleKeyboardPan()
    {
        if (keyboard == null) return;

        float h = 0f, v = 0f;

        if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed)    v = 1f;
        if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed)  v = -1f;
        if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed)  h = -1f;
        if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) h = 1f;

        if (Mathf.Abs(h) > 0.01f || Mathf.Abs(v) > 0.01f)
        {
            float scaledSpeed = PanSpeed * Mathf.Max(0.2f, (cam.orthographicSize / MaxOrthoSize));
            cam.transform.position += new Vector3(h, v, 0) * scaledSpeed * Time.unscaledDeltaTime;
        }
    }

    private void HandleEdgePan()
    {
        if (isDragging) return;

        Vector2 mousePos = mouse.position.ReadValue();
        float h = 0f, v = 0f;

        if (mousePos.x <= EdgePanThreshold) h = -1f;
        else if (mousePos.x >= Screen.width - EdgePanThreshold) h = 1f;
        if (mousePos.y <= EdgePanThreshold) v = -1f;
        else if (mousePos.y >= Screen.height - EdgePanThreshold) v = 1f;

        if (Mathf.Abs(h) > 0.01f || Mathf.Abs(v) > 0.01f)
        {
            float scaledSpeed = PanSpeed * (cam.orthographicSize / MaxOrthoSize) * 0.5f;
            cam.transform.position += new Vector3(h, v, 0) * scaledSpeed * Time.unscaledDeltaTime;
        }
    }

    private void ClampPosition()
    {
        Vector3 pos = cam.transform.position;
        float mx = TerrainMapRenderer.Instance != null ? TerrainMapRenderer.Instance.Width : MapMaxX;
        float my = TerrainMapRenderer.Instance != null ? TerrainMapRenderer.Instance.Height : MapMaxY;
        pos.x = Mathf.Clamp(pos.x, MapMinX, mx);
        pos.y = Mathf.Clamp(pos.y, MapMinY, my);
        cam.transform.position = pos;
    }
}
