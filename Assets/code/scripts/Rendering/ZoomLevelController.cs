using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Chef d'orchestre du système LOD basé sur le zoom.
/// Surveille l'orthographicSize de la caméra et gère la transition
/// entre vue Stratégique (slime map) et vue Tactique (terrain + sprites).
///
/// Architecture extensible : les futurs systèmes de rendu (AgentTacticalLayer,
/// VegetalLayer, BuildingLayer) s'enregistrent via RegisterLayer().
///
/// Exécution avant SlimeMapRenderer (DefaultExecutionOrder -10) pour que
/// slimeDisplayAlpha soit set avant que ComposeDisplay soit dispatché.
/// </summary>
[DefaultExecutionOrder(-10)]
public class ZoomLevelController : MonoBehaviour
{
    public static ZoomLevelController Instance { get; private set; }

    // ── Thresholds ────────────────────────────────────────────────────────
    [Header("Zoom Thresholds (OrthoSize)")]
    [Tooltip("Au-dessus : slime map pure, pas de terrain visible.")]
    public float StrategicThreshold = 80f;
    [Tooltip("En-dessous : mode tactique actif, sprites d'agents autorisés.")]
    public float TacticalThreshold = 30f;

    // ── Références ────────────────────────────────────────────────────────
    [Header("Materials")]
    [Tooltip("Material du quad terrain overlay (doit avoir la propriété _Alpha).")]
    public Material TerrainOverlayMaterial;

    // ── État public (lecture seule depuis l'extérieur) ────────────────────
    /// <summary>0 = vue stratégique, 1 = vue tactique (SmoothStep sur la zone de transition).</summary>
    public float TacticalBlend { get; private set; }
    /// <summary>Multiplicateur alpha de la slime map (1 = pleine opacité, 0 = invisible).</summary>
    public float SlimeAlpha    { get; private set; } = 1f;
    /// <summary>Alpha du terrain overlay (0 = invisible, 1 = opaque).</summary>
    public float TerrainAlpha  { get; private set; }
    /// <summary>True quand OrthoSize < TacticalThreshold.</summary>
    public bool  IsInTacticalMode { get; private set; }
    /// <summary>Bounds caméra en espace simulation (0-512). (minX, minY, maxX, maxY).</summary>
    public Vector4 CameraSimBounds { get; private set; }

    // ── Couches tactiques ──────────────────────────────────────────────────
    private readonly List<ITacticalLayer> _layers = new();
    private bool _wasInTacticalMode;

    // ── Privé ─────────────────────────────────────────────────────────────
    private Camera _cam;
    private SlimeMapRenderer _slimeRenderer;

    // ─────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        _cam           = Camera.main;
        _slimeRenderer = SlimeMapRenderer.Instance;

        // Initialiser slimeDisplayAlpha à 1 (au cas où SlimeMapRenderer ne l'a pas encore fait)
        if (_slimeRenderer != null && _slimeRenderer.SlimeShader != null)
            _slimeRenderer.SlimeShader.SetFloat("slimeDisplayAlpha", 1f);
    }

    /// <summary>
    /// Enregistre une couche tactique pour recevoir les callbacks
    /// OnEnterTactical / OnExitTactical / OnCameraBoundsChanged.
    /// </summary>
    public void RegisterLayer(ITacticalLayer layer)
    {
        if (!_layers.Contains(layer))
            _layers.Add(layer);
    }

    public void UnregisterLayer(ITacticalLayer layer)
    {
        _layers.Remove(layer);
    }

    private void Update()
    {
        if (_cam == null) { _cam = Camera.main; return; }
        if (_slimeRenderer == null) { _slimeRenderer = SlimeMapRenderer.Instance; return; }

        float ortho = _cam.orthographicSize;

        // ── Blend factor ────────────────────────────────────────────────
        TacticalBlend = Mathf.SmoothStep(0f, 1f,
            Mathf.InverseLerp(StrategicThreshold, TacticalThreshold, ortho));

        SlimeAlpha   = 1f - TacticalBlend;
        TerrainAlpha = TacticalBlend;
        IsInTacticalMode = ortho < TacticalThreshold;

        // ── Shader uniform slimeDisplayAlpha ────────────────────────────
        // Set avant que SlimeMapRenderer dispatche ComposeDisplay (grâce à DefaultExecutionOrder -10)
        if (_slimeRenderer.SlimeShader != null)
            _slimeRenderer.SlimeShader.SetFloat("slimeDisplayAlpha", SlimeAlpha);

        // ── Terrain material ─────────────────────────────────────────────
        if (TerrainOverlayMaterial != null)
            TerrainOverlayMaterial.SetFloat("_Alpha", TerrainAlpha);

        // ── Camera bounds (world = sim space 0-512) ─────────────────────
        Vector3 bl = _cam.ViewportToWorldPoint(new Vector3(0f, 0f, _cam.nearClipPlane));
        Vector3 tr = _cam.ViewportToWorldPoint(new Vector3(1f, 1f, _cam.nearClipPlane));

        int simW = _slimeRenderer.MapWidth;
        int simH = _slimeRenderer.MapHeight;
        CameraSimBounds = new Vector4(
            Mathf.Max(0f, bl.x),
            Mathf.Max(0f, bl.y),
            Mathf.Min(simW, tr.x),
            Mathf.Min(simH, tr.y)
        );

        // ── Notifications aux couches ────────────────────────────────────
        bool enteringTactical = IsInTacticalMode && !_wasInTacticalMode;
        bool exitingTactical  = !IsInTacticalMode && _wasInTacticalMode;

        if (enteringTactical)
            foreach (var l in _layers) l.OnEnterTactical(CameraSimBounds);
        else if (exitingTactical)
            foreach (var l in _layers) l.OnExitTactical();
        else if (IsInTacticalMode)
            foreach (var l in _layers) l.OnCameraBoundsChanged(CameraSimBounds);

        _wasInTacticalMode = IsInTacticalMode;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
