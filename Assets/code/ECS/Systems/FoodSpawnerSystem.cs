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
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state) {
        float dt = SystemAPI.GetSingleton<GameTime>().ScaledDeltaTime;
        if (dt <= 0f) return;

        var configEntity = SystemAPI.GetSingletonEntity<FoodSpawnerConfig>();
        var config = SystemAPI.GetComponent<FoodSpawnerConfig>(configEntity);
        
        config.TimeSinceLastSpawn += dt;

        if (config.TimeSinceLastSpawn >= config.SpawnInterval) {
            
            // Count current food
            int currentFood = 0;
            foreach (var _ in SystemAPI.Query<RefRO<FoodComponent>>()) {
                currentFood++;
            }

            if (currentFood < config.MaxFoodCount) {
                // Time to spawn
                var ecb = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);
                uint seed = (uint)(SystemAPI.Time.ElapsedTime * 1000) + 1;
                Random rand = new Random(seed);
                
                Entity newFood = ecb.Instantiate(config.FoodPrefab);
                float3 spawnPos = new float3(
                    rand.NextFloat(0, config.MapSize.x),
                    rand.NextFloat(0, config.MapSize.y),
                    0f
                );
                
                ecb.SetComponent(newFood, LocalTransform.FromPosition(spawnPos));
                
                ecb.Playback(state.EntityManager);
                ecb.Dispose();
            }
            
            config.TimeSinceLastSpawn = 0f;
        }
        
        SystemAPI.SetComponent(configEntity, config);
    }
}
