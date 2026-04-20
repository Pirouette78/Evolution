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

        // Entête température (colonnes)
        float gridX = position.x + LabelWidth;
        float gridY = position.y + HeaderH;

        EditorGUI.LabelField(new Rect(position.x, gridY, LabelWidth + CellSize * BiomeGrid.Size, HeaderH),
            "↑ Humidité    Température →", EditorStyles.miniLabel);
        gridY += HeaderH;

        for (int h = BiomeGrid.Size - 1; h >= 0; h--)
        {
            // Label humidité
            EditorGUI.LabelField(new Rect(position.x, gridY, LabelWidth, CellSize),
                h.ToString(), EditorStyles.miniLabel);

            for (int t = 0; t < BiomeGrid.Size; t++)
            {
                int idx    = t + h * BiomeGrid.Size;
                int biome  = cells.GetArrayElementAtIndex(idx).intValue;
                biome      = Mathf.Clamp(biome, 0, BiomeGrid.BiomeNames.Length - 1);

                Rect cellRect = new Rect(gridX + t * CellSize, gridY, CellSize - 1, CellSize - 1);

                Color old = GUI.backgroundColor;
                GUI.backgroundColor = BiomeGrid.BiomeColors[biome];
                if (GUI.Button(cellRect, BiomeGrid.BiomeNames[biome].Substring(0, Mathf.Min(3, BiomeGrid.BiomeNames[biome].Length)),
                    EditorStyles.miniButton))
                {
                    // Cycle vers le biome suivant au clic
                    int next = (biome + 1) % BiomeGrid.BiomeNames.Length;
                    cells.GetArrayElementAtIndex(idx).intValue = next;
                }
                GUI.backgroundColor = old;
            }
            gridY += CellSize;
        }

        // Labels température en bas
        for (int t = 0; t < BiomeGrid.Size; t++)
            EditorGUI.LabelField(new Rect(gridX + t * CellSize, gridY, CellSize, HeaderH),
                t.ToString(), EditorStyles.centeredGreyMiniLabel);
    }
}
