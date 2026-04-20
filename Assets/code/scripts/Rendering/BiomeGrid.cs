using UnityEngine;

/// <summary>
/// Grille 10x10 de biomes : axe X = température (0=froid, 9=chaud), axe Y = humidité (0=sec, 9=humide).
/// Hauteur utilisée comme override (eau, neige, roche sur pente).
/// </summary>
[System.Serializable]
public class BiomeGrid
{
    public const int Size = 10;

    // Noms et couleurs pour l'affichage dans l'éditeur
    public static readonly string[] BiomeNames  = { "Eau", "Sable", "Herbe", "Forêt", "Roche", "Neige", "Marais", "Jungle" };
    public static readonly Color[]  BiomeColors =
    {
        new Color(0.20f, 0.50f, 0.80f), // 0 Eau
        new Color(0.90f, 0.85f, 0.55f), // 1 Sable
        new Color(0.30f, 0.70f, 0.30f), // 2 Herbe
        new Color(0.10f, 0.40f, 0.10f), // 3 Forêt
        new Color(0.55f, 0.50f, 0.45f), // 4 Roche
        new Color(0.95f, 0.95f, 0.97f), // 5 Neige
        new Color(0.20f, 0.45f, 0.30f), // 6 Marais
        new Color(0.05f, 0.35f, 0.05f), // 7 Jungle
    };

    // Stockage plat : cells[tempIdx + humIdx * Size]
    // tempIdx = colonne (X), humIdx = ligne (Y)
    public int[] cells = new int[Size * Size];

    public BiomeGrid()
    {
        // Valeurs par défaut inspirées de la table du projet
        // Lignes = humidité 0(sec)→9(humide), Colonnes = température 0(froid)→9(chaud)
        int[,] defaults = {
        //  T0  T1  T2  T3  T4  T5  T6  T7  T8  T9   ← température (0=froid, 9=chaud)
            { 4,  4,  4,  4,  4,  4,  4,  4,  4,  4 }, // H0 sec
            { 4,  4,  4,  1,  1,  1,  1,  1,  1,  1 }, // H1
            { 4,  4,  2,  2,  1,  1,  1,  1,  1,  1 }, // H2
            { 4,  4,  2,  2,  2,  1,  1,  1,  1,  1 }, // H3
            { 4,  3,  2,  2,  2,  2,  1,  1,  1,  1 }, // H4
            { 4,  3,  3,  2,  2,  2,  2,  3,  3,  3 }, // H5
            { 4,  3,  3,  3,  2,  2,  3,  3,  3,  3 }, // H6
            { 4,  3,  3,  3,  3,  2,  3,  3,  3,  3 }, // H7
            { 4,  3,  3,  3,  2,  2,  3,  3,  3,  3 }, // H8
            { 4,  3,  3,  2,  2,  2,  3,  3,  3,  3 }, // H9 humide
        };

        for (int h = 0; h < Size; h++)
            for (int t = 0; t < Size; t++)
                cells[t + h * Size] = defaults[h, t];
    }

    public int Get(int tempIdx, int humIdx)
    {
        tempIdx = Mathf.Clamp(tempIdx, 0, Size - 1);
        humIdx  = Mathf.Clamp(humIdx,  0, Size - 1);
        return cells[tempIdx + humIdx * Size];
    }

    public void Set(int tempIdx, int humIdx, int biomeId)
    {
        tempIdx = Mathf.Clamp(tempIdx, 0, Size - 1);
        humIdx  = Mathf.Clamp(humIdx,  0, Size - 1);
        cells[tempIdx + humIdx * Size] = biomeId;
    }
}
