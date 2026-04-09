using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Overlay de debug : affiche la grille des tiles par-dessus le terrain.
/// F8 = toggle.
/// </summary>
public class TileGridDebugOverlay : MonoBehaviour
{
    public static TileGridDebugOverlay Instance { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoCreate()
    {
        if (Instance != null) return;
        var go = new GameObject("[TileGridDebugOverlay]");
        go.AddComponent<TileGridDebugOverlay>();
        DontDestroyOnLoad(go);
    }

    private bool visible = false;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current[Key.F8].wasPressedThisFrame)
        {
            visible = !visible;
            Debug.Log($"[TILE-GRID-DBG] Grille {(visible ? "ON" : "OFF")}");
        }
    }

    private void OnGUI()
    {
        if (!visible) return;

        var smr = SlimeMapRenderer.Instance;
        if (smr == null || smr.DisplayTarget == null || Camera.main == null) return;

        var terrain = TerrainMapRenderer.Instance;
        if (terrain == null) return;

        Bounds b = smr.DisplayTarget.bounds;

        Vector3 bl = Camera.main.WorldToScreenPoint(new Vector3(b.min.x, b.min.y, b.center.z));
        Vector3 tr = Camera.main.WorldToScreenPoint(new Vector3(b.max.x, b.max.y, b.center.z));

        float sx = bl.x;
        float sy = Screen.height - tr.y;
        float sw = tr.x - bl.x;
        float sh = tr.y - bl.y;

        int cols = terrain.Width;
        int rows = terrain.Height;

        float cellW = sw / cols;
        float cellH = sh / rows;

        var lineColor = new Color(1f, 1f, 1f, 0.4f);
        GUI.color = lineColor;

        // Lignes verticales
        for (int x = 0; x <= cols; x++)
        {
            float px = sx + x * cellW;
            GUI.DrawTexture(new Rect(px, sy, 1f, sh), Texture2D.whiteTexture);
        }

        // Lignes horizontales
        for (int y = 0; y <= rows; y++)
        {
            float py = sy + y * cellH;
            GUI.DrawTexture(new Rect(sx, py, sw, 1f), Texture2D.whiteTexture);
        }

        GUI.color = Color.white;
    }
}
