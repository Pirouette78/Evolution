using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.InputSystem;
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
    private Button        btnDiplomatie;
    private VisualElement diploMatrixPanel;
    private bool          diploMatrixOpen = false;
    private int           diploColPlayer  = 0;
    private int           diploRowPlayer  = 0;
    private Button[,]     diploMatrixCells;
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

    // ── Building stats popup ────────────────────────────────────────
    private VisualElement buildingStatsPanel;
    private int           statsShownPlacementId = -1;

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
                if (WaypointOverlayRenderer.Instance != null)
                    WaypointOverlayRenderer.Instance.ShowPOI = e.newValue;
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

        // Diplomatic matrix panel
        btnDiplomatie = root.Q<Button>("BtnDiplomatie");
        if (btnDiplomatie != null) btnDiplomatie.clicked += ToggleDiploMatrixPanel;

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
        var lib = PlayerLibrary.Instance;
        if (lib != null && lib.GetAll().Count > 0)
        {
            selectedPlayerId = lib.GetAll()[0].id;
            var species = lib.GetSpeciesForPlayer(selectedPlayerId);
            if (species.Count > 0) selectedSpeciesId = species[0];

            // Couleurs des boutons de sélection joueur depuis le JSON
            var players = lib.GetAll();
            for (int i = 0; i < playerSelectButtons.Length && i < players.Count; i++)
            {
                if (playerSelectButtons[i] == null) continue;
                var c = players[i].color;
                if (c != null && c.Length >= 3)
                    playerSelectButtons[i].style.backgroundColor = new StyleColor(new Color(c[0], c[1], c[2]));
            }
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
                toggleSpeciesOverlay.SetValueWithoutNotify(WaypointOverlayRenderer.Instance.ShowPOI);
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

        if (diploMatrixOpen) RefreshDiploMatrixCells();
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
        var smr = SlimeMapRenderer.Instance;
        if (smr != null)
        {
            var s = smr.speciesSettings[slot];
            maxAgeSlider?.SetValueWithoutNotify(s.maxAge);
            if (maxAgeLabel != null) maxAgeLabel.text = s.maxAge.ToString("F0", System.Globalization.CultureInfo.InvariantCulture);
            trailWeightSlider?.SetValueWithoutNotify(s.trailWeight);
            if (trailWeightLabel != null) trailWeightLabel.text = s.trailWeight.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
            decayRateSlider?.SetValueWithoutNotify(s.decayRate);
            if (decayRateLabel != null) decayRateLabel.text = s.decayRate.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
            diffuseRateSlider?.SetValueWithoutNotify(s.diffuseRate);
            if (diffuseRateLabel != null) diffuseRateLabel.text = s.diffuseRate.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
            moveSpeedSlider?.SetValueWithoutNotify(s.moveSpeed);
            if (moveSpeedLabel != null) moveSpeedLabel.text = s.moveSpeed.ToString("F0", System.Globalization.CultureInfo.InvariantCulture);
            turnSpeedSlider?.SetValueWithoutNotify(s.turnSpeed);
            if (turnSpeedLabel != null) turnSpeedLabel.text = s.turnSpeed.ToString("F0", System.Globalization.CultureInfo.InvariantCulture);
            sensorAngleSlider?.SetValueWithoutNotify(s.sensorAngleRad * Mathf.Rad2Deg);
            if (sensorAngleLabel != null) sensorAngleLabel.text = (s.sensorAngleRad * Mathf.Rad2Deg).ToString("F0", System.Globalization.CultureInfo.InvariantCulture) + "°";
            sensorOffsetSlider?.SetValueWithoutNotify(s.sensorOffsetDst);
            if (sensorOffsetLabel != null) sensorOffsetLabel.text = s.sensorOffsetDst.ToString("F0", System.Globalization.CultureInfo.InvariantCulture);
            sensorSizeSlider?.SetValueWithoutNotify(s.sensorSize);
            if (sensorSizeLabel != null) sensorSizeLabel.text = s.sensorSize.ToString();
            toggleVisibility?.SetValueWithoutNotify(smr.GetPlayerVisibility(slot));
        }
        if (toggleSpeciesOverlay != null && WaypointOverlayRenderer.Instance != null)
            toggleSpeciesOverlay.SetValueWithoutNotify(WaypointOverlayRenderer.Instance.ShowPOI);

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

    // ── Diplomatic matrix panel ────────────────────────────────────

    private void ToggleDiploMatrixPanel()
    {
        if (diploMatrixOpen) { CloseDiploMatrixPanel(); return; }
        var lib = PlayerLibrary.Instance;
        if (lib == null) return;
        int count = lib.GetAll().Count;
        diploColPlayer = Mathf.Clamp(diploColPlayer, 0, count - 1);
        diploRowPlayer = Mathf.Clamp(diploRowPlayer, 0, count - 1);
        BuildDiploMatrixPanel();
    }

    private void CloseDiploMatrixPanel()
    {
        diploMatrixPanel?.RemoveFromHierarchy();
        diploMatrixPanel = null;
        diploMatrixOpen  = false;
    }

    private void BuildDiploMatrixPanel()
    {
        var lib = PlayerLibrary.Instance;
        var smr = SlimeMapRenderer.Instance;
        if (lib == null || smr == null) return;

        var players   = lib.GetAll();
        int numPlayers = players.Count;
        if (numPlayers == 0) return;

        var colSpecies = lib.GetSpeciesForPlayer(players[diploColPlayer].id);
        var rowSpecies = lib.GetSpeciesForPlayer(players[diploRowPlayer].id);
        int nCols      = colSpecies.Count;
        int nRows      = rowSpecies.Count;

        const int CELL = 34;
        const int PAD  = 10;

        // ── Panel container ────────────────────────────────────────
        diploMatrixPanel = new VisualElement();
        diploMatrixPanel.style.position          = Position.Absolute;
        diploMatrixPanel.style.top               = 120;
        diploMatrixPanel.style.left              = 295;
        diploMatrixPanel.style.backgroundColor   = new StyleColor(new Color(0.05f, 0.05f, 0.08f, 0.95f));
        diploMatrixPanel.style.paddingTop        = PAD;
        diploMatrixPanel.style.paddingBottom     = PAD;
        diploMatrixPanel.style.paddingLeft       = PAD;
        diploMatrixPanel.style.paddingRight      = PAD;
        diploMatrixPanel.style.borderTopLeftRadius     = new StyleLength(8);
        diploMatrixPanel.style.borderTopRightRadius    = new StyleLength(8);
        diploMatrixPanel.style.borderBottomLeftRadius  = new StyleLength(8);
        diploMatrixPanel.style.borderBottomRightRadius = new StyleLength(8);

        // ── Titre ─────────────────────────────────────────────────
        var titleRow = new VisualElement();
        titleRow.style.flexDirection  = FlexDirection.Row;
        titleRow.style.justifyContent = Justify.SpaceBetween;
        titleRow.style.alignItems     = Align.Center;
        titleRow.style.marginBottom   = 8;

        var titleLbl = new Label("Matrice Diplomatique");
        titleLbl.style.color               = new StyleColor(new Color(0.9f, 0.8f, 1f));
        titleLbl.style.fontSize            = 13;
        titleLbl.style.unityFontStyleAndWeight = FontStyle.Bold;

        var closeBtn = new Button(CloseDiploMatrixPanel) { text = "✕" };
        closeBtn.style.fontSize         = 12;
        closeBtn.style.paddingLeft      = 6;
        closeBtn.style.paddingRight     = 6;
        closeBtn.style.paddingTop       = 2;
        closeBtn.style.paddingBottom    = 2;
        closeBtn.style.backgroundColor  = new StyleColor(new Color(0.4f, 0.1f, 0.1f));
        closeBtn.style.color            = new StyleColor(Color.white);
        closeBtn.style.borderTopLeftRadius     = new StyleLength(4);
        closeBtn.style.borderTopRightRadius    = new StyleLength(4);
        closeBtn.style.borderBottomLeftRadius  = new StyleLength(4);
        closeBtn.style.borderBottomRightRadius = new StyleLength(4);

        titleRow.Add(titleLbl);
        titleRow.Add(closeBtn);
        diploMatrixPanel.Add(titleRow);

        // ── Sélecteur colonnes ─────────────────────────────────────
        diploMatrixPanel.Add(MakePlayerSelectorRow("Colonnes :", numPlayers, players, diploColPlayer, i => {
            diploColPlayer = i; CloseDiploMatrixPanel(); BuildDiploMatrixPanel();
        }));

        // ── Sélecteur lignes ──────────────────────────────────────
        diploMatrixPanel.Add(MakePlayerSelectorRow("Lignes :", numPlayers, players, diploRowPlayer, i => {
            diploRowPlayer = i; CloseDiploMatrixPanel(); BuildDiploMatrixPanel();
        }));

        // ── Légende ───────────────────────────────────────────────
        var legend = new VisualElement();
        legend.style.flexDirection = FlexDirection.Row;
        legend.style.marginBottom  = 8;
        legend.style.alignItems    = Align.Center;
        AddLegendDot(legend, new Color(0.08f, 0.6f, 0.15f), "Allié");
        AddLegendDot(legend, new Color(0.38f, 0.38f, 0.38f), "Paix");
        AddLegendDot(legend, new Color(0.7f, 0.1f, 0.1f),  "Guerre");
        diploMatrixPanel.Add(legend);

        // ── Grille ────────────────────────────────────────────────
        var grid = new VisualElement();
        grid.style.flexDirection = FlexDirection.Column;

        // En-tête colonnes
        var headerRow = new VisualElement();
        headerRow.style.flexDirection = FlexDirection.Row;
        headerRow.Add(MakeCornerCell(CELL)); // coin vide
        for (int c = 0; c < nCols; c++)
        {
            int colSlot = lib.GetSlotIndex(players[diploColPlayer].id, colSpecies[c]);
            headerRow.Add(MakeSpeciesHeader(colSpecies[c], colSlot, smr, CELL));
        }
        grid.Add(headerRow);

        // Rangées données
        diploMatrixCells = new Button[nRows, nCols];
        for (int r = 0; r < nRows; r++)
        {
            int rowSlot = lib.GetSlotIndex(players[diploRowPlayer].id, rowSpecies[r]);
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.Add(MakeSpeciesHeader(rowSpecies[r], rowSlot, smr, CELL));
            for (int c = 0; c < nCols; c++)
            {
                int colSlot   = lib.GetSlotIndex(players[diploColPlayer].id, colSpecies[c]);
                int capturedR = rowSlot;
                int capturedC = colSlot;

                var cell = new Button();
                cell.style.width  = CELL;
                cell.style.height = CELL;
                cell.style.marginTop    = 1;
                cell.style.marginBottom = 1;
                cell.style.marginLeft   = 1;
                cell.style.marginRight  = 1;
                cell.style.borderTopLeftRadius     = new StyleLength(3);
                cell.style.borderTopRightRadius    = new StyleLength(3);
                cell.style.borderBottomLeftRadius  = new StyleLength(3);
                cell.style.borderBottomRightRadius = new StyleLength(3);

                bool isDiag = (capturedR >= 0 && capturedR == capturedC);
                if (isDiag)
                {
                    cell.style.backgroundColor = new StyleColor(new Color(0.1f, 0.1f, 0.1f));
                    cell.SetEnabled(false);
                }
                else
                {
                    var state = (capturedR >= 0 && capturedC >= 0)
                        ? smr.GetDiplomaticState(capturedR, capturedC)
                        : SlimeMapRenderer.DiplomaticState.Neutral;
                    cell.style.backgroundColor = new StyleColor(DiploStateToColor(state));
                    if (capturedR >= 0 && capturedC >= 0)
                    {
                        cell.clicked += () =>
                        {
                            var cur  = smr.GetDiplomaticState(capturedR, capturedC);
                            var next = CycleDiploState(cur);
                            smr.SetInteractionOneWay(capturedR, capturedC, next);
                            RefreshDiploMatrixCells();
                        };
                    }
                }

                diploMatrixCells[r, c] = cell;
                row.Add(cell);
            }
            grid.Add(row);
        }

        diploMatrixPanel.Add(grid);
        uiDocument.rootVisualElement.Add(diploMatrixPanel);
        diploMatrixOpen = true;
    }

    private VisualElement MakePlayerSelectorRow(string label, int numPlayers,
        System.Collections.Generic.IReadOnlyList<PlayerDefinition> players,
        int activeIndex, System.Action<int> onSelect)
    {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems    = Align.Center;
        row.style.marginBottom  = 4;

        var lbl = new Label(label);
        lbl.style.color     = new StyleColor(new Color(0.7f, 0.7f, 0.7f));
        lbl.style.fontSize  = 11;
        lbl.style.minWidth  = 60;
        row.Add(lbl);

        for (int i = 0; i < numPlayers; i++)
        {
            int captured = i;
            var pc_arr = players[i].color;
            Color pc = (pc_arr != null && pc_arr.Length >= 3)
                ? new Color(pc_arr[0], pc_arr[1], pc_arr[2])
                : new Color(0.25f, 0.25f, 0.35f);
            var btn = new Button(() => onSelect(captured)) { text = $"Joueur {i + 1}" };
            btn.style.fontSize        = 10;
            btn.style.paddingTop      = 2;
            btn.style.paddingBottom   = 2;
            btn.style.paddingLeft     = 5;
            btn.style.paddingRight    = 5;
            btn.style.marginRight     = 3;
            btn.style.color           = new StyleColor(Color.white);
            btn.style.backgroundColor = new StyleColor(pc);
            btn.style.borderTopLeftRadius     = new StyleLength(4);
            btn.style.borderTopRightRadius    = new StyleLength(4);
            btn.style.borderBottomLeftRadius  = new StyleLength(4);
            btn.style.borderBottomRightRadius = new StyleLength(4);
            btn.style.opacity         = (i == activeIndex) ? 1f : 0.45f;
            btn.style.borderBottomWidth = (i == activeIndex) ? 2 : 0;
            btn.style.borderBottomColor = new StyleColor(Color.white);
            row.Add(btn);
        }
        return row;
    }

    private static VisualElement MakeCornerCell(int size)
    {
        var ve = new VisualElement();
        ve.style.width  = size;
        ve.style.height = size;
        return ve;
    }

    private static VisualElement MakeSpeciesHeader(string specId, int slot,
        SlimeMapRenderer smr, int size)
    {
        var ve = new VisualElement();
        ve.style.width  = size;
        ve.style.height = size;
        ve.style.marginTop    = 1;
        ve.style.marginBottom = 1;
        ve.style.marginLeft   = 1;
        ve.style.marginRight  = 1;
        ve.style.alignItems     = Align.Center;
        ve.style.justifyContent = Justify.Center;
        ve.style.borderTopLeftRadius     = new StyleLength(3);
        ve.style.borderTopRightRadius    = new StyleLength(3);
        ve.style.borderBottomLeftRadius  = new StyleLength(3);
        ve.style.borderBottomRightRadius = new StyleLength(3);

        Color bg = new Color(0.15f, 0.15f, 0.2f);
        var specDef = SpeciesLibrary.Instance?.Get(specId);
        if (specDef?.color != null && specDef.color.Length >= 3)
            bg = new Color(specDef.color[0], specDef.color[1], specDef.color[2]);
        else if (slot >= 0 && smr.slotColors != null && slot < smr.slotColors.Length)
        {
            var v = smr.slotColors[slot];
            bg = new Color(v.x, v.y, v.z);
        }
        ve.style.backgroundColor = new StyleColor(bg);

        // Abréviation 2 chars
        string abbrev = specId.Length >= 2 ? specId.Substring(0, 2).ToUpper() : specId.ToUpper();
        var lbl = new Label(abbrev);
        lbl.style.fontSize = 9;
        lbl.style.color    = new StyleColor(Color.white);
        lbl.style.unityFontStyleAndWeight = FontStyle.Bold;
        lbl.style.unityTextAlign          = TextAnchor.MiddleCenter;
        ve.Add(lbl);
        return ve;
    }

    private static void AddLegendDot(VisualElement parent, Color color, string text)
    {
        var dot = new VisualElement();
        dot.style.width  = 12;
        dot.style.height = 12;
        dot.style.borderTopLeftRadius     = new StyleLength(6);
        dot.style.borderTopRightRadius    = new StyleLength(6);
        dot.style.borderBottomLeftRadius  = new StyleLength(6);
        dot.style.borderBottomRightRadius = new StyleLength(6);
        dot.style.backgroundColor = new StyleColor(color);
        dot.style.marginRight     = 4;

        var lbl = new Label(text);
        lbl.style.color       = new StyleColor(new Color(0.75f, 0.75f, 0.75f));
        lbl.style.fontSize    = 10;
        lbl.style.marginRight = 10;

        parent.Add(dot);
        parent.Add(lbl);
    }

    private void RefreshDiploMatrixCells()
    {
        var lib = PlayerLibrary.Instance;
        var smr = SlimeMapRenderer.Instance;
        if (lib == null || smr == null || diploMatrixCells == null) return;

        var players    = lib.GetAll();
        var colSpecies = lib.GetSpeciesForPlayer(players[diploColPlayer].id);
        var rowSpecies = lib.GetSpeciesForPlayer(players[diploRowPlayer].id);

        int nRows = diploMatrixCells.GetLength(0);
        int nCols = diploMatrixCells.GetLength(1);

        for (int r = 0; r < nRows && r < rowSpecies.Count; r++)
        {
            int rowSlot = lib.GetSlotIndex(players[diploRowPlayer].id, rowSpecies[r]);
            for (int c = 0; c < nCols && c < colSpecies.Count; c++)
            {
                int colSlot = lib.GetSlotIndex(players[diploColPlayer].id, colSpecies[c]);
                var cell    = diploMatrixCells[r, c];
                if (cell == null || rowSlot < 0 || colSlot < 0) continue;
                if (rowSlot == colSlot) continue; // diagonal — static
                var state = smr.GetDiplomaticState(rowSlot, colSlot);
                cell.style.backgroundColor = new StyleColor(DiploStateToColor(state));
            }
        }
    }

    private static Color DiploStateToColor(SlimeMapRenderer.DiplomaticState state)
    {
        switch (state)
        {
            case SlimeMapRenderer.DiplomaticState.Ally:  return new Color(0.08f, 0.60f, 0.15f);
            case SlimeMapRenderer.DiplomaticState.Peace: return new Color(0.38f, 0.38f, 0.38f);
            case SlimeMapRenderer.DiplomaticState.War:   return new Color(0.70f, 0.10f, 0.10f);
            default:                                     return new Color(0.15f, 0.22f, 0.32f);
        }
    }

    private static SlimeMapRenderer.DiplomaticState CycleDiploState(SlimeMapRenderer.DiplomaticState s)
    {
        switch (s)
        {
            case SlimeMapRenderer.DiplomaticState.Ally:  return SlimeMapRenderer.DiplomaticState.Peace;
            case SlimeMapRenderer.DiplomaticState.Peace: return SlimeMapRenderer.DiplomaticState.War;
            default:                                     return SlimeMapRenderer.DiplomaticState.Ally;
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
            // Slot GPU : premier output si disponible, sinon espèce sous laquelle le bâtiment est listé.
            string spawnSid = (capturedDef.outputs != null && capturedDef.outputs.Length > 0)
                ? capturedDef.outputs[0].speciesId
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
    // Convertit une position écran en coordonnées pixel de la carte (même logique que BuildingPlacementController)
    private Vector2? ScreenToMapPixel(Vector2 screenPos)
    {
        var smr = SlimeMapRenderer.Instance;
        if (smr == null || Camera.main == null) return null;
        float depth = -Camera.main.transform.position.z;
        Vector3 world = Camera.main.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, depth));
        int px = (int)world.x;
        int py = (int)world.y;
        if (px < 0 || px >= smr.Width || py < 0 || py >= smr.Height) return null;
        return new Vector2(px, py);
    }

    private void Update()
    {
        // Détection clic gauche sur la carte (hors mode placement)
        var mouse = Mouse.current;
        if (mouse != null && mouse.leftButton.wasPressedThisFrame)
        {
            bool placing = BuildingPlacementController.Instance != null &&
                           BuildingPlacementController.Instance.IsPlacing;
            if (!placing)
            {
                Vector2? px = ScreenToMapPixel(mouse.position.ReadValue());
                if (px.HasValue)
                    OnMapClick(px.Value);
                else
                    CloseBuildingStats();
            }
        }

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

        // Rafraîchir le popup stats si ouvert
        if (buildingStatsPanel != null && statsShownPlacementId >= 0)
            RefreshBuildingStatsPanel(statsShownPlacementId);
    }

    // ── Building stats popup ────────────────────────────────────────

    /// <summary>
    /// Appelé depuis la détection de clic sur la carte (pixel space).
    /// Ouvre le popup pour le bâtiment cliqué, ou le ferme si clic dans le vide.
    /// </summary>
    public void OnMapClick(Vector2 pixelPos)
    {
        if (WaypointManager.Instance == null) return;
        int pid = WaypointManager.Instance.GetPlacementAt(pixelPos);
        if (pid >= 0)
            ShowBuildingStats(pid);
        else
            CloseBuildingStats();
    }

    private void ShowBuildingStats(int placementId)
    {
        statsShownPlacementId = placementId;
        if (buildingStatsPanel != null) buildingStatsPanel.RemoveFromHierarchy();

        buildingStatsPanel = new VisualElement();
        buildingStatsPanel.style.position        = Position.Absolute;
        buildingStatsPanel.style.top             = 120;
        buildingStatsPanel.style.left            = 295;
        buildingStatsPanel.style.minWidth        = 200;
        buildingStatsPanel.style.backgroundColor = new StyleColor(new Color(0.05f, 0.05f, 0.12f, 0.96f));
        buildingStatsPanel.style.paddingTop      = 10;
        buildingStatsPanel.style.paddingBottom   = 10;
        buildingStatsPanel.style.paddingLeft     = 14;
        buildingStatsPanel.style.paddingRight    = 14;
        buildingStatsPanel.style.borderTopLeftRadius     = new StyleLength(8);
        buildingStatsPanel.style.borderTopRightRadius    = new StyleLength(8);
        buildingStatsPanel.style.borderBottomLeftRadius  = new StyleLength(8);
        buildingStatsPanel.style.borderBottomRightRadius = new StyleLength(8);

        uiDocument.rootVisualElement.Add(buildingStatsPanel);
        RefreshBuildingStatsPanel(placementId);
    }

    private void RefreshBuildingStatsPanel(int placementId)
    {
        if (buildingStatsPanel == null || WaypointManager.Instance == null) return;
        buildingStatsPanel.Clear();

        var primary = WaypointManager.Instance.GetPrimaryHive(placementId);
        var allHives = WaypointManager.Instance.GetHivesForPlacement(placementId);
        if (primary == null && allHives.Count == 0) return;

        var def = primary?.definition ?? allHives[0].definition;
        string buildingName = def?.displayName ?? "Bâtiment";

        // Somme agents de tous les outputs
        float totalSpawned = 0f;
        foreach (var h in allHives) totalSpawned += h.totalAgentsSpawned;

        int level = primary?.level ?? 1;
        string stars = new string('★', level) + new string('☆', 10 - level);

        // Uptime
        float uptime = primary?.lifetimeSeconds ?? allHives[0].lifetimeSeconds;
        int mins = (int)(uptime / 60f);
        int secs = (int)(uptime % 60f);

        // Titre
        var title = new Label($"🏠 {buildingName}  [Niv. {level}]");
        title.style.color       = new StyleColor(new Color(0.55f, 0.78f, 1f));
        title.style.fontSize    = 13;
        title.style.unityFontStyleAndWeight = UnityEngine.FontStyle.Bold;
        title.style.marginBottom = 2;
        buildingStatsPanel.Add(title);

        var starsLabel = new Label(stars);
        starsLabel.style.color      = new StyleColor(new Color(1f, 0.85f, 0.2f));
        starsLabel.style.fontSize   = 11;
        starsLabel.style.marginBottom = 6;
        buildingStatsPanel.Add(starsLabel);

        AddStatLine($"Agents produits",          $"{(int)totalSpawned:N0}");
        AddStatLine($"Uptime",                   $"{mins}m {secs:D2}s");

        // Ressources en cours (stock local du bâtiment)
        if (def != null)
        {
            string scaleRes = def.ResolvedScaleResource;
            if (!string.IsNullOrEmpty(scaleRes) && primary != null)
            {
                AddStatLine($"Stock {scaleRes}", $"{primary.localStock:F0} / {primary.maxLocalStock:F0}");
            }

            // Ressources produites
            if (primary != null && primary.totalResourceProduced > 0f)
            {
                string prodRes = def.produces != null && def.produces.Length > 0 ? def.produces[0].resource : "—";
                AddStatLine($"Produit ({prodRes})", $"{primary.totalResourceProduced:F0} u");
            }

            // Ressources consommées
            if (primary != null && primary.totalResourceConsumed > 0f)
            {
                string consRes = !string.IsNullOrEmpty(def.ResolvedScaleResource)
                    ? def.ResolvedScaleResource
                    : (def.consumes != null && def.consumes.Length > 0 ? def.consumes[0].resource : "—");
                AddStatLine($"Consommé ({consRes})", $"{primary.totalResourceConsumed:F0} u");
            }
        }

        // Bouton fermer
        var closeBtn = new Button(() => CloseBuildingStats()) { text = "✕ Fermer" };
        closeBtn.style.marginTop       = 8;
        closeBtn.style.fontSize        = 11;
        closeBtn.style.color           = new StyleColor(new Color(0.7f, 0.7f, 0.7f));
        closeBtn.style.backgroundColor = new StyleColor(new Color(0.15f, 0.15f, 0.2f));
        buildingStatsPanel.Add(closeBtn);
    }

    private void AddStatLine(string label, string value)
    {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.justifyContent = Justify.SpaceBetween;
        row.style.marginBottom = 2;

        var lbl = new Label(label);
        lbl.style.color    = new StyleColor(new Color(0.6f, 0.6f, 0.7f));
        lbl.style.fontSize = 11;

        var val = new Label(value);
        val.style.color    = new StyleColor(Color.white);
        val.style.fontSize = 11;
        val.style.unityFontStyleAndWeight = UnityEngine.FontStyle.Bold;

        row.Add(lbl);
        row.Add(val);
        buildingStatsPanel.Add(row);
    }

    private void CloseBuildingStats()
    {
        buildingStatsPanel?.RemoveFromHierarchy();
        buildingStatsPanel = null;
        statsShownPlacementId = -1;
    }
}
