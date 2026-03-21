using UnityEngine;

/// <summary>
/// Pool global de ressources partagé entre tous les bâtiments.
/// Les bâtiments y versent leur production (Poumon → oxygène)
/// et en prélèvent leur consommation (Rate ← oxygène).
/// </summary>
public enum ResourceType { Oxygen = 0, Glucose = 1, Iron = 2 }

public class ResourceManager : MonoBehaviour
{
    public static ResourceManager Instance { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void AutoCreate()
    {
        if (Instance != null) return;
        var go = new GameObject("[ResourceManager]");
        go.AddComponent<ResourceManager>();
        DontDestroyOnLoad(go);
    }

    // Valeur max par ressource pour éviter l'accumulation infinie
    [SerializeField] private float maxPerResource = 10000f;

    private readonly float[] pool = new float[System.Enum.GetValues(typeof(ResourceType)).Length];

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    // ── API publique ─────────────────────────────────────────────────

    public void Produce(ResourceType type, float amount)
    {
        int i = (int)type;
        pool[i] = Mathf.Min(pool[i] + amount, maxPerResource);
    }

    /// <summary>
    /// Prélève jusqu'à <paramref name="amount"/> unités.
    /// Retourne la quantité réellement prélevée (peut être inférieure si stock insuffisant).
    /// </summary>
    public float Consume(ResourceType type, float amount)
    {
        int   i      = (int)type;
        float actual = Mathf.Min(pool[i], amount);
        pool[i] -= actual;
        return actual;
    }

    /// <summary>Stock actuel d'une ressource.</summary>
    public float Get(ResourceType type) => pool[(int)type];

    // ── Utilitaire ───────────────────────────────────────────────────

    /// <summary>Convertit un nom de ressource JSON en ResourceType. Retourne Oxygen par défaut.</summary>
    public static ResourceType Parse(string name)
    {
        return name?.ToLowerInvariant() switch
        {
            "oxygen"  => ResourceType.Oxygen,
            "glucose" => ResourceType.Glucose,
            "iron"    => ResourceType.Iron,
            _         => ResourceType.Oxygen
        };
    }
}
