using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Scenes;

[InitializeOnLoad]
public class SetupSceneShortcut {
    static SetupSceneShortcut() {
        EditorApplication.delayCall += DoSetup;
    }
    
    [MenuItem("Evolution/Setup Mission Scene")]
    static void DoSetup() {
        if (UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode) return;
        
        var cam = Camera.main;
        if (cam != null) {
            cam.transform.position = new Vector3(256, 256, -10);
            cam.orthographic = true;
            cam.orthographicSize = 256;
        }
        
        var bg = GameObject.Find("BackgroundRenderer");
        if (bg == null) {
            bg = GameObject.CreatePrimitive(PrimitiveType.Quad);
            bg.name = "BackgroundRenderer";
        }
        bg.transform.position = new Vector3(256f, 256f, 0f); // Move to Z=0
        bg.transform.localScale = new Vector3(512f, 512f, 1f);
        
        var meshFilter = bg.GetComponent<MeshFilter>();
        if (meshFilter == null) bg.AddComponent<MeshFilter>().mesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
        
        var meshRenderer = bg.GetComponent<MeshRenderer>();
        if (meshRenderer == null) meshRenderer = bg.AddComponent<MeshRenderer>();
        
        var renderer = bg.GetComponent<SlimeMapRenderer>();
        if (renderer == null) renderer = bg.AddComponent<SlimeMapRenderer>();
        
        renderer.SlimeShader = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Art/Shaders/SlimeTrailRender.compute");

        // --- Terrain Map Renderer ---
        var terrainRenderer = bg.GetComponent<TerrainMapRenderer>();
        if (terrainRenderer == null) terrainRenderer = bg.AddComponent<TerrainMapRenderer>();
        terrainRenderer.DisplayTarget = meshRenderer;
        terrainRenderer.Width = 512;
        terrainRenderer.Height = 512;
        EditorUtility.SetDirty(bg);
        
        Material material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/SlimeDisplayMaterial.mat");
        if (material == null) {
            material = new Material(Shader.Find("Unlit/Texture"));
            material.name = "BackgroundMaterial";
            if (!AssetDatabase.IsValidFolder("Assets/Art/Materials")) AssetDatabase.CreateFolder("Assets/Art", "Materials");
            AssetDatabase.CreateAsset(material, "Assets/Art/Materials/SlimeDisplayMaterial.mat");
        } else {
            material.shader = Shader.Find("Unlit/Texture");
            material.color = Color.white;
        }
        meshRenderer.material = material;
        
        // --- ECS SubScene Setup ---
        var authoring = Object.FindAnyObjectByType<GlobalManagerAuthoring>();
        string subScenePath = "Assets/Scenes/ECS_Setup.unity";
        bool subSceneExists = System.IO.File.Exists(subScenePath);

        // Only proceed if we found it and it's in the Main Scene (not already a prefab or in a subscene)
        if (authoring != null && authoring.gameObject.scene.name != "ECS_Setup") {
            
            if (subSceneExists) {
                // If the subscene already exists, we should check if it already has an authoring object
                // If so, the one in the main scene is a duplicate and should be removed
                Debug.LogWarning("[SETUP] SubScene 'ECS_Setup' already exists. Deleting duplicate authoring object in Main Scene.");
                Object.DestroyImmediate(authoring.gameObject);
                authoring = null; // Mark as null so prefab assignment logic below doesn't run on a destroyed object
            } else {
                if (!AssetDatabase.IsValidFolder("Assets/Scenes")) {
                    AssetDatabase.CreateFolder("Assets", "Scenes");
                }

                Scene newScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                EditorSceneManager.SaveScene(newScene, subScenePath);

                // Move the authoring object to the newly created subscene
                SceneManager.MoveGameObjectToScene(authoring.gameObject, newScene);
                EditorSceneManager.SaveScene(newScene);
                
                // Unload the scene natively because SubScene component will manage it
                EditorSceneManager.CloseScene(newScene, true);

                // Create the SubScene component loader in the active (Main) scene
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

            // --- Terrain Map Authoring (in same SubScene) ---
            var terrainAuth = authoring.GetComponent<TerrainMapAuthoring>();
            if (terrainAuth == null) terrainAuth = authoring.gameObject.AddComponent<TerrainMapAuthoring>();
            terrainAuth.Width = 512;
            terrainAuth.Height = 512;
            terrainAuth.WaterThreshold = 0.35f;
            EditorUtility.SetDirty(terrainAuth);
        }

        // --- PROJECT-WIDE SUBSENE CLEANUP ---
        var allAuthorings = Object.FindObjectsByType<GlobalManagerAuthoring>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        int totalFound = 0;
        foreach (var a in allAuthorings) {
            totalFound++;
            if (totalFound > 1) {
                Debug.LogWarning($"[FIX] Removing duplicate GlobalManagerAuthoring from scene: {a.gameObject.scene.name} (Object: {a.gameObject.name})");
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
                    if (subCount > 1 || (totalFound > 0 && subCount > 0 && totalFound > subCount)) {
                        Debug.LogWarning($"[FIX] Removing internal duplicate from ECS_Setup: {a.gameObject.name}");
                        Object.DestroyImmediate(a.gameObject);
                    }
                }
            }
            
            if (subCount > 1) EditorSceneManager.SaveScene(subScene);
            EditorSceneManager.CloseScene(subScene, true);
        }

        Debug.Log("Scene and SubScene have been cleaned of duplicates.");
        EditorPrefs.SetBool("SceneSetupDone_Evo", true);
    }
}
