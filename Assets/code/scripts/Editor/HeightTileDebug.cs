using UnityEngine;
using UnityEditor;

public class HeightTileDebug
{
    [MenuItem("Tools/Debug Height Tile")]
    static void DebugHeightTile()
    {
        var atlas = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/Tilesets/nHtn9i_v3.png");
        if (atlas == null) { Debug.LogError("Atlas not found"); return; }

        int tileSize = 32;
        int atlasCols = atlas.width  / tileSize;
        int atlasRows = atlas.height / tileSize;

        // Height tile herbe : col=12, row=7 depuis le haut (0-based)
        int col = 12;
        int row = 7;
        int invRow = atlasRows - 1 - row;

        int px = col * tileSize;
        int py = invRow * tileSize;

        var pixels = atlas.GetPixels(px, py, tileSize, tileSize);

        float minL = 1f, maxL = 0f, avgL = 0f;
        float minA = 1f, maxA = 0f, avgA = 0f;
        foreach (var p in pixels)
        {
            float lum = 0.299f * p.r + 0.587f * p.g + 0.114f * p.b;
            if (lum < minL) minL = lum;
            if (lum > maxL) maxL = lum;
            avgL += lum;
            if (p.a < minA) minA = p.a;
            if (p.a > maxA) maxA = p.a;
            avgA += p.a;
        }
        avgL /= pixels.Length;
        avgA /= pixels.Length;

        Debug.Log($"[HeightTile Grass] col={col} row={row} invRow={invRow} px=({px},{py}) atlas={atlas.width}x{atlas.height} cols={atlasCols} rows={atlasRows}");
        Debug.Log($"[HeightTile Grass] Luminance: min={minL:F3} max={maxL:F3} avg={avgL:F3}");
        Debug.Log($"[HeightTile Grass] Alpha:     min={minA:F3} max={maxA:F3} avg={avgA:F3}");
        Debug.Log($"[HeightTile Grass] pixels[0]={pixels[0]}  pixels[mid]={pixels[pixels.Length/2]}");
    }
}
