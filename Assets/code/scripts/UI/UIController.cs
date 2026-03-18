using UnityEngine;
using UnityEngine.UIElements;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

public class UIController : MonoBehaviour {
    
    private UIDocument uiDocument;

    // Top bar
    private Label energyLabel;
    private Label entityCountLabel;
    private Button pauseButton;

    // Control panel
    private Slider speedSlider;
    private Label speedValueLabel;
    private Button addEntitiesButton;
    private Button toggleStrategyMapButton;
    private Button overlayModeButton;

    // Tech panel
    private Label techNameLabel;
    private Label techCostLabel;
    private Button researchButton;

    // State
    private bool isPaused = false;
    private float previousTimeScale = 1f;
    private bool strategyMapVisible = true;
    private bool overlayMode = false;

    // Cached references
    private GameObject strategyLayerGO;
    private Shader opaqueShader;
    private Shader additiveShader;

    private float currentP1Energy = 0f;
    private bool hasResearchedMembrane = false;
    private string targetTechId = "tech_membrane";
    private int techCostCache = 1500;

    // Performance: throttle
    private EntityManager entityManager;
    private int uiUpdateCounter = 0;

    private void OnEnable() {
        uiDocument = GetComponent<UIDocument>();
        if (uiDocument == null) return;

        var root = uiDocument.rootVisualElement;
        
        energyLabel = root.Q<Label>("EnergyLabel");
        entityCountLabel = root.Q<Label>("EntityCountLabel");
        pauseButton = root.Q<Button>("PauseButton");
        speedSlider = root.Q<Slider>("SpeedSlider");
        speedValueLabel = root.Q<Label>("SpeedValueLabel");
        addEntitiesButton = root.Q<Button>("AddEntitiesButton");
        toggleStrategyMapButton = root.Q<Button>("ToggleStrategyMapButton");
        overlayModeButton = root.Q<Button>("OverlayModeButton");
        techNameLabel = root.Q<Label>("TechNameLabel");
        techCostLabel = root.Q<Label>("TechCostLabel");
        researchButton = root.Q<Button>("ResearchButton");

        if (pauseButton != null) pauseButton.clicked += TogglePause;
        if (researchButton != null) researchButton.clicked += OnResearchClicked;
        if (speedSlider != null) speedSlider.RegisterValueChangedCallback(OnSpeedChanged);
        if (addEntitiesButton != null) addEntitiesButton.clicked += OnAddEntities;
        if (toggleStrategyMapButton != null) toggleStrategyMapButton.clicked += OnToggleStrategyMap;
        if (overlayModeButton != null) overlayModeButton.clicked += OnToggleOverlay;

        // Cache strategy layer reference
        strategyLayerGO = GameObject.Find("StrategyLayer");

        // Cache shaders
        opaqueShader = Shader.Find("Unlit/Texture");
        additiveShader = Shader.Find("Custom/UnlitAdditive");

        entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
        UpdateTechUI();
    }

    private void OnDisable() {
        if (pauseButton != null) pauseButton.clicked -= TogglePause;
        if (researchButton != null) researchButton.clicked -= OnResearchClicked;
        if (addEntitiesButton != null) addEntitiesButton.clicked -= OnAddEntities;
        if (toggleStrategyMapButton != null) toggleStrategyMapButton.clicked -= OnToggleStrategyMap;
        if (overlayModeButton != null) overlayModeButton.clicked -= OnToggleOverlay;
        if (speedSlider != null) speedSlider.UnregisterValueChangedCallback(OnSpeedChanged);
    }

    // ── Speed Slider ──────────────────────────────────────────
    private void OnSpeedChanged(ChangeEvent<float> evt) {
        float newSpeed = evt.newValue;
        if (speedValueLabel != null)
            speedValueLabel.text = $"{newSpeed:F1}x";

        if (entityManager == default) return;
        var query = entityManager.CreateEntityQuery(typeof(GameTime));
        if (query.IsEmpty) return;

        var entity = query.GetSingletonEntity();
        var gt = entityManager.GetComponentData<GameTime>(entity);
        gt.TimeScale = newSpeed;
        entityManager.SetComponentData(entity, gt);

        isPaused = newSpeed <= 0.01f;
        if (pauseButton != null)
            pauseButton.text = isPaused ? "Play" : "Pause";
    }

    // ── Add 10 000 GPU Agents ─────────────────────────────────
    private void OnAddEntities() {
        if (SlimeMapRenderer.Instance != null)
            SlimeMapRenderer.Instance.AddAgents(10000);
    }

