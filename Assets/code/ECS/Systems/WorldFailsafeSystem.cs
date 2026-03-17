using Unity.Entities;
using UnityEngine;
using Unity.Transforms;

[UpdateInGroup(typeof(InitializationSystemGroup))]
public partial class WorldFailsafeSystem : SystemBase {
    protected override void OnUpdate() {
        if (SystemAPI.HasSingleton<GlobalBootstrapData>()) return;

        var authoring = Object.FindAnyObjectByType<GlobalManagerAuthoring>();
        if (authoring == null) return;

        Debug.LogWarning("[FAILSAFE] GlobalBootstrapData singleton missing. Creating manually from GameObject...");

        var entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
        var entity = entityManager.CreateEntity();
        
        // Note: This won't handle prefabs as entities perfectly unless we convert them,
        // but it will trigger the WorldBootstrapSystem to at least see the singleton.
        // Actually, let's just trigger the spawning logic here if it's missing.
        
        entityManager.AddComponentData(entity, new GlobalBootstrapData {
            CellPrefab = Entity.Null, // We'll need to fix this if we want it to work perfectly
            FoodPrefab = Entity.Null,
            InitialCellCount = authoring.InitialCellCount,
            InitialFoodCount = authoring.InitialFoodCount,
            SpawnRadius = authoring.SpawnRadius,
            HasSpawned = false
        });
        
        Debug.LogWarning("[FAILSAFE] Singleton created. Note: Prefabs might be null. Recommending Scene Re-bake.");
    }
}
