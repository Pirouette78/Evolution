using UnityEditor;
using UnityEngine;

public static class BuildSettingsSetup {
    [MenuItem("Evolution/Fixed Build Settings")]
    public static void Setup() {
        var scenes = new EditorBuildSettingsScene[] {
            new EditorBuildSettingsScene("Assets/Scenes/MainMenu.unity", true),
            new EditorBuildSettingsScene("Assets/Scenes/MacroMap.unity", true),
            new EditorBuildSettingsScene("Assets/Scenes/ECS_Setup.unity", true)
        };
        EditorBuildSettings.scenes = scenes;
        Debug.Log("[FIX] Build Settings updated with MainMenuScene, MacroMap, and ECS_Setup.");
    }
}
