using Unity.Entities;
using UnityEngine;

public class RuntimeSingletonInjector : MonoBehaviour {
    public GlobalManagerAuthoring authoring;

    void Start() {
        if (authoring == null) authoring = GetComponent<GlobalManagerAuthoring>();
        if (authoring == null) return;

        var world = World.DefaultGameObjectInjectionWorld;
        if (world == null) return;

        var entityManager = world.EntityManager;
        
        // Check if singleton already exists
        var query = entityManager.CreateEntityQuery(typeof(GlobalBootstrapData));
        if (query.CalculateEntityCount() > 0) return;

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
