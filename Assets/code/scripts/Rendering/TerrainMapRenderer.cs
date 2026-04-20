using UnityEngine;

/// <summary>
/// Generates a Perlin noise terrain map, creates a visual texture, and stores walkability data.
/// Attach to the background Quad/Plane in the scene.
/// </summary>
public class TerrainMapRenderer : MonoBehaviour
{
    public static TerrainMapRenderer Instance { get; private set; }

    [Header("Map Dimensions")]
    public int Width = 2560;
    public int Height = 1440;

    [Header("Noise Settings")]
    public NoiseSettings Noise = new NoiseSettings();

    [Header("Terrain Thresholds")]
    [Range(0f, 1f)] public float WaterThreshold = 0.35f;

    // Seuils legacy — utilisés uniquement pour la texture visuelle de fond et le shading
    // Le rendu biome passe par la grille Température×Humidité
    const float SandThreshold   = 0.05f;
    const float GrassThreshold  = 0.40f;
    const float ForestThreshold = 0.70f;
    const float RockThreshold   = 0.95f;

    [Header("Biome Grid (Température x Humidité)")]
    public BiomeGrid Biomes = new BiomeGrid();

    [Header("Altitude Grid")]
    public AltitudeGrid AltitudeBiomes = new AltitudeGrid();
    [Range(0f, 1f)] public float AltitudeGridInfluence = 0f;
    [Range(0f,1f)] public float SnowAltitude = 0.85f;
    [Range(0f,1f)] public float SlopeRockMax = 0.30f;
    [Range(0f, 0.3f)] public float BeachWidth = 0.05f;

    [Header("Temperature Noise")]
    [Range(0.1f, 10f)] public float TemperatureScale  = 1.5f;
    public Vector2 TemperatureOffset = Vector2.zero;
    [Range(0f, 1f)] public float AltitudeTemperatureInfluence = 0.5f;
    [Range(0f, 1f)] public float LatitudeTemperatureInfluence = 0.5f;

    [Header("Humidity Noise")]
    [Range(0.1f, 10f)] public float HumidityScale = 1.2f;
    public Vector2 HumidityOffset = new Vector2(100f, 100f);

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
    private byte[]    rawWalkPixels;

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
        if (DisplayTarget != null)
        {
            DisplayTarget.transform.localScale = new Vector3(Width, Height, 1f);
            DisplayTarget.transform.position = new Vector3(Width / 2f, Height / 2f, DisplayTarget.transform.position.z);
        }
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

        if (rawWalkPixels == null || rawWalkPixels.Length != Width * Height)
            rawWalkPixels = new byte[Width * Height];

        for (int y = 0; y < Height; y++)
        {
            int rowOffset = y * Width;
            for (int x = 0; x < Width; x++)
            {
                rawWalkPixels[rowOffset + x] = WalkabilityGrid[x, y] ? (byte)255 : (byte)0;
            }
        }

