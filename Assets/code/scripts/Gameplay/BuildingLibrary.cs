using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Charge et expose les BuildingDefinition depuis :
///   1. StreamingAssets/Buildings/*.json          (données de base)
///   2. StreamingAssets/Buildings/Mods/**/*.json  (surcharges et nouveaux bâtiments de mods)
///
/// Un fichier de mod dont l'id correspond à un bâtiment existant le remplace entièrement.
/// S'il n'existe aucun fichier JSON (ex : éditeur sans StreamingAssets configurés),
/// des définitions built-in sont utilisées comme fallback.
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
            Debug.LogWarning("[BuildingLibrary] Aucun fichier JSON trouvé — utilisation des définitions built-in.");
            LoadBuiltIn();
        }

        Debug.Log($"[BuildingLibrary] {byId.Count} bâtiment(s) chargé(s).");
    }

    // ── Accès public ─────────────────────────────────────────────────

    /// <summary>Retourne la définition par id ("poumon", "rate"…). Null si inconnu.</summary>
    public BuildingDefinition Get(string id)
    {
        byId.TryGetValue(id.ToLowerInvariant(), out var def);
        return def;
    }

    /// <summary>Retourne tous les bâtiments définis.</summary>
    public IReadOnlyCollection<BuildingDefinition> GetAll() => byId.Values;

    /// <summary>Retourne les bâtiments d'un speciesSlot et waypointType donnés.</summary>
    public List<BuildingDefinition> GetFor(int speciesSlot, int waypointType = -1)
    {
        var result = new List<BuildingDefinition>();
        foreach (var def in byId.Values)
        {
            if (def.speciesSlot != speciesSlot) continue;
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

        // 1. Fichiers de base (*.json directement dans Buildings/)
        foreach (string file in Directory.GetFiles(basePath, "*.json", SearchOption.TopDirectoryOnly))
            LoadFile(file);

        // 2. Mods (surcharges + ajouts)
        string modsPath = Path.Combine(basePath, "Mods");
        if (Directory.Exists(modsPath))
        {
            foreach (string file in Directory.GetFiles(modsPath, "*.json", SearchOption.AllDirectories))
                LoadFile(file);
        }
    }

    private void LoadFile(string path)
    {
        try
        {
            string json = File.ReadAllText(path);
            var def = JsonUtility.FromJson<BuildingDefinition>(json);
            if (def == null || string.IsNullOrEmpty(def.id))
            {
                Debug.LogWarning($"[BuildingLibrary] Fichier ignoré (id manquant) : {path}");
                return;
            }
            byId[def.id.ToLowerInvariant()] = def;
            Debug.Log($"[BuildingLibrary] Chargé : {def.id} ({path})");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[BuildingLibrary] Erreur lecture {path} : {e.Message}");
        }
    }

    // ── Fallback built-in ────────────────────────────────────────────

    private void LoadBuiltIn()
    {
        var builtIn = new BuildingDefinition[]
        {
            new BuildingDefinition
            {
                id                    = "poumon",
                displayName           = "Poumon",
                poiImagePath          = "POI/poumon",
                speciesSlot           = 0,
                waypointType          = 0,
                spawnsPerSecond       = 2f,
                maxPopulation         = 5000,
                consumes              = new ResourceAmount[0],
                produces              = new ResourceAmount[]
                {
                    new ResourceAmount { resource = "oxygen", amount = 5f }
                },
                scalesWithOxygen      = false,
                oxygenRequiredPerSecond = 0f
            },
            new BuildingDefinition
            {
                id                    = "rate",
                displayName           = "Rate",
                poiImagePath          = "POI/rate",
                speciesSlot           = 1,
                waypointType          = 0,
                spawnsPerSecond       = 1f,
                maxPopulation         = 2000,
                consumes              = new ResourceAmount[]
                {
                    new ResourceAmount { resource = "oxygen", amount = 2f }
                },
                produces              = new ResourceAmount[0],
                scalesWithOxygen      = true,
                oxygenRequiredPerSecond = 2f
            }
        };

        foreach (var def in builtIn)
            byId[def.id.ToLowerInvariant()] = def;
    }
}
