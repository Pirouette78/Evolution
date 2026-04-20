using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(TerrainMapRenderer))]
public class TerrainMapRendererEditor : Editor
{
    enum DebugView { None, Altitude, Temperature, Humidity, Biome }
    DebugView _debugView = DebugView.None;

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        TerrainMapRenderer r = (TerrainMapRenderer)target;

        EditorGUILayout.Space();
        if (GUILayout.Button("Regenerate Map", GUILayout.Height(30)))
        {
            // Applique les changements de la grille avant de régénérer
            serializedObject.ApplyModifiedProperties();
            r.GenerateMap();
            if (_debugView != DebugView.None)
                ApplyDebugView(r, _debugView);
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Debug View", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        foreach (DebugView v in System.Enum.GetValues(typeof(DebugView)))
        {
            bool active = _debugView == v;
            GUI.backgroundColor = active ? Color.yellow : Color.white;
            if (GUILayout.Button(v.ToString()))
            {
                _debugView = v;
                if (v == DebugView.None)
                    r.RestoreNormalView();
                else
                    ApplyDebugView(r, v);
            }
        }
        GUI.backgroundColor = Color.white;
        EditorGUILayout.EndHorizontal();
    }

    void ApplyDebugView(TerrainMapRenderer r, DebugView view)
    {
        int w = r.Width, h = r.Height;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.filterMode = FilterMode.Bilinear;
        var pixels = new Color[w * h];

        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            float val = 0f;
            switch (view)
            {
                case DebugView.Altitude:
                    val = r.HeightMap[x, y];
                    pixels[y * w + x] = new Color(val, val, val);
                    break;
                case DebugView.Temperature:
                    val = r.GetTemperaturePublic(x, y);
                    pixels[y * w + x] = new Color(val, 1f - val, 0f); // rouge=chaud, vert=froid
                    break;
                case DebugView.Humidity:
                    val = r.GetHumidityPublic(x, y);
                    pixels[y * w + x] = new Color(0f, val * 0.5f, val); // bleu=humide
                    break;
                case DebugView.Biome:
                    int biome = r.GetBiomePublic(x, y);
                    biome = Mathf.Clamp(biome, 0, BiomeGrid.BiomeColors.Length - 1);
                    pixels[y * w + x] = BiomeGrid.BiomeColors[biome];
                    break;
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        r.SetDebugTexture(tex);
    }
}
