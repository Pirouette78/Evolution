using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Unity.Scenes;

[InitializeOnLoad]
public class FixPostRestructureReferences
{
    static FixPostRestructureReferences()
    {
        EditorApplication.delayCall += () => {
            if (!Application.isPlaying) FixReferences();
        };
    }

    [MenuItem("Evolution/Fix Architecture References")]
    public static void FixReferences()
    {
        Debug.Log("[FIX] Starting reference restoration...");

        // 1. SlimeMapRenderer Fix
        var renderers = Object.FindObjectsByType<SlimeMapRenderer>(FindObjectsSortMode.None);
        bool changed = false;

        foreach (var renderer in renderers)
        {
            if (renderer.SlimeShader == null)
            {
                string path = "Assets/Art/Shaders/SlimeTrailRender.compute";
                renderer.SlimeShader = AssetDatabase.LoadAssetAtPath<ComputeShader>(path);
                if (renderer.SlimeShader != null)
                {
                    Debug.Log($"[FIX] Restored SlimeShader on {renderer.gameObject.name}");
                    EditorUtility.SetDirty(renderer);
                    changed = true;
                }
            }
            
            if (renderer.DisplayTarget == null)
            {
                renderer.DisplayTarget = renderer.GetComponent<MeshRenderer>();
                if (renderer.DisplayTarget != null)
                {
                    Debug.Log($"[FIX] Restored DisplayTarget on {renderer.gameObject.name}");
                    EditorUtility.SetDirty(renderer);
                    changed = true;
                }
            }
        }

        // 2. SubScene Fix (Unity.Scenes)
        var subScenes = Object.FindObjectsByType<SubScene>(FindObjectsSortMode.None);
        foreach (var subScene in subScenes)
        {
            // Check if the SceneAsset is null or invalid
            if (subScene.SceneAsset == null)
            {
                string subScenePath = "Assets/Scenes/ECS_Setup.unity";
                var sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(subScenePath);
                if (sceneAsset != null)
                {
                    subScene.SceneAsset = sceneAsset;
                    Debug.Log("[FIX] Linked SubScene to: " + subScenePath);
                    EditorUtility.SetDirty(subScene); // Mark the SubScene component as dirty
                    changed = true;
                }
            }
        }

        // 3. GlobalManagerAuthoring Fix
        var managers = Object.FindObjectsByType<GlobalManagerAuthoring>(FindObjectsSortMode.None);
        foreach (var manager in managers)
        {
             if (manager.transform.position == Vector3.zero) {
                 // Initialize default position if newly created
                 manager.transform.position = new Vector3(256, 256, 0);
                 changed = true;
             }
        }

        if (changed)
        {
            EditorSceneManager.MarkAllScenesDirty();
            Debug.Log("[FIX] References restored and scenes marked dirty.");
        }
        else
        {
            Debug.Log("[FIX] No missing references found.");
        }
    }
}
