using Unity.Entities;
using Unity.Burst;

public struct GameTime : IComponentData {
    public float TimeScale; // Defaults to 1. Use 0 for Pause, 2 for 2x, 4 for 4x
    public float ScaledDeltaTime;
}

[UpdateInGroup(typeof(InitializationSystemGroup))]
public partial struct GameSpeedSystem : ISystem {
    
    public void OnCreate(ref SystemState state) {
        var archetype = state.EntityManager.CreateArchetype(typeof(GameTime));
        var entity = state.EntityManager.CreateEntity(archetype);
        state.EntityManager.SetComponentData(entity, new GameTime { 
            TimeScale = 1f, 
            ScaledDeltaTime = 0f 
        });
        
        state.RequireForUpdate<GameTime>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state) {
        var gameTimeEntity = SystemAPI.GetSingletonEntity<GameTime>();
        var gameTime = SystemAPI.GetComponent<GameTime>(gameTimeEntity);
        
        // Calculate the scaled delta time for the current frame
        gameTime.ScaledDeltaTime = SystemAPI.Time.DeltaTime * gameTime.TimeScale;
        
        // Update the singleton
        SystemAPI.SetComponent(gameTimeEntity, gameTime);
    }
}
