using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial class WorldBootstrapSystem : SystemBase {

    protected override void OnUpdate() {
        if (!SystemAPI.HasSingleton<GlobalBootstrapData>()) return;
        if (!SystemAPI.HasSingleton<TerrainMapData>()) return;

        var bootstrapEntity = SystemAPI.GetSingletonEntity<GlobalBootstrapData>();
        var bootstrap = SystemAPI.GetComponent<GlobalBootstrapData>(bootstrapEntity);

        Entity cellPrefab = bootstrap.CellPrefab;
        Entity foodPrefab = bootstrap.FoodPrefab;

        if (cellPrefab == Entity.Null || foodPrefab == Entity.Null) {
             UnityEngine.Debug.LogWarning("[BOOTSTRAP] Prefabs missing from singleton. Attempting fallback...");
        }

        if (cellPrefab == Entity.Null || foodPrefab == Entity.Null) return;

        var terrainData = SystemAPI.GetSingleton<TerrainMapData>();
        ref var walkBlob = ref terrainData.WalkabilityRef.Value;

        UnityEngine.Debug.LogWarning($"<BOOTSTRAP RUNNING!> Prefabs assigned? Cell: {cellPrefab != Entity.Null}, Food: {foodPrefab != Entity.Null}");

        var ecb = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);
        
        float2 mapCenter = new float2(256f, 256f);
        uint seed = 12345;
        Random rand = new Random(seed);

        // Spawn Cells – only on walkable tiles
        for (int i = 0; i < bootstrap.InitialCellCount; i++) {
            Entity newCell = ecb.Instantiate(cellPrefab);
            
            int playerID = (i < bootstrap.InitialCellCount / 2f) ? 0 : 1;
            float2 clusterCenter = (playerID == 0) ? new float2(128f, 256f) : new float2(384f, 256f);

            // Find a walkable spawn position (max 50 retries)
            float3 pos = float3.zero;
            for (int attempt = 0; attempt < 50; attempt++)
            {
                float angle = rand.NextFloat(0f, math.PI * 2f);
                float radius = rand.NextFloat(0f, bootstrap.SpawnRadius);
                pos = new float3(
                    clusterCenter.x + math.cos(angle) * radius,
                    clusterCenter.y + math.sin(angle) * radius,
                    0f);

                int ix = math.clamp((int)pos.x, 0, walkBlob.Width - 1);
                int iy = math.clamp((int)pos.y, 0, walkBlob.Height - 1);
                if (walkBlob.Walkable[iy * walkBlob.Width + ix] == 1)
                    break;
            }
            
            ecb.SetComponent(newCell, LocalTransform.FromPosition(pos));
            
            float angle2 = rand.NextFloat(0f, math.PI * 2f);
            ecb.AddComponent(newCell, new CellComponent {
                Energy = 20f,
                Speed = 50f,
                PlayerID = playerID,
                TimeSinceLastMove = 0f,
                TargetDirection = new float3(math.cos(angle2), math.sin(angle2), 0f)
            });
        }

        // Spawn Food – only on walkable tiles
        for (int i = 0; i < bootstrap.InitialFoodCount; i++) {
            Entity newFood = ecb.Instantiate(foodPrefab);
            
            float3 pos = float3.zero;
            for (int attempt = 0; attempt < 50; attempt++)
            {
                float angle = rand.NextFloat(0f, math.PI * 2f);
                float radius = rand.NextFloat(0f, bootstrap.SpawnRadius * 1.5f);
                pos = new float3(
                    mapCenter.x + math.cos(angle) * radius,
                    mapCenter.y + math.sin(angle) * radius,
                    0f);

                int ix = math.clamp((int)pos.x, 0, walkBlob.Width - 1);
                int iy = math.clamp((int)pos.y, 0, walkBlob.Height - 1);
                if (walkBlob.Walkable[iy * walkBlob.Width + ix] == 1)
                    break;
            }
            
            ecb.SetComponent(newFood, LocalTransform.FromPosition(pos));
            
            ecb.AddComponent(newFood, new FoodComponent {
                EnergyValue = 5f
            });
        }
        ecb.Playback(EntityManager);
        ecb.Dispose();
        UnityEngine.Debug.LogWarning($"[BOOTSTRAP] Finished spawning {bootstrap.InitialCellCount} cells.");
        
        Enabled = false; // Never run again
    }
}
