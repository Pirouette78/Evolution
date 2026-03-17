using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// Singleton to control spawning
public struct FoodSpawnerConfig : IComponentData {
    public Entity FoodPrefab;
    public float SpawnInterval;
    public float TimeSinceLastSpawn;
    public float2 MapSize; // e.g., (width, height)
    public int MaxFoodCount;
}

[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial struct FoodSpawnerSystem : ISystem {
    
    [BurstCompile]
    public void OnCreate(ref SystemState state) {
        state.RequireForUpdate<FoodSpawnerConfig>();
        state.RequireForUpdate<GameTime>();
        state.RequireForUpdate<TerrainMapData>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state) {
        float dt = SystemAPI.GetSingleton<GameTime>().ScaledDeltaTime;
        if (dt <= 0f) return;

        var configEntity = SystemAPI.GetSingletonEntity<FoodSpawnerConfig>();
        var config = SystemAPI.GetComponent<FoodSpawnerConfig>(configEntity);
        
        config.TimeSinceLastSpawn += dt;

        if (config.TimeSinceLastSpawn >= config.SpawnInterval) {
            
            // Count current food (fast, no allocation)
            var foodQuery = SystemAPI.QueryBuilder().WithAll<FoodComponent>().Build();
            int currentFood = foodQuery.CalculateEntityCount();

            if (currentFood < config.MaxFoodCount) {
                var terrainData = SystemAPI.GetSingleton<TerrainMapData>();
                ref var walkBlob = ref terrainData.WalkabilityRef.Value;

                var ecb = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);
                uint seed = (uint)(SystemAPI.Time.ElapsedTime * 1000) + 1;
                Random rand = new Random(seed);
                
                Entity newFood = ecb.Instantiate(config.FoodPrefab);

                // Find walkable position (max 50 retries)
                float3 spawnPos = float3.zero;
                for (int attempt = 0; attempt < 50; attempt++)
                {
                    spawnPos = new float3(
                        rand.NextFloat(0, config.MapSize.x),
                        rand.NextFloat(0, config.MapSize.y),
                        0f);

                    int ix = math.clamp((int)spawnPos.x, 0, walkBlob.Width - 1);
                    int iy = math.clamp((int)spawnPos.y, 0, walkBlob.Height - 1);
                    if (walkBlob.Walkable[iy * walkBlob.Width + ix] == 1)
                        break;
                }
                
                ecb.SetComponent(newFood, LocalTransform.FromPosition(spawnPos));
                
                ecb.Playback(state.EntityManager);
                ecb.Dispose();
            }
            
            config.TimeSinceLastSpawn = 0f;
        }
        
        SystemAPI.SetComponent(configEntity, config);
    }
}
