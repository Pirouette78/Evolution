using Unity.Burst;
using Unity.Entities;

[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial struct EvolutionSystem : ISystem {
    
    [BurstCompile]
    public void OnCreate(ref SystemState state) {
        state.RequireForUpdate<TechResearchedEvent>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state) {
        var ecb = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);
        
        foreach (var (evt, entity) in SystemAPI.Query<RefRO<TechResearchedEvent>>().WithEntityAccess()) {
            
            if (evt.ValueRO.TechID == "tech_membrane") {
                // Double speed of Player 1 cells (PlayerID == 0)
                foreach (var cellBox in SystemAPI.Query<RefRW<CellComponent>>()) {
                    if (cellBox.ValueRO.PlayerID == 0) {
                        cellBox.ValueRW.Speed *= 2f;
                    }
                }
            }
            
            // Destroy the event entity so it only runs once
            ecb.DestroyEntity(entity);
        }
        
        ecb.Playback(state.EntityManager);
        ecb.Dispose();
    }
}
