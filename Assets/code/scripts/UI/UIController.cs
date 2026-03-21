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

    private Button[] playerSelectButtons  = new Button[6];
    private Button[] typeButtons          = new Button[10];
    private string[] typeButtonBaseTexts  = new string[10];
    private Button[] warButtons           = new Button[6];
    private Toggle toggleVisibility;
    private Toggle toggleSpeciesOverlay;
    private int    selectedPlayerIndex = 0; // index dans PlayerLibrary.GetAll()
    private string selectedPlayerId    = "";
    private string selectedSpeciesId   = ""; // espèce sélectionnée au sein du joueur

    private static readonly string[] typeButtonNames = {
        "BtnTypePlante","BtnTypeAnimal","BtnTypeChampignon","BtnTypeInsecte","BtnTypeBacterie","BtnTypeAlgue",
        "BtnTypeGlobuleRouge","BtnTypeGlobuleBlanc","BtnTypeVirus","BtnTypePlaquette"
    };

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

    // ── Species count ──────────────────────────────────────────────
    private Label speciesCountLabel;

    // ── Construction ───────────────────────────────────────────────
    private Button        btnConstruction;
    private VisualElement constructionPanel;
    private bool          constructionPanelOpen = false;
    private VisualElement placementBanner;
    private Label         placementBannerText;
    private Button        btnCancelPlacement;
    private bool          wasInOverlayBeforePlacement = false;

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

        BindSlider(maxAgeSlider,       maxAgeLabel,       "F0", v => { var smr = SlimeMapRenderer.Instance; if (smr != null) { int sl = GetCurrentSpeciesSlot(); smr.speciesSettings[sl].maxAge = v; } });
        BindSlider(trailWeightSlider,  trailWeightLabel,  "F1", v => { var smr = SlimeMapRenderer.Instance; if (smr != null) { int sl = GetCurrentSpeciesSlot(); smr.speciesSettings[sl].trailWeight = v; } });
        BindSlider(decayRateSlider,    decayRateLabel,    "F1", v => { var smr = SlimeMapRenderer.Instance; if (smr != null) { int sl = GetCurrentSpeciesSlot(); smr.speciesSettings[sl].decayRate = v; } });
        BindSlider(diffuseRateSlider,  diffuseRateLabel,  "F1", v => { var smr = SlimeMapRenderer.Instance; if (smr != null) { int sl = GetCurrentSpeciesSlot(); smr.speciesSettings[sl].diffuseRate = v; } });
        BindSlider(moveSpeedSlider,    moveSpeedLabel,    "F0", v => { var smr = SlimeMapRenderer.Instance; if (smr != null) { int sl = GetCurrentSpeciesSlot(); smr.speciesSettings[sl].moveSpeed = v; } });
        BindSlider(turnSpeedSlider,    turnSpeedLabel,    "F0", v => { var smr = SlimeMapRenderer.Instance; if (smr != null) { int sl = GetCurrentSpeciesSlot(); smr.speciesSettings[sl].turnSpeed = v; } });
        BindSlider(sensorAngleSlider,  sensorAngleLabel,  "F0°",v => { var smr = SlimeMapRenderer.Instance; if (smr != null) { int sl = GetCurrentSpeciesSlot(); smr.speciesSettings[sl].sensorAngleRad = v * Mathf.Deg2Rad; } });
        BindSlider(sensorOffsetSlider, sensorOffsetLabel, "F0", v => { var smr = SlimeMapRenderer.Instance; if (smr != null) { int sl = GetCurrentSpeciesSlot(); smr.speciesSettings[sl].sensorOffsetDst = v; } });
        sensorSizeSlider?.RegisterValueChangedCallback(e => {
            if (sensorSizeLabel  != null) sensorSizeLabel.text  = e.newValue.ToString();
            var smr = SlimeMapRenderer.Instance; if (smr != null) { int sl = GetCurrentSpeciesSlot(); smr.speciesSettings[sl].sensorSize = e.newValue; }
        });

        // Maps and Toggles
        playerSelectButtons[0] = root.Q<Button>("BtnSelectP1");
        playerSelectButtons[1] = root.Q<Button>("BtnSelectP2");
        playerSelectButtons[2] = root.Q<Button>("BtnSelectP3");
        playerSelectButtons[3] = root.Q<Button>("BtnSelectP4");
        playerSelectButtons[4] = root.Q<Button>("BtnSelectP5");
        playerSelectButtons[5] = root.Q<Button>("BtnSelectP6");

        speciesCountLabel = root.Q<Label>("SpeciesCountLabel");

        toggleVisibility = root.Q<Toggle>("ToggleVisibility");
        if (toggleVisibility != null) {
            toggleVisibility.RegisterValueChangedCallback(e => {
                if (SlimeMapRenderer.Instance != null) {
                    SlimeMapRenderer.Instance.SetPlayerVisibility(GetCurrentSpeciesSlot(), e.newValue);
                }
            });
        }

        toggleSpeciesOverlay = root.Q<Toggle>("ToggleSpeciesOverlay");
        if (toggleSpeciesOverlay != null) {
            toggleSpeciesOverlay.RegisterValueChangedCallback(e => {
                if (WaypointOverlayRenderer.Instance != null) {
                    WaypointOverlayRenderer.Instance.ShowSpeciesOverlay[GetCurrentSpeciesSlot()] = e.newValue;
                }
            });
        }

        for (int i = 0; i < 6; i++) {
            int index = i;
            if (playerSelectButtons[i] != null) {
                playerSelectButtons[i].clicked += () => SelectPlayer(index);
            }
        }

        // Species buttons — sélecteur d'espèce au sein du joueur actif
        for (int i = 0; i < 10; i++) {
            typeButtons[i] = root.Q<Button>(typeButtonNames[i]);
            typeButtonBaseTexts[i] = typeButtons[i]?.text ?? ((SpeciesType)i).ToString();
            string typeId = ((SpeciesType)i).ToString().ToLowerInvariant();
            if (typeButtons[i] != null) {
                string capturedId = typeId;
                typeButtons[i].clicked += () => OnSelectSpecies(capturedId);
            }
        }

        // War buttons
        for (int i = 0; i < 6; i++) {
            warButtons[i] = root.Q<Button>($"BtnWar{i}");
            int enemyIndex = i;
            if (warButtons[i] != null) {
                warButtons[i].clicked += () => OnToggleWar(enemyIndex);
            }
        }

        // Construction panel
        btnConstruction = root.Q<Button>("BtnConstruction");
        if (btnConstruction != null) btnConstruction.clicked += ToggleConstructionPanel;

        placementBanner     = root.Q<VisualElement>("PlacementBanner");
        placementBannerText = root.Q<Label>("PlacementBannerText");
        btnCancelPlacement  = root.Q<Button>("BtnCancelPlacement");
        if (btnCancelPlacement != null)
            btnCancelPlacement.clicked += () => BuildingPlacementController.Instance?.CancelPlacement();

        // Events are subscribed lazily the first time the panel is opened
        // (BuildingPlacementController auto-creates itself on AfterSceneLoad, after OnEnable)

        // Scene refs
        strategyLayerGO = GameObject.Find("StrategyLayer");
        opaqueShader    = Shader.Find("Unlit/Texture");
        additiveShader  = Shader.Find("Custom/UnlitAdditive");

        if (World.DefaultGameObjectInjectionWorld != null)
        {
            entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
        }
        UpdateTechUI();
        
        // Use a coroutine or deferred call to let SlimeMapRenderer initialize if this runs before it
        StartCoroutine(InitSelectionDeferred());
    }

    private System.Collections.IEnumerator InitSelectionDeferred()
    {
        yield return null; // wait 1 frame
        // Initialise selectedPlayerId et selectedSpeciesId depuis PlayerLibrary
        var lib = PlayerLibrary.Instance;
        if (lib != null && lib.GetAll().Count > 0)
        {
            selectedPlayerId = lib.GetAll()[0].id;
            var species = lib.GetSpeciesForPlayer(selectedPlayerId);
            if (species.Count > 0) selectedSpeciesId = species[0];
        }
        SelectPlayer(0);
    }

    // Slot GPU pour l'espèce sélectionnée dans le joueur actif
    private int GetCurrentSpeciesSlot()
    {
        var lib = PlayerLibrary.Instance;
        if (lib != null && !string.IsNullOrEmpty(selectedSpeciesId))
        {
            int s = lib.GetSlotIndex(selectedPlayerId, selectedSpeciesId);
            if (s >= 0) return s;
        }
        return GetFirstSlotForPlayer(selectedPlayerId);
    }

    // Retourne le premier slot GPU du joueur sélectionné (pour sliders et visibilité)
    private int GetFirstSlotForPlayer(string playerId)
    {
        var lib = PlayerLibrary.Instance;
        if (lib == null || string.IsNullOrEmpty(playerId)) return 0;
        var species = lib.GetSpeciesForPlayer(playerId);
        if (species.Count == 0) return 0;
        int s = lib.GetSlotIndex(playerId, species[0]);
        return s >= 0 ? s : 0;
    }

    private void SelectPlayer(int index)
    {
        selectedPlayerIndex = index;

        var lib = PlayerLibrary.Instance;
        var players = lib?.GetAll();
        if (players != null && index >= 0 && index < players.Count)
        {
            selectedPlayerId = players[index].id;
            // Sélectionne la première espèce du joueur par défaut
            var species = lib.GetSpeciesForPlayer(selectedPlayerId);
            if (species.Count > 0) selectedSpeciesId = species[0];
        }

        // Close construction panel when switching player (it's species-specific)
        if (constructionPanelOpen)
        {
            constructionPanel?.RemoveFromHierarchy();
            constructionPanel     = null;
            constructionPanelOpen = false;
        }

        int firstSlot = GetCurrentSpeciesSlot();

        if (SlimeMapRenderer.Instance != null) {
            SlimeMapRenderer.Instance.SelectedPlayerIndex = firstSlot;

            var settings = SlimeMapRenderer.Instance.speciesSettings[firstSlot];
            maxAgeSlider?.SetValueWithoutNotify(settings.maxAge);
            if (maxAgeLabel != null) maxAgeLabel.text = settings.maxAge.ToString("F0", System.Globalization.CultureInfo.InvariantCulture);

            trailWeightSlider?.SetValueWithoutNotify(settings.trailWeight);
            if (trailWeightLabel != null) trailWeightLabel.text = settings.trailWeight.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);

            decayRateSlider?.SetValueWithoutNotify(settings.decayRate);
            if (decayRateLabel != null) decayRateLabel.text = settings.decayRate.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);

            diffuseRateSlider?.SetValueWithoutNotify(settings.diffuseRate);
            if (diffuseRateLabel != null) diffuseRateLabel.text = settings.diffuseRate.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);

            moveSpeedSlider?.SetValueWithoutNotify(settings.moveSpeed);
            if (moveSpeedLabel != null) moveSpeedLabel.text = settings.moveSpeed.ToString("F0", System.Globalization.CultureInfo.InvariantCulture);

            turnSpeedSlider?.SetValueWithoutNotify(settings.turnSpeed);
            if (turnSpeedLabel != null) turnSpeedLabel.text = settings.turnSpeed.ToString("F0", System.Globalization.CultureInfo.InvariantCulture);

            sensorAngleSlider?.SetValueWithoutNotify(settings.sensorAngleRad * Mathf.Rad2Deg);
            if (sensorAngleLabel != null) sensorAngleLabel.text = (settings.sensorAngleRad * Mathf.Rad2Deg).ToString("F0", System.Globalization.CultureInfo.InvariantCulture) + "°";

            sensorOffsetSlider?.SetValueWithoutNotify(settings.sensorOffsetDst);
            if (sensorOffsetLabel != null) sensorOffsetLabel.text = settings.sensorOffsetDst.ToString("F0", System.Globalization.CultureInfo.InvariantCulture);

            sensorSizeSlider?.SetValueWithoutNotify(settings.sensorSize);
            if (sensorSizeLabel != null) sensorSizeLabel.text = settings.sensorSize.ToString();

            if (toggleVisibility != null) {
                toggleVisibility.SetValueWithoutNotify(SlimeMapRenderer.Instance.GetPlayerVisibility(firstSlot));
            }

            if (toggleSpeciesOverlay != null && WaypointOverlayRenderer.Instance != null) {
                toggleSpeciesOverlay.SetValueWithoutNotify(WaypointOverlayRenderer.Instance.ShowSpeciesOverlay[firstSlot]);
            }
        }

        // Highlight selected button
        for (int i = 0; i < 6; i++) {
            if (playerSelectButtons[i] == null) continue;
            playerSelectButtons[i].style.borderBottomWidth = (i == index) ? 3 : 0;
            playerSelectButtons[i].style.borderBottomColor = Color.white;
            playerSelectButtons[i].style.opacity = (i == index) ? 1.0f : 0.5f;
        }

        if (speciesCountLabel != null && lib != null && !string.IsNullOrEmpty(selectedPlayerId))
        {
            int count = lib.GetSpeciesForPlayer(selectedPlayerId).Count;
            string pName = lib.GetPlayer(selectedPlayerId)?.displayName ?? selectedPlayerId;
            speciesCountLabel.text = $"{pName} — {count} espèce{(count > 1 ? "s" : "")}";
        }

        RefreshWarButtons();
        RefreshTypeButtons();
    }

    private void OnSelectSpecies(string speciesId)
    {
        var lib = PlayerLibrary.Instance;
        if (lib == null) return;
        var playerSpecies = lib.GetSpeciesForPlayer(selectedPlayerId);
        if (!playerSpecies.Contains(speciesId)) return;
        selectedSpeciesId = speciesId;

        int slot = GetCurrentSpeciesSlot();
        if (toggleVisibility != null && SlimeMapRenderer.Instance != null)
            toggleVisibility.SetValueWithoutNotify(SlimeMapRenderer.Instance.GetPlayerVisibility(slot));
        if (toggleSpeciesOverlay != null && WaypointOverlayRenderer.Instance != null)
            toggleSpeciesOverlay.SetValueWithoutNotify(WaypointOverlayRenderer.Instance.ShowSpeciesOverlay[slot]);

        RefreshTypeButtons();
    }

    private void RefreshTypeButtons()
    {
        var lib = PlayerLibrary.Instance;
        var playerSpecies = lib != null
            ? lib.GetSpeciesForPlayer(selectedPlayerId)
            : new System.Collections.Generic.List<string>();

        for (int i = 0; i < 10; i++) {
            if (typeButtons[i] == null) continue;
            string typeId = ((SpeciesType)i).ToString().ToLowerInvariant();
            bool inPlayer = playerSpecies.Contains(typeId);
            typeButtons[i].style.display            = inPlayer ? UnityEngine.UIElements.DisplayStyle.Flex
                                                               : UnityEngine.UIElements.DisplayStyle.None;
            typeButtons[i].style.opacity            = (typeId == selectedSpeciesId) ? 1.0f : 0.6f;
            typeButtons[i].style.borderBottomWidth  = (typeId == selectedSpeciesId) ? 3 : 0;
            typeButtons[i].style.borderBottomColor  = Color.white;
        }
    }

    private void OnToggleWar(int enemyPlayerIndex)
    {
        var lib = PlayerLibrary.Instance;
        var smr = SlimeMapRenderer.Instance;
        if (lib == null || smr == null || enemyPlayerIndex == selectedPlayerIndex) return;
        var players = lib.GetAll();
        if (enemyPlayerIndex < 0 || enemyPlayerIndex >= players.Count) return;

        string enemyId      = players[enemyPlayerIndex].id;
        var    mySpecies    = lib.GetSpeciesForPlayer(selectedPlayerId);
        var    enemySpecies = lib.GetSpeciesForPlayer(enemyId);
        if (mySpecies.Count == 0 || enemySpecies.Count == 0) return;

        // Toggle based on current state of first slot pair
        int myFirst    = lib.GetSlotIndex(selectedPlayerId, mySpecies[0]);
        int enemyFirst = lib.GetSlotIndex(enemyId, enemySpecies[0]);
        bool newWar = !smr.IsAtWar(myFirst, enemyFirst);

        // Apply to all (my slot × enemy slot) pairs
        foreach (string ms in mySpecies)
        {
            int mSlot = lib.GetSlotIndex(selectedPlayerId, ms);
            if (mSlot < 0) continue;
            foreach (string es in enemySpecies)
            {
                int eSlot = lib.GetSlotIndex(enemyId, es);
                if (eSlot < 0) continue;
                smr.SetWar(mSlot, eSlot, newWar);
            }
        }
        RefreshWarButtons();
    }

    private void RefreshWarButtons()
    {
        var lib = PlayerLibrary.Instance;
        var smr = SlimeMapRenderer.Instance;
        if (lib == null || smr == null) return;
        var players = lib.GetAll();

        for (int i = 0; i < 6; i++)
        {
            if (warButtons[i] == null) continue;
            if (i >= players.Count) { warButtons[i].SetEnabled(false); warButtons[i].style.opacity = 0f; continue; }

            if (i == selectedPlayerIndex) {
                warButtons[i].SetEnabled(false);
                warButtons[i].style.opacity = 0.2f;
                warButtons[i].text = players[i].displayName;
                continue;
            }

            string enemyId      = players[i].id;
            var    mySpecies    = lib.GetSpeciesForPlayer(selectedPlayerId);
            var    enemySpecies = lib.GetSpeciesForPlayer(enemyId);

            bool atWar = false;
            if (mySpecies.Count > 0 && enemySpecies.Count > 0)
            {
                int ms = lib.GetSlotIndex(selectedPlayerId, mySpecies[0]);
                int es = lib.GetSlotIndex(enemyId, enemySpecies[0]);
                atWar = smr.IsAtWar(ms, es);
            }

            warButtons[i].SetEnabled(true);
            warButtons[i].text = players[i].displayName;
            warButtons[i].style.opacity = atWar ? 1.0f : 0.4f;
            warButtons[i].style.borderBottomWidth = atWar ? 3 : 0;
            warButtons[i].style.borderBottomColor = Color.red;
        }
    }

    // ── Construction panel ─────────────────────────────────────────

    private bool placementEventsSubscribed = false;

    private void EnsurePlacementEventsSubscribed()
    {
        if (placementEventsSubscribed) return;
        var bpc = BuildingPlacementController.Instance;
        if (bpc == null) return;
        bpc.OnPlacementCancelled += OnPlacementEnded;
        bpc.OnPlacementConfirmed += _ => OnPlacementEnded();
        placementEventsSubscribed = true;
    }

    private void ToggleConstructionPanel()
    {
        EnsurePlacementEventsSubscribed();

        if (constructionPanelOpen)
        {
            constructionPanel?.RemoveFromHierarchy();
            constructionPanel     = null;
            constructionPanelOpen = false;
            return;
        }

        var root = uiDocument.rootVisualElement;

        // Catalogue data-driven : tous les bâtiments du joueur actif, regroupés par espèce
        var lib = PlayerLibrary.Instance;
        // Liste de (buildingDef, speciesId) dans l'ordre des espèces du joueur
        var buildingEntries = new System.Collections.Generic.List<(BuildingDefinition def, string sid)>();
        if (lib != null && !string.IsNullOrEmpty(selectedPlayerId))
        {
            foreach (string sid in lib.GetSpeciesForPlayer(selectedPlayerId))
            {
                foreach (var b in BuildingPlacementController.GetBuildings(sid))
                    if (!buildingEntries.Exists(e => e.def.id == b.id))
                        buildingEntries.Add((b, sid));
            }
        }
        // Fallback : bâtiments de l'espèce sélectionnée uniquement
        if (buildingEntries.Count == 0)
            foreach (var b in BuildingPlacementController.GetBuildings(selectedSpeciesId))
                buildingEntries.Add((b, selectedSpeciesId));
        if (buildingEntries.Count == 0)
        {
            // No buildings available for this species type — show brief message
            constructionPanel = new VisualElement();
            constructionPanel.style.position         = Position.Absolute;
            constructionPanel.style.top              = 120;
            constructionPanel.style.left             = 295;
            constructionPanel.style.backgroundColor  = new StyleColor(new Color(0.05f, 0.05f, 0.08f, 0.95f));
            constructionPanel.style.paddingTop        = 10;
            constructionPanel.style.paddingBottom     = 10;
            constructionPanel.style.paddingLeft       = 14;
            constructionPanel.style.paddingRight      = 14;
            constructionPanel.style.borderTopLeftRadius     = new StyleLength(8);
            constructionPanel.style.borderTopRightRadius    = new StyleLength(8);
            constructionPanel.style.borderBottomLeftRadius  = new StyleLength(8);
            constructionPanel.style.borderBottomRightRadius = new StyleLength(8);
            var msg = new Label("Aucun bâtiment disponible\npour ce joueur.");
            msg.style.color     = new StyleColor(new Color(0.7f, 0.7f, 0.7f));
            msg.style.fontSize  = 12;
            constructionPanel.Add(msg);
            root.Add(constructionPanel);
            constructionPanelOpen = true;
            return;
        }

        constructionPanel = new VisualElement();
        constructionPanel.style.position         = Position.Absolute;
        constructionPanel.style.top              = 120;
        constructionPanel.style.left             = 295;
        constructionPanel.style.minWidth         = 180;
        constructionPanel.style.backgroundColor  = new StyleColor(new Color(0.05f, 0.08f, 0.05f, 0.95f));
        constructionPanel.style.paddingTop        = 10;
        constructionPanel.style.paddingBottom     = 10;
        constructionPanel.style.paddingLeft       = 12;
        constructionPanel.style.paddingRight      = 12;
        constructionPanel.style.borderTopLeftRadius     = new StyleLength(8);
        constructionPanel.style.borderTopRightRadius    = new StyleLength(8);
        constructionPanel.style.borderBottomLeftRadius  = new StyleLength(8);
        constructionPanel.style.borderBottomRightRadius = new StyleLength(8);

        string playerDisplayName = lib?.GetPlayer(selectedPlayerId)?.displayName ?? $"P{selectedPlayerIndex + 1}";
        var title = new Label($"Bâtiments — {playerDisplayName}");
        title.style.color         = new StyleColor(new Color(0.55f, 0.78f, 1f));
        title.style.fontSize      = 13;
        title.style.unityFontStyleAndWeight = UnityEngine.FontStyle.Bold;
        title.style.marginBottom  = 6;
        constructionPanel.Add(title);

        string lastSid = null;
        string capturedPlayerName = lib?.GetPlayer(selectedPlayerId)?.displayName ?? $"P{selectedPlayerIndex + 1}";
        foreach (var (def, sid) in buildingEntries)
        {
            // Ajout d'un en-tête d'espèce quand l'espèce change
            if (sid != lastSid)
            {
                lastSid = sid;
                var specDef = SpeciesLibrary.Instance?.Get(sid);
                string specName = specDef?.displayName ?? sid;
                int specSlot = PlayerLibrary.Instance?.GetSlotIndex(selectedPlayerId, sid) ?? -1;
                Color headerColor = Color.white;
                if (specSlot >= 0 && SlimeMapRenderer.Instance != null && specSlot < SlimeMapRenderer.Instance.slotColors.Length)
                {
                    var v = SlimeMapRenderer.Instance.slotColors[specSlot];
                    headerColor = new Color(v.x, v.y, v.z);
                }
                var header = new Label(specName);
                header.style.color     = new StyleColor(headerColor);
                header.style.fontSize  = 11;
                header.style.unityFontStyleAndWeight = UnityEngine.FontStyle.Bold;
                header.style.marginTop    = 6;
                header.style.marginBottom = 2;
                constructionPanel.Add(header);
            }

            // Quand listé via linkedSpeciesId (ex: Rate sous GlobuleRouge), montrer comme destination 🟠
            bool isLinkedEntry = !string.IsNullOrEmpty(def.linkedSpeciesId) &&
                                 def.linkedSpeciesId.ToLowerInvariant() == sid.ToLowerInvariant();
            int displayType = isLinkedEntry ? 1 : def.waypointType;

            var btn  = new Button();
            btn.text = displayType == 0
                ? $"🟢  {def.displayName}"
                : $"🟠  {def.displayName}";
            btn.style.fontSize       = 13;
            btn.style.paddingTop     = 6;
            btn.style.paddingBottom  = 6;
            btn.style.paddingLeft    = 10;
            btn.style.paddingRight   = 10;
            btn.style.marginBottom   = 4;
            btn.style.borderTopLeftRadius     = new StyleLength(4);
            btn.style.borderTopRightRadius    = new StyleLength(4);
            btn.style.borderBottomLeftRadius  = new StyleLength(4);
            btn.style.borderBottomRightRadius = new StyleLength(4);
            var bg = displayType == 0
                ? new Color(0.15f, 0.4f, 0.15f)
                : new Color(0.45f, 0.25f, 0.05f);
            btn.style.backgroundColor = new StyleColor(bg);
            btn.style.color           = new StyleColor(Color.white);

            var capturedDef = def;
            // Slot GPU : priorité à waypointSpeciesId (espèce que le bâtiment CRÉE),
            // sinon sid (espèce sous laquelle le bâtiment est listé, ex: linkedSpeciesId).
            string spawnSid = !string.IsNullOrEmpty(capturedDef.waypointSpeciesId)
                ? capturedDef.waypointSpeciesId
                : sid;
            int capturedSpecies = PlayerLibrary.Instance != null
                ? PlayerLibrary.Instance.GetSlotIndex(selectedPlayerId, spawnSid)
                : 0;
            if (capturedSpecies < 0) capturedSpecies = GetCurrentSpeciesSlot();

            btn.clicked += () =>
            {
                BuildingPlacementController.Instance?.StartPlacement(
                    capturedDef.waypointType, capturedSpecies, capturedDef.id);

                // Close panel, show banner, dim left panel
                constructionPanel?.RemoveFromHierarchy();
                constructionPanel     = null;
                constructionPanelOpen = false;

                if (placementBannerText != null)
                    placementBannerText.text =
                        $"Placer : {capturedDef.displayName}  ({capturedPlayerName})  |  Clic gauche pour confirmer";

                if (placementBanner != null) placementBanner.style.display = DisplayStyle.Flex;

                // Masquer le panneau gauche et passer en overlay pour voir la carte terrain
                var cp = uiDocument.rootVisualElement.Q<VisualElement>("ControlPanel");
                if (cp != null) cp.style.display = DisplayStyle.None;
                wasInOverlayBeforePlacement = overlayMode;
                if (!overlayMode) OnToggleOverlay();
            };
            constructionPanel.Add(btn);
        }

        root.Add(constructionPanel);
        constructionPanelOpen = true;
    }

    private void OnPlacementEnded()
    {
        if (placementBanner != null) placementBanner.style.display = DisplayStyle.None;
        var panel = uiDocument?.rootVisualElement?.Q<VisualElement>("ControlPanel");
        if (panel != null) { panel.style.display = DisplayStyle.Flex; panel.style.opacity = 1f; }
        // Restaurer l'état overlay d'avant le placement
        if (!wasInOverlayBeforePlacement && overlayMode) OnToggleOverlay();
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
        SlimeMapRenderer.Instance?.AddAgents(10000, GetCurrentSpeciesSlot());
    }

    // ── Game speed / pause ────────────────────────────────────────
    private void SetGameTimeScale(float scale)
    {
        if (entityManager == default || World.DefaultGameObjectInjectionWorld == null) return;
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

        // GPU agent count par joueur
        if (SlimeMapRenderer.Instance != null)
        {
            uint totalAlive = 0;
            var lib = PlayerLibrary.Instance;
            var players = lib?.GetAll();
            int playerCount = players?.Count ?? 0;

            for (int i = 0; i < 6; i++)
            {
                if (playerSelectButtons[i] == null) continue;
                if (lib == null || i >= playerCount)
                {
                    playerSelectButtons[i].style.display = UnityEngine.UIElements.DisplayStyle.None;
                    continue;
                }
                playerSelectButtons[i].style.display = UnityEngine.UIElements.DisplayStyle.Flex;

                // Somme des slots de ce joueur
                uint alive = 0;
                var species = lib.GetSpeciesForPlayer(players[i].id);
                foreach (string s in species)
                {
                    int slot = lib.GetSlotIndex(players[i].id, s);
                    if (slot >= 0 && slot < 16)
                        alive += SlimeMapRenderer.Instance.AliveSpeciesCounts[slot];
                }
                totalAlive += alive;
                playerSelectButtons[i].text = $"{players[i].displayName}\n<size=9>{alive:N0}</size>";
            }
            if (entityCountLabel != null)
                entityCountLabel.text = $"Agents (Vivants) : {totalAlive:N0}";

            // Compteur par espèce dans les boutons de type
            for (int i = 0; i < 10; i++)
            {
                if (typeButtons[i] == null) continue;
                string speciesId = ((SpeciesType)i).ToString().ToLowerInvariant();
                int slot = lib != null ? lib.GetSlotIndex(selectedPlayerId, speciesId) : -1;
                if (slot >= 0 && slot < 16)
                {
                    uint count = SlimeMapRenderer.Instance.AliveSpeciesCounts[slot];
                    typeButtons[i].text = $"{typeButtonBaseTexts[i]}\n<size=9>{count:N0}</size>";
                }
                else
                {
                    typeButtons[i].text = typeButtonBaseTexts[i];
                }
            }
        }

        if (energyLabel != null) energyLabel.text = "Énergie : —";
    }
}
