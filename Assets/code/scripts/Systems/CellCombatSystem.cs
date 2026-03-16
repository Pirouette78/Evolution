using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(CellMovementSystem))] // Eat/fight after moving
public partial struct CellCombatSystem : ISystem {
    
    [BurstCompile]
    public void OnCreate(ref SystemState state) {
        state.RequireForUpdate<GameTime>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state) {
        float dt = SystemAPI.GetSingleton<GameTime>().ScaledDeltaTime;
        if (dt <= 0f) return;

        var cellQuery = SystemAPI.QueryBuilder().WithAll<CellComponent, LocalTransform>().Build();
        
        var ecb = new EntityCommandBuffer(Allocator.TempJob);
        
        var cellEntities = cellQuery.ToEntityArray(Allocator.TempJob);
        var cellTransforms = cellQuery.ToComponentDataArray<LocalTransform>(Allocator.TempJob);
        var cellData = cellQuery.ToComponentDataArray<CellComponent>(Allocator.TempJob);
        
        var deadCells = new NativeHashSet<Entity>(cellEntities.Length, Allocator.TempJob);

        var job = new CombatJob {
            CellEntities = cellEntities,
            CellTransforms = cellTransforms,
            CellData = cellData,
            DeadCells = deadCells,
            Ecb = ecb,
            DistanceSqThreshold = 3.0f * 3.0f // 3 units combat radius
        };
        
        // Single thread schedule like EatingSystem
        state.Dependency = job.Schedule(state.Dependency);
        state.Dependency.Complete();

        ecb.Playback(state.EntityManager);
        
        ecb.Dispose();
        cellEntities.Dispose();
        cellTransforms.Dispose();
        cellData.Dispose();
        deadCells.Dispose();
    }
}

[BurstCompile]
public partial struct CombatJob : IJobEntity {
    [ReadOnly] public NativeArray<Entity> CellEntities;
    [ReadOnly] public NativeArray<LocalTransform> CellTransforms;
    [ReadOnly] public NativeArray<CellComponent> CellData;
    public NativeHashSet<Entity> DeadCells;
    public EntityCommandBuffer Ecb;
    public float DistanceSqThreshold;

    public void Execute(Entity cellEntity, ref CellComponent cell, in LocalTransform transform) {
        if (DeadCells.Contains(cellEntity)) return; // Already dead

        for (int i = 0; i < CellEntities.Length; i++) {
            Entity otherEntity = CellEntities[i];
            
            if (cellEntity == otherEntity) continue;
            if (DeadCells.Contains(otherEntity)) continue;

            var otherCell = CellData[i];
            
            // Only fight different factions
            if (cell.PlayerID == otherCell.PlayerID) continue;

            float distSq = math.distancesq(transform.Position, CellTransforms[i].Position);

            if (distSq <= DistanceSqThreshold) {
                // Fight! The one with more energy wins.
                if (cell.Energy > otherCell.Energy) {
                    cell.Energy += otherCell.Energy * 0.5f; // Consume half their energy
                    Ecb.DestroyEntity(otherEntity);
                    DeadCells.Add(otherEntity);
                } else if (cell.Energy == otherCell.Energy && cellEntity.Index > otherEntity.Index) {
                    cell.Energy += otherCell.Energy * 0.5f;
                    Ecb.DestroyEntity(otherEntity);
                    DeadCells.Add(otherEntity);
                }
            }
        }
    }
}
