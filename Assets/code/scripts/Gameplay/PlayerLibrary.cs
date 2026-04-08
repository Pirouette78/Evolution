using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Charge les définitions de joueurs depuis StreamingAssets/Players/*.json.
/// Assigne un slot GPU à chaque paire (joueur × espèce × catégorie) et configure
/// SlimeMapRenderer (couleurs, paramètres, guerre) dès qu'il est prêt.
///
/// Multi-catégorie : une SpeciesDefinition avec N categories[] génère N slots GPU
/// qui partagent tous la même tranche TrailMap (trailSliceIndex).
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
        /// <summary>Catégorie dans la SpeciesDefinition (0 pour espèce simple).</summary>
        public int                categoryIndex;
        /// <summary>SpeciesDefinition aplatie pour cette catégorie.</summary>
        public SpeciesDefinition  categoryDef;
    }

    private readonly List<PlayerDefinition>  players   = new List<PlayerDefinition>();
    private readonly List<SlotInfo>          slots     = new List<SlotInfo>();

    // Clé : "playerId:speciesId" → premier slot de cette espèce (catégorie 0)
    private readonly Dictionary<string, int> slotIndex = new Dictionary<string, int>();

    // Clé : "playerId:speciesId:catIndex" → slot précis
    private readonly Dictionary<string, int> slotIndexCat = new Dictionary<string, int>();

    // trailSliceIndex attribués : clé "playerId:speciesId" → trailSliceIndex
    private readonly Dictionary<string, int> trailSlices = new Dictionary<string, int>();

    // Nombre réel de tranches TrailMap actives (avec RGBA packing : < numActiveSlots)
    private int activeTrailSliceCount = 0;

    public int NumActiveSlots        => slots.Count;
    public int NumActiveTrailSlices  => activeTrailSliceCount;

    // ── Lifecycle ────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        LoadFromStreamingAssets();
        if (players.Count == 0)
            Debug.LogError("[PlayerLibrary] Aucun fichier JSON trouvé dans StreamingAssets/Players/. Créez au moins un fichier .json de joueur.");
    }

    private IEnumerator Start()
    {
        // Attendre que SpeciesLibrary soit prêt (son Awake charge les JSON)
        while (SpeciesLibrary.Instance == null)
            yield return null;

        AssignSlots();
        Debug.Log($"[PlayerLibrary] {players.Count} joueur(s), {slots.Count} slots GPU, {activeTrailSliceCount} tranches TrailMap (RGBA packing).");

        while (SlimeMapRenderer.Instance == null || !SlimeMapRenderer.Instance.IsReady)
            yield return null;

        ApplyToRenderer();
    }

    // ── Slot assignment ──────────────────────────────────────────────

    private void AssignSlots()
    {
        slots.Clear();
        slotIndex.Clear();
        slotIndexCat.Clear();
        trailSlices.Clear();

        int nextTrailSlice = 0;

        foreach (var player in players)
        {
            if (player.species == null) continue;
            foreach (var entry in player.species)
            {
                if (entry == null || string.IsNullOrEmpty(entry.speciesId)) continue;

                string specId  = entry.speciesId.ToLowerInvariant();
                string baseKey = MakeKey(player.id, specId);

                if (slotIndex.ContainsKey(baseKey)) continue; // déjà enregistré

                // Résoudre la SpeciesDefinition pour connaître CategoryCount
                var def = SpeciesLibrary.Instance?.Get(specId);
                int catCount = def != null ? def.CategoryCount : 1;

                // Vérifier qu'on a assez de slots GPU restants
                if (slots.Count + catCount > SlimeMapRenderer.MaxSlots)
                {
                    Debug.LogWarning($"[PlayerLibrary] Limite {SlimeMapRenderer.MaxSlots} slots GPU atteinte — {specId} ignoré.");
                    continue;
                }

                int speciesIdBase = slots.Count;
                if (def != null) def.speciesIdBase = speciesIdBase;

                // Enregistrer le premier slot (catégorie 0)
                slotIndex[baseKey] = slots.Count;

                // RGBA packing : 4 catégories par tranche TrailMap
                // catégorie c → tranche = baseSlice + c/4, canal = c % 4
                int baseSlice = nextTrailSlice;

                for (int c = 0; c < catCount; c++)
                {
                    string catKey = MakeKeyCat(player.id, specId, c);
                    slotIndexCat[catKey] = slots.Count;

                    int tsi = baseSlice + c / 4;
                    int tch = c % 4;
                    string trailKey = MakeKeyCat(player.id, specId, c);
                    trailSlices[trailKey] = tsi; // clé unique par catégorie

                    var catDef = def?.GetCategoryDefinition(c) ?? new SpeciesDefinition
                    {
                        id              = specId,
                        trailSliceIndex = tsi,
                        speciesIdBase   = speciesIdBase,
                    };

                    catDef.slotIndex       = slots.Count;
                    catDef.trailSliceIndex = tsi;
                    catDef.trailChannel    = tch;
                    catDef.speciesIdBase   = speciesIdBase;

                    slots.Add(new SlotInfo
                    {
                        player        = player,
                        speciesEntry  = entry,
                        categoryIndex = c,
                        categoryDef   = catDef,
                    });
                }

                // Avancer nextTrailSlice : ceiling division de catCount par 4
                nextTrailSlice = baseSlice + (catCount + 3) / 4;

                // trailSlices[baseKey] = premier tsi de l'espèce (pour GetTrailSliceIndex rétro-compat)
                if (!trailSlices.ContainsKey(baseKey))
                    trailSlices[baseKey] = baseSlice;
            }
        }

        activeTrailSliceCount = nextTrailSlice;
    }

    // ── Application dans SlimeMapRenderer ───────────────────────────

    private void ApplyToRenderer()
    {
        var smr = SlimeMapRenderer.Instance;
        smr.numActiveSlots        = slots.Count;
        smr.numActiveTrailSlices  = activeTrailSliceCount; // RGBA packing : jusqu'à 4 catégories par tranche

        // Remplir le tableau trailSliceToSlot : trailSliceIndex → premier slot de cette tranche
        // On itère à rebours pour que la catégorie 0 (première) écrase les suivantes
        for (int s = slots.Count - 1; s >= 0; s--)
        {
            int tsi = slots[s].categoryDef?.trailSliceIndex ?? s;
            if (tsi >= 0 && tsi < SlimeMapRenderer.MaxSlots)
                smr.trailSliceToSlotData[tsi] = s;
        }
        smr.trailSliceToSlotBuffer.SetData(smr.trailSliceToSlotData);

        for (int s = 0; s < slots.Count; s++)
        {
            var info   = slots[s];
            var catDef = info.categoryDef;
            string specId = info.speciesEntry.speciesId.ToLowerInvariant();

            smr.speciesIds[s] = catDef.id; // ex: "humain_0", "humain_1"…

            // Paramètres comportement (depuis catDef aplati)
            if (catDef != null)
            {
                var gpuSettings = catDef.ToSpeciesSettings();
                smr.speciesSettings[s]       = gpuSettings;
                smr.speciesBlocksMovement[s] = catDef.blocksMovement;
            }

            // Couleur : catégorie JSON en priorité, player JSON en fallback
            Vector4 slotColor;
            if (catDef?.color != null && catDef.color.Length >= 3)
                slotColor = new Vector4(catDef.color[0], catDef.color[1], catDef.color[2], 1f);
            else
                slotColor = info.speciesEntry.ToVector4();
            smr.SetSlotColor(s, slotColor);
        }

        // Matrice d'interaction diplomatique (data-driven via DiplomacyLibrary)
        var diploLib = DiplomacyLibrary.Instance;
        for (int a = 0; a < slots.Count; a++)
        {
            for (int b = a + 1; b < slots.Count; b++)
            {
                if (slots[a].player.id == slots[b].player.id) continue;

                bool atWar  = IsWarDeclared (slots[a].player, slots[b].player.id)
                           || IsWarDeclared (slots[b].player, slots[a].player.id);
                bool isAlly = IsAllyDeclared(slots[a].player, slots[b].player.id)
                           || IsAllyDeclared(slots[b].player, slots[a].player.id);

                DiplomacyLevelDefinition level =
                    atWar  ? diploLib?.WarLevel          :
                    isAlly ? diploLib?.DefaultAllyLevel  :
                             diploLib?.DefaultNeutralLevel;

                if (level != null)
                    smr.SetInteractionSymmetric(a, b, level);
            }
        }

        // Intra-joueur : catégories différentes du même joueur → Allié par défaut
        // (inclut les catégories de la même espèce entre elles)
        var allyLevel = diploLib?.DefaultAllyLevel;
        for (int a = 0; a < slots.Count; a++)
            for (int b = 0; b < slots.Count; b++)
            {
                if (a == b) continue;
                if (slots[a].player.id != slots[b].player.id) continue;
                if (allyLevel != null)
                    smr.SetInteractionOneWay(a, b, allyLevel);
                else
                    smr.SetInteraction(a, b, 0.5f);
            }

        Debug.Log($"[PlayerLibrary] {slots.Count} slots configurés ({trailSlices.Count} tranches TrailMap).");
    }

    private static bool IsWarDeclared(PlayerDefinition player, string otherId)
    {
        if (player.warsWith == null) return false;
        foreach (var id in player.warsWith)
            if (id?.ToLowerInvariant() == otherId) return true;
        return false;
    }

    private static bool IsAllyDeclared(PlayerDefinition player, string otherId)
    {
        if (player.alliesWith == null) return false;
        foreach (var id in player.alliesWith)
            if (id?.ToLowerInvariant() == otherId) return true;
        return false;
    }

    // ── API publique ─────────────────────────────────────────────────

    /// <summary>
    /// Slot GPU pour (joueur, espèce, catégorie). -1 si non trouvé.
    /// catIndex=0 pour espèce simple ou première catégorie.
    /// </summary>
    public int GetSlotIndex(string playerId, string speciesId, int catIndex = 0)
    {
        if (string.IsNullOrEmpty(playerId) || string.IsNullOrEmpty(speciesId)) return -1;
        string pid = playerId.ToLowerInvariant();
        string sid = speciesId.ToLowerInvariant();
        if (catIndex == 0 && slotIndex.TryGetValue(MakeKey(pid, sid), out int s0)) return s0;
        return slotIndexCat.TryGetValue(MakeKeyCat(pid, sid, catIndex), out int s) ? s : -1;
    }

    /// <summary>
    /// trailSliceIndex pour (joueur, espèce). -1 si non trouvé.
    /// Partagé par toutes les catégories de cette espèce.
    /// </summary>
    public int GetTrailSliceIndex(string playerId, string speciesId)
    {
        if (string.IsNullOrEmpty(playerId) || string.IsNullOrEmpty(speciesId)) return -1;
        return trailSlices.TryGetValue(MakeKey(playerId.ToLowerInvariant(), speciesId.ToLowerInvariant()), out int tsi) ? tsi : -1;
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

    public IReadOnlyList<PlayerDefinition> GetAll()      => players;
    public IReadOnlyList<SlotInfo>         GetAllSlots() => slots;

    /// <summary>
    /// Retourne les speciesIds de BASE d'un joueur — un entry par espèce, sans doublons de catégorie.
    /// Ex: pour human (3 catégories), retourne ["human"] et non ["human_0","human_1","human_2"].
    /// </summary>
    public List<string> GetSpeciesForPlayer(string playerId)
    {
        var result = new List<string>();
        var seen   = new HashSet<string>();
        string pid = playerId?.ToLowerInvariant();
        foreach (var sl in slots)
        {
            if (sl.player.id != pid) continue;
            string baseId = sl.speciesEntry.speciesId.ToLowerInvariant();
            if (seen.Add(baseId)) result.Add(baseId);
        }
        return result;
    }

    /// <summary>
    /// Retourne les noms de catégories d'une espèce pour un joueur donné.
    /// Pour une espèce simple (pas de catégories), retourne une liste avec le displayName de l'espèce.
    /// </summary>
    public List<string> GetCategoriesForSpecies(string playerId, string baseSpeciesId)
    {
        var result = new List<string>();
        string pid = playerId?.ToLowerInvariant();
        string sid = baseSpeciesId?.ToLowerInvariant();
        var def = SpeciesLibrary.Instance?.Get(sid);
        if (def != null && def.CategoryCount > 1 && def.categories != null)
        {
            foreach (var cat in def.categories)
                result.Add(cat);
        }
        else
        {
            // Espèce simple — une seule "catégorie" avec le displayName
            result.Add(def?.displayName ?? baseSpeciesId);
        }
        return result;
    }

    /// <summary>
    /// Slot GPU pour (joueur, espèce de base, index de catégorie).
    /// Pour une espèce simple, catIndex = 0.
    /// </summary>
    public int GetSlotIndexByCat(string playerId, string baseSpeciesId, int catIndex)
        => GetSlotIndex(playerId, baseSpeciesId, catIndex);

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

    private static string MakeKey(string playerId, string speciesId)
        => $"{playerId}:{speciesId}";

    private static string MakeKeyCat(string playerId, string speciesId, int catIndex)
        => $"{playerId}:{speciesId}:{catIndex}";
}
