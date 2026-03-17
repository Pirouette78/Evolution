using Unity.Burst;
using Unity.Entities;
using Unity.Transforms;
using Unity.Mathematics;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(EatingSystem))]
public partial struct DivisionSystem : ISystem {

    private EntityQuery cellQuery;

    [BurstCompile]
    public void OnCreate(ref SystemState state) {
        state.RequireForUpdate<GameTime>();
        cellQuery = SystemAPI.QueryBuilder().WithAll<CellComponent>().Build();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state) {
        float dt = SystemAPI.GetSingleton<GameTime>().ScaledDeltaTime;
        if (dt <= 0f) return;

        // Population cap: don't divide if already too many cells
        int currentPop = cellQuery.CalculateEntityCount();
        if (currentPop >= 5000) return;

        var ecbSingleton = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

        float threshold = 50f;
        float elapsedTime = (float)SystemAPI.Time.ElapsedTime;

        var job = new DivideJob {
            Ecb = ecb.AsParallelWriter(),
            EnergyThreshold = threshold,
            Time = elapsedTime
        };
        
        job.ScheduleParallel();
    }
}

[BurstCompile]
public partial struct DivideJob : IJobEntity {
    public EntityCommandBuffer.ParallelWriter Ecb;
    public float EnergyThreshold;
    public float Time;

    public void Execute(Entity entity, [ChunkIndexInQuery] int chunkIndex, ref CellComponent cell, ref LocalTransform transform) {
        if (cell.Energy >= EnergyThreshold) {
            cell.Energy /= 2f;
            
            Entity newCell = Ecb.Instantiate(chunkIndex, entity);
            
            Ecb.SetComponent(chunkIndex, newCell, new CellComponent {
                Energy = cell.Energy,
                Speed = cell.Speed,
                PlayerID = cell.PlayerID,
                TimeSinceLastMove = 0f,
                TargetDirection = cell.TargetDirection
            });

            uint seed = (uint)(transform.Position.x * 100 + Time * 1000) + 1;
            Unity.Mathematics.Random rand = new Unity.Mathematics.Random(seed);
            
            float3 offset = new float3(rand.NextFloat(-1f, 1f), rand.NextFloat(-1f, 1f), 0f);
            LocalTransform newTransform = transform;
            newTransform.Position += offset;
            
            Ecb.SetComponent(chunkIndex, newCell, newTransform);
        }
    }
}
