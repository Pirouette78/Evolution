using Unity.Entities;
using Unity.Collections;

/// <summary>
/// Blob asset storing the flattened walkability grid for Burst-compatible lookups.
/// </summary>
public struct TerrainWalkabilityBlob
{
    public int Width;
    public int Height;
    public BlobArray<byte> Walkable; // 1 = walkable, 0 = blocked
}

/// <summary>
/// ECS singleton component referencing the terrain walkability data.
/// </summary>
public struct TerrainMapData : IComponentData
{
    public BlobAssetReference<TerrainWalkabilityBlob> WalkabilityRef;
}

/// <summary>
/// Helper methods for querying walkability from ECS jobs.
/// </summary>
public static class TerrainMapHelper
{
    /// <summary>
    /// Returns true if the world position (x,y) maps to a walkable tile.
    /// Positions outside the grid are treated as non-walkable.
    /// </summary>
    public static bool IsWalkable(ref BlobAssetReference<TerrainWalkabilityBlob> blobRef, float x, float y)
    {
        ref var blob = ref blobRef.Value;
        int ix = (int)x;
        int iy = (int)y;

        if (ix < 0 || ix >= blob.Width || iy < 0 || iy >= blob.Height)
            return false;

        return blob.Walkable[iy * blob.Width + ix] == 1;
    }
}
