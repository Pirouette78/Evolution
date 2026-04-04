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
    /// Grille de marchabilité combinée (terrain + unités bloquantes). True = passable.
    /// Utilisée par Dijkstra pour les flow fields. Dimensions: [Width, Height].
    /// </summary>
    public bool[,] WalkabilityGrid { get; private set; }

    // Compteur de blocage par unité par cellule (pour gérer chevauchements).
    // Une valeur > 0 signifie qu'au moins une unité bloque cette cellule.
    private int[,] unitBlockingGrid;

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

    /// <summary>
    /// Position (en terrain tiles) du coin bas-gauche du chunk actif dans le monde.
    /// (0,0) par défaut jusqu'à ce que GenerateMapForChunk() soit appelé.
    /// </summary>
    public Vector2Int ChunkOrigin { get; private set; } = Vector2Int.zero;

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
        DestroyTextures();
        BuildTexture();
        ApplyTexture();
        Debug.Log($"[TERRAIN] Map generated ({Width}x{Height}), WaterThreshold={WaterThreshold}");
    }

    /// <summary>
    /// Génère le terrain pour le chunk (cx, cy) en décalant le bruit Perlin.
    /// Chaque chunk a un terrain unique et continu avec ses voisins.
    /// </summary>
    public void GenerateMapForChunk(int cx, int cy)
    {
        ChunkOrigin = new Vector2Int(cx * Width, cy * Height);
        Noise.Validate();
        // Copier les settings et ajouter l'offset du chunk dans l'espace Perlin.
        // L'offset en tiles = cx*Width/cy*Height garantit la continuité aux bords.
        var chunkNoise = new NoiseSettings
        {
            Scale      = Noise.Scale,
            Octaves    = Noise.Octaves,
            Persistance= Noise.Persistance,
            Lacunarity = Noise.Lacunarity,
            Seed       = Noise.Seed,
            Offset     = Noise.Offset + new Vector2(cx * Width, cy * Height)
        };
        HeightMap = NoiseMapGenerator.Generate(Width, Height, chunkNoise);
        BuildWalkabilityGrid();
        DestroyTextures();
        BuildTexture();
        ApplyTexture();
        Debug.Log($"[TERRAIN] Chunk ({cx},{cy}) generated at world origin {ChunkOrigin}");
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
        WalkabilityGrid  = new bool[Width, Height];
        unitBlockingGrid = new int[Width, Height];
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                WalkabilityGrid[x, y] = HeightMap[x, y] >= WaterThreshold;
    }

    /// <summary>
    /// Marque ou démarque un disque de cellules comme bloqué par une unité statique.
    /// N'affecte pas les cellules déjà infranchissables (eau/hors-carte).
    /// Appeler RebuildWalkabilityTexture() après pour propager au GPU.
    /// </summary>
    public void SetUnitBlock(int cx, int cy, int radius, bool add)
    {
        if (unitBlockingGrid == null) return;
        int delta = add ? 1 : -1;
        for (int dy = -radius; dy <= radius; dy++)
        for (int dx = -radius; dx <= radius; dx++)
        {
            if (radius > 0 && dx * dx + dy * dy > radius * radius) continue;
            int x = cx + dx, y = cy + dy;
            if (x < 0 || x >= Width || y < 0 || y >= Height) continue;
            if (HeightMap[x, y] < WaterThreshold) continue; // l'eau reste l'eau
            unitBlockingGrid[x, y] = Mathf.Max(0, unitBlockingGrid[x, y] + delta);
            WalkabilityGrid[x, y]  = unitBlockingGrid[x, y] == 0;
        }
    }

    /// <summary>
    /// Marque/démarque un rectangle de cellules comme bloqué par une unité statique.
    /// cx/cy = position de l'ancre en pixels sim.
    /// offX/offY = offset de l'angle bas-gauche du rect depuis l'ancre, en pixels sim.
    /// w/h = dimensions du rect en pixels sim.
    /// Appeler RebuildWalkabilityTexture() après pour propager au GPU.
    /// </summary>
    public void SetUnitBlockRect(int cx, int cy, int offX, int offY, int w, int h, bool add)
    {
        if (unitBlockingGrid == null || w <= 0 || h <= 0) return;
        int delta  = add ? 1 : -1;
        int startX = cx + offX;
        int startY = cy + offY;
        for (int y = startY; y < startY + h; y++)
        for (int x = startX; x < startX + w; x++)
        {
            if (x < 0 || x >= Width || y < 0 || y >= Height) continue;
            if (HeightMap[x, y] < WaterThreshold) continue;
            unitBlockingGrid[x, y] = Mathf.Max(0, unitBlockingGrid[x, y] + delta);
            WalkabilityGrid[x, y]  = unitBlockingGrid[x, y] == 0;
        }
    }

    /// <summary>
    /// Reconstruit la texture de walkabilité depuis WalkabilityGrid et l'envoie au GPU.
    /// À appeler après un ou plusieurs SetUnitBlock().
    /// </summary>
    public void RebuildWalkabilityTexture()
    {
        if (walkabilityTexture == null) return;
        Color[] walkPixels = new Color[Width * Height];
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                walkPixels[y * Width + x] = WalkabilityGrid[x, y] ? Color.white : Color.black;
        walkabilityTexture.SetPixels(walkPixels);
        walkabilityTexture.Apply();
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

        // Texture DATA (Auto-tiling Multi-Layers)
        // Format R8 est idéal (1 octet par pixel) mais RGBA32 est universel. On utilisera juste le canal R = Type de Terrain
        mapDataTexture = new Texture2D(Width, Height, TextureFormat.RGBA32, false, true); 
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

                // Data (R = Terrain Type Brut)
                int type = types[x, y];
                dataPixels[y * Width + x] = new Color32((byte)type, 0, 0, 255);
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

    private void DestroyTextures()
    {
        if (terrainTexture != null)     { Destroy(terrainTexture);     terrainTexture     = null; }
        if (mapDataTexture != null)     { Destroy(mapDataTexture);     mapDataTexture     = null; }
        if (walkabilityTexture != null) { Destroy(walkabilityTexture); walkabilityTexture = null; }
    }

    // ── Chunk streaming helpers ──────────────────────────────────────

    /// <summary>
    /// Generates heightmap + walkability for chunk (cx, cy). Entirely thread-safe — no Unity API.
    /// Safe to call from Task.Run(). noiseCopy must be a detached copy (not the live Noise field).
    /// </summary>
    public static (float[,] heightMap, bool[,] walkability) GenerateChunkDataRaw(
        int cx, int cy, int width, int height, NoiseSettings noiseCopy, float waterThreshold)
    {
        var chunkNoise = new NoiseSettings
        {
            Scale       = noiseCopy.Scale,
            Octaves     = noiseCopy.Octaves,
            Persistance = noiseCopy.Persistance,
            Lacunarity  = noiseCopy.Lacunarity,
            Seed        = noiseCopy.Seed,
            Offset      = noiseCopy.Offset + new Vector2(cx * width, cy * height)
        };
        float[,] hm = NoiseMapGenerator.Generate(width, height, chunkNoise);
        bool[,]  wk = new bool[width, height];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width;  x++)
            wk[x, y] = hm[x, y] >= waterThreshold;
        return (hm, wk);
    }

    /// <summary>
    /// Applies pre-computed height/walkability data (from a background thread) to this renderer.
    /// Only rebuilds textures on the main thread — much faster than a full GenerateMapForChunk().
    /// </summary>
    public void ApplyPrecomputedChunkData(int cx, int cy, float[,] heightMap, bool[,] walkability)
    {
        ChunkOrigin      = new Vector2Int(cx * Width, cy * Height);
        HeightMap        = heightMap;
        WalkabilityGrid  = walkability;
        unitBlockingGrid = new int[Width, Height];
        DestroyTextures();
        BuildTexture();
        ApplyTexture();
        Debug.Log($"[TERRAIN] Chunk ({cx},{cy}) applied from pre-computed data, origin={ChunkOrigin}.");
    }

    /// <summary>
    /// Builds a low-resolution preview Texture2D using the same colour ramp as the main terrain.
    /// Uses nearest-neighbour sampling. Caller owns and must Destroy the returned texture.
    /// </summary>
    public Texture2D BuildLowResTexture(float[,] srcHeightMap, int srcW, int srcH, int texRes)
    {
        var tex = new Texture2D(texRes, texRes, TextureFormat.RGB24, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode   = TextureWrapMode.Clamp
        };
        var pixels = new Color[texRes * texRes];
        for (int y = 0; y < texRes; y++)
        for (int x = 0; x < texRes; x++)
        {
            int sx = Mathf.Clamp(x * srcW / texRes, 0, srcW - 1);
            int sy = Mathf.Clamp(y * srcH / texRes, 0, srcH - 1);
            pixels[y * texRes + x] = HeightToColour(srcHeightMap[sx, sy]);
        }
        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    private void OnDestroy()
    {
        DestroyTextures();
    }
}
