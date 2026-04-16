using UnityEngine;

[ExecuteAlways]
public class GlobalSunManager : MonoBehaviour
{
    public static GlobalSunManager Instance { get; private set; }

    [Header("Global Sun Settings")]
    [Tooltip("Définit la position du soleil (1 = droite, -1 = gauche). Utilisé par le terrain et les sprites.")]
    [Range(-1f, 1f)]
    public float sunPosition = 1f;

    [Tooltip("Facteur d'inclinaison pour les ombres des sprites sous forme géométrique.")]
    public float shadowSkewMultiplier = 1f;

    [Tooltip("Opacité de l'ombre des sprites (0 = invisible, 1 = noir opaque).")]
    [Range(0f, 1f)]
    public float shadowOpacity = 0.5f;

    private static readonly int GlobalSunPositionId = Shader.PropertyToID("_GlobalSunPosition");
    private static readonly int GlobalShadowSkewId  = Shader.PropertyToID("_GlobalShadowSkew");
    private static readonly int GlobalShadowOpacityId = Shader.PropertyToID("_GlobalShadowOpacity");

    void Awake()
    {
        if (Instance != null && Instance != this) return;
        Instance = this;
    }

    void Start()
    {
        Debug.Log("[SUN] GlobalSunManager actif. Prêt à piloter le terrain et les ombres.");
    }

    void Update()
    {
        // Envoie la variable à TOUS les shaders en même temps !
        Shader.SetGlobalFloat(GlobalSunPositionId, sunPosition);
        
        // On calcule une "inclinaison" (skew) globale prête à être utilisée par des Sprite Renderers
        Shader.SetGlobalFloat(GlobalShadowSkewId, -sunPosition * shadowSkewMultiplier);

        // Envoi de l'opacité et l'écrasement de l'ombre à tout le monde
        Shader.SetGlobalFloat(GlobalShadowOpacityId, shadowOpacity);
        Shader.SetGlobalFloat("_GlobalShadowScaleY", 0.5f);
    }
}
