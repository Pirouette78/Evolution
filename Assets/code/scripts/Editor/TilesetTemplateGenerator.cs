using UnityEngine;
using UnityEditor;
using System.IO;

public class TilesetTemplateGenerator
{
    [MenuItem("Evolution/Analyze Boris Wang Tileset")]
    public static void AnalyzeBorisWang()
    {
        string path = Application.dataPath + "/wangbl.png";
        if (!System.IO.File.Exists(path)) {
            Debug.LogError("Error: Assets/wangbl.png not found.");
            return;
        }
        
        byte[] data = System.IO.File.ReadAllBytes(path);
        Texture2D tex = new Texture2D(2, 2);
        tex.LoadImage(data);
        
        int w = tex.width;
        int h = tex.height;
        int cols = 7;
        int rows = 7;
        int tw = w / cols;
        int th = h / rows;
        
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.AppendLine("private Vector2Int GetWangBorisPosition(int mask) {");
        sb.AppendLine("    int ortho = mask & 15;");
        sb.AppendLine("    int diag = mask >> 4;");
        
        for (int ty = 0; ty < rows; ty++)
        {
            for (int tx = 0; tx < cols; tx++)
            {
                int cx = tx * tw;
                int cy = h - (ty + 1) * th;
                
                bool IsFg(int dx, int dy)
                {
                    Color32 c = tex.GetPixel(cx + dx, cy + dy);
                    // L'image de Boris utilise un centre sombre (R=68) et un fond clair (R=128)
                    return c.r < 110;
                }
                
                bool center = IsFg(tw/2, th/2);
                bool n = IsFg(tw/2, th - 3);
                bool s = IsFg(tw/2, 2);
                bool e = IsFg(tw - 3, th/2);
                bool w_fg = IsFg(2, th/2);
                
                if (!center && !n && !s && !e && !w_fg) continue;
                
                bool ne = IsFg(tw - 3, th - 3);
                bool se = IsFg(tw - 3, 2);
                bool sw = IsFg(2, 2);
                bool nw = IsFg(2, th - 3);
                
                int ortho = (n?1:0) | (e?2:0) | (s?4:0) | (w_fg?8:0);
                int d1 = (ne && n && e)?1:0;
                int d2 = (se && s && e)?2:0;
                int d3 = (sw && s && w_fg)?4:0;
                int d4 = (nw && n && w_fg)?8:0;
                int diag = d1 | d2 | d3 | d4;
                
                sb.AppendLine($"    if (ortho == {ortho} && diag == {diag}) return new Vector2Int({tx}, {ty});");
            }
        }
        sb.AppendLine("    return new Vector2Int(0, 0);");
        sb.AppendLine("}");
        
        System.IO.File.WriteAllText(Application.dataPath + "/WangBorisMapping.txt", sb.ToString());
        Debug.Log("Analysis complete: " + Application.dataPath + "/WangBorisMapping.txt");
    }

    [MenuItem("Evolution/Check Wang Image Visually ASCII")]
    public static void CheckWangVisually()
    {
        string path = Application.dataPath + "/wangbl.png";
        byte[] data = System.IO.File.ReadAllBytes(path);
        Texture2D tex = new Texture2D(2, 2);
        tex.LoadImage(data);
        
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.AppendLine($"Dimensions reelles: {tex.width}x{tex.height}");
        for (int ty = 0; ty < 7; ty++) {
            // Dans Unity, on lit les lignes du haut vers le bas de la tuile en inversant r ?
            // ty=0 est la ligne du haut. Les pixels locaux y vont de 31 (haut) à 0 (bas).
            for (int r = 31; r >= 0; r -= 4) {
               for (int tx = 0; tx < 7; tx++) {
                   for (int c = 0; c < 32; c += 4) {
                       Color32 pixel = tex.GetPixel(tx*32 + c, tex.height - (ty+1)*32 + r);
                       sb.Append(pixel.r < 110 ? "X " : ". ");
                   }
                   sb.Append("|");
               }
               sb.AppendLine();
            }
            sb.AppendLine(new string('-', 70));
        }
        System.IO.File.WriteAllText(Application.dataPath + "/WangVisual.txt", sb.ToString());
        Debug.Log("Dump visuel ASCII généré dans WangVisual.txt");
    }

