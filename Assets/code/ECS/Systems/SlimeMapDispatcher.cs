using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// Struct exactly matching HLSL for Compute Shader
public struct SlimeAgentData {
    public float2 position;
    public float angle;
    public float4 speciesMask;
    public int speciesIndex;
}

[UpdateInGroup(typeof(PresentationSystemGroup))]
public partial class SlimeMapDispatcher : SystemBase {
    
    // We will use this list to hold the structured buffer data
    private NativeList<SlimeAgentData> agentDataList;

    protected override void OnCreate() {
        RequireForUpdate<GameTime>();
        agentDataList = new NativeList<SlimeAgentData>(Allocator.Persistent);
    }

    protected override void OnDestroy() {
        if (agentDataList.IsCreated) {
            agentDataList.Dispose();
        }
    }

    protected override void OnUpdate() {
        // Query all cells
        var query = SystemAPI.QueryBuilder().WithAll<CellComponent, LocalTransform>().Build();
        int entityCount = query.CalculateEntityCount();

        if (entityCount == 0) return;

        // Resize the buffer to exactly the number of cells
        agentDataList.Clear();
        if (agentDataList.Capacity < entityCount) {
             agentDataList.SetCapacity(entityCount);
        }
        
        // This resize trick reserves space without initialising
        agentDataList.ResizeUninitialized(entityCount);

        // Schedule Burst job to fill the array quickly
        var gatherJob = new GatherSlimeAgentsJob {
            Agents = agentDataList.AsArray()
        };
        Dependency = gatherJob.ScheduleParallel(query, Dependency);
        
        // Check if renderer exists and has a buffer
        if (SlimeMapRenderer.Instance == null || SlimeMapRenderer.Instance.AgentBuffer == null) return;

        // Wait for job completion before accessing Data
        Dependency.Complete();

        // Push data to GPU buffer
        SlimeMapRenderer.Instance.AgentBuffer.SetData(agentDataList.AsArray());

        // Dispatch shaders
        var gameTimeEntity = SystemAPI.GetSingletonEntity<GameTime>();
        var gameTime = SystemAPI.GetComponent<GameTime>(gameTimeEntity);
        
        SlimeMapRenderer.Instance.DispatchCompute(entityCount, gameTime.ScaledDeltaTime);
    }
}

[BurstCompile]
public partial struct GatherSlimeAgentsJob : IJobEntity {
    [NativeDisableParallelForRestriction] // Safe because we assign based on explicit unique indices if we track them, 
    // but honestly we can just use IJobChunk to safely fill an array. For simplicity in parallel, let's just 
    // do a linear iteration instead of true parallel, or use an appending native queue/list if order doesn't matter.
    // Let's rewrite as simple Job Entity filling an array securely if we had an internal counter, 
    // but better yet, let's just use IJobChunk for direct index-based writes.
    // For now, mapping directly in a single thread is extremely fast for thousands anyway if not using chunk index.
    
    // Quick Hack: Because parallel writes to an array need an index, we'll use a [NativeDisableParallelForRestriction] 
    // and rely on a shared counter, OR we can just use the [EntityIndexInQuery]
    
    [WriteOnly] public NativeArray<SlimeAgentData> Agents;

    public void Execute([EntityIndexInQuery] int index, in CellComponent cell, in LocalTransform transform) {
        
        // Match player ID to RGB channel (1,0,0,0), (0,1,0,0), (0,0,1,0)
        float4 mask = new float4(0, 0, 0, 0);
        if (cell.PlayerID == 0) mask.x = 1f; // R
        else if (cell.PlayerID == 1) mask.y = 1f; // G
        else if (cell.PlayerID == 2) mask.z = 1f; // B

        // Using basic Atan2 to find angle of current TargetDirection
        float headingAngle = math.atan2(cell.TargetDirection.y, cell.TargetDirection.x);

        Agents[index] = new SlimeAgentData {
            position = new float2(transform.Position.x, transform.Position.y),
            angle = headingAngle,
            speciesMask = mask,
            speciesIndex = cell.PlayerID // Usually 0, 1, or 2
        };
    }
}