        walkabilityTexture.SetPixelData(rawWalkPixels, 0);
        walkabilityTexture.Apply();
    }

    // ── Noise helpers ──────────────────────────────────────────────────────
    static float Rand2(float x, float y) =>
        Mathf.Abs(Mathf.Sin(Vector2.Dot(new Vector2(x, y), new Vector2(12.9898f, 78.233f))) * 43758.5453f % 1f);

    static float SmoothN(float x, float y)
    {
        float ix = Mathf.Floor(x), iy = Mathf.Floor(y);
        float fx = x - ix, fy = y - iy;
        float ux = fx * fx * (3f - 2f * fx), uy = fy * fy * (3f - 2f * fy);
        float a = Rand2(ix, iy), b = Rand2(ix+1, iy), c = Rand2(ix, iy+1), d = Rand2(ix+1, iy+1);
        return a + (b-a)*ux + (c-a)*uy + (a-b-c+d)*ux*uy;
    }

    float FBM(float x, float y)
    {
        float n  = SmoothN(x, y)               * 0.500f;
              n += SmoothN(x*2.03f+1.7f, y*2.03f+9.2f) * 0.250f;
              n += SmoothN(x*4.01f+8.3f, y*4.01f+2.8f) * 0.125f;
              n += SmoothN(x*8.05f+3.1f, y*8.05f+6.4f) * 0.125f;
        return Mathf.Clamp01(n);
    }

    float FBMCustom(float x, float y, int octaves, float persistence, float lacunarity)
    {
        float value = 0f, amplitude = 1f, freq = 1f, maxVal = 0f;
        for (int o = 0; o < octaves; o++)
        {
            value  += SmoothN(x * freq + o * 31.7f, y * freq + o * 17.3f) * amplitude;
            maxVal += amplitude;
            amplitude *= persistence;
            freq      *= lacunarity;
        }
        return Mathf.Clamp01(value / maxVal);
    }

    float GetTemperature(int x, int y)
    {
        float baseTemp = FBM(x / (float)Width * TemperatureScale + TemperatureOffset.x,
                             y / (float)Height * TemperatureScale + TemperatureOffset.y);
        float altitude = HeightMap[x, y];
        // Latitude : 0=équateur (chaud), 1=pôles (froid) — symétrique par rapport au centre
        float latNorm = Mathf.Abs((y / (float)Height) - 0.5f) * 2f; // 0 au centre, 1 aux bords
        // (0.5 - latNorm) : +0.5 à l'équateur, -0.5 aux pôles → chaud au centre, froid aux bords
        return Mathf.Clamp01(baseTemp - altitude * AltitudeTemperatureInfluence
                                      + (0.5f - latNorm) * LatitudeTemperatureInfluence);
    }

    float GetHumidity(int x, int y) =>
        FBM(x / (float)Width * HumidityScale + HumidityOffset.x,
            y / (float)Height * HumidityScale + HumidityOffset.y);

    // Méthodes publiques pour l'éditeur debug
    public float GetTemperaturePublic(int x, int y) => GetTemperature(x, y);
    public float GetHumidityPublic(int x, int y)    => GetHumidity(x, y);
    public int   GetBiomePublic(int x, int y)       => GetBiome(HeightMap[x,y], GetTemperature(x,y), GetHumidity(x,y), GetSlope(x,y), x, y);

    public void SetDebugTexture(Texture2D tex)
    {
        if (DisplayTarget != null)
        {
            var mat = DisplayTarget.sharedMaterial;
            if (mat != null) mat.mainTexture = tex;
        }
    }

    public void RestoreNormalView()
    {
        if (DisplayTarget != null)
        {
            var mat = DisplayTarget.sharedMaterial;
            if (mat != null) mat.mainTexture = GetTexture();
        }
    }

    float GetSlope(int x, int y)
    {
        float hL = HeightMap[Mathf.Max(0, x-1), y], hR = HeightMap[Mathf.Min(Width-1, x+1), y];
        float hD = HeightMap[x, Mathf.Max(0, y-1)], hU = HeightMap[x, Mathf.Min(Height-1, y+1)];
        return Mathf.Clamp01(Mathf.Sqrt((hR-hL)*(hR-hL) + (hU-hD)*(hU-hD)) * 4f);
    }

    // Température canonique par biome (0=froid, 1=chaud) — pour l'influence continue de l'altitude
    static readonly float[] BiomeCanonicalTemp = { 0.5f, 0.85f, 0.65f, 0.40f, 0.15f, 0.05f };

    int GetBiome(float h, float temp, float humidity, float slope, int x = 0, int y = 0)
    {
        if (h < WaterThreshold) return 0;
        float land = Mathf.InverseLerp(WaterThreshold, 1f, h);
        if (land > SnowAltitude) return 5;

        // L'altitude grid modifie la température d'entrée de façon continue
        if (AltitudeGridInfluence > 0f)
        {
            float landNorm = Mathf.Clamp01(land / SnowAltitude);
            int aIdx = Mathf.Clamp((int)(landNorm * AltitudeGrid.Size), 0, AltitudeGrid.Size - 1);
            int altBiome = Mathf.Clamp(AltitudeBiomes.Get(aIdx), 0, 5);
            float targetTemp = BiomeCanonicalTemp[altBiome];
            temp = Mathf.Lerp(temp, targetTemp, AltitudeGridInfluence);
        }

        int tIdx = Mathf.Clamp((int)(temp     * BiomeGrid.Size), 0, BiomeGrid.Size - 1);
        int hIdx = Mathf.Clamp((int)(humidity * BiomeGrid.Size), 0, BiomeGrid.Size - 1);
        int gridBiome = Mathf.Clamp(Biomes.Get(tIdx, hIdx), 0, 5);

        // Plage : sable si proche de l'eau ET grille ne donne pas un biome froid (roche/neige)
        // BeachWidth contrôle la largeur, la grille peut l'overrider avec roche/neige
        if (land < BeachWidth && gridBiome != 4 && gridBiome != 5)
        {
            float beachStrength = 1f - (land / BeachWidth); // 1 au bord, 0 à BeachWidth
            if (beachStrength > 0.5f) return 1; // sable
        }

        return gridBiome;
    }

    private int GetTerrainType(float h)
    {
        if (h < WaterThreshold) return 0;
        float land = Mathf.InverseLerp(WaterThreshold, 1f, h);
        if (land < SandThreshold)   return 1;
        if (land < GrassThreshold)  return 2;
        if (land < ForestThreshold) return 3;
        if (land < RockThreshold)   return 4;
        return 5;
    }

    private float GetLocalBiomeHeight(float h)
    {
        if (h < WaterThreshold) return Mathf.InverseLerp(0f, WaterThreshold, h); // Water
        
        float land = Mathf.InverseLerp(WaterThreshold, 1f, h);
        if (land < 0.05f) return Mathf.InverseLerp(0f, 0.05f, land); // Sand
        if (land < 0.4f)  return Mathf.InverseLerp(0.05f, 0.4f, land); // Grass
        if (land < 0.7f)  return Mathf.InverseLerp(0.4f, 0.7f, land); // Forest
        if (land < 0.95f) return Mathf.InverseLerp(0.7f, 0.95f, land); // Rock
        return Mathf.InverseLerp(0.95f, 1f, land); // Snow
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
        mapDataTexture.filterMode = FilterMode.Bilinear;
        mapDataTexture.wrapMode = TextureWrapMode.Clamp;

        Color[] displayPixels = new Color[Width * Height];
        Color32[] dataPixels = new Color32[Width * Height];

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                // Visual — couleur du biome calculé
                float h = HeightMap[x, y];
                float tempV  = GetTemperature(x, y);
                float humidV = GetHumidity(x, y);
                float slopeV = GetSlope(x, y);
                int biomeV = GetBiome(h, tempV, humidV, slopeV, x, y);
                biomeV = Mathf.Clamp(biomeV, 0, BiomeGrid.BiomeColors.Length - 1);
                displayPixels[y * Width + x] = BiomeGrid.BiomeColors[biomeV];

                // Data (R = biome ID, G = hauteur, B = temp, A = humid)
                dataPixels[y * Width + x] = new Color32(
                    (byte)biomeV,
                    (byte)Mathf.Clamp(h       * 255f, 0f, 255f),
                    (byte)Mathf.Clamp(tempV   * 255f, 0f, 255f),
                    (byte)Mathf.Clamp(humidV  * 255f, 0f, 255f));
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

        RebuildWalkabilityTexture();

        Debug.Log("[TERRAIN] Walkability texture & Auto-Tiling Data generated.");
    }

    private Color HeightToColour(float h)
    {
        if (h < WaterThreshold * 0.6f) return DeepWater;
        if (h < WaterThreshold) return Color.Lerp(DeepWater, ShallowWater, Mathf.InverseLerp(WaterThreshold * 0.6f, WaterThreshold, h));

        float land = Mathf.InverseLerp(WaterThreshold, 1f, h);

        if (land < SandThreshold)   return Sand;
        if (land < GrassThreshold)  return Color.Lerp(Grass, Forest, Mathf.InverseLerp(SandThreshold, GrassThreshold, land));
        if (land < ForestThreshold) return Color.Lerp(Forest, Rock,  Mathf.InverseLerp(GrassThreshold, ForestThreshold, land));
        return Color.Lerp(Rock, Snow, Mathf.InverseLerp(ForestThreshold, 1f, land));
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
            var mat = ZoomLevelController.Instance.TerrainOverlayMaterial;
            mat.SetTexture("_MainTex", mapDataTexture);
            mat.SetFloat("_WaterThreshold",  WaterThreshold);
            mat.SetFloat("_SandThreshold",   SandThreshold);
            mat.SetFloat("_GrassThreshold",  GrassThreshold);
            mat.SetFloat("_ForestThreshold", ForestThreshold);
            mat.SetFloat("_RockThreshold",   RockThreshold);
            mat.SetVector("_MapSize",      new Vector4(Width, Height, 0, 0));
            mat.SetVector("_WaterOffset",  new Vector4( 0, 0, 0, 0));
            mat.SetVector("_SandOffset",   new Vector4( 7, 0, 0, 0));
            mat.SetVector("_GrassOffset",  new Vector4(14, 0, 0, 0));
            mat.SetVector("_ForestOffset", new Vector4(21, 0, 0, 0));
            mat.SetVector("_RockOffset",   new Vector4(28, 0, 0, 0));
            mat.SetVector("_SnowOffset",   new Vector4(35, 0, 0, 0));
            mat.SetVector("_CliffOffset",  new Vector4( 0, 7, 0, 0));

            // Grille de biomes → texture 10x10, R = biomeID / 7.0
            var gridTex = new Texture2D(10, 10, TextureFormat.R8, false, true); // linear=true, pas de correction gamma
            gridTex.filterMode = FilterMode.Point;
            gridTex.wrapMode   = TextureWrapMode.Clamp;
            var gridPixels = new Color[100];
            for (int gi = 0; gi < 100; gi++)
                gridPixels[gi] = new Color(Mathf.Clamp(Biomes.cells[gi], 0, 5) / 5f, 0, 0, 1);
            gridTex.SetPixels(gridPixels);
            gridTex.Apply();
            mat.SetTexture("_BiomeGridTex", gridTex);

            // Altitude grid → texture 10x1
            var altTex = new Texture2D(10, 1, TextureFormat.R8, false, true);
            altTex.filterMode = FilterMode.Point;
            altTex.wrapMode   = TextureWrapMode.Clamp;
            var altPixels = new Color[10];
            for (int ai = 0; ai < 10; ai++)
                altPixels[ai] = new Color(Mathf.Clamp(AltitudeBiomes.cells[ai], 0, 5) / 5f, 0, 0, 1);
            altTex.SetPixels(altPixels);
            altTex.Apply();
            mat.SetTexture("_AltitudeGridTex", altTex);
            mat.SetFloat("_AltitudeGridInfluence", AltitudeGridInfluence);
            mat.SetFloat("_SnowAltitude", SnowAltitude);
            mat.SetFloat("_SlopeRockMax", SlopeRockMax);
            mat.SetFloat("_BeachWidth",   BeachWidth);
            mat.SetFloat("_WaterThreshold", WaterThreshold);
        }
    }

    private void OnDestroy()
    {
        if (terrainTexture != null)     Destroy(terrainTexture);
        if (mapDataTexture != null)     Destroy(mapDataTexture);
        if (walkabilityTexture != null) Destroy(walkabilityTexture);
    }
}
