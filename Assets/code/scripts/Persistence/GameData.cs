using Unity.Entities;
using Unity.Mathematics;

// Defines a cell entity
// [GhostComponent] // Uncomment when Unity.NetCode package is fully installed
public struct CellComponent : IComponentData {
    /* [GhostField] */ public float Energy;
    /* [GhostField] */ public float Speed;
    /* [GhostField] */ public int PlayerID; // 0=R, 1=G, 2=B
    
    // Internal state for brownian motion
    public float TimeSinceLastMove;
    public float3 TargetDirection;
}

// Defines a food entity
public struct FoodComponent : IComponentData {
    public float EnergyValue;
}

// Event triggered when UI researches a tech
public struct TechResearchedEvent : IComponentData {
    public Unity.Collections.FixedString32Bytes TechID;
}