    // ── Toggle Strategy Map ───────────────────────────────────
    private void OnToggleStrategyMap() {
        strategyMapVisible = !strategyMapVisible;

        if (strategyLayerGO != null) {
            strategyLayerGO.SetActive(strategyMapVisible);
        }

        if (toggleStrategyMapButton != null) {
            toggleStrategyMapButton.text = strategyMapVisible
                ? "Cacher Strategy Map"
                : "Montrer Strategy Map";
        }
    }

    // ── Toggle Overlay Mode ──────────────────────────────────
    private void OnToggleOverlay() {
        overlayMode = !overlayMode;

        if (strategyLayerGO != null) {
            var mr = strategyLayerGO.GetComponent<MeshRenderer>();
            if (mr != null && mr.sharedMaterial != null) {
                if (overlayMode && additiveShader != null) {
                    mr.sharedMaterial.shader = additiveShader;
                } else if (opaqueShader != null) {
                    mr.sharedMaterial.shader = opaqueShader;
                }
            }

            // Ensure the strategy layer is visible when enabling overlay
            if (overlayMode && !strategyMapVisible) {
                strategyMapVisible = true;
                strategyLayerGO.SetActive(true);
                if (toggleStrategyMapButton != null)
                    toggleStrategyMapButton.text = "Cacher Strategy Map";
            }
        }

        if (overlayModeButton != null) {
            overlayModeButton.text = overlayMode ? "Mode Opaque" : "Mode Overlay";
            overlayModeButton.style.backgroundColor = new StyleColor(
                overlayMode ? new Color(0.20f, 0.50f, 0.35f) : new Color(0.20f, 0.31f, 0.39f));
        }
    }

    // ── Pause ──────────────────────────────────────────────────
    private void TogglePause() {
        if (entityManager == default) return;
        
        var query = entityManager.CreateEntityQuery(typeof(GameTime));
        if (query.IsEmpty) return;

        var gameTimeEntity = query.GetSingletonEntity();
        var gameTime = entityManager.GetComponentData<GameTime>(gameTimeEntity);

        isPaused = !isPaused;

        if (isPaused) {
            previousTimeScale = gameTime.TimeScale;
            gameTime.TimeScale = 0f;
            pauseButton.text = "Play";
            if (speedSlider != null) speedSlider.value = 0f;
        } else {
            gameTime.TimeScale = previousTimeScale > 0 ? previousTimeScale : 1f;
            pauseButton.text = "Pause";
            if (speedSlider != null) speedSlider.value = gameTime.TimeScale;
        }

        entityManager.SetComponentData(gameTimeEntity, gameTime);
    }
    
    // ── Tech UI ────────────────────────────────────────────────
    private void UpdateTechUI() {
        if (DataLoader.Instance != null && DataLoader.Instance.TechDatabase.Count > 0) {
            var tech = DataLoader.Instance.GetTech(targetTechId);
            if (tech.id != null) {
                techNameLabel.text = "Tech: " + LocalizationManager.Instance.GetText(tech.nameKey);
                techCostCache = tech.energyCost;
                techCostLabel.text = $"Coût : {techCostCache} Énergie";
            }
        }
    }

    private void OnResearchClicked() {
        if (hasResearchedMembrane) return;
        if (currentP1Energy >= techCostCache) {
            DeductEnergyFromPlayer1(techCostCache);
            hasResearchedMembrane = true;
            researchButton.text = "Recherché !";
            researchButton.SetEnabled(false);
            techCostLabel.text = "Débloqué";
            if (entityManager != default) {
                var evtEntity = entityManager.CreateEntity();
                entityManager.AddComponentData(evtEntity, new TechResearchedEvent { TechID = targetTechId });
            }
        }
    }

    private void DeductEnergyFromPlayer1(float amountToDeduct) {
        // Energy tracking will be re-added via GPU readback in a future step
    }

    // ── Update Loop (THROTTLED) ────────────────────────────────
    private void Update() {
        uiUpdateCounter++;
        if (uiUpdateCounter < 15) return;
        uiUpdateCounter = 0;

        // Agent count from GPU manager
        if (entityCountLabel != null) {
            int count = SlimeMapRenderer.Instance != null ? SlimeMapRenderer.Instance.AgentCount : 0;
            entityCountLabel.text = $"Entités : {count:N0}";
        }

        // Energy placeholder (GPU readback not yet implemented)
        if (energyLabel != null)
            energyLabel.text = "Énergie : —";
    }
}
