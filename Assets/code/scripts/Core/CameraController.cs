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
    public float ZoomSpeed = 500f;
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

    private Camera cam;
    private Vector3 dragOrigin;
    private bool isDragging;

    private Mouse mouse;
    private Keyboard keyboard;

    private void Awake()
    {
        cam = GetComponent<Camera>();
        if (cam == null) cam = Camera.main;

        // Centre on map at startup
        float centerX = (MapMinX + MapMaxX) * 0.5f;
        float centerY = (MapMinY + MapMaxY) * 0.5f;
        cam.transform.position = new Vector3(centerX, centerY, cam.transform.position.z);
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

        // scroll is typically ±120 per notch, normalise
        float delta = (scroll / 120f) * 5f; // Multiplie par 5 pour un zoom plus rapide
        float newSize = cam.orthographicSize - delta * ZoomSpeed;
        cam.orthographicSize = Mathf.Clamp(newSize, MinOrthoSize, MaxOrthoSize);
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
        pos.x = Mathf.Clamp(pos.x, MapMinX, MapMaxX);
        pos.y = Mathf.Clamp(pos.y, MapMinY, MapMaxY);
        cam.transform.position = pos;
    }
}
