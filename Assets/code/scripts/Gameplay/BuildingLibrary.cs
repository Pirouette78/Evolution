using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Charge et expose les BuildingDefinition depuis :
///   1. StreamingAssets/Buildings/*.json          (données de base)
///   2. StreamingAssets/Buildings/Mods/**/*.json  (surcharges et nouveaux bâtiments de mods)
///
/// Un fichier de mod dont l'id correspond à un bâtiment existant le remplace entièrement.
/// S'il n'existe aucun fichier JSON, des définitions built-in sont utilisées comme fallback.
/// </summary>
public class BuildingLibrary : MonoBehaviour
{
    public static BuildingLibrary Instance { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void AutoCreate()
    {
        if (Instance != null) return;
        var go = new GameObject("[BuildingLibrary]");
        go.AddComponent<BuildingLibrary>();
        DontDestroyOnLoad(go);
    }

    private readonly Dictionary<string, BuildingDefinition> byId
        = new Dictionary<string, BuildingDefinition>();

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        LoadFromStreamingAssets();
        if (byId.Count == 0)
            Debug.LogError("[BuildingLibrary] Aucun fichier JSON trouvé dans StreamingAssets/Buildings/. Créez au moins un fichier .json de bâtiment.");

        Debug.Log($"[BuildingLibrary] {byId.Count} bâtiment(s) : {string.Join(", ", byId.Keys)}");
    }

    // ── Accès public ─────────────────────────────────────────────────

    /// <summary>Retourne la définition par id ("poumon", "rate"…). Null si inconnu.</summary>
    public BuildingDefinition Get(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        byId.TryGetValue(id.ToLowerInvariant(), out var def);
        return def;
    }

    /// <summary>Retourne tous les bâtiments définis.</summary>
    public IReadOnlyCollection<BuildingDefinition> GetAll() => byId.Values;

    /// <summary>
    /// Retourne les bâtiments pour une espèce donnée (par string id).
    /// Filtre optionnel sur waypointType (0=Source, 1=Destination, -1=tous).
    /// </summary>
    public List<BuildingDefinition> GetFor(string speciesId, int waypointType = -1)
    {
        var result = new List<BuildingDefinition>();
        if (string.IsNullOrEmpty(speciesId)) return result;
        string sid = speciesId.ToLowerInvariant();

        foreach (var def in byId.Values)
        {
            // Correspondance via waypointSpeciesId, linkedSpeciesId (nouveau), ou speciesSlot résolu (héritage)
            bool match = (!string.IsNullOrEmpty(def.waypointSpeciesId) &&
                          def.waypointSpeciesId.ToLowerInvariant() == sid)
                      || (!string.IsNullOrEmpty(def.linkedSpeciesId) &&
                          def.linkedSpeciesId.ToLowerInvariant() == sid)
                      || (string.IsNullOrEmpty(def.waypointSpeciesId) &&
                          SpeciesLibrary.Instance != null &&
                          SpeciesLibrary.Instance.GetSlot(sid) == def.speciesSlot);

            if (!match) continue;
            if (waypointType >= 0 && def.waypointType != waypointType) continue;
            result.Add(def);
        }
        return result;
    }

    /// <summary>[Héritage] Retourne les bâtiments par slot GPU et waypointType.</summary>
    public List<BuildingDefinition> GetFor(int speciesSlot, int waypointType = -1)
    {
        var result = new List<BuildingDefinition>();
        foreach (var def in byId.Values)
        {
            if (def.ResolvedSpeciesSlot != speciesSlot) continue;
            if (waypointType >= 0 && def.waypointType != waypointType) continue;
            result.Add(def);
        }
        return result;
    }

    // ── Chargement ───────────────────────────────────────────────────

    private void LoadFromStreamingAssets()
    {
        string basePath = Path.Combine(Application.streamingAssetsPath, "Buildings");
        if (!Directory.Exists(basePath)) return;

        foreach (string file in Directory.GetFiles(basePath, "*.json", SearchOption.TopDirectoryOnly))
            LoadFile(file);

        string modsPath = Path.Combine(basePath, "Mods");
        if (Directory.Exists(modsPath))
            foreach (string file in Directory.GetFiles(modsPath, "*.json", SearchOption.AllDirectories))
                LoadFile(file);
    }

    private void LoadFile(string path)
    {
        try
        {
            string json = File.ReadAllText(path);
            var def = JsonUtility.FromJson<BuildingDefinition>(json);
            if (def == null || string.IsNullOrEmpty(def.id))
            {
                Debug.LogWarning($"[BuildingLibrary] Ignoré (id manquant) : {path}");
                return;
            }
            byId[def.id.ToLowerInvariant()] = def;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[BuildingLibrary] Erreur lecture {path} : {e.Message}");
        }
    }

    private void Register(BuildingDefinition def)
    {
        def.id = def.id.ToLowerInvariant();
        byId[def.id] = def;
    }
}
