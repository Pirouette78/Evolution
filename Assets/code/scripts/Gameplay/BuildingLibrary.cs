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
        {
            Debug.LogWarning("[BuildingLibrary] Aucun fichier JSON trouvé — définitions built-in utilisées.");
            LoadBuiltIn();
        }

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
            // Correspondance via waypointSpeciesId (nouveau) ou speciesSlot résolu (héritage)
            bool match = (!string.IsNullOrEmpty(def.waypointSpeciesId) &&
                          def.waypointSpeciesId.ToLowerInvariant() == sid)
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

    // ── Fallback built-in ────────────────────────────────────────────

    private void LoadBuiltIn()
    {
        Register(new BuildingDefinition
        {
            id = "poumon", displayName = "Poumon", poiImagePath = "POI/poumon",
            waypointSpeciesId = "globulerouge", waypointType = 0,
            spawnsPerSecond = 2f, maxPopulation = 5000,
            consumes = new ResourceAmount[0],
            produces = new[] { new ResourceAmount { resource = "oxygen", amount = 5f } }
        });
        Register(new BuildingDefinition
        {
            id = "rate_reception", displayName = "Rate", poiImagePath = "POI/rate",
            waypointSpeciesId = "globulerouge", waypointType = 1
        });
        Register(new BuildingDefinition
        {
            id = "rate", displayName = "Rate", poiImagePath = "POI/rate",
            waypointSpeciesId = "globuleblanc", waypointType = 0,
            spawnsPerSecond = 1f, maxPopulation = 2000,
            consumes = new[] { new ResourceAmount { resource = "oxygen", amount = 2f } },
            produces = new ResourceAmount[0],
            scalesWithResource = "oxygen", resourceRequiredPerSecond = 2f
        });
        Register(new BuildingDefinition
        {
            id = "source_nutriments", displayName = "Source Nutriments", waypointSpeciesId = "bacterie", waypointType = 0,
            spawnsPerSecond = 3f, maxPopulation = 8000
        });
        Register(new BuildingDefinition
        {
            id = "zone_infection", displayName = "Zone Infection", waypointSpeciesId = "bacterie", waypointType = 1
        });
        Register(new BuildingDefinition
        {
            id = "noeud_viral", displayName = "Nœud Viral", waypointSpeciesId = "virus", waypointType = 0,
            spawnsPerSecond = 5f, maxPopulation = 10000
        });
        Register(new BuildingDefinition
        {
            id = "cellule_hote", displayName = "Cellule Hôte", waypointSpeciesId = "virus", waypointType = 1
        });
        Register(new BuildingDefinition
        {
            id = "moelle", displayName = "Moelle", waypointSpeciesId = "plaquette", waypointType = 0,
            spawnsPerSecond = 1f, maxPopulation = 3000
        });
        Register(new BuildingDefinition
        {
            id = "lesion", displayName = "Lésion", waypointSpeciesId = "plaquette", waypointType = 1
        });
    }

    private void Register(BuildingDefinition def)
    {
        def.id = def.id.ToLowerInvariant();
        byId[def.id] = def;
    }
}
