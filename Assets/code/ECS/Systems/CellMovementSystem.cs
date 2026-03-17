using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial struct CellMovementSystem : ISystem {
    
    private EntityQuery foodQuery;
    private EntityQuery cellQuery;
    
    [BurstCompile]
    public void OnCreate(ref SystemState state) {
        state.RequireForUpdate<GameTime>();
        state.RequireForUpdate<TerrainMapData>();
        foodQuery = SystemAPI.QueryBuilder().WithAll<FoodComponent, LocalTransform>().Build();
        cellQuery = SystemAPI.QueryBuilder().WithAll<CellComponent, LocalTransform>().Build();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state) {
        float dt = SystemAPI.GetSingleton<GameTime>().ScaledDeltaTime;
        if (dt <= 0f) return;

        float elapsedTime = (float)SystemAPI.Time.ElapsedTime;
        int foodCount = foodQuery.CalculateEntityCount();
        
        var terrainData = SystemAPI.GetSingleton<TerrainMapData>();

        // If very few food, skip the expensive search entirely
        if (foodCount == 0) {
            var simpleJob = new SimpleMovementJob {
                DeltaTime = dt,
                Time = elapsedTime,
                WalkabilityRef = terrainData.WalkabilityRef
            };
            state.Dependency = simpleJob.ScheduleParallel(state.Dependency);
            return;
        }

        // Build spatial hash grid for food (cell size = sensor radius)
        const float cellSize = 50f;  // matches sensor radius
        const int gridDim = 11;      // 512 / 50 ≈ 11
        int totalBuckets = gridDim * gridDim;

        // Count food per bucket
        var bucketCounts = new NativeArray<int>(totalBuckets, Allocator.TempJob);
        var foodPositions = foodQuery.ToComponentDataArray<LocalTransform>(Allocator.TempJob);

        // Pass 1: count per bucket
        for (int i = 0; i < foodPositions.Length; i++) {
            int bx = math.clamp((int)(foodPositions[i].Position.x / cellSize), 0, gridDim - 1);
            int by = math.clamp((int)(foodPositions[i].Position.y / cellSize), 0, gridDim - 1);
            bucketCounts[by * gridDim + bx]++;
        }

        // Compute offsets (prefix sum)
        var bucketOffsets = new NativeArray<int>(totalBuckets, Allocator.TempJob);
        int total = 0;
        for (int i = 0; i < totalBuckets; i++) {
            bucketOffsets[i] = total;
            total += bucketCounts[i];
        }

        // Pass 2: place food positions into sorted array
        var sortedFood = new NativeArray<float2>(foodPositions.Length, Allocator.TempJob);
        var writeIndices = new NativeArray<int>(totalBuckets, Allocator.TempJob);
        for (int i = 0; i < totalBuckets; i++) writeIndices[i] = bucketOffsets[i];

        for (int i = 0; i < foodPositions.Length; i++) {
            int bx = math.clamp((int)(foodPositions[i].Position.x / cellSize), 0, gridDim - 1);
            int by = math.clamp((int)(foodPositions[i].Position.y / cellSize), 0, gridDim - 1);
            int bucket = by * gridDim + bx;
            int idx = writeIndices[bucket]++;
            sortedFood[idx] = foodPositions[i].Position.xy;
        }

        writeIndices.Dispose();
        foodPositions.Dispose();

        var job = new SpatialMovementJob {
            DeltaTime = dt,
            Time = elapsedTime,
            SortedFood = sortedFood,
            BucketOffsets = bucketOffsets,
            BucketCounts = bucketCounts,
            GridDim = gridDim,
            CellSize = cellSize,
            SensorRadius = 50f,
            SensorRadiusSq = 50f * 50f,
            WalkabilityRef = terrainData.WalkabilityRef
        };
        
        state.Dependency = job.ScheduleParallel(state.Dependency);
        
        sortedFood.Dispose(state.Dependency);
        bucketOffsets.Dispose(state.Dependency);
        bucketCounts.Dispose(state.Dependency);
    }
}

// Fallback job when no food exists (very fast)
[BurstCompile]
public partial struct SimpleMovementJob : IJobEntity {
    public float DeltaTime;
    public float Time;
    [ReadOnly] public BlobAssetReference<TerrainWalkabilityBlob> WalkabilityRef;

