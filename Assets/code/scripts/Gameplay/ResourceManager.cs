using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Pool global de ressources partagé entre tous les bâtiments.
/// Les ressources sont identifiées par string ("oxygen", "glucose"…) et définies dans resources.json.
/// Ajouter une ressource = l'ajouter dans resources.json, sans modification de ce script.
/// </summary>
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

    [SerializeField] private float maxPerResource = 10000f;

    private readonly Dictionary<string, float> pool = new Dictionary<string, float>();

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    // ── API publique ─────────────────────────────────────────────────

    /// <summary>Ajoute <paramref name="amount"/> unités à la ressource <paramref name="id"/>.</summary>
    public void Produce(string id, float amount)
    {
        if (string.IsNullOrEmpty(id) || amount <= 0f) return;
        id = id.ToLowerInvariant();
        pool.TryGetValue(id, out float current);
        pool[id] = Mathf.Min(current + amount, maxPerResource);
    }

    /// <summary>
    /// Prélève jusqu'à <paramref name="amount"/> unités de <paramref name="id"/>.
    /// Retourne la quantité réellement prélevée (peut être inférieure si stock insuffisant).
    /// </summary>
    public float Consume(string id, float amount)
    {
        if (string.IsNullOrEmpty(id) || amount <= 0f) return 0f;
        id = id.ToLowerInvariant();
        pool.TryGetValue(id, out float current);
        float actual = Mathf.Min(current, amount);
        pool[id] = current - actual;
        return actual;
    }

    /// <summary>Stock actuel d'une ressource (0 si inconnue).</summary>
    public float Get(string id)
    {
        if (string.IsNullOrEmpty(id)) return 0f;
        pool.TryGetValue(id.ToLowerInvariant(), out float v);
        return v;
    }
}