    [MenuItem("Evolution/Generate 9x8 Seamless Template")]
    public static void Generate()
    {
        int tileSize = 32;
        int cols = 9;
        int rows = 8;
        Texture2D tex = new Texture2D(cols * tileSize, rows * tileSize, TextureFormat.RGBA32, false);
        
        Color bg = new Color(0.2f, 0.4f, 0.8f, 1f); // Eau (bleu clair)
        Color fg = new Color(0.4f, 0.8f, 0.3f, 1f); // Terre (vert)
        Color grid = new Color(0f, 0f, 0f, 0.7f); // Lignes de grille
        
        // Remplir le fond d'eau
        for (int y = 0; y < tex.height; y++)
        {
            for (int x = 0; x < tex.width; x++)
            {
                tex.SetPixel(x, y, bg);
            }
        }

        // Dessiner chaque pièce de terrain par-dessus
        for (int m = 0; m < 256; m++)
        {
            bool n = (m & 1) != 0; bool e = (m & 2) != 0; bool s = (m & 4) != 0; bool w = (m & 8) != 0;
            bool ne = (m & 16) != 0 && n && e;
            bool se = (m & 32) != 0 && s && e;
            bool sw = (m & 64) != 0 && s && w;
            bool nw = (m & 128) != 0 && n && w;
            int cleanMask = (n?1:0)|(e?2:0)|(s?4:0)|(w?8:0)|(ne?16:0)|(se?32:0)|(sw?64:0)|(nw?128:0);

            Vector2Int pos = GetSeamless9x8Position(cleanMask);
            DrawShape(tex, pos.x, pos.y, cleanMask, tileSize, fg);
        }

        // Dessiner la grille (1px par tuile)
        for (int x = 0; x < tex.width; x++)
        {
            for (int y = 0; y < tex.height; y++)
            {
                if (x % tileSize == 0 || y % tileSize == 0) tex.SetPixel(x, y, grid);
                // Bordures extérieures
                if (x == tex.width - 1 || y == tex.height - 1) tex.SetPixel(x, y, grid);
            }
        }

        tex.Apply();
        byte[] bytes = tex.EncodeToPNG();
        
        string dir = Application.dataPath + "/Art/Tilesets";
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        
        string path = dir + "/PerfectSeamless9x8.png";
        File.WriteAllBytes(path, bytes);
        AssetDatabase.Refresh();
        Debug.Log("[Evolution] Template de Tileset généré avec succès dans : " + path);
    }

    private static void DrawShape(Texture2D tex, int tx, int ty, int mask, int size, Color fg)
    {
        // Unity a le 0,0 en bas à gauche de la texture.
        // Notre code considère ty=0 comme la ligne du HAUT (Row 0). 
        // Donc on doit l'inverser pour dessiner sur "8" lignes (indice max 7) :
        int drawY = 7 - ty;
        
        int startX = tx * size;
        int startY = drawY * size;

        bool n = (mask & 1) != 0;
        bool e = (mask & 2) != 0;
        bool s = (mask & 4) != 0;
        bool w = (mask & 8) != 0;
        bool ne = (mask & 16) != 0;
        bool se = (mask & 32) != 0;
        bool sw = (mask & 64) != 0;
        bool nw = (mask & 128) != 0;
        
        // Marge pour afficher le bleu autour des formes (comme des bords)
        int margin = 8;
        int limit = size - margin; // 24

        // Dessiner la forme géométrique pure !
        for (int py = 0; py < size; py++)
        {
            for (int px = 0; px < size; px++)
            {
                bool isFg = false;
                
                // Zone centrale à la base
                if (px >= margin && px < limit && py >= margin && py < limit) isFg = true;

                // Extensions cardinales (Attention, y part du bas, donc y >= limit est le NORD)
                if (n && py >= limit && px >= margin && px < limit) isFg = true;
                if (s && py < margin && px >= margin && px < limit) isFg = true;
                if (e && px >= limit && py >= margin && py < limit) isFg = true;
                if (w && px < margin && py >= margin && py < limit) isFg = true;

                // Angles intérieurs :
                // S'ils s'étendent en Nord et Est, l'angle s'affiche SEULEMENT si "ne" est true !
                if (ne && px >= limit && py >= limit) isFg = true;
                if (nw && px < margin && py >= limit) isFg = true;
                if (se && px >= limit && py < margin) isFg = true;
                if (sw && px < margin && py < margin) isFg = true;
                
                // Dessiner par-dessus le fond
                if (isFg)
                {
                    tex.SetPixel(startX + px, startY + py, fg);
                }
            }
        }
    }

