using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Charge les définitions de joueurs depuis StreamingAssets/Players/*.json.
/// Assigne un slot GPU à chaque paire (joueur × espèce) et configure
/// SlimeMapRenderer (couleurs, paramètres, guerre) dès qu'il est prêt.
///
/// Ajouter un joueur = créer un fichier JSON dans StreamingAssets/Players/, sans toucher au code.
/// </summary>
public class PlayerLibrary : MonoBehaviour
{
    public static PlayerLibrary Instance { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void AutoCreate()
    {
        if (Instance != null) return;
        var go = new GameObject("[PlayerLibrary]");
        go.AddComponent<PlayerLibrary>();
        DontDestroyOnLoad(go);
    }

    // ── Données ──────────────────────────────────────────────────────

    public struct SlotInfo
    {
        public PlayerDefinition   player;
        public PlayerSpeciesEntry speciesEntry;
    }

    private readonly List<PlayerDefinition> players = new List<PlayerDefinition>();
    private readonly List<SlotInfo>         slots   = new List<SlotInfo>();
    private readonly Dictionary<string, int> slotIndex = new Dictionary<string, int>();

    public int NumActiveSlots => slots.Count;

    // ── Lifecycle ────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        LoadFromStreamingAssets();
        if (players.Count == 0)
            Debug.LogError("[PlayerLibrary] Aucun fichier JSON trouvé dans StreamingAssets/Players/. Créez au moins un fichier .json de joueur.");

        AssignSlots();
        Debug.Log($"[PlayerLibrary] {players.Count} joueur(s), {slots.Count} slots GPU assignés.");
    }

    private IEnumerator Start()
    {
        while (SlimeMapRenderer.Instance == null || !SlimeMapRenderer.Instance.IsReady)
            yield return null;

        ApplyToRenderer();
    }

    // ── Slot assignment ──────────────────────────────────────────────

    private void AssignSlots()
    {
        slots.Clear();
        slotIndex.Clear();

        foreach (var player in players)
        {
            if (player.species == null) continue;
            foreach (var entry in player.species)
            {
                if (entry == null || string.IsNullOrEmpty(entry.speciesId)) continue;
                string key = MakeKey(player.id, entry.speciesId.ToLowerInvariant());
                if (slotIndex.ContainsKey(key)) continue;
                if (slots.Count >= 16)
                {
                    Debug.LogWarning("[PlayerLibrary] Limite de 16 slots GPU atteinte — espèces supplémentaires ignorées.");
                    break;
                }
                slotIndex[key] = slots.Count;
                slots.Add(new SlotInfo { player = player, speciesEntry = entry });
            }
        }
    }

    // ── Application dans SlimeMapRenderer ───────────────────────────

    private void ApplyToRenderer()
    {
        var smr = SlimeMapRenderer.Instance;
        smr.numActiveSlots = slots.Count;

        for (int s = 0; s < slots.Count; s++)
        {
            var info      = slots[s];
            string specId = info.speciesEntry.speciesId.ToLowerInvariant();

            smr.speciesIds[s] = specId;

            // Paramètres comportement (depuis SpeciesLibrary JSON)
            var def = SpeciesLibrary.Instance?.Get(specId);
            if (def != null)
                smr.speciesSettings[s] = def.ToSpeciesSettings();

            // Couleur
            smr.SetSlotColor(s, info.speciesEntry.ToVector4());
        }

        // Guerre : tous les slots de joueurs différents et déclarés ennemis
        for (int a = 0; a < slots.Count; a++)
        {
            for (int b = a + 1; b < slots.Count; b++)
            {
                if (slots[a].player.id == slots[b].player.id) continue;
                bool atWar = IsWarDeclared(slots[a].player, slots[b].player.id)
                          || IsWarDeclared(slots[b].player, slots[a].player.id);
                if (atWar) smr.SetWar(a, b, true);
            }
        }

        Debug.Log($"[PlayerLibrary] {slots.Count} slots configurés dans SlimeMapRenderer.");
    }

    private static bool IsWarDeclared(PlayerDefinition player, string otherId)
    {
        if (player.warsWith == null) return false;
        foreach (var id in player.warsWith)
            if (id?.ToLowerInvariant() == otherId) return true;
        return false;
    }

    // ── API publique ─────────────────────────────────────────────────

    /// <summary>Slot GPU pour (joueur, espèce). -1 si non trouvé.</summary>
    public int GetSlotIndex(string playerId, string speciesId)
    {
        if (string.IsNullOrEmpty(playerId) || string.IsNullOrEmpty(speciesId)) return -1;
        return slotIndex.TryGetValue(MakeKey(playerId.ToLowerInvariant(), speciesId.ToLowerInvariant()), out int s) ? s : -1;
    }

    public SlotInfo? GetSlotInfo(int slot)
        => (slot >= 0 && slot < slots.Count) ? slots[slot] : (SlotInfo?)null;

    public string GetPlayerIdForSlot(int slot)
        => (slot >= 0 && slot < slots.Count) ? slots[slot].player.id : null;

    public PlayerDefinition GetPlayer(string playerId)
    {
        string pid = playerId?.ToLowerInvariant();
        foreach (var p in players)
            if (p.id == pid) return p;
        return null;
    }

    public IReadOnlyList<PlayerDefinition>   GetAll()     => players;
    public IReadOnlyList<SlotInfo>           GetAllSlots() => slots;

    /// <summary>Retourne les speciesIds contrôlés par un joueur (dans l'ordre des slots).</summary>
    public List<string> GetSpeciesForPlayer(string playerId)
    {
        var result = new List<string>();
        string pid = playerId?.ToLowerInvariant();
        foreach (var sl in slots)
            if (sl.player.id == pid)
                result.Add(sl.speciesEntry.speciesId.ToLowerInvariant());
        return result;
    }

    // ── Chargement ───────────────────────────────────────────────────

    private void LoadFromStreamingAssets()
    {
        string basePath = Path.Combine(Application.streamingAssetsPath, "Players");
        if (!Directory.Exists(basePath)) return;
        foreach (string file in Directory.GetFiles(basePath, "*.json", SearchOption.TopDirectoryOnly))
            LoadFile(file);
    }

    private void LoadFile(string path)
    {
        try
        {
            var def = JsonUtility.FromJson<PlayerDefinition>(File.ReadAllText(path));
            if (def == null || string.IsNullOrEmpty(def.id))
            {
                Debug.LogWarning($"[PlayerLibrary] Ignoré (id manquant) : {path}");
                return;
            }
            def.id = def.id.ToLowerInvariant();
            players.Add(def);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[PlayerLibrary] Erreur {path} : {e.Message}");
        }
    }

    private static string MakeKey(string playerId, string speciesId) => $"{playerId}:{speciesId}";
}
