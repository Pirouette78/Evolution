using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Charge et expose les types de ressources depuis StreamingAssets/Resources/resources.json.
/// Fallback sur des définitions built-in si le fichier est absent.
/// Permet d'ajouter de nouvelles ressources sans modifier le code.
/// </summary>
public class ResourceLibrary : MonoBehaviour
{
    public static ResourceLibrary Instance { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void AutoCreate()
    {
        if (Instance != null) return;
        var go = new GameObject("[ResourceLibrary]");
        go.AddComponent<ResourceLibrary>();
        DontDestroyOnLoad(go);
    }

    // JsonUtility ne peut pas désérialiser un tableau JSON directement
    [Serializable]
    private class Wrapper { public ResourceDefinition[] resources; }

    private readonly List<ResourceDefinition>    definitions = new List<ResourceDefinition>();
    private readonly HashSet<string>             ids         = new HashSet<string>();

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        LoadFromFile();
        if (definitions.Count == 0)
        {
            Debug.LogWarning("[ResourceLibrary] resources.json absent — définitions built-in utilisées.");
            LoadBuiltIn();
        }

        Debug.Log($"[ResourceLibrary] {definitions.Count} ressource(s) : {string.Join(", ", ids)}");
    }

    // ── API publique ─────────────────────────────────────────────────

    public bool Exists(string id) => ids.Contains(id?.ToLowerInvariant());

    public IReadOnlyList<ResourceDefinition> GetAll() => definitions;

    // ── Chargement ───────────────────────────────────────────────────

    private void LoadFromFile()
    {
        string path = Path.Combine(Application.streamingAssetsPath, "Resources", "resources.json");
        if (!File.Exists(path)) return;

        try
        {
            string json    = File.ReadAllText(path);
            var    wrapper = JsonUtility.FromJson<Wrapper>("{\"resources\":" + json + "}");
            if (wrapper?.resources == null) return;

            foreach (var r in wrapper.resources)
            {
                if (string.IsNullOrEmpty(r.id)) continue;
                r.id = r.id.ToLowerInvariant();
                definitions.Add(r);
                ids.Add(r.id);
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[ResourceLibrary] Erreur lecture : {e.Message}");
        }
    }

    private void LoadBuiltIn()
    {
        Add("oxygen",  "Oxygène");
        Add("glucose", "Glucose");
        Add("iron",    "Fer");
    }

    private void Add(string id, string displayName)
    {
        definitions.Add(new ResourceDefinition { id = id, displayName = displayName });
        ids.Add(id);
    }
}
