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

    // Performance: cached query + throttle
    private EntityManager entityManager;
    private EntityQuery cellQuery;
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

        // Cache strategy layer reference (works even if it becomes inactive later)
        strategyLayerGO = GameObject.Find("StrategyLayer");

        // Cache shaders
        opaqueShader = Shader.Find("Unlit/Texture");
        additiveShader = Shader.Find("Custom/UnlitAdditive");

        entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
        cellQuery = entityManager.CreateEntityQuery(typeof(CellComponent));
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

    // ── Add 1000 Entities ─────────────────────────────────────
    private void OnAddEntities() {
        if (entityManager == default) return;

        var bQuery = entityManager.CreateEntityQuery(typeof(GlobalBootstrapData));
        if (bQuery.IsEmpty) return;

        var bootstrap = bQuery.GetSingleton<GlobalBootstrapData>();
        if (bootstrap.CellPrefab == Entity.Null) return;

        bool hasWalkability = false;
        TerrainMapData terrainData = default;
        var tQuery = entityManager.CreateEntityQuery(typeof(TerrainMapData));
        if (!tQuery.IsEmpty) {
            terrainData = tQuery.GetSingleton<TerrainMapData>();
            hasWalkability = true;
        }

        var ecb = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);
        uint seed = (uint)(Time.time * 10000) + 7;
        Unity.Mathematics.Random rand = new Unity.Mathematics.Random(seed);

        for (int i = 0; i < 1000; i++) {
            Entity e = ecb.Instantiate(bootstrap.CellPrefab);
            int playerID = rand.NextInt(0, 2);

            float3 pos = float3.zero;
            for (int attempt = 0; attempt < 50; attempt++) {
                pos = new float3(rand.NextFloat(10f, 502f), rand.NextFloat(10f, 502f), 0f);
                if (!hasWalkability) break;
                ref var blob = ref terrainData.WalkabilityRef.Value;
                int ix = math.clamp((int)pos.x, 0, blob.Width - 1);
                int iy = math.clamp((int)pos.y, 0, blob.Height - 1);
                if (blob.Walkable[iy * blob.Width + ix] == 1) break;
            }

            ecb.SetComponent(e, LocalTransform.FromPosition(pos));

            float angle = rand.NextFloat(0f, math.PI * 2f);
            ecb.AddComponent(e, new CellComponent {
                Energy = 20f,
                Speed = 50f,
                PlayerID = playerID,
                TimeSinceLastMove = 0f,
                TargetDirection = new float3(math.cos(angle), math.sin(angle), 0f)
            });
        }

        ecb.Playback(entityManager);
        ecb.Dispose();
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
        if (entityManager == default) return;
        var cellEntities = cellQuery.ToEntityArray(Unity.Collections.Allocator.Temp);
        float remaining = amountToDeduct;
        foreach (var entity in cellEntities) {
            if (remaining <= 0) break;
            var cell = entityManager.GetComponentData<CellComponent>(entity);
            if (cell.PlayerID == 0) {
                if (cell.Energy >= remaining) { cell.Energy -= remaining; remaining = 0; }
                else { remaining -= cell.Energy; cell.Energy = 0; }
                entityManager.SetComponentData(entity, cell);
            }
        }
        cellEntities.Dispose();
    }
    
    // ── Update Loop (THROTTLED) ────────────────────────────────
    private void Update() {
        if (entityManager == default) return;

        // Only update UI every 15 frames to avoid main-thread sync overhead
        uiUpdateCounter++;
        if (uiUpdateCounter < 15) return;
        uiUpdateCounter = 0;

        // Use CalculateEntityCount (no allocation, very fast)
        int totalEntities = cellQuery.CalculateEntityCount();
        if (entityCountLabel != null)
            entityCountLabel.text = $"Entités : {totalEntities}";

        // Energy: use Allocator.Temp (stack-like, much cheaper than TempJob)
        if (energyLabel != null) {
            var cellArray = cellQuery.ToComponentDataArray<CellComponent>(Unity.Collections.Allocator.Temp);
            float totalEnergy = 0f;
            for (int i = 0; i < cellArray.Length; i++) {
                if (cellArray[i].PlayerID == 0)
                    totalEnergy += cellArray[i].Energy;
            }
            cellArray.Dispose();
            currentP1Energy = totalEnergy;
            energyLabel.text = $"Énergie : {Mathf.FloorToInt(totalEnergy)}";

            if (researchButton != null && !hasResearchedMembrane)
                researchButton.SetEnabled(currentP1Energy >= techCostCache);
        }
    }
}
