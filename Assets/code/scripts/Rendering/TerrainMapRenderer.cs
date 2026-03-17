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

    private void BuildTexture()
    {
        terrainTexture = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
        terrainTexture.filterMode = FilterMode.Point;
        terrainTexture.wrapMode = TextureWrapMode.Clamp;

        Color[] pixels = new Color[Width * Height];

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                float h = HeightMap[x, y];
                pixels[y * Width + x] = HeightToColour(h);
            }
        }

        terrainTexture.SetPixels(pixels);
        terrainTexture.Apply();
    }

    private Color HeightToColour(float h)
    {
        // Water band
        if (h < WaterThreshold * 0.6f)
            return DeepWater;
        if (h < WaterThreshold)
            return Color.Lerp(DeepWater, ShallowWater, Mathf.InverseLerp(WaterThreshold * 0.6f, WaterThreshold, h));

        // Land bands
        float land = Mathf.InverseLerp(WaterThreshold, 1f, h); // 0..1 within land range

        if (land < 0.05f)
            return Sand;
        if (land < 0.4f)
            return Color.Lerp(Grass, Forest, Mathf.InverseLerp(0.05f, 0.4f, land));
        if (land < 0.7f)
            return Color.Lerp(Forest, Rock, Mathf.InverseLerp(0.4f, 0.7f, land));

        return Color.Lerp(Rock, Snow, Mathf.InverseLerp(0.7f, 1f, land));
    }

    private void ApplyTexture()
    {
        if (DisplayTarget == null)
        {
            Debug.LogWarning("[TERRAIN] No DisplayTarget assigned.");
            return;
        }

        // Use sharedMaterial to avoid leaking materials
        var mat = DisplayTarget.sharedMaterial;
        if (mat == null) return;

        mat.mainTexture = terrainTexture;
        if (mat.HasProperty("_BaseMap"))
            mat.SetTexture("_BaseMap", terrainTexture);
    }

    private void OnDestroy()
    {
        if (terrainTexture != null)
            Destroy(terrainTexture);
    }
}
