using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(TerrainMapRenderer))]
public class TerrainMapRendererEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        TerrainMapRenderer renderer = (TerrainMapRenderer)target;

        EditorGUILayout.Space();
        if (GUILayout.Button("Regenerate Map", GUILayout.Height(30)))
        {
            renderer.GenerateMap();
        }
    }
}
