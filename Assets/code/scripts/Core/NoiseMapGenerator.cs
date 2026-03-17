using UnityEngine;

/// <summary>
/// Static utility that generates a 2D Perlin noise map.
/// Inspired by the SurvivalEcosystem NoiseMapGenerator.
/// </summary>
public static class NoiseMapGenerator
{
    /// <summary>
    /// Generate a noise map with values normalised to [0, 1].
    /// </summary>
    public static float[,] Generate(int width, int height, NoiseSettings settings)
    {
        float[,] noiseMap = new float[width, height];

        System.Random prng = new System.Random(settings.Seed);
        Vector2[] octaveOffsets = new Vector2[settings.Octaves];

        float maxPossibleHeight = 0f;
        float amplitude = 1f;

        for (int i = 0; i < settings.Octaves; i++)
        {
            float offsetX = prng.Next(-100000, 100000) + settings.Offset.x;
            float offsetY = prng.Next(-100000, 100000) + settings.Offset.y;
            octaveOffsets[i] = new Vector2(offsetX, offsetY);

            maxPossibleHeight += amplitude;
            amplitude *= settings.Persistance;
        }

        float halfWidth = width / 2f;
        float halfHeight = height / 2f;

        float maxLocal = float.MinValue;
        float minLocal = float.MaxValue;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                amplitude = 1f;
                float frequency = 1f;
                float noiseHeight = 0f;

                for (int i = 0; i < settings.Octaves; i++)
                {
                    float sampleX = (x - halfWidth + octaveOffsets[i].x) / settings.Scale * frequency;
                    float sampleY = (y - halfHeight + octaveOffsets[i].y) / settings.Scale * frequency;

                    float perlinValue = Mathf.PerlinNoise(sampleX, sampleY) * 2f - 1f;
                    noiseHeight += perlinValue * amplitude;

                    amplitude *= settings.Persistance;
                    frequency *= settings.Lacunarity;
                }

                if (noiseHeight > maxLocal) maxLocal = noiseHeight;
                if (noiseHeight < minLocal) minLocal = noiseHeight;

                noiseMap[x, y] = noiseHeight;
            }
        }

        // Normalise to [0, 1]
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                noiseMap[x, y] = Mathf.InverseLerp(minLocal, maxLocal, noiseMap[x, y]);
            }
        }

        return noiseMap;
    }
}

[System.Serializable]
public class NoiseSettings
{
    public float Scale = 50f;
    public int Octaves = 6;
    [Range(0f, 1f)] public float Persistance = 0.5f;
    public float Lacunarity = 2f;
    public int Seed = 42;
    public Vector2 Offset = Vector2.zero;

    public void Validate()
    {
        Scale = Mathf.Max(Scale, 0.01f);
        Octaves = Mathf.Max(Octaves, 1);
        Lacunarity = Mathf.Max(Lacunarity, 1f);
        Persistance = Mathf.Clamp01(Persistance);
    }
}
