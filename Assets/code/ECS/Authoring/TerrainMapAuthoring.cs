using Unity.Entities;
using Unity.Collections;
using UnityEngine;

/// <summary>
/// Authoring component that bakes noise-based walkability data into an ECS singleton.
/// Place this on a SubScene GameObject alongside the GlobalManagerAuthoring.
/// </summary>
public class TerrainMapAuthoring : MonoBehaviour
{
    [Header("Map Dimensions")]
    public int Width = 512;
    public int Height = 512;

    [Header("Noise Settings")]
    public NoiseSettings Noise = new NoiseSettings();

    [Header("Terrain")]
    [Range(0f, 1f)] public float WaterThreshold = 0.35f;

    public class TerrainMapBaker : Baker<TerrainMapAuthoring>
    {
        public override void Bake(TerrainMapAuthoring authoring)
        {
            authoring.Noise.Validate();

            // Generate the noise map at bake time
            float[,] noiseMap = NoiseMapGenerator.Generate(
                authoring.Width, authoring.Height, authoring.Noise);

            // Build the blob asset
            var builder = new BlobBuilder(Allocator.Temp);
            ref TerrainWalkabilityBlob root = ref builder.ConstructRoot<TerrainWalkabilityBlob>();
            root.Width = authoring.Width;
            root.Height = authoring.Height;

            int totalCells = authoring.Width * authoring.Height;
            var walkableArray = builder.Allocate(ref root.Walkable, totalCells);

            for (int y = 0; y < authoring.Height; y++)
            {
                for (int x = 0; x < authoring.Width; x++)
                {
                    bool passable = noiseMap[x, y] >= authoring.WaterThreshold;
                    walkableArray[y * authoring.Width + x] = passable ? (byte)1 : (byte)0;
                }
            }

            var blobRef = builder.CreateBlobAssetReference<TerrainWalkabilityBlob>(Allocator.Persistent);
            builder.Dispose();

            // Register with the baking system so ECS manages its lifetime
            AddBlobAsset(ref blobRef, out _);

            var entity = GetEntity(TransformUsageFlags.None);
            AddComponent(entity, new TerrainMapData
            {
                WalkabilityRef = blobRef
            });

            Debug.Log($"[TERRAIN BAKER] Baked walkability grid {authoring.Width}x{authoring.Height}, seed={authoring.Noise.Seed}");
        }
    }
}
