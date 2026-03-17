using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;

public class MainMenuManager_V2 : MonoBehaviour {
    private VisualElement root;
    private VisualElement mainMenuContainer;
    private VisualElement playSubmenu;
    
    private Button btnPlay;
    private Button btnOptions;
    private Button btnQuit;
    private Button btnLaunch;
    private Button btnBack;
    
    private SliderInt sliderCells;
    private Label lblCells;
    
    public static int StartEntityCount = 100;

    void Awake() {
        Debug.LogWarning("[MAIN MENU] AWAKE called!");
    }

    void Start() {
        Debug.LogWarning("[MAIN MENU] Start called.");
        InitializeUI();
    }

    private void OnEnable() {
        Debug.LogWarning("[MAIN MENU] OnEnable called.");
        InitializeUI();
    }

    private void InitializeUI() {
        var doc = GetComponent<UIDocument>();
        if (doc == null) {
            Debug.LogError("[MAIN MENU] UIDocument component NOT FOUND on " + gameObject.name);
            return;
        }
        root = doc.rootVisualElement;
        if (root == null) {
            Debug.LogError("[MAIN MENU] Root visual element is null!");
            return;
        }

        Debug.LogWarning("[MAIN MENU] Root found: " + root.name + ". Child count: " + root.childCount);
        
        mainMenuContainer = root.Q<VisualElement>("MainMenuContainer");
        if (mainMenuContainer != null) {
            Debug.LogWarning($"[MAIN MENU] Container found. Opacity: {mainMenuContainer.resolvedStyle.opacity}, Display: {mainMenuContainer.resolvedStyle.display}, Visibility: {mainMenuContainer.resolvedStyle.visibility}");
        }
        playSubmenu = root.Q<VisualElement>("PlaySubmenu");
        
        btnPlay = root.Q<Button>("BtnPlay");
        btnOptions = root.Q<Button>("BtnOptions");
        btnQuit = root.Q<Button>("BtnQuit");
        btnLaunch = root.Q<Button>("BtnLaunch");
        btnBack = root.Q<Button>("BtnBack");
        
        sliderCells = root.Q<SliderInt>("SliderCells");
        lblCells = root.Q<Label>("LblCells");

        if (btnPlay == null || mainMenuContainer == null || playSubmenu == null) {
            Debug.LogError("[MAIN MENU] Critical UI elements missing! Check UXML names.");
            return;
        }
        
        if (sliderCells != null) sliderCells.value = StartEntityCount;
        if (lblCells != null) lblCells.text = StartEntityCount.ToString();
        
        btnPlay.clicked += ShowPlaySubmenu;
        btnQuit.clicked += QuitGame;
        btnBack.clicked += ShowMainMenu;
        btnLaunch.clicked += LaunchSimulation;
        
        sliderCells.RegisterValueChangedCallback(evt => {
            lblCells.text = evt.newValue.ToString();
            StartEntityCount = evt.newValue;
        });
    }

    private void ShowPlaySubmenu() {
        mainMenuContainer.style.display = DisplayStyle.None;
        playSubmenu.AddToClassList("submenu-show");
    }

    private void ShowMainMenu() {
        mainMenuContainer.style.display = DisplayStyle.Flex;
        playSubmenu.RemoveFromClassList("submenu-show");
    }

    private void LaunchSimulation() {
        SceneManager.LoadScene("MacroMap");
    }

    private void QuitGame() {
        Debug.Log("Quitting Game...");
        Application.Quit();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }
}
