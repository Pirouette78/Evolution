using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Charge et expose les définitions d'espèces depuis StreamingAssets/Species/*.json.
/// Applique automatiquement les paramètres GPU à SlimeMapRenderer dès qu'il est prêt.
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
            Debug.LogError("[SpeciesLibrary] Aucun fichier JSON trouvé dans StreamingAssets/Species/. Créez au moins un fichier .json d'espèce.");

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
            if (def.slotIndex < 0 || def.slotIndex >= 16) continue;
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

    /// <summary>Retourne la définition par slot GPU (0-15). Null si inconnu.</summary>
    public SpeciesDefinition GetBySlot(int slotIndex)
    {
        foreach (var def in byId.Values)
            if (def.slotIndex == slotIndex) return def;
        return null;
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

    private void Register(SpeciesDefinition def)
    {
        def.id = def.id.ToLowerInvariant();
        byId[def.id] = def;
    }
}
