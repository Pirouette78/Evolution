using UnityEngine;
using UnityEditor;

[CustomPropertyDrawer(typeof(BiomeGrid))]
public class BiomeGridDrawer : PropertyDrawer
{
    const int CellSize   = 36;
    const int LabelWidth = 20;
    const int HeaderH    = 18;

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        return HeaderH * 2 + CellSize * BiomeGrid.Size + 8;
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.LabelField(new Rect(position.x, position.y, position.width, HeaderH),
            label, EditorStyles.boldLabel);

        var cells = property.FindPropertyRelative("cells");
        float gridX = position.x + LabelWidth;
        float gridY = position.y + HeaderH;

        EditorGUI.LabelField(new Rect(position.x, gridY, LabelWidth + CellSize * BiomeGrid.Size, HeaderH),
            "↑ Humidité    Température →", EditorStyles.miniLabel);
        gridY += HeaderH;

        for (int h = BiomeGrid.Size - 1; h >= 0; h--)
        {
            EditorGUI.LabelField(new Rect(position.x, gridY, LabelWidth, CellSize),
                h.ToString(), EditorStyles.miniLabel);

            for (int t = 0; t < BiomeGrid.Size; t++)
            {
                int idx   = t + h * BiomeGrid.Size;
                int biome = Mathf.Clamp(cells.GetArrayElementAtIndex(idx).intValue, 0, BiomeGrid.BiomeNames.Length - 1);

                Rect cellRect = new Rect(gridX + t * CellSize, gridY, CellSize - 1, CellSize - 1);
                Color old = GUI.backgroundColor;
                GUI.backgroundColor = BiomeGrid.BiomeColors[biome];
                if (GUI.Button(cellRect, BiomeGrid.BiomeNames[biome].Substring(0, Mathf.Min(3, BiomeGrid.BiomeNames[biome].Length)), EditorStyles.miniButton))
                    cells.GetArrayElementAtIndex(idx).intValue = (biome + 1) % BiomeGrid.BiomeNames.Length;
                GUI.backgroundColor = old;
            }
            gridY += CellSize;
        }

        for (int t = 0; t < BiomeGrid.Size; t++)
            EditorGUI.LabelField(new Rect(gridX + t * CellSize, gridY, CellSize, HeaderH),
                t.ToString(), EditorStyles.centeredGreyMiniLabel);
    }
}

[CustomPropertyDrawer(typeof(AltitudeGrid))]
public class AltitudeGridDrawer : PropertyDrawer
{
    const int CellSize = 52;
    const int HeaderH  = 18;

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        return HeaderH + CellSize + HeaderH + 4;
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.LabelField(new Rect(position.x, position.y, position.width, HeaderH),
            label, EditorStyles.boldLabel);

        var cells = property.FindPropertyRelative("cells");
        float gridY = position.y + HeaderH;

        for (int a = 0; a < AltitudeGrid.Size; a++)
        {
            int biome = Mathf.Clamp(cells.GetArrayElementAtIndex(a).intValue, 0, BiomeGrid.BiomeNames.Length - 1);
            Rect cellRect = new Rect(position.x + a * CellSize, gridY, CellSize - 1, CellSize - 1);

            Color old = GUI.backgroundColor;
            GUI.backgroundColor = BiomeGrid.BiomeColors[biome];
            if (GUI.Button(cellRect, BiomeGrid.BiomeNames[biome].Substring(0, Mathf.Min(3, BiomeGrid.BiomeNames[biome].Length)), EditorStyles.miniButton))
                cells.GetArrayElementAtIndex(a).intValue = (biome + 1) % BiomeGrid.BiomeNames.Length;
            GUI.backgroundColor = old;
        }

        gridY += CellSize;
        for (int a = 0; a < AltitudeGrid.Size; a++)
            EditorGUI.LabelField(new Rect(position.x + a * CellSize, gridY, CellSize, HeaderH),
                a.ToString(), EditorStyles.centeredGreyMiniLabel);
    }
}
