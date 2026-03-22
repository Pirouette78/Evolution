using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Charge les niveaux diplomatiques depuis StreamingAssets/Diplomacy/levels.json.
/// L'ordre du tableau JSON définit l'ordre de navigation dans la matrice diplomatique.
///
/// Modifier un niveau = éditer levels.json, sans toucher au code.
/// </summary>
public class DiplomacyLibrary : MonoBehaviour
{
    public static DiplomacyLibrary Instance { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void AutoCreate()
    {
        if (Instance != null) return;
        var go = new GameObject("[DiplomacyLibrary]");
        go.AddComponent<DiplomacyLibrary>();
        DontDestroyOnLoad(go);
    }

    // ── Données ──────────────────────────────────────────────────────

    [System.Serializable]
    private class Wrapper { public List<DiplomacyLevelDefinition> levels; }

    private List<DiplomacyLevelDefinition> levels = new List<DiplomacyLevelDefinition>();

    /// <summary>Niveaux dans l'ordre du JSON (= ordre de navigation : index 0 = min, dernier = max).</summary>
    public List<DiplomacyLevelDefinition> Levels => levels;

    // ── Lifecycle ────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        LoadFromStreamingAssets();
        if (levels.Count == 0)
            Debug.LogError("[DiplomacyLibrary] Aucun niveau trouvé dans StreamingAssets/Diplomacy/levels.json.");
        else
            Debug.Log($"[DiplomacyLibrary] {levels.Count} niveaux : {string.Join(", ", levels.ConvertAll(l => l.id))}");
    }

    // ── API publique ─────────────────────────────────────────────────

    /// <summary>Premier niveau avec isWar=true. Null si aucun.</summary>
    public DiplomacyLevelDefinition WarLevel
    {
        get { foreach (var l in levels) if (l.isWar) return l; return levels.Count > 0 ? levels[0] : null; }
    }

    /// <summary>Niveau avec id="allie", ou dernier niveau de la liste.</summary>
    public DiplomacyLevelDefinition DefaultAllyLevel
    {
        get { var l = Get("allie"); return l ?? (levels.Count > 0 ? levels[levels.Count - 1] : null); }
    }

    /// <summary>Niveau avec id="neutre", ou premier niveau sans isWar.</summary>
    public DiplomacyLevelDefinition DefaultNeutralLevel
    {
        get
        {
            var l = Get("neutre");
            if (l != null) return l;
            foreach (var lvl in levels) if (!lvl.isWar) return lvl;
            return levels.Count > 0 ? levels[0] : null;
        }
    }

    /// <summary>Retourne un niveau par id (insensible à la casse). Null si inconnu.</summary>
    public DiplomacyLevelDefinition Get(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        string lower = id.ToLowerInvariant();
        foreach (var l in levels)
            if (l.id == lower) return l;
        return null;
    }

    /// <summary>
    /// Retourne le niveau dont la valeur est la plus proche de <paramref name="value"/>.
    /// Correspondance exacte si écart &lt; 0.05.
    /// </summary>
    public DiplomacyLevelDefinition GetByValue(float value)
    {
        if (levels.Count == 0) return null;
        DiplomacyLevelDefinition best = levels[0];
        float bestDist = Mathf.Abs(value - best.value);
        for (int i = 1; i < levels.Count; i++)
        {
            float d = Mathf.Abs(value - levels[i].value);
            if (d < bestDist) { bestDist = d; best = levels[i]; }
        }
        return best;
    }

    // ── Chargement ───────────────────────────────────────────────────

    private void LoadFromStreamingAssets()
    {
        string path = Path.Combine(Application.streamingAssetsPath, "Diplomacy", "levels.json");
        if (!File.Exists(path))
        {
            Debug.LogWarning($"[DiplomacyLibrary] Fichier introuvable : {path}");
            return;
        }
        try
        {
            var wrapper = JsonUtility.FromJson<Wrapper>(File.ReadAllText(path));
            if (wrapper?.levels != null)
                foreach (var l in wrapper.levels)
                    if (!string.IsNullOrEmpty(l.id))
                    {
                        l.id = l.id.ToLowerInvariant();
                        levels.Add(l);
                    }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[DiplomacyLibrary] Erreur de chargement : {e.Message}");
        }
    }
}
