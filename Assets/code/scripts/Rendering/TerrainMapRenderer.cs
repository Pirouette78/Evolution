using UnityEngine;

/// <summary>
/// Generates a Perlin noise terrain map, creates a visual texture, and stores walkability data.
/// Attach to the background Quad/Plane in the scene.
/// </summary>
public class TerrainMapRenderer : MonoBehaviour
{
    public static TerrainMapRenderer Instance { get; private set; }

    [Header("Map Dimensions")]
    public int Width = 512;
    public int Height = 512;

    [Header("Noise Settings")]
    public NoiseSettings Noise = new NoiseSettings();

    [Header("Terrain Thresholds")]
    [Range(0f, 1f)] public float WaterThreshold = 0.35f;

    [Header("Output")]
    public MeshRenderer DisplayTarget;

    // --- Colour palette for height bands ---
    [Header("Terrain Colours")]
    public Color DeepWater   = new Color(0.05f, 0.15f, 0.40f);
    public Color ShallowWater = new Color(0.10f, 0.35f, 0.60f);
    public Color Sand         = new Color(0.76f, 0.70f, 0.50f);
    public Color Grass        = new Color(0.22f, 0.55f, 0.22f);
    public Color Forest       = new Color(0.13f, 0.37f, 0.13f);
    public Color Rock         = new Color(0.45f, 0.40f, 0.35f);
    public Color Snow         = new Color(0.95f, 0.95f, 0.97f);

    /// <summary>
    /// The generated walkability grid. True = passable.
    /// Dimensions: [Width, Height].
    /// </summary>
    public bool[,] WalkabilityGrid { get; private set; }

    /// <summary>
    /// The raw noise map values [0..1]. Dimensions: [Width, Height].
    /// </summary>
    public float[,] HeightMap { get; private set; }

    private Texture2D terrainTexture;
    private Texture2D mapDataTexture;
    private Texture2D walkabilityTexture;  // White = land, Black = water

