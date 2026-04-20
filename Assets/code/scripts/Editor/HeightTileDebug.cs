using UnityEngine;
using UnityEditor;

public class HeightTileDebug
{
    [MenuItem("Tools/Debug BiomeGrid Texture")]
    static void DebugBiomeGrid()
    {
        var r = GameObject.FindObjectOfType<TerrainMapRenderer>();
        if (r == null) { Debug.LogError("TerrainMapRenderer not found"); return; }

        // La _BiomeGridTex est créée au runtime — on compare directement les grilles C#
        // Log uniquement la grille C# pour vérifier l'encodage
        var mat = (Material)null;
        if (mat == null) { Debug.LogError("TerrainOverlayMaterial not found"); return; }

        var tex = mat.GetTexture("_BiomeGridTex") as Texture2D;
        if (tex == null) { Debug.LogError("_BiomeGridTex not assigned"); return; }

        Debug.Log($"[BiomeGrid] Texture: {tex.width}x{tex.height} format={tex.format} filter={tex.filterMode}");

        // Log toutes les valeurs de la grille
        for (int h = 9; h >= 0; h--)
        {
            string row = $"H={h}: ";
            for (int t = 0; t < 10; t++)
            {
                // UV centre de chaque cellule
                float u = (t * 10f + 5f) / 100f;
                float v = (h * 10f + 5f) / 100f;
                Color c = tex.GetPixelBilinear(u, v);
                int biome = Mathf.RoundToInt(c.r * 5f);
                row += $"{biome}({c.r:F2}) ";
            }
            Debug.Log(row);
        }

        // Compare avec la grille C#
        Debug.Log("=== Grille C# ===");
        for (int h = 9; h >= 0; h--)
        {
            string row = $"H={h}: ";
            for (int t = 0; t < 10; t++)
                row += $"{r.Biomes.Get(t, h)} ";
            Debug.Log(row);
        }
    }
}
