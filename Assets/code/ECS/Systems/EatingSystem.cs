using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(CellMovementSystem))] // Make sure we eat *after* moving
public partial struct EatingSystem : ISystem {
    
    [BurstCompile]
    public void OnCreate(ref SystemState state) {
        state.RequireForUpdate<GameTime>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state) {
        float dt = SystemAPI.GetSingleton<GameTime>().ScaledDeltaTime;
        if (dt <= 0f) return;

        var ecb = new EntityCommandBuffer(Allocator.TempJob);
        
        // Find all food manually to compare against cells
        // Since physics isn't installed yet, doing an O(N*M) naive distance check
        
        var foodQuery = SystemAPI.QueryBuilder().WithAll<FoodComponent, LocalTransform>().Build();
        var foodEntities = foodQuery.ToEntityArray(Allocator.TempJob);
        var foodTransforms = foodQuery.ToComponentDataArray<LocalTransform>(Allocator.TempJob);
        var foodData = foodQuery.ToComponentDataArray<FoodComponent>(Allocator.TempJob);
        
        // Track consumed food so multiple cells don't eat the same piece in one frame
        var consumedFood = new NativeHashSet<Entity>(foodEntities.Length, Allocator.TempJob);

        var job = new EatJob {
            FoodEntities = foodEntities,
            FoodTransforms = foodTransforms,
            FoodData = foodData,
            ConsumedFood = consumedFood,
            Ecb = ecb.AsParallelWriter(),
            DistanceSqThreshold = 2.0f * 2.0f // 2 units eating radius
        };
        
        // Must wait for this job because we are reading/modifying local native collections
        state.Dependency = job.Schedule(state.Dependency);
        state.Dependency.Complete();

        ecb.Playback(state.EntityManager);
        
        ecb.Dispose();
        foodEntities.Dispose();
        foodTransforms.Dispose();
        foodData.Dispose();
        consumedFood.Dispose();
    }
}

[BurstCompile]
public partial struct EatJob : IJobEntity {
    [ReadOnly] public NativeArray<Entity> FoodEntities;
    [ReadOnly] public NativeArray<LocalTransform> FoodTransforms;
    [ReadOnly] public NativeArray<FoodComponent> FoodData;
    public NativeHashSet<Entity> ConsumedFood;
    public EntityCommandBuffer.ParallelWriter Ecb;
    public float DistanceSqThreshold;

    public void Execute(Entity cellEntity, [ChunkIndexInQuery] int chunkIndex, ref CellComponent cell, in LocalTransform transform) {
        for (int i = 0; i < FoodEntities.Length; i++) {
            Entity foodEntity = FoodEntities[i];
            
            // Skip if another cell in this job already ate it
            if (ConsumedFood.Contains(foodEntity)) continue;

            float3 foodPos = FoodTransforms[i].Position;
            float distSq = math.distancesq(transform.Position, foodPos);

            if (distSq <= DistanceSqThreshold) {
                // Eat the food
                cell.Energy += FoodData[i].EnergyValue;
                
                // Mark for destruction and record it's consumed
                Ecb.DestroyEntity(chunkIndex, foodEntity);
                ConsumedFood.Add(foodEntity);
                break; // One piece of food per update per cell is fine
            }
        }
    }
}
