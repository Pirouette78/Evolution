using Unity.Entities;
using UnityEngine;
using Unity.Transforms;

[UpdateInGroup(typeof(InitializationSystemGroup))]
public partial class WorldFailsafeSystem : SystemBase {
    protected override void OnUpdate() {
        if (SystemAPI.HasSingleton<GlobalBootstrapData>()) return;

        var authoring = Object.FindAnyObjectByType<GlobalManagerAuthoring>();
        if (authoring == null) return;

        var entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
        var entity = entityManager.CreateEntity();
        entityManager.AddComponentData(entity, new GlobalBootstrapData {
            CellPrefab = Entity.Null,
            FoodPrefab = Entity.Null,
            InitialCellCount = authoring.InitialCellCount,
            InitialFoodCount = authoring.InitialFoodCount,
            SpawnRadius = authoring.SpawnRadius,
            HasSpawned = false
        });
    }
}
