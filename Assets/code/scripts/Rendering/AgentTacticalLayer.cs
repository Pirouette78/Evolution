using UnityEngine;

public class AgentTacticalLayer : MonoBehaviour, ITacticalLayer
{
    [Header("Rendering")]
    public Mesh agentMesh;
    public Material agentMaterial;

    private MaterialPropertyBlock _mpb;

    private void Start()
    {
        if (ZoomLevelController.Instance != null)
        {
            ZoomLevelController.Instance.RegisterLayer(this);
        }
        _mpb = new MaterialPropertyBlock();
    }

    public void OnEnterTactical(Vector4 cameraBounds)
    {
        // Appelé une seule fois à l'entrée dans le mode tactique (ortho size < TacticalThreshold)
    }

    public void OnCameraBoundsChanged(Vector4 cameraBounds)
    {
        var renderer = SlimeMapRenderer.Instance;
        if (renderer == null) return;
        
        if (renderer.VisibleArgsBuffer == null || renderer.VisibleAgentIdsBuffer == null) return;
        if (agentMesh == null || agentMaterial == null) return;

        // Assigner le buffer d'IDs au MaterialPropertyBlock
        _mpb.SetBuffer("_VisibleAgentIds", renderer.VisibleAgentIdsBuffer);

        // Définir des bounds qui englobent toute la map pour forcer le dessin
        // (idéalement basé sur cameraBounds, mais pour commencer 512x512)
        Bounds bounds = new Bounds(new Vector3(256f, 256f, 0f), new Vector3(512f, 512f, 10f));

        Graphics.DrawMeshInstancedIndirect(
            agentMesh,
            0,
            agentMaterial,
            bounds,
            renderer.VisibleArgsBuffer,
            0,
            _mpb
        );
    }

    public void OnExitTactical()
    {
        // Appelé quand le zoom recule
    }

    private void OnDestroy()
    {
        if (ZoomLevelController.Instance != null)
        {
            ZoomLevelController.Instance.UnregisterLayer(this);
        }
    }
}
