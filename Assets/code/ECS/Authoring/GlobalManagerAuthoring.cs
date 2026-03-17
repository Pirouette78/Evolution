using Unity.Entities;
using UnityEngine;
using Unity.Mathematics;

// ECS Component to hold global spawning data
public struct GlobalBootstrapData : IComponentData {
    public Entity CellPrefab;
    public Entity FoodPrefab;
    public int InitialCellCount;
    public int InitialFoodCount;
    public float SpawnRadius;
    public bool HasSpawned;
}

public class GlobalManagerAuthoring : MonoBehaviour {
    [Header("Prefabs")]
    public GameObject CellPrefab;
    public GameObject FoodPrefab;

    [Header("Spawn Settings")]
    public int InitialCellCount = 100;
    public int InitialFoodCount = 500;
    public float SpawnRadius = 250f; // Given the map is ~512x512, 250 radius fits well

    public class GlobalManagerBaker : Baker<GlobalManagerAuthoring> {
        public override void Bake(GlobalManagerAuthoring authoring) {
            
            // Registering prefabs specifically so ECS knows to convert them
            
            var entity = GetEntity(TransformUsageFlags.None);
            
            var data = new GlobalBootstrapData
            {
                CellPrefab = GetEntity(authoring.CellPrefab, TransformUsageFlags.Dynamic),
                FoodPrefab = GetEntity(authoring.FoodPrefab, TransformUsageFlags.Dynamic),
                InitialCellCount = authoring.InitialCellCount,
                InitialFoodCount = authoring.InitialFoodCount,
                SpawnRadius = authoring.SpawnRadius,
                HasSpawned = false
            };
            AddComponent(entity, data);
        }
    }
}
