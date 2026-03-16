using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial class WorldBootstrapSystem : SystemBase {

    protected override void OnUpdate() {
        // UnityEngine.Debug.LogWarning($"WorldBootstrapSystem ticks! HasSingleton: {SystemAPI.HasSingleton<GlobalBootstrapData>()}");
        if (!SystemAPI.HasSingleton<GlobalBootstrapData>()) return;

        var bootstrapEntity = SystemAPI.GetSingletonEntity<GlobalBootstrapData>();
        var bootstrap = SystemAPI.GetComponent<GlobalBootstrapData>(bootstrapEntity);
        
        UnityEngine.Debug.LogWarning($"<BOOTSTRAP RUNNING!> Prefabs assigned? Cell: {bootstrap.CellPrefab != Entity.Null}, Food: {bootstrap.FoodPrefab != Entity.Null}");

        var ecb = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);
        
        // Spawn pattern: Circle distribution
        // Center of the 512x512 map approximately 256, 256
        float2 mapCenter = new float2(256f, 256f);
        uint seed = 12345;
        Random rand = new Random(seed);

        // Spawn Cells
        for (int i = 0; i < bootstrap.InitialCellCount; i++) {
            Entity newCell = ecb.Instantiate(bootstrap.CellPrefab);
            
            // Divide initial cells into Red (Player 0) and Green (Player 1)
            int playerID = (i < bootstrap.InitialCellCount / 2f) ? 0 : 1;
            
            // Spawn Red left, Green right
            float2 clusterCenter = (playerID == 0) ? new float2(128f, 256f) : new float2(384f, 256f);

            // Random point in circle around cluster center
            float angle = rand.NextFloat(0f, math.PI * 2f);
            float radius = rand.NextFloat(0f, bootstrap.SpawnRadius);
            float3 pos = new float3(clusterCenter.x + math.cos(angle) * radius, clusterCenter.y + math.sin(angle) * radius, 0f);
            
            ecb.SetComponent(newCell, LocalTransform.FromPosition(pos));
            
            ecb.AddComponent(newCell, new CellComponent {
                Energy = 20f,
                Speed = 50f, // Base speed
                PlayerID = playerID,
                TimeSinceLastMove = 0f,
                TargetDirection = new float3(math.cos(angle), math.sin(angle), 0f)
            });
        }

        // Spawn Food
        for (int i = 0; i < bootstrap.InitialFoodCount; i++) {
            Entity newFood = ecb.Instantiate(bootstrap.FoodPrefab);
            
            float angle = rand.NextFloat(0f, math.PI * 2f);
            float radius = rand.NextFloat(0f, bootstrap.SpawnRadius * 1.5f); // Food can spawn slightly further out
            float3 pos = new float3(mapCenter.x + math.cos(angle) * radius, mapCenter.y + math.sin(angle) * radius, 0f);
            
            ecb.SetComponent(newFood, LocalTransform.FromPosition(pos));
            
            ecb.AddComponent(newFood, new FoodComponent {
                EnergyValue = 5f
            });
        }
        ecb.Playback(EntityManager);
        ecb.Dispose();
        
        Enabled = false; // Never run again
    }
}
