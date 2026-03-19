using UnityEngine;
using UnityEngine.UIElements;
using Unity.Entities;

public class UIController : MonoBehaviour
{
    private UIDocument uiDocument;

    // ── Top bar ────────────────────────────────────────────────────
    private Label  energyLabel;
    private Label  entityCountLabel;
    private Button pauseButton;

    // ── Game controls ──────────────────────────────────────────────
    private Slider    speedSlider;
    private Label     speedValueLabel;
    private Button    addEntitiesButton;
    private Slider    maxAgeSlider;
    private Label     maxAgeLabel;
    private SliderInt stepsPerFrameSlider;
    private Label     stepsPerFrameLabel;

    // ── Trail ──────────────────────────────────────────────────────
    private Slider trailWeightSlider;  private Label trailWeightLabel;
    private Slider decayRateSlider;    private Label decayRateLabel;
    private Slider diffuseRateSlider;  private Label diffuseRateLabel;

    // ── Movement ───────────────────────────────────────────────────
    private Slider moveSpeedSlider;    private Label moveSpeedLabel;
    private Slider turnSpeedSlider;    private Label turnSpeedLabel;

    // ── Sensors ────────────────────────────────────────────────────
    private Slider    sensorAngleSlider;  private Label sensorAngleLabel;
    private Slider    sensorOffsetSlider; private Label sensorOffsetLabel;
    private SliderInt sensorSizeSlider;   private Label sensorSizeLabel;

    // ── Map ────────────────────────────────────────────────────────
    private Button toggleStrategyMapButton;
    private Button overlayModeButton;

    // ── Tech ───────────────────────────────────────────────────────
    private Label  techNameLabel;
    private Label  techCostLabel;
    private Button researchButton;

    // ── State ──────────────────────────────────────────────────────
    private bool  isPaused          = false;
    private float previousTimeScale = 1f;
    private bool  strategyMapVisible = true;
    private bool  overlayMode        = false;
    private bool  hasResearchedMembrane = false;

    private GameObject strategyLayerGO;
    private Shader     opaqueShader;
    private Shader     additiveShader;

    private EntityManager entityManager;
    private int  uiUpdateCounter = 0;
    private string targetTechId  = "tech_membrane";
    private int    techCostCache  = 1500;
    private float  currentP1Energy = 0f;