    public void Execute(ref LocalTransform transform, ref CellComponent cell) {
        cell.TimeSinceLastMove += DeltaTime;

        if (cell.TimeSinceLastMove > 1.5f) {
            uint seed = (uint)(transform.Position.x * 1000 + transform.Position.y * 1000 + Time * 1000) + 1;
            var rand = new Unity.Mathematics.Random(seed);
            float angle = rand.NextFloat(0f, math.PI * 2f);
            cell.TargetDirection = new float3(math.cos(angle), math.sin(angle), 0f);
            cell.TimeSinceLastMove = 0f;
        }

        float lenSq = math.lengthsq(cell.TargetDirection);
        if (lenSq > 0.01f) cell.TargetDirection = math.normalize(cell.TargetDirection);

        float3 candidatePos = transform.Position + cell.TargetDirection * cell.Speed * DeltaTime;
        candidatePos.x = math.clamp(candidatePos.x, 0f, 512f);
        candidatePos.y = math.clamp(candidatePos.y, 0f, 512f);

        if (TerrainMapHelper.IsWalkable(ref WalkabilityRef, candidatePos.x, candidatePos.y)) {
            transform.Position = candidatePos;
        } else {
            cell.TargetDirection = -cell.TargetDirection;
            cell.TimeSinceLastMove = 0f;
        }
    }
}

// Main job with spatial hash food lookup — O(N * K) where K = food in nearby buckets
[BurstCompile]
public partial struct SpatialMovementJob : IJobEntity {
    public float DeltaTime;
    public float Time;
    
    [ReadOnly] public NativeArray<float2> SortedFood;
    [ReadOnly] public NativeArray<int> BucketOffsets;
    [ReadOnly] public NativeArray<int> BucketCounts;
    public int GridDim;
    public float CellSize;
    public float SensorRadius;
    public float SensorRadiusSq;

    [ReadOnly] public BlobAssetReference<TerrainWalkabilityBlob> WalkabilityRef;

    public void Execute(ref LocalTransform transform, ref CellComponent cell) {
        cell.TimeSinceLastMove += DeltaTime;

        // Spatial hash food search: only check 3x3 neighboring buckets
        int cx = math.clamp((int)(transform.Position.x / CellSize), 0, GridDim - 1);
        int cy = math.clamp((int)(transform.Position.y / CellSize), 0, GridDim - 1);

        float2 myPos = transform.Position.xy;
        float minDistSq = SensorRadiusSq;
        float2 closestFood = float2.zero;
        bool foundFood = false;

        for (int dy = -1; dy <= 1; dy++) {
            for (int dx = -1; dx <= 1; dx++) {
                int bx = cx + dx;
                int by = cy + dy;
                if (bx < 0 || bx >= GridDim || by < 0 || by >= GridDim) continue;

                int bucket = by * GridDim + bx;
                int start = BucketOffsets[bucket];
                int count = BucketCounts[bucket];

                for (int i = start; i < start + count; i++) {
                    float distSq = math.lengthsq(SortedFood[i] - myPos);
                    if (distSq < minDistSq) {
                        minDistSq = distSq;
                        closestFood = SortedFood[i];
                        foundFood = true;
                    }
                }
            }
        }

        if (foundFood) {
            float2 toFood = closestFood - myPos;
            float dist = math.length(toFood);
            if (dist > 0.001f) {
                float3 dir = new float3(toFood / dist, 0f);
                cell.TargetDirection = math.lerp(cell.TargetDirection, dir, DeltaTime * 5f);
                cell.TargetDirection = math.normalize(cell.TargetDirection);
            }
            cell.TimeSinceLastMove = 0f;
        } else {
            if (cell.TimeSinceLastMove > 1.5f) {
                uint seed = (uint)(transform.Position.x * 1000 + transform.Position.y * 1000 + Time * 1000) + 1;
                var rand = new Unity.Mathematics.Random(seed);
                float angle = rand.NextFloat(0f, math.PI * 2f);
                cell.TargetDirection = new float3(math.cos(angle), math.sin(angle), 0f);
                cell.TimeSinceLastMove = 0f;
            }
        }

        float lenSq = math.lengthsq(cell.TargetDirection);
        if (lenSq > 0.01f) cell.TargetDirection = math.normalize(cell.TargetDirection);

        float3 candidatePos = transform.Position + cell.TargetDirection * cell.Speed * DeltaTime;
        candidatePos.x = math.clamp(candidatePos.x, 0f, 512f);
        candidatePos.y = math.clamp(candidatePos.y, 0f, 512f);

        if (TerrainMapHelper.IsWalkable(ref WalkabilityRef, candidatePos.x, candidatePos.y)) {
            transform.Position = candidatePos;
        } else {
            cell.TargetDirection = -cell.TargetDirection;
            uint bounceSeed = (uint)(transform.Position.x * 731 + transform.Position.y * 997 + Time * 1301) + 1;
            var bounceRand = new Unity.Mathematics.Random(bounceSeed);
            float perturbAngle = bounceRand.NextFloat(-0.5f, 0.5f);
            float currentAngle = math.atan2(cell.TargetDirection.y, cell.TargetDirection.x) + perturbAngle;
            cell.TargetDirection = new float3(math.cos(currentAngle), math.sin(currentAngle), 0f);
            cell.TimeSinceLastMove = 0f;
        }
    }
}
