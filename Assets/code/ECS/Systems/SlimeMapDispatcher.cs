using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

public struct SlimeAgentData {
    public float2 position;
    public float angle;
    public float4 speciesMask;
    public int speciesIndex;
}

[UpdateInGroup(typeof(PresentationSystemGroup))]
public partial class SlimeMapDispatcher : SystemBase {
    
    private NativeList<SlimeAgentData> agentDataList;
    private int frameSkip = 0;

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
        // Skip if renderer is missing or inactive
        if (SlimeMapRenderer.Instance == null || !SlimeMapRenderer.Instance.gameObject.activeInHierarchy || SlimeMapRenderer.Instance.AgentBuffer == null) return;

        // Throttle: dispatch every 2 frames instead of every frame
        frameSkip++;
        if (frameSkip < 2) return;
        frameSkip = 0;

        var query = SystemAPI.QueryBuilder().WithAll<CellComponent, LocalTransform>().Build();
        int entityCount = query.CalculateEntityCount();

        if (entityCount == 0) return;

        agentDataList.Clear();
        if (agentDataList.Capacity < entityCount) {
             agentDataList.SetCapacity(entityCount);
        }
        agentDataList.ResizeUninitialized(entityCount);

        var gatherJob = new GatherSlimeAgentsJob {
            Agents = agentDataList.AsArray()
        };
        Dependency = gatherJob.ScheduleParallel(query, Dependency);
        
        Dependency.Complete();
        
        SlimeMapRenderer.Instance.AgentBuffer.SetData(agentDataList.AsArray());

        var gameTimeEntity = SystemAPI.GetSingletonEntity<GameTime>();
        var gameTime = SystemAPI.GetComponent<GameTime>(gameTimeEntity);
        
        SlimeMapRenderer.Instance.DispatchCompute(entityCount, gameTime.ScaledDeltaTime);
    }
}

[BurstCompile]
public partial struct GatherSlimeAgentsJob : IJobEntity {
    [NativeDisableParallelForRestriction]
    [WriteOnly] public NativeArray<SlimeAgentData> Agents;

    public void Execute([EntityIndexInQuery] int index, in CellComponent cell, in LocalTransform transform) {
        float4 mask = new float4(0, 0, 0, 0);
        if (cell.PlayerID == 0) mask.x = 1f;
        else if (cell.PlayerID == 1) mask.y = 1f;
        else if (cell.PlayerID == 2) mask.z = 1f;

        float headingAngle = math.atan2(cell.TargetDirection.y, cell.TargetDirection.x);

        Agents[index] = new SlimeAgentData {
            position = new float2(transform.Position.x, transform.Position.y),
            angle = headingAngle,
            speciesMask = mask,
            speciesIndex = cell.PlayerID
        };
    }
}
