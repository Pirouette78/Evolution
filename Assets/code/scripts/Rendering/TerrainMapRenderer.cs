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

                // Algorithme Godot 3x3 Minimal (Diagonales conditionnées aux bords)
                int mask = 0;
                if (n) mask |= 1;
                if (e) mask |= 4;
                if (s) mask |= 16;
                if (w) mask |= 64;
                if (ne && n && e) mask |= 2;
                if (se && s && e) mask |= 8;
                if (sw && s && w) mask |= 32;
                if (nw && n && w) mask |= 128;
                
                // Map the 0-255 mask to the user's specific 7x7 image coordinates
                Vector2Int localCoord = GetGodotImagePosition(mask);

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
    /// Format Godot 3x3 Minimal. Route la forme topologique directement sur les nombres
    /// imprimés dans l'image 7x7 de l'utilisateur (28, 116, 84 etc.).
    /// </summary>
    private Vector2Int GetGodotImagePosition(int mask)
    {
        switch (mask) {
            // Ligne 0
            case 28: return new Vector2Int(0, 0);
            case 116: return new Vector2Int(1, 0);
            case 84: return new Vector2Int(2, 0);
            case 92: return new Vector2Int(3, 0);
            case 124: return new Vector2Int(4, 0);
            case 112: return new Vector2Int(5, 0);
            case 16: return new Vector2Int(6, 0);
            
            // Ligne 1
            case 23: return new Vector2Int(0, 1);
            case 213: return new Vector2Int(1, 1);
            case 85: return new Vector2Int(2, 1);
            case 95: return new Vector2Int(3, 1);
            case 253: return new Vector2Int(5, 1);
            case 113: return new Vector2Int(6, 1);
            
            // Ligne 2
            case 21: return new Vector2Int(0, 2);
            case 93: return new Vector2Int(1, 2);
            case 125: return new Vector2Int(2, 2);
            case 119: return new Vector2Int(3, 2);
            case 215: return new Vector2Int(4, 2);
            case 199: return new Vector2Int(5, 2);
            case 209: return new Vector2Int(6, 2);

            // Ligne 3
            case 29: return new Vector2Int(0, 3);
            case 127: return new Vector2Int(1, 3);
            case 247: return new Vector2Int(2, 3);
            case 221: return new Vector2Int(3, 3);
            case 117: return new Vector2Int(4, 3);
            case 68: return new Vector2Int(5, 3);
            case 81: return new Vector2Int(6, 3);

            // Ligne 4
            case 31: return new Vector2Int(0, 4);
            case 255: return new Vector2Int(1, 4); // Apparaît aussi en (4,1), on garde (1,4)
            case 245: return new Vector2Int(2, 4);
            case 87: return new Vector2Int(3, 4);
            case 193: return new Vector2Int(4, 4);
            case 1: return new Vector2Int(6, 4);

            // Ligne 5
            case 7: return new Vector2Int(0, 5);
            case 223: return new Vector2Int(1, 5);
            case 241: return new Vector2Int(2, 5);
            case 17: return new Vector2Int(3, 5);
            case 0: return new Vector2Int(4, 5); // Tuile isolée (0) => case bleue dans l'image
            case 20: return new Vector2Int(5, 5);
            case 80: return new Vector2Int(6, 5);

            // Ligne 6
            case 4: return new Vector2Int(0, 6);
            case 71: return new Vector2Int(1, 6);
            case 197: return new Vector2Int(2, 6);
            case 69: return new Vector2Int(3, 6);
            case 64: return new Vector2Int(4, 6);
            case 5: return new Vector2Int(5, 6);
            case 65: return new Vector2Int(6, 6);
        }
        
        return new Vector2Int(4, 5); // Secours : Tuile Isolée (0)
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
