using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Charge et expose les définitions d'espèces depuis StreamingAssets/Species/*.json.
/// Applique automatiquement les paramètres GPU à SlimeMapRenderer dès qu'il est prêt.
/// Fallback sur des définitions built-in si le dossier est absent.
///
/// Ajouter une espèce = créer un fichier JSON dans StreamingAssets/Species/, sans toucher au code.
/// </summary>
public class SpeciesLibrary : MonoBehaviour
{
    public static SpeciesLibrary Instance { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void AutoCreate()
    {
        if (Instance != null) return;
        var go = new GameObject("[SpeciesLibrary]");
        go.AddComponent<SpeciesLibrary>();
        DontDestroyOnLoad(go);
    }

    private readonly Dictionary<string, SpeciesDefinition> byId = new Dictionary<string, SpeciesDefinition>();

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        LoadFromStreamingAssets();
        if (byId.Count == 0)
        {
            Debug.LogWarning("[SpeciesLibrary] Aucun fichier JSON trouvé — définitions built-in utilisées.");
            LoadBuiltIn();
        }

        Debug.Log($"[SpeciesLibrary] {byId.Count} espèce(s) : {string.Join(", ", byId.Keys)}");
    }

    private IEnumerator Start()
    {
        // Attendre que le renderer soit prêt, puis appliquer les paramètres GPU
        while (SlimeMapRenderer.Instance == null || !SlimeMapRenderer.Instance.IsReady)
            yield return null;

        ApplyToRenderer();
    }

    // ── Application GPU ──────────────────────────────────────────────

    private void ApplyToRenderer()
    {
        var smr = SlimeMapRenderer.Instance;
        // Applique uniquement les paramètres GPU (speciesSettings).
        // speciesIds est géré exclusivement par SetSpeciesType / l'init dans Awake,
        // pour rester synchronisé avec ce que le joueur a choisi dans l'UI.
        foreach (var def in byId.Values)
        {
            if (def.slotIndex < 0 || def.slotIndex >= 6) continue;
            // N'applique les settings JSON que si le slot a déjà ce type assigné
            string currentId = smr.speciesIds[def.slotIndex];
            if (currentId == def.id)
                smr.speciesSettings[def.slotIndex] = def.ToSpeciesSettings();
        }
    }

    // ── API publique ─────────────────────────────────────────────────

    /// <summary>Retourne la définition par id ("globulerouge", …). Null si inconnu.</summary>
    public SpeciesDefinition Get(string id)
    {
        byId.TryGetValue(id?.ToLowerInvariant(), out var def);
        return def;
    }

    /// <summary>Retourne le slot GPU (0-5) pour un id d'espèce. -1 si inconnu.</summary>
    public int GetSlot(string id)
    {
        var def = Get(id);
        return def != null ? def.slotIndex : -1;
    }

    public IReadOnlyCollection<SpeciesDefinition> GetAll() => byId.Values;

    // ── Chargement ───────────────────────────────────────────────────

    private void LoadFromStreamingAssets()
    {
        string basePath = Path.Combine(Application.streamingAssetsPath, "Species");
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
            var def = JsonUtility.FromJson<SpeciesDefinition>(File.ReadAllText(path));
            if (def == null || string.IsNullOrEmpty(def.id))
            {
                Debug.LogWarning($"[SpeciesLibrary] Ignoré (id manquant) : {path}");
                return;
            }
            def.id = def.id.ToLowerInvariant();
            byId[def.id] = def;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[SpeciesLibrary] Erreur {path} : {e.Message}");
        }
    }

    // ── Fallback built-in ────────────────────────────────────────────

    private void LoadBuiltIn()
    {
        Register(new SpeciesDefinition
        {
            id = "globulerouge", displayName = "Globule Rouge", slotIndex = 0,
            moveSpeed = 60, turnSpeed = 20, sensorAngleDeg = 30, sensorOffsetDst = 15,
            sensorSize = 2, maxAge = 400, trailWeight = 8, decayRate = 0.3f, diffuseRate = 3f,
            warDamageRate = 0.1f, behaviorType = "GlobuleRouge",
            energyReward = 5f, arrivalRadius = 20f, loadingTime = 2f, unloadingTime = 1f
        });
        Register(new SpeciesDefinition
        {
            id = "globuleblanc", displayName = "Globule Blanc", slotIndex = 1,
            moveSpeed = 120, turnSpeed = 20, sensorAngleDeg = 30, sensorOffsetDst = 25,
            sensorSize = 2, maxAge = 60, trailWeight = 1, decayRate = 3f, diffuseRate = 0.5f,
            warDamageRate = 3f, behaviorType = "GlobuleBlanc"
        });
        Register(new SpeciesDefinition
        {
            id = "bacterie", displayName = "Bactérie", slotIndex = 2,
            moveSpeed = 50, turnSpeed = 20, sensorAngleDeg = 45, sensorOffsetDst = 15,
            sensorSize = 2, maxAge = 30, trailWeight = 6, decayRate = 3f, diffuseRate = 4f,
            warDamageRate = 4f, behaviorType = "Bacterie",
            energyConsumptionRate = 5f, startingEnergy = 100f
        });
        Register(new SpeciesDefinition
        {
            id = "virus", displayName = "Virus", slotIndex = 3,
            moveSpeed = 80, turnSpeed = 30, sensorAngleDeg = 20, sensorOffsetDst = 20,
            sensorSize = 2, maxAge = 20, trailWeight = 1, decayRate = 5f, diffuseRate = 0.3f,
            warDamageRate = 5f, behaviorType = "Virus"
        });
        Register(new SpeciesDefinition
        {
            id = "plaquette", displayName = "Plaquette", slotIndex = 4,
            moveSpeed = 25, turnSpeed = 5, sensorAngleDeg = 45, sensorOffsetDst = 8,
            sensorSize = 4, maxAge = 300, trailWeight = 20, decayRate = 0.1f, diffuseRate = 6f,
            warDamageRate = 0f, behaviorType = "Plaquette"
        });
    }

    private void Register(SpeciesDefinition def)
    {
        def.id = def.id.ToLowerInvariant();
        byId[def.id] = def;
    }
}