    // ==============================================================
    private void OnEnable()
    {
        uiDocument = GetComponent<UIDocument>();
        if (uiDocument == null) return;
        var root = uiDocument.rootVisualElement;

        // Top bar
        energyLabel        = root.Q<Label> ("EnergyLabel");
        entityCountLabel   = root.Q<Label> ("EntityCountLabel");
        pauseButton        = root.Q<Button>("PauseButton");

        // Game controls
        speedSlider        = root.Q<Slider>   ("SpeedSlider");
        speedValueLabel    = root.Q<Label>    ("SpeedValueLabel");
        addEntitiesButton  = root.Q<Button>   ("AddEntitiesButton");
        maxAgeSlider       = root.Q<Slider>   ("MaxAgeSlider");
        maxAgeLabel        = root.Q<Label>    ("MaxAgeLabel");
        stepsPerFrameSlider= root.Q<SliderInt>("StepsPerFrameSlider");
        stepsPerFrameLabel = root.Q<Label>    ("StepsPerFrameLabel");

        // Trail
        trailWeightSlider  = root.Q<Slider>("TrailWeightSlider");  trailWeightLabel  = root.Q<Label>("TrailWeightLabel");
        decayRateSlider    = root.Q<Slider>("DecayRateSlider");    decayRateLabel    = root.Q<Label>("DecayRateLabel");
        diffuseRateSlider  = root.Q<Slider>("DiffuseRateSlider");  diffuseRateLabel  = root.Q<Label>("DiffuseRateLabel");

        // Movement
        moveSpeedSlider    = root.Q<Slider>("MoveSpeedSlider");    moveSpeedLabel    = root.Q<Label>("MoveSpeedLabel");
        turnSpeedSlider    = root.Q<Slider>("TurnSpeedSlider");    turnSpeedLabel    = root.Q<Label>("TurnSpeedLabel");

        // Sensors
        sensorAngleSlider  = root.Q<Slider>   ("SensorAngleSlider");  sensorAngleLabel  = root.Q<Label>   ("SensorAngleLabel");
        sensorOffsetSlider = root.Q<Slider>   ("SensorOffsetSlider"); sensorOffsetLabel = root.Q<Label>   ("SensorOffsetLabel");
        sensorSizeSlider   = root.Q<SliderInt>("SensorSizeSlider");   sensorSizeLabel   = root.Q<Label>   ("SensorSizeLabel");

        // Map
        toggleStrategyMapButton = root.Q<Button>("ToggleStrategyMapButton");
        overlayModeButton       = root.Q<Button>("OverlayModeButton");

        // Tech
        techNameLabel  = root.Q<Label> ("TechNameLabel");
        techCostLabel  = root.Q<Label> ("TechCostLabel");
        researchButton = root.Q<Button>("ResearchButton");

        // Hooks
        if (pauseButton        != null) pauseButton.clicked        += TogglePause;
        if (addEntitiesButton  != null) addEntitiesButton.clicked  += OnAddEntities;
        if (researchButton     != null) researchButton.clicked     += OnResearchClicked;
        if (toggleStrategyMapButton != null) toggleStrategyMapButton.clicked += OnToggleStrategyMap;
        if (overlayModeButton  != null) overlayModeButton.clicked  += OnToggleOverlay;

        speedSlider?.RegisterValueChangedCallback(e => {
            if (speedValueLabel != null) speedValueLabel.text = $"{e.newValue:F1}x";
            SetGameTimeScale(e.newValue);
        });

        stepsPerFrameSlider?.RegisterValueChangedCallback(e => {
            if (stepsPerFrameLabel != null) stepsPerFrameLabel.text = e.newValue.ToString();
            if (SlimeMapRenderer.Instance != null) SlimeMapRenderer.Instance.StepsPerFrame = e.newValue;
        });

        BindSlider(maxAgeSlider,       maxAgeLabel,       "F0", v => { if (SlimeMapRenderer.Instance != null) SlimeMapRenderer.Instance.MaxAge       = v; });
        BindSlider(trailWeightSlider,  trailWeightLabel,  "F1", v => { if (SlimeMapRenderer.Instance != null) SlimeMapRenderer.Instance.TrailWeight  = v; });
        BindSlider(decayRateSlider,    decayRateLabel,    "F1", v => { if (SlimeMapRenderer.Instance != null) SlimeMapRenderer.Instance.DecayRate    = v; });
        BindSlider(diffuseRateSlider,  diffuseRateLabel,  "F1", v => { if (SlimeMapRenderer.Instance != null) SlimeMapRenderer.Instance.DiffuseRate  = v; });
        BindSlider(moveSpeedSlider,    moveSpeedLabel,    "F0", v => { if (SlimeMapRenderer.Instance != null) SlimeMapRenderer.Instance.MoveSpeed    = v; });
        BindSlider(turnSpeedSlider,    turnSpeedLabel,    "F0", v => { if (SlimeMapRenderer.Instance != null) SlimeMapRenderer.Instance.TurnSpeed    = v; });
        BindSlider(sensorAngleSlider,  sensorAngleLabel,  "F0°",v => { if (SlimeMapRenderer.Instance != null) SlimeMapRenderer.Instance.SensorAngleDeg = v; });
        BindSlider(sensorOffsetSlider, sensorOffsetLabel, "F0", v => { if (SlimeMapRenderer.Instance != null) SlimeMapRenderer.Instance.SensorOffsetDst = v; });
        sensorSizeSlider?.RegisterValueChangedCallback(e => {
            if (sensorSizeLabel  != null) sensorSizeLabel.text  = e.newValue.ToString();
            if (SlimeMapRenderer.Instance != null) SlimeMapRenderer.Instance.SensorSize = e.newValue;
        });

        // Scene refs
        strategyLayerGO = GameObject.Find("StrategyLayer");
        opaqueShader    = Shader.Find("Unlit/Texture");
        additiveShader  = Shader.Find("Custom/UnlitAdditive");

        entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
        UpdateTechUI();
    }

    private void BindSlider(Slider slider, Label label, string fmt, System.Action<float> onChange)
    {
        if (slider == null) return;
        slider.RegisterValueChangedCallback(e => {
            if (label != null) label.text = fmt.EndsWith("°") ? $"{e.newValue:F0}°" : e.newValue.ToString(fmt, System.Globalization.CultureInfo.InvariantCulture);
            onChange?.Invoke(e.newValue);
        });
    }

    private void OnDisable()
    {
        if (pauseButton != null) pauseButton.clicked -= TogglePause;
        if (addEntitiesButton != null) addEntitiesButton.clicked -= OnAddEntities;
        if (toggleStrategyMapButton != null) toggleStrategyMapButton.clicked -= OnToggleStrategyMap;
        if (overlayModeButton != null) overlayModeButton.clicked -= OnToggleOverlay;
        if (researchButton != null) researchButton.clicked -= OnResearchClicked;
    }