    public Texture2D GetTexture()             => terrainTexture;
    public Texture2D GetMapDataTexture()      => mapDataTexture;
    public Texture2D GetWalkabilityTexture()  => walkabilityTexture;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (DisplayTarget == null)
            DisplayTarget = GetComponent<MeshRenderer>();
    }

    private void Start()
    {
        GenerateMap();
    }

    /// <summary>
    /// Generate the noise map, walkability grid, and visual texture.
    /// </summary>
    public void GenerateMap()
    {
        Noise.Validate();
        HeightMap = NoiseMapGenerator.Generate(Width, Height, Noise);
        BuildWalkabilityGrid();
        BuildTexture();
        ApplyTexture();
        Debug.Log($"[TERRAIN] Map generated ({Width}x{Height}), WaterThreshold={WaterThreshold}");
    }

    /// <summary>
    /// Check whether a world position is on a walkable tile.
    /// </summary>
    public bool IsWalkable(float x, float y)
    {
        int ix = (int)x;
        int iy = (int)y;
        if (ix < 0 || ix >= Width || iy < 0 || iy >= Height)
            return false;
        return WalkabilityGrid[ix, iy];
    }

    // ------------------------------------------------------------------

    private void BuildWalkabilityGrid()
    {
        WalkabilityGrid = new bool[Width, Height];
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                WalkabilityGrid[x, y] = HeightMap[x, y] >= WaterThreshold;
            }
        }
    }

    private int GetTerrainType(float h)
    {
        if (h < WaterThreshold) return 0; // Water
        
        float land = Mathf.InverseLerp(WaterThreshold, 1f, h);
        if (land < 0.05f) return 1; // Sand
        if (land < 0.4f) return 2; // Grass
        if (land < 0.7f) return 3; // Forest
        if (land < 0.95f) return 4; // Rock
        return 5; // Snow
    }

    private void BuildTexture()
    {
        int[,] types = new int[Width, Height];
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                types[x, y] = GetTerrainType(HeightMap[x, y]);
            }
        }

        // Texture VISUELLE (Couleurs)
        terrainTexture = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
        terrainTexture.filterMode = FilterMode.Point;
        terrainTexture.wrapMode = TextureWrapMode.Clamp;

        // Texture DATA (Auto-tiling)
        mapDataTexture = new Texture2D(Width, Height, TextureFormat.RGBA32, false, true); // true = linear space !
        mapDataTexture.filterMode = FilterMode.Point;
        mapDataTexture.wrapMode = TextureWrapMode.Clamp;

        Color[] displayPixels = new Color[Width * Height];
        Color32[] dataPixels = new Color32[Width * Height];

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                // Visual
                float h = HeightMap[x, y];
                displayPixels[y * Width + x] = HeightToColour(h);

                // Data (8-bit masking)
                int type = types[x, y];
                
                bool n = y < Height-1 && types[x, y+1] == type;
                bool e = x < Width-1 && types[x+1, y] == type;
                bool s = y > 0 && types[x, y-1] == type;
                bool w = x > 0 && types[x-1, y] == type;
                bool ne = n && e && (x < Width-1 && y < Height-1 && types[x+1, y+1] == type);
                bool se = s && e && (x < Width-1 && y > 0 && types[x+1, y-1] == type);
                bool sw = s && w && (x > 0 && y > 0 && types[x-1, y-1] == type);
                bool nw = n && w && (x > 0 && y < Height-1 && types[x-1, y+1] == type);

                int mask = (n?1:0) | (e?2:0) | (s?4:0) | (w?8:0) | (ne?16:0) | (se?32:0) | (sw?64:0) | (nw?128:0);
                
                // Map the 0-255 mask to the 12x5 block coordinates!
                Vector2Int localCoord = GetBlob12x5Position(mask);

                // R = Type de Terrain, G = Ligne Local X, B = Colonne Local Y
                dataPixels[y * Width + x] = new Color32((byte)type, (byte)localCoord.x, (byte)localCoord.y, 255);
            }
        }

        terrainTexture.SetPixels(displayPixels);
        terrainTexture.Apply();

        mapDataTexture.SetPixels32(dataPixels);
        mapDataTexture.Apply();

        // --- Binary walkability texture (R8: 1.0 = land, 0.0 = water) ---
        walkabilityTexture = new Texture2D(Width, Height, TextureFormat.R8, false, true);
        walkabilityTexture.filterMode = FilterMode.Point;
        walkabilityTexture.wrapMode   = TextureWrapMode.Clamp;

        Color[] walkPixels = new Color[Width * Height];
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                walkPixels[y * Width + x] = WalkabilityGrid[x, y] ? Color.white : Color.black;

        walkabilityTexture.SetPixels(walkPixels);
        walkabilityTexture.Apply();
        Debug.Log("[TERRAIN] Walkability texture & Auto-Tiling Data generated.");
    }

    /// <summary>
    /// Convertit un masque à 8 bits (256 valeurs) en coordonnée X,Y de votre Atlas 12x5 Blob.
    /// Modifier ici si une bordure ne correspond pas exactement à votre dessin dans le PNG !
    /// </summary>
    private Vector2Int GetBlob12x5Position(int mask)
    {
        int ortho = mask & 15;
        int diag = mask >> 4;

        if (ortho == 0) return new Vector2Int(0, 4); // Ile
        if (ortho == 1) return new Vector2Int(0, 3); // N
        if (ortho == 4) return new Vector2Int(0, 0); // S
        if (ortho == 5) return new Vector2Int(0, 1); // N, S
        if (ortho == 2) return new Vector2Int(1, 4); // E
        if (ortho == 8) return new Vector2Int(4, 4); // W
        if (ortho == 10) return new Vector2Int(2, 4); // E, W

        // Coins extérieurs (et avec leur diag pleine correspondante)
        if (ortho == 3) return diag == 1 ? new Vector2Int(5, 3) : new Vector2Int(1, 3); // N, E
        if (ortho == 6) return diag == 2 ? new Vector2Int(5, 0) : new Vector2Int(1, 0); // S, E
        if (ortho == 12) return diag == 4 ? new Vector2Int(8, 0) : new Vector2Int(4, 0); // S, W
        if (ortho == 9) return diag == 8 ? new Vector2Int(8, 3) : new Vector2Int(4, 3); // N, W

        // Lignes droites (avec diags intérieures pleines)
        if (ortho == 7) { // N, E, S
            if (diag == 0) return new Vector2Int(1, 1);
            if (diag == 1) return new Vector2Int(5, 1);
            if (diag == 2) return new Vector2Int(5, 2);
            if (diag == 3) return new Vector2Int(6, 1); // Ligne totalement pleine à gauche
        }
        if (ortho == 14) { // E, S, W
            if (diag == 0) return new Vector2Int(2, 0);
            if (diag == 2) return new Vector2Int(6, 0); // Ligne totalement pleine en haut
            if (diag == 4) return new Vector2Int(7, 0);
            if (diag == 6) return new Vector2Int(6, 2);
        }
        if (ortho == 13) { // N, S, W
            if (diag == 0) return new Vector2Int(4, 1);
            if (diag == 4) return new Vector2Int(8, 1);
            if (diag == 8) return new Vector2Int(8, 2);
            if (diag == 12) return new Vector2Int(7, 2); // Ligne totalement pleine à droite
        }
        if (ortho == 11) { // N, E, W
            if (diag == 0) return new Vector2Int(2, 3);
            if (diag == 1) return new Vector2Int(7, 3);
            if (diag == 8) return new Vector2Int(6, 3); // Ligne totalement pleine en bas
            if (diag == 9) return new Vector2Int(7, 1);
        }

        // Complètement entouré en orthogonal (N, E, S, W) avec X diagonales manquantes
        if (ortho == 15) {
            switch (diag) {
                case 15: return new Vector2Int(6, 1); // PLEIN, BORDURE INVISIBLE (Tuile centrale 100% pleine !)
                // 1 coin interne manquant
                case 14: return new Vector2Int(9, 0); // Manque NE
                case 13: return new Vector2Int(11, 0); // Manque SE
                case 11: return new Vector2Int(11, 2); // Manque SW
                case 7: return new Vector2Int(9, 2); // Manque NW
                // 2 coins internes manquants (opposés)
                case 10: return new Vector2Int(10, 0); // Manque NE, SW
                case 5: return new Vector2Int(10, 2); // Manque SE, NW
                // 2 coins internes manquants (adjacents)
                case 12: return new Vector2Int(9, 1); // Manque NE, SE
                case 9: return new Vector2Int(11, 1); // Manque SE, SW
                case 3: return new Vector2Int(11, 3); // Manque SW, NW
                case 6: return new Vector2Int(9, 3); // Manque NW, NE
                // 3 coins internes manquants
                case 8: return new Vector2Int(10, 3); // Reste SE ? (Ici c'est arbitraire, modifiez selon votre image)
                case 4: return new Vector2Int(10, 1); 
                case 2: return new Vector2Int(2, 2); 
                case 1: return new Vector2Int(3, 2); 
                // 4 coins internes manquants !
                case 0: return new Vector2Int(2, 1); 
            }
        }

        return new Vector2Int(0, 4); // Secours
    }

    private Color HeightToColour(float h)
    {
        // Water band
        if (h < WaterThreshold * 0.6f) return DeepWater;
        if (h < WaterThreshold) return Color.Lerp(DeepWater, ShallowWater, Mathf.InverseLerp(WaterThreshold * 0.6f, WaterThreshold, h));

        // Land bands
        float land = Mathf.InverseLerp(WaterThreshold, 1f, h);

        if (land < 0.05f) return Sand;
        if (land < 0.4f) return Color.Lerp(Grass, Forest, Mathf.InverseLerp(0.05f, 0.4f, land));
        if (land < 0.7f) return Color.Lerp(Forest, Rock, Mathf.InverseLerp(0.4f, 0.7f, land));

        return Color.Lerp(Rock, Snow, Mathf.InverseLerp(0.7f, 1f, land));
    }

    private void ApplyTexture()
    {
        if (DisplayTarget != null)
        {
            var mat = DisplayTarget.sharedMaterial;
            if (mat != null)
            {
                mat.mainTexture = terrainTexture;
                if (mat.HasProperty("_BaseMap"))
                    mat.SetTexture("_BaseMap", terrainTexture);
            }
        }
        else
        {
            Debug.LogWarning("[TERRAIN] No DisplayTarget assigned for original background.");
        }

        // --- On donne la Map Data (Texture de calculs) au Shader d'Auto-Tiling ! ---
        if (ZoomLevelController.Instance != null && ZoomLevelController.Instance.TerrainOverlayMaterial != null)
        {
            ZoomLevelController.Instance.TerrainOverlayMaterial.SetTexture("_MainTex", mapDataTexture);
        }
    }

    private void OnDestroy()
    {
        if (terrainTexture != null)     Destroy(terrainTexture);
        if (mapDataTexture != null)     Destroy(mapDataTexture);
        if (walkabilityTexture != null) Destroy(walkabilityTexture);
    }
}