    /// <summary>
    /// Format Seamless personnalisé "9x8" ultra facile à dessiner :
    /// Organisé en blocs logiques (3x3 continu sans coins intérieurs, 3x3 avec tous les coins, etc.)
    /// </summary>
    private static Vector2Int GetSeamless9x8Position(int mask)
    {
        int ortho = mask & 15;
        int diag = mask >> 4;

        if (ortho == 0) return new Vector2Int(4, 4); // Island
        if (ortho == 1) return new Vector2Int(5, 4); // N cap
        if (ortho == 4) return new Vector2Int(6, 4); // S cap
        if (ortho == 2) return new Vector2Int(7, 4); // E cap
        if (ortho == 8) return new Vector2Int(8, 4); // W cap
        if (ortho == 5) return new Vector2Int(4, 5); // N, S (Vertical)
        if (ortho == 10) return new Vector2Int(5, 5); // E, W (Horizontal)

        // Block 1: 0 Diags 3x3 at (0,0) -> (2,2)
        if (ortho == 6 && diag == 0) return new Vector2Int(0, 0); // E,S
        if (ortho == 14 && diag == 0) return new Vector2Int(1, 0); // E,S,W
        if (ortho == 12 && diag == 0) return new Vector2Int(2, 0); // S,W
        if (ortho == 7 && diag == 0) return new Vector2Int(0, 1); // N,E,S
        if (ortho == 15 && diag == 0) return new Vector2Int(1, 1); // N,E,S,W (empty cross)
        if (ortho == 13 && diag == 0) return new Vector2Int(2, 1); // N,S,W
        if (ortho == 3 && diag == 0) return new Vector2Int(0, 2); // N,E
        if (ortho == 11 && diag == 0) return new Vector2Int(1, 2); // N,E,W
        if (ortho == 9 && diag == 0) return new Vector2Int(2, 2); // N,W

        // Block 2: Full Diags 3x3 at (4,0) -> (6,2)
        if (ortho == 6 && diag == 2) return new Vector2Int(4, 0); // SE
        if (ortho == 14 && diag == 6) return new Vector2Int(5, 0); // SE,SW
        if (ortho == 12 && diag == 4) return new Vector2Int(6, 0); // SW
        if (ortho == 7 && diag == 3) return new Vector2Int(4, 1); // NE,SE
        if (ortho == 15 && diag == 15) return new Vector2Int(5, 1); // All 4!
        if (ortho == 13 && diag == 12) return new Vector2Int(6, 1); // NW,SW
        if (ortho == 3 && diag == 1) return new Vector2Int(4, 2); // NE
        if (ortho == 11 && diag == 9) return new Vector2Int(5, 2); // NE,NW
        if (ortho == 9 && diag == 8) return new Vector2Int(6, 2); // NW

        // Block 4: Edges with 1 missing diag at (7,0) -> (8,3)
        if (ortho == 14 && diag == 2) return new Vector2Int(7, 0); // Top missing SW
        if (ortho == 14 && diag == 4) return new Vector2Int(8, 0); // Top missing SE
        if (ortho == 7 && diag == 1) return new Vector2Int(7, 1); // Left missing SE
        if (ortho == 7 && diag == 2) return new Vector2Int(8, 1); // Left missing NE
        if (ortho == 13 && diag == 8) return new Vector2Int(7, 2); // Right missing SW
        if (ortho == 13 && diag == 4) return new Vector2Int(8, 2); // Right missing NW
        if (ortho == 11 && diag == 1) return new Vector2Int(7, 3); // Bottom missing NW
        if (ortho == 11 && diag == 8) return new Vector2Int(8, 3); // Bottom missing NE

        // Block 3: Fully surrounded (ortho=15) missing diags
        if (ortho == 15) {
            switch (diag) {
                // 1 missing
                case 7: return new Vector2Int(0, 4); // NW missing
                case 14: return new Vector2Int(1, 4); // NE missing
                case 13: return new Vector2Int(2, 4); // SE missing
                case 11: return new Vector2Int(3, 4); // SW missing
                // 2 missing opp
                case 5: return new Vector2Int(0, 5); // NW, SE
                case 10: return new Vector2Int(1, 5); // NE, SW
                // 2 missing adj
                case 6: return new Vector2Int(2, 5); // NW, NE
                case 9: return new Vector2Int(3, 5); // SE, SW
                case 12: return new Vector2Int(0, 6); // NE, SE
                case 3: return new Vector2Int(1, 6); // NW, SW
                // 3 missing
                case 2: return new Vector2Int(2, 6); // SE remains
                case 4: return new Vector2Int(3, 6); // SW remains
                case 8: return new Vector2Int(0, 7); // NW remains
                case 1: return new Vector2Int(1, 7); // NE remains
            }
        }
        
        return new Vector2Int(4, 4); // Island par défaut
    }
}
