using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial struct CellMovementSystem : ISystem {
    
    private EntityQuery foodQuery;
    
    [BurstCompile]
    public void OnCreate(ref SystemState state) {
        state.RequireForUpdate<GameTime>();
        foodQuery = SystemAPI.QueryBuilder().WithAll<FoodComponent, LocalTransform>().Build();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state) {
        float dt = SystemAPI.GetSingleton<GameTime>().ScaledDeltaTime;
        if (dt <= 0f) return;

        float elapsedTime = (float)SystemAPI.Time.ElapsedTime;
        
        var foodTransforms = foodQuery.ToComponentDataArray<LocalTransform>(Allocator.TempJob);
        
        var job = new BrownianMotionJob {
            DeltaTime = dt,
            Time = elapsedTime,
            FoodTransforms = foodTransforms,
            SensorRadiusSq = 50f * 50f
        };
        
        state.Dependency = job.ScheduleParallel(state.Dependency);
        foodTransforms.Dispose(state.Dependency);
    }
}

[BurstCompile]
public partial struct BrownianMotionJob : IJobEntity {
    public float DeltaTime;
    public float Time;
    
    [ReadOnly]
    public NativeArray<LocalTransform> FoodTransforms;
    
    public float SensorRadiusSq;

    public void Execute(ref LocalTransform transform, ref CellComponent cell) {
        cell.TimeSinceLastMove += DeltaTime;

        float3 closestFoodPos = float3.zero;
        float minDistanceSq = SensorRadiusSq;
        bool foundFood = false;

        for (int i = 0; i < FoodTransforms.Length; i++) {
            float3 foodPos = FoodTransforms[i].Position;
            float distSq = math.lengthsq(foodPos - transform.Position);
            if (distSq < minDistanceSq) {
                minDistanceSq = distSq;
                closestFoodPos = foodPos;
                foundFood = true;
            }
        }

        if (foundFood) {
            float3 toFood = closestFoodPos - transform.Position;
            float dist = math.length(toFood);
            if (dist > 0.001f) {
                float3 dir = toFood / dist;
                cell.TargetDirection = math.lerp(cell.TargetDirection, dir, DeltaTime * 5f);
                cell.TargetDirection = math.normalize(cell.TargetDirection);
            }
            cell.TimeSinceLastMove = 0f;
        } else {
            if (cell.TimeSinceLastMove > 1.5f) {
                uint seed = (uint)(transform.Position.x * 1000 + transform.Position.y * 1000 + Time * 1000) + 1;
                Unity.Mathematics.Random rand = new Unity.Mathematics.Random(seed);
                
                float angle = rand.NextFloat(0f, math.PI * 2f);
                cell.TargetDirection = new float3(math.cos(angle), math.sin(angle), 0f);
                
                cell.TimeSinceLastMove = 0f;
            }
        }

        float lenSq = math.lengthsq(cell.TargetDirection);
        if (lenSq > 0.01f) {
            cell.TargetDirection = math.normalize(cell.TargetDirection);
        }

        transform.Position += cell.TargetDirection * cell.Speed * DeltaTime;

        transform.Position.x = math.clamp(transform.Position.x, 0f, 512f);
        transform.Position.y = math.clamp(transform.Position.y, 0f, 512f);
    }
}
