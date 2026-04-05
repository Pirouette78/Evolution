using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Scenes;

[InitializeOnLoad]
public class SetupSceneShortcut {
    static SetupSceneShortcut() {
        if (!EditorPrefs.GetBool("SceneSetupDone_Evo", false)) {
            EditorApplication.delayCall += DoSetup;
        }
    }
    
    [MenuItem("Evolution/Setup Mission Scene")]
    static void DoSetup() {
        if (UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode) return;
        // Find existing TerrainMapRenderer to extract current user dimensions, to avoid hardcoding
        var existingTerrain = Object.FindAnyObjectByType<TerrainMapRenderer>();
        int mapWidth = 2560;
        int mapHeight = 1440;
        if (existingTerrain != null && existingTerrain.Width > 0) {
            mapWidth = existingTerrain.Width;
            mapHeight = existingTerrain.Height;
        }

        var cam = Camera.main;
        if (cam != null) {
            cam.transform.position = new Vector3(mapWidth / 2f, mapHeight / 2f, -10f);
            cam.orthographic = true;
            cam.orthographicSize = mapHeight / 2f;

            var camCtrl = cam.GetComponent<CameraController>();
            if (camCtrl == null) camCtrl = cam.gameObject.AddComponent<CameraController>();
            EditorUtility.SetDirty(cam.gameObject);
        }

        // --- Game HUD (UIDocument) ---
        var uiGO = GameObject.Find("GameHUD");
        if (uiGO == null) uiGO = new GameObject("GameHUD");
        var uiDoc = uiGO.GetComponent<UnityEngine.UIElements.UIDocument>();
        if (uiDoc == null) uiDoc = uiGO.AddComponent<UnityEngine.UIElements.UIDocument>();
        var uxmlAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.UIElements.VisualTreeAsset>("Assets/UI/GameUI.uxml");
        if (uxmlAsset != null) uiDoc.visualTreeAsset = uxmlAsset;
        var panelSettings = AssetDatabase.LoadAssetAtPath<UnityEngine.UIElements.PanelSettings>("Assets/UI/GamePanelSettings.asset");
        if (panelSettings == null) {
            panelSettings = ScriptableObject.CreateInstance<UnityEngine.UIElements.PanelSettings>();
            panelSettings.scaleMode = UnityEngine.UIElements.PanelScaleMode.ScaleWithScreenSize;
            panelSettings.referenceResolution = new Vector2Int(1920, 1080);
            if (!AssetDatabase.IsValidFolder("Assets/UI")) AssetDatabase.CreateFolder("Assets", "UI");
            AssetDatabase.CreateAsset(panelSettings, "Assets/UI/GamePanelSettings.asset");
        }
        uiDoc.panelSettings = panelSettings;
        var uiCtrl = uiGO.GetComponent<UIController>();
        if (uiCtrl == null) uiCtrl = uiGO.AddComponent<UIController>();
        EditorUtility.SetDirty(uiGO);

        // ============================================================
        //  LAYER 1: TERRAIN MAP (back layer, z = 1)
        // ============================================================
        var terrainGO = GameObject.Find("TerrainLayer");
        if (terrainGO == null) {
            terrainGO = GameObject.CreatePrimitive(PrimitiveType.Quad);
            terrainGO.name = "TerrainLayer";
        }
        terrainGO.transform.position = new Vector3(mapWidth / 2f, mapHeight / 2f, 1f); // z=1 (behind)
        terrainGO.transform.localScale = new Vector3((float)mapWidth, (float)mapHeight, 1f);

        var terrainMR = terrainGO.GetComponent<MeshRenderer>();
        if (terrainMR == null) terrainMR = terrainGO.AddComponent<MeshRenderer>();

        // Terrain material (separate)
        Material terrainMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/TerrainDisplayMaterial.mat");
        if (terrainMat == null) {
            terrainMat = new Material(Shader.Find("Unlit/Texture"));
            terrainMat.name = "TerrainDisplayMaterial";
            if (!AssetDatabase.IsValidFolder("Assets/Art/Materials")) AssetDatabase.CreateFolder("Assets/Art", "Materials");
            AssetDatabase.CreateAsset(terrainMat, "Assets/Art/Materials/TerrainDisplayMaterial.mat");
        }
        terrainMat.shader = Shader.Find("Unlit/Texture");
        terrainMat.color = Color.white;
        terrainMR.sharedMaterial = terrainMat;

        var terrainRenderer = terrainGO.GetComponent<TerrainMapRenderer>();
        if (terrainRenderer == null) {
            terrainRenderer = terrainGO.AddComponent<TerrainMapRenderer>();
            terrainRenderer.Width = mapWidth;
            terrainRenderer.Height = mapHeight;
        }
        terrainRenderer.DisplayTarget = terrainMR;

        // Remove SlimeMapRenderer from terrain if it was there from old setup
        var oldSlime = terrainGO.GetComponent<SlimeMapRenderer>();
        if (oldSlime != null) Object.DestroyImmediate(oldSlime);

        EditorUtility.SetDirty(terrainGO);

        // ============================================================
        //  LAYER 2: STRATEGY / SLIME MAP (front layer, z = 0)
        // ============================================================
        var strategyGO = GameObject.Find("StrategyLayer");
        if (strategyGO == null) {
            strategyGO = GameObject.CreatePrimitive(PrimitiveType.Quad);
            strategyGO.name = "StrategyLayer";
        }
        strategyGO.transform.position = new Vector3(mapWidth / 2f, mapHeight / 2f, 0f); // z=0 (in front)
        strategyGO.transform.localScale = new Vector3((float)mapWidth, (float)mapHeight, 1f);

        var strategyMR = strategyGO.GetComponent<MeshRenderer>();
        if (strategyMR == null) strategyMR = strategyGO.AddComponent<MeshRenderer>();

        // Strategy material (separate)
        Material stratMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/SlimeDisplayMaterial.mat");
        if (stratMat == null) {
            stratMat = new Material(Shader.Find("Unlit/Texture"));
            stratMat.name = "SlimeDisplayMaterial";
            if (!AssetDatabase.IsValidFolder("Assets/Art/Materials")) AssetDatabase.CreateFolder("Assets/Art", "Materials");
            AssetDatabase.CreateAsset(stratMat, "Assets/Art/Materials/SlimeDisplayMaterial.mat");
        }
        stratMat.shader = Shader.Find("Unlit/Texture");
        stratMat.color = Color.white;
        strategyMR.sharedMaterial = stratMat;

        var slimeRenderer = strategyGO.GetComponent<SlimeMapRenderer>();
        if (slimeRenderer == null) slimeRenderer = strategyGO.AddComponent<SlimeMapRenderer>();
        slimeRenderer.SlimeShader = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Art/Shaders/SlimeTrailRender.compute");
        slimeRenderer.DisplayTarget = strategyMR;

        // Remove TerrainMapRenderer from strategy layer if it was there
        var oldTerrain = strategyGO.GetComponent<TerrainMapRenderer>();
        if (oldTerrain != null) Object.DestroyImmediate(oldTerrain);

        EditorUtility.SetDirty(strategyGO);

        // ============================================================
        //  CLEANUP: Remove old BackgroundRenderer if it exists
        // ============================================================
        var oldBg = GameObject.Find("BackgroundRenderer");
        if (oldBg != null) {
            Debug.Log("[SETUP] Removing old BackgroundRenderer — replaced by TerrainLayer + StrategyLayer.");
            Object.DestroyImmediate(oldBg);
        }

        // --- ECS SubScene Setup ---
        var authoring = Object.FindAnyObjectByType<GlobalManagerAuthoring>();
        string subScenePath = "Assets/Scenes/ECS_Setup.unity";
        bool subSceneExists = System.IO.File.Exists(subScenePath);

        if (authoring != null && authoring.gameObject.scene.name != "ECS_Setup") {
            if (subSceneExists) {
                Object.DestroyImmediate(authoring.gameObject);
                authoring = null;
            } else {
                if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
                    AssetDatabase.CreateFolder("Assets", "Scenes");

                Scene newScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                EditorSceneManager.SaveScene(newScene, subScenePath);
                SceneManager.MoveGameObjectToScene(authoring.gameObject, newScene);
                EditorSceneManager.SaveScene(newScene);
                EditorSceneManager.CloseScene(newScene, true);

                var subSceneGO = GameObject.Find("ECS_Setup_SubScene");
                if (subSceneGO == null) {
                    subSceneGO = new GameObject("ECS_Setup_SubScene");
                    var subScene = subSceneGO.AddComponent<SubScene>();
                    subScene.SceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(subScenePath);
                }
            }
        }

        // --- ENSURE PREFABS ARE ASSIGNED ---
        var cellPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/Prefabs/Cell.prefab");
        var foodPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/Prefabs/Food.prefab");
        if (authoring != null) {
            authoring.CellPrefab = cellPrefab;
            authoring.FoodPrefab = foodPrefab;
            EditorUtility.SetDirty(authoring);

            var terrainAuth = authoring.GetComponent<TerrainMapAuthoring>();
            if (terrainAuth == null) terrainAuth = authoring.gameObject.AddComponent<TerrainMapAuthoring>();
            terrainAuth.Width = mapWidth;
            terrainAuth.Height = mapHeight;
            terrainAuth.WaterThreshold = 0.35f;
            EditorUtility.SetDirty(terrainAuth);
        }

        // --- PROJECT-WIDE CLEANUP ---
        var allAuthorings = Object.FindObjectsByType<GlobalManagerAuthoring>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        int totalFound = 0;
        foreach (var a in allAuthorings) {
            totalFound++;
            if (totalFound > 1) {
                Object.DestroyImmediate(a.gameObject);
            }
        }

        if (subSceneExists) {
            var subScene = EditorSceneManager.OpenScene(subScenePath, OpenSceneMode.Additive);
            var authoringsInSub = Object.FindObjectsByType<GlobalManagerAuthoring>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            int subCount = 0;
            foreach (var a in authoringsInSub) {
                if (a.gameObject.scene == subScene) {
                    subCount++;
                    if (subCount > 1) {
                        Object.DestroyImmediate(a.gameObject);
                    }
                }
            }
            if (subCount > 1) EditorSceneManager.SaveScene(subScene);
            EditorSceneManager.CloseScene(subScene, true);
        }

        Debug.Log("[SETUP] Scene configured: TerrainLayer (z=1) + StrategyLayer (z=0) + GameHUD.");
        EditorPrefs.SetBool("SceneSetupDone_Evo", true);
    }
}
