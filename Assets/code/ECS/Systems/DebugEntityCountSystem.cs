using Unity.Entities;
using UnityEngine;

public partial class DebugEntityCountSystem : SystemBase {
    private bool hasLogged = false;
    protected override void OnUpdate() {
        if (hasLogged) return;
        hasLogged = true;

        var cellQuery = SystemAPI.QueryBuilder().WithAll<CellComponent>().Build();
        var foodQuery = SystemAPI.QueryBuilder().WithAll<FoodComponent>().Build();
        if (SystemAPI.HasSingleton<GlobalBootstrapData>()) {
            var bData = SystemAPI.GetSingleton<GlobalBootstrapData>();
            Debug.LogWarning($"!!! DEBUG INIT 3 !!! Cells: {cellQuery.CalculateEntityCount()}, Food: {foodQuery.CalculateEntityCount()}, BootstrapData: 1");
            Debug.LogWarning($"[BOOTSTRAP DATA] InitialCells: {bData.InitialCellCount}");
        } else {
            Debug.LogWarning($"!!! DEBUG INIT 3 !!! Cells: {cellQuery.CalculateEntityCount()}, Food: {foodQuery.CalculateEntityCount()}, BootstrapData: 0 (Singleton not found)");
        }
    }
}