    // ── Add 10,000 GPU Agents ─────────────────────────────────────
    private void OnAddEntities()
    {
        SlimeMapRenderer.Instance?.AddAgents(10000);
    }

    // ── Game speed / pause ────────────────────────────────────────
    private void SetGameTimeScale(float scale)
    {
        if (entityManager == default) return;
        var q = entityManager.CreateEntityQuery(typeof(GameTime));
        if (q.IsEmpty) return;
        var e  = q.GetSingletonEntity();
        var gt = entityManager.GetComponentData<GameTime>(e);
        gt.TimeScale = scale;
        entityManager.SetComponentData(e, gt);
        isPaused = scale <= 0.01f;
        if (pauseButton != null) pauseButton.text = isPaused ? "Play" : "Pause";
    }

    private void TogglePause()
    {
        isPaused = !isPaused;
        float newScale = isPaused ? 0f : (previousTimeScale > 0f ? previousTimeScale : 1f);
        if (!isPaused && speedSlider != null) previousTimeScale = speedSlider.value;
        SetGameTimeScale(newScale);
        if (speedSlider != null && !isPaused) speedSlider.value = newScale;
        if (pauseButton != null) pauseButton.text = isPaused ? "Play" : "Pause";
    }

    // ── Strategy map ──────────────────────────────────────────────
    private void OnToggleStrategyMap()
    {
        strategyMapVisible = !strategyMapVisible;
        strategyLayerGO?.SetActive(strategyMapVisible);
        if (toggleStrategyMapButton != null)
            toggleStrategyMapButton.text = strategyMapVisible ? "Cacher Strategy Map" : "Montrer Strategy Map";
    }

    private void OnToggleOverlay()
    {
        overlayMode = !overlayMode;
        if (strategyLayerGO != null)
        {
            var mr = strategyLayerGO.GetComponent<MeshRenderer>();
            if (mr?.sharedMaterial != null)
                mr.sharedMaterial.shader = overlayMode && additiveShader != null ? additiveShader : opaqueShader;
            if (overlayMode && !strategyMapVisible)
            {
                strategyMapVisible = true;
                strategyLayerGO.SetActive(true);
                if (toggleStrategyMapButton != null) toggleStrategyMapButton.text = "Cacher Strategy Map";
            }
        }
        if (overlayModeButton != null)
        {
            overlayModeButton.text = overlayMode ? "Mode Opaque" : "Mode Overlay";
            overlayModeButton.style.backgroundColor = new StyleColor(
                overlayMode ? new Color(0.2f, 0.5f, 0.35f) : new Color(0.2f, 0.31f, 0.39f));
        }
    }

    // ── Tech ──────────────────────────────────────────────────────
    private void UpdateTechUI()
    {
        if (DataLoader.Instance != null && DataLoader.Instance.TechDatabase.Count > 0)
        {
            var tech = DataLoader.Instance.GetTech(targetTechId);
            if (tech.id != null)
            {
                if (techNameLabel != null) techNameLabel.text = "Tech: " + LocalizationManager.Instance.GetText(tech.nameKey);
                techCostCache = tech.energyCost;
                if (techCostLabel != null) techCostLabel.text = $"Coût : {techCostCache} Énergie";
            }
        }
    }

    private void OnResearchClicked()
    {
        if (hasResearchedMembrane) return;
        if (currentP1Energy >= techCostCache)
        {
            hasResearchedMembrane = true;
            if (researchButton != null) { researchButton.text = "Recherché !"; researchButton.SetEnabled(false); }
            if (techCostLabel  != null)   techCostLabel.text  = "Débloqué";
            if (entityManager  != default)
            {
                var ev = entityManager.CreateEntity();
                entityManager.AddComponentData(ev, new TechResearchedEvent { TechID = targetTechId });
            }
        }
    }

    // ── Update Loop ───────────────────────────────────────────────
    private void Update()
    {
        uiUpdateCounter++;
        if (uiUpdateCounter < 15) return;
        uiUpdateCounter = 0;

        // GPU agent count
        if (entityCountLabel != null)
        {
            int n = SlimeMapRenderer.Instance != null ? SlimeMapRenderer.Instance.AgentCount : 0;
            entityCountLabel.text = $"Agents : {n:N0}";
        }

        if (energyLabel != null) energyLabel.text = "Énergie : —";
    }
}
