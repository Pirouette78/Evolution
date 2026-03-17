using Unity.Entities;
using UnityEngine;

public class ForceBakeManager : MonoBehaviour {
    public class ForceBakeBaker : Baker<ForceBakeManager> {
        public override void Bake(ForceBakeManager authoring) {
            Debug.LogWarning("!!! BAKER IS RUNNING for ForceBakeManager !!!");
            var entity = GetEntity(TransformUsageFlags.None);
            AddComponent(entity, new ForceBakeTag());
        }
    }
}

public struct ForceBakeTag : IComponentData {}
