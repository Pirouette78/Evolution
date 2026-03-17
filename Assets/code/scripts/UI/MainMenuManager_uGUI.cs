using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class MainMenuManager_uGUI : MonoBehaviour {
    [Header("UI Windows")]
    public GameObject mainMenuWindow;
    public GameObject playSubmenuWindow;

    [Header("Buttons")]
    public Button btnPlay;
    public Button btnOptions;
    public Button btnQuit;
    public Button btnLaunch;
    public Button btnBack;

    [Header("Settings")]
    public Slider sliderCells;
    public TextMeshProUGUI txtCells;
    
    public static int StartEntityCount = 100;

    void Awake() {
        Debug.LogWarning("[MAIN MENU uGUI] Awake called! Auto-binding...");
        
        // Auto-detect windows
        if (!mainMenuWindow) mainMenuWindow = transform.Find("MenuWindow")?.gameObject;
        if (!playSubmenuWindow) playSubmenuWindow = transform.Find("PlaySubmenuWindow")?.gameObject;

        // Auto-detect main buttons
        if (mainMenuWindow) {
            if (!btnPlay) btnPlay = mainMenuWindow.transform.Find("BtnPlay_uGUI")?.GetComponent<Button>();
            if (!btnOptions) btnOptions = mainMenuWindow.transform.Find("BtnOptions_uGUI")?.GetComponent<Button>();
            if (!btnQuit) btnQuit = mainMenuWindow.transform.Find("BtnQuit_uGUI")?.GetComponent<Button>();
        }

        // Auto-detect submenu buttons
        if (playSubmenuWindow) {
            if (!btnLaunch) btnLaunch = playSubmenuWindow.transform.Find("BtnLaunch_uGUI")?.GetComponent<Button>();
            if (!btnBack) btnBack = playSubmenuWindow.transform.Find("BtnBack_uGUI")?.GetComponent<Button>();
        }

        if (btnPlay) btnPlay.onClick.AddListener(ShowPlaySubmenu);
        if (btnQuit) btnQuit.onClick.AddListener(QuitGame);
        if (btnBack) btnBack.onClick.AddListener(ShowMainMenu);
        if (btnLaunch) btnLaunch.onClick.AddListener(LaunchSimulation);
        
        // Settings (optional for now to avoid TMP errors)
        if (sliderCells) {
            sliderCells.onValueChanged.AddListener(val => {
                StartEntityCount = (int)val;
                if (txtCells) txtCells.text = StartEntityCount.ToString();
            });
            sliderCells.value = StartEntityCount;
            if (txtCells) txtCells.text = StartEntityCount.ToString();
        }
    }

    void Start() {
        Debug.LogWarning("[MAIN MENU uGUI] Running visual auto-setup...");
        SetLayerRecursively(gameObject, 5); // UI Layer
        SetupVisuals();
        ShowMainMenu();
    }

    // Removed Update to avoid Input System conflicts

    private void SetLayerRecursively(GameObject obj, int layer) {
        obj.layer = layer;
        foreach (Transform child in obj.transform) {
            SetLayerRecursively(child.gameObject, layer);
        }
    }

    private void SetupVisuals() {
        // Setup Canvas
        var canvas = GetComponent<Canvas>();
        if (canvas) {
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 999;
        }

        // Setup Background
        var bgObj = transform.Find("Background");
        if (bgObj) {
            var img = bgObj.GetComponent<Image>();
            img.color = new Color(0.05f, 0.05f, 0.08f, 0.95f);
            var rt = bgObj.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.sizeDelta = Vector2.zero;
        }

        // Setup Windows
        if (mainMenuWindow) SetupWindow(mainMenuWindow);
        if (playSubmenuWindow) SetupWindow(playSubmenuWindow);
    }

    private void SetupWindow(GameObject win) {
        var rt = win.GetComponent<RectTransform>();
        if (!rt) rt = win.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(400, 600);
        rt.anchoredPosition = Vector2.zero;

        // Simple Vertical Layout
        var buttons = win.GetComponentsInChildren<Button>(true);
        float startY = 200;
        float spacing = 70;
        for (int i = 0; i < buttons.Length; i++) {
            SetupButtonVisual(buttons[i]);
            var brt = buttons[i].GetComponent<RectTransform>();
            brt.anchoredPosition = new Vector2(0, startY - (i * spacing));
        }
    }

    private void SetupButtonVisual(Button btn) {
        var img = btn.GetComponent<Image>();
        if (!img) img = btn.gameObject.AddComponent<Image>();
        img.color = new Color(0.2f, 0.2f, 0.25f, 1f);

        var rt = btn.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(300, 50);

        var textObj = btn.transform.Find("Text");
        if (!textObj) {
            var go = new GameObject("Text");
            go.transform.SetParent(btn.transform, false);
            var txt = go.AddComponent<Text>();
            txt.text = btn.name.Replace("Btn", "").Replace("_uGUI", "").ToUpper();
            txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            txt.alignment = TextAnchor.MiddleCenter;
            txt.color = Color.white;
            txt.fontSize = 24;
            var textRt = go.GetComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.sizeDelta = Vector2.zero;
        }
    }

    public void ShowPlaySubmenu() {
        if (mainMenuWindow) mainMenuWindow.SetActive(false);
        if (playSubmenuWindow) playSubmenuWindow.SetActive(true);
    }

    public void ShowMainMenu() {
        if (mainMenuWindow) mainMenuWindow.SetActive(true);
        if (playSubmenuWindow) playSubmenuWindow.SetActive(false);
    }

    public void LaunchSimulation() {
        Debug.Log("[MAIN MENU] Launching MacroMap with " + StartEntityCount + " entities.");
        SceneManager.LoadScene("MacroMap");
    }

    public void QuitGame() {
        Debug.Log("Quitting Game...");
        Application.Quit();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }
}