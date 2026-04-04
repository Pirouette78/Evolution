using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Chef d'orchestre du système LOD basé sur le zoom.
/// Surveille l'orthographicSize de la caméra et gère la transition
/// entre vue Stratégique (slime map) et vue Tactique (terrain + sprites).
///
/// TacticalBlend : 0 = zoom loin (slime pur), 1 = zoom près (sprites + terrain).
/// SlimeAlpha    = 1 - TacticalBlend  (slime disparaît en zoomant)
/// TerrainAlpha  = TacticalBlend      (terrain apparaît en zoomant)
/// SpriteAlpha   = TacticalBlend      (sprites apparaissent en même temps que le terrain)
/// </summary>
[DefaultExecutionOrder(-10)]
public class ZoomLevelController : MonoBehaviour
{
    public static ZoomLevelController Instance { get; private set; }

    // ── Thresholds ────────────────────────────────────────────────────────
    [Header("Zoom Thresholds (OrthoSize)")]
    [Tooltip("Au-dessus : slime map pure, rien d'autre visible.")]
    public float StrategicThreshold = 80f;
    [Tooltip("En-dessous : slime invisible, sprites + terrain à 100%.")]
    public float TacticalThreshold = 30f;

    // ── Références ────────────────────────────────────────────────────────
    [Header("Materials")]
    [Tooltip("Material du quad terrain overlay (doit avoir la propriété _Alpha).")]
    public Material TerrainOverlayMaterial;

    // ── État public ───────────────────────────────────────────────────────
    /// <summary>0 = vue stratégique, 1 = vue tactique. SmoothStep entre les deux thresholds.</summary>
    public float TacticalBlend { get; private set; }
    /// <summary>Alpha de la slime map (1 = opaque, 0 = invisible).</summary>
    public float SlimeAlpha    { get; private set; } = 1f;
    /// <summary>Alpha du terrain overlay et des sprites (0 = invisible, 1 = opaque).</summary>
    public float TerrainAlpha  { get; private set; }
    /// <summary>Alpha des sprites agents et arbres — identique à TerrainAlpha.</summary>
    public float SpriteAlpha   { get; private set; }
    /// <summary>True quand OrthoSize &lt; TacticalThreshold.</summary>
    public bool  IsInTacticalMode { get; private set; }
    /// <summary>Bounds caméra en espace simulation (0-512). (minX, minY, maxX, maxY).</summary>
    public Vector4 CameraSimBounds { get; private set; }

    // ── Couches tactiques ──────────────────────────────────────────────────
    private readonly List<ITacticalLayer> _layers = new();
    private bool _wasTactical;

    // ── Privé ─────────────────────────────────────────────────────────────
    private Camera _cam;
    private SlimeMapRenderer _slimeRenderer;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        _cam           = Camera.main;
        _slimeRenderer = SlimeMapRenderer.Instance;

        if (_slimeRenderer != null && _slimeRenderer.SlimeShader != null)
            _slimeRenderer.SlimeShader.SetFloat("slimeDisplayAlpha", 1f);
    }

    public void RegisterLayer(ITacticalLayer layer)
    {
        if (!_layers.Contains(layer)) _layers.Add(layer);
    }

    public void UnregisterLayer(ITacticalLayer layer)
    {
        _layers.Remove(layer);
    }

    private float _logTimer;
    private void Update()
    {
        if (_cam == null) { _cam = Camera.main; return; }
        if (_slimeRenderer == null) { _slimeRenderer = SlimeMapRenderer.Instance; return; }

        float ortho = _cam.orthographicSize;

        // ── Crossfade ────────────────────────────────────────────────────
        // TacticalBlend : 0 au-delà de StrategicThreshold, 1 en-deçà de TacticalThreshold
        TacticalBlend = Mathf.SmoothStep(0f, 1f,
            Mathf.InverseLerp(StrategicThreshold, TacticalThreshold, ortho));

        SlimeAlpha   = 1f - TacticalBlend;   // slime disparaît en zoomant
        TerrainAlpha = TacticalBlend;          // terrain apparaît en mode tactique

        // Sprites visibles dès StrategicThreshold (alpha=0), et totalement opaques à TacticalThreshold (alpha=1)
        SpriteAlpha = TacticalBlend;

        IsInTacticalMode = ortho < TacticalThreshold;

        // ── Log périodique (toutes les 2s) ───────────────────────────────
        _logTimer += Time.deltaTime;
        if (_logTimer >= 2f)
        {
            _logTimer = 0f;
            string mode = ortho > StrategicThreshold ? "STRATEGIC"
                        : ortho > TacticalThreshold  ? "TRANSITION"
                        :                              "TACTICAL";
            Debug.Log($"[Zoom] ortho={ortho:F1}  mode={mode}  slime={SlimeAlpha:F2}  terrain={TerrainAlpha:F2}  sprite={SpriteAlpha:F2}");
        }

        // ── Shader uniforms ──────────────────────────────────────────────
        if (_slimeRenderer.SlimeShader != null)
            _slimeRenderer.SlimeShader.SetFloat("slimeDisplayAlpha", SlimeAlpha);

        if (TerrainOverlayMaterial != null)
            TerrainOverlayMaterial.SetFloat("_Alpha", TerrainAlpha);

        // ── Camera bounds (sim space 0–512) ──────────────────────────────
        Vector3 bl = _cam.ViewportToWorldPoint(new Vector3(0f, 0f, _cam.nearClipPlane));
        Vector3 tr = _cam.ViewportToWorldPoint(new Vector3(1f, 1f, _cam.nearClipPlane));

        int simW = _slimeRenderer.MapWidth;
        int simH = _slimeRenderer.MapHeight;

        float simBlX = bl.x, simBlY = bl.y, simTrX = tr.x, simTrY = tr.y;

        if (_slimeRenderer.DisplayTarget != null)
        {
            Bounds b  = _slimeRenderer.DisplayTarget.bounds;
            float  sx = Mathf.Max(0.0001f, b.size.x);
            float  sy = Mathf.Max(0.0001f, b.size.y);
            simBlX = ((bl.x - b.min.x) / sx) * simW;
            simBlY = ((bl.y - b.min.y) / sy) * simH;
            simTrX = ((tr.x - b.min.x) / sx) * simW;
            simTrY = ((tr.y - b.min.y) / sy) * simH;
        }

        CameraSimBounds = new Vector4(
            Mathf.Max(0f, simBlX), Mathf.Max(0f, simBlY),
            Mathf.Min(simW, simTrX), Mathf.Min(simH, simTrY));

        // ── Notifications aux couches ────────────────────────────────────
        // On déclenche les layers dès que SpriteAlpha > 0 (pas seulement en mode tactique pur)
        // afin que les sprites soient visibles dès StrategicThreshold.
        bool spriteVisible = SpriteAlpha > 0f;
        bool entering = spriteVisible && !_wasTactical;
        bool exiting  = !spriteVisible &&  _wasTactical;

        if (entering)
            foreach (var l in _layers) l.OnEnterTactical(CameraSimBounds);
        else if (exiting)
            foreach (var l in _layers) l.OnExitTactical();
        else if (spriteVisible)
            foreach (var l in _layers) l.OnCameraBoundsChanged(CameraSimBounds);

        _wasTactical = spriteVisible;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
