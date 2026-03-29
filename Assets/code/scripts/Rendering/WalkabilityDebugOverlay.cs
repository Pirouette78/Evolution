using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Overlay de debug : affiche les zones bloquées par les unités en rouge semi-transparent.
/// F9 = toggle. Utilise OnGUI (toujours au-dessus de tout).
/// Auto-créé au démarrage, DontDestroyOnLoad.
/// </summary>
public class WalkabilityDebugOverlay : MonoBehaviour
{
    public static WalkabilityDebugOverlay Instance { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoCreate()
    {
        if (Instance != null) return;
        var go = new GameObject("[WalkabilityDebugOverlay]");
        go.AddComponent<WalkabilityDebugOverlay>();
        DontDestroyOnLoad(go);
    }

    private Texture2D debugTex;
    private bool      isDirty = true;
    public  bool      Visible = true;

    private int blockedCellCount = 0;  // pour debug log

    // ── Unity lifecycle ────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current[Key.F9].wasPressedThisFrame)
        {
            Visible = !Visible;
            Debug.Log($"[WALKABILITY-DBG] Overlay {(Visible ? "ON" : "OFF")} — {blockedCellCount} cellules bloquées");
        }

        var terrain = TerrainMapRenderer.Instance;
        if (terrain == null) return;

        if (isDirty)
        {
            RebuildDebugTexture(terrain);
            isDirty = false;
        }
    }

    private void OnGUI()
    {
        if (!Visible || debugTex == null) return;

        var smr = SlimeMapRenderer.Instance;
        if (smr == null || smr.DisplayTarget == null || Camera.main == null) return;

        Bounds b = smr.DisplayTarget.bounds;

        // Coins du quad map en coordonnées écran
        Vector3 bl = Camera.main.WorldToScreenPoint(new Vector3(b.min.x, b.min.y, b.center.z));
        Vector3 tr = Camera.main.WorldToScreenPoint(new Vector3(b.max.x, b.max.y, b.center.z));

        // GUI : Y inversé (0 = haut de l'écran)
        float sx = bl.x;
        float sy = Screen.height - tr.y;
        float sw = tr.x - bl.x;
        float sh = tr.y - bl.y;

        GUI.DrawTexture(new Rect(sx, sy, sw, sh), debugTex, ScaleMode.StretchToFill, true);
    }

    private void OnDestroy()
    {
        if (debugTex != null) Destroy(debugTex);
    }

    // ── API publique ───────────────────────────────────────────────

    public void MarkDirty() => isDirty = true;

    // ── Reconstruction texture ────────────────────────────────────

    private void RebuildDebugTexture(TerrainMapRenderer terrain)
    {
        int w = terrain.Width, h = terrain.Height;

        if (debugTex == null || debugTex.width != w || debugTex.height != h)
        {
            if (debugTex != null) Destroy(debugTex);
            debugTex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode   = TextureWrapMode.Clamp
            };
        }

        var   grid   = terrain.WalkabilityGrid;
        var   hmap   = terrain.HeightMap;
        float wt     = terrain.WaterThreshold;
        var   pixels = new Color32[w * h];

        blockedCellCount = 0;
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            if (!grid[x, y] && hmap[x, y] >= wt)
            {
                pixels[y * w + x] = new Color32(255, 0, 0, 180);  // bloqué par unité → rouge
                blockedCellCount++;
            }
            else
            {
                pixels[y * w + x] = new Color32(0, 0, 0, 0);       // transparent
            }
        }

        debugTex.SetPixels32(pixels);
        debugTex.Apply();
        Debug.Log($"[WALKABILITY-DBG] Texture reconstruite — {blockedCellCount} cellules bloquées (non-eau)");
    }
}
