using Unity.Entities;

/// <summary>
/// Destroys the GlobalBootstrapData singleton after handing off control
/// to SlimeMapRenderer (which spawns agents autonomously via coroutine).
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial class WorldBootstrapSystem : SystemBase
{
    protected override void OnUpdate()
    {
        if (!SystemAPI.HasSingleton<GlobalBootstrapData>()) return;

        // Destroy the singleton — renderer handles spawning itself.
        var ecb = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);
        ecb.DestroyEntity(SystemAPI.GetSingletonEntity<GlobalBootstrapData>());
        ecb.Playback(EntityManager);
        ecb.Dispose();

        Enabled = false;
    }
}
