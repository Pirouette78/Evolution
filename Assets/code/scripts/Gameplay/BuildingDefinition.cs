using System;

/// <summary>
/// Données d'un type de bâtiment.
/// Chargé depuis StreamingAssets/Buildings/*.json via BuildingLibrary.
/// Un mod peut surcharger un bâtiment existant (même id) ou en créer de nouveaux.
/// </summary>
[Serializable]
public class BuildingDefinition
{
    // ── Identité ──────────────────────────────────────────────────────
    /// <summary>Clé stable (minuscules) : "poumon", "rate"…</summary>
    public string id;

    /// <summary>Nom affiché en UI.</summary>
    public string displayName;

    /// <summary>Chemin vers la texture POI dans Resources/ (sans extension).</summary>
    public string poiImagePath;

    // ── Liaison espèce ────────────────────────────────────────────────
    /// <summary>
    /// ID de l'espèce qui utilise ce bâtiment comme waypoint ("globulerouge", "globuleblanc"…).
    /// Prend le pas sur speciesSlot si renseigné.
    /// </summary>
    public string waypointSpeciesId;

    /// <summary>
    /// [Héritage] Index de slot GPU (0-5). Utilisé si waypointSpeciesId est vide.
    /// Ignoré quand waypointSpeciesId est défini.
    /// </summary>
    public int speciesSlot;

    /// <summary>
    /// Espèce liée : si renseignée, placer ce bâtiment crée AUSSI un waypoint Destination
    /// pour cette espèce au même endroit (ex: "globulerouge" pour la Rate).
    /// </summary>
    public string linkedSpeciesId;

    /// <summary>0 = Source (génère des agents ici), 1 = Destination (point d'arrivée).</summary>
    public int waypointType;

    // ── Spawn ─────────────────────────────────────────────────────────
    /// <summary>Taux de spawn de base (agents/seconde), modulé par l'efficacité.</summary>
    public float spawnsPerSecond;

    /// <summary>Population maximale pour l'espèce principale.</summary>
    public int maxPopulation;

    // ── Ressources ────────────────────────────────────────────────────
    /// <summary>Ressources consommées passivement (unités/seconde).</summary>
    public ResourceAmount[] consumes;

    /// <summary>Ressources produites passivement (unités/seconde).</summary>
    public ResourceAmount[] produces;

    // ── Efficacité conditionnelle ──────────────────────────────────────
    /// <summary>
    /// ID de la ressource dont dépend l'efficacité ("oxygen", "glucose"…).
    /// Laissé vide = pas de scaling. Prend le pas sur scalesWithOxygen.
    /// </summary>
    public string scalesWithResource;

    /// <summary>Quantité de la ressource nécessaire (u/s) pour fonctionner à 100%.</summary>
    public float resourceRequiredPerSecond;

    /// <summary>[Héritage] Équivalent à scalesWithResource = "oxygen". Ignoré si scalesWithResource est renseigné.</summary>
    public bool scalesWithOxygen;

    /// <summary>[Héritage] Équivalent à resourceRequiredPerSecond. Ignoré si resourceRequiredPerSecond > 0.</summary>
    public float oxygenRequiredPerSecond;

    // ── Propriétés résolues (runtime, non sérialisées) ────────────────

    [NonSerialized] private int _resolvedSlot = int.MinValue;

    /// <summary>
    /// Slot GPU résolu : utilise waypointSpeciesId → SpeciesLibrary en priorité,
    /// puis speciesSlot comme fallback.
    /// </summary>
    public int ResolvedSpeciesSlot
    {
        get
        {
            if (_resolvedSlot != int.MinValue) return _resolvedSlot;

            if (!string.IsNullOrEmpty(waypointSpeciesId) && SpeciesLibrary.Instance != null)
            {
                int s = SpeciesLibrary.Instance.GetSlot(waypointSpeciesId);
                if (s >= 0) { _resolvedSlot = s; return s; }
            }
            _resolvedSlot = speciesSlot;
            return speciesSlot;
        }
    }

    /// <summary>
    /// Ressource de scaling résolue : utilise scalesWithResource en priorité,
    /// puis "oxygen" si scalesWithOxygen est true (héritage).
    /// </summary>
    public string ResolvedScaleResource
    {
        get
        {
            if (!string.IsNullOrEmpty(scalesWithResource)) return scalesWithResource.ToLowerInvariant();
            if (scalesWithOxygen) return "oxygen";
            return null;
        }
    }

    /// <summary>
    /// Quantité de scaling résolue : utilise resourceRequiredPerSecond en priorité,
    /// puis oxygenRequiredPerSecond (héritage).
    /// </summary>
    public float ResolvedScaleAmount =>
        resourceRequiredPerSecond > 0f ? resourceRequiredPerSecond : oxygenRequiredPerSecond;
}

/// <summary>Paire ressource/quantité utilisée dans consumes[] et produces[].</summary>
[Serializable]
public class ResourceAmount
{
    /// <summary>ID de la ressource : "oxygen", "glucose"… (définie dans resources.json)</summary>
    public string resource;

    /// <summary>Quantité par seconde.</summary>
    public float amount;
}
