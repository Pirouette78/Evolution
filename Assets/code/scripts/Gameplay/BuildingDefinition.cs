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
    /// Espèce liée : si renseignée, placer ce bâtiment crée AUSSI un waypoint Destination
    /// pour cette espèce au même endroit (ex: "globulerouge" pour la Rate).
    /// </summary>
    public string linkedSpeciesId;

    /// <summary>0 = Source (génère des agents ici), 1 = Destination (point d'arrivée).</summary>
    public int waypointType;

    // ── Outputs (espèces produites) ───────────────────────────────────
    /// <summary>
    /// Liste des espèces produites par ce bâtiment.
    /// Pour un bâtiment Source (type=0) : chaque entrée crée sa propre Hive.
    /// Pour un bâtiment Destination (type=1) : seul speciesId est utilisé (pour le routage).
    /// </summary>
    public OutputEntry[] outputs;

    /// <summary>Retourne outputs ou un tableau vide si null.</summary>
    public OutputEntry[] ResolvedOutputs() => outputs ?? new OutputEntry[0];

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

    // ── Propriétés résolues (runtime, non sérialisées) ────────────────

    /// <summary>Ressource de scaling résolue (null si non définie).</summary>
    public string ResolvedScaleResource =>
        string.IsNullOrEmpty(scalesWithResource) ? null : scalesWithResource.ToLowerInvariant();

    /// <summary>Quantité de la ressource de scaling nécessaire par seconde.</summary>
    public float ResolvedScaleAmount => resourceRequiredPerSecond;
}

/// <summary>Une espèce produite par un bâtiment, avec son propre taux et sa population max.</summary>
[Serializable]
public class OutputEntry
{
    public string speciesId;
    public float  spawnsPerSecond;
    public int    maxPopulation;
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
