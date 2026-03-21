using System;

/// <summary>
/// Données d'un type de bâtiment : taux de spawn, ressources consommées/produites, efficacité conditionnelle.
/// Chargé depuis StreamingAssets/Buildings/*.json au démarrage via BuildingLibrary.
/// Un mod peut surcharger n'importe quel bâtiment existant (même id) ou en créer de nouveaux.
/// </summary>
[Serializable]
public class BuildingDefinition
{
    // ── Identité ──────────────────────────────────────────────────────
    /// <summary>Clé stable utilisée par les mods pour surcharger ce bâtiment.</summary>
    public string id;               // "poumon", "rate"

    /// <summary>Nom affiché en UI.</summary>
    public string displayName;      // "Poumon", "Rate"

    /// <summary>Chemin vers la texture POI dans Resources/ (sans extension).</summary>
    public string poiImagePath;     // "POI/poumon"

    /// <summary>Index espèce 0-5.</summary>
    public int    speciesSlot;

    /// <summary>0 = Source (spawner), 1 = Destination (récepteur).</summary>
    public int    waypointType;

    // ── Spawn ─────────────────────────────────────────────────────────
    /// <summary>Taux de spawn de base (entités/seconde), modulé par l'efficacité.</summary>
    public float  spawnsPerSecond;

    /// <summary>Population maximale pour cette espèce.</summary>
    public int    maxPopulation;

    // ── Ressources ────────────────────────────────────────────────────
    /// <summary>Ressources consommées passivement par ce bâtiment (unités/seconde).</summary>
    public ResourceAmount[] consumes;

    /// <summary>Ressources produites passivement par ce bâtiment (unités/seconde).</summary>
    public ResourceAmount[] produces;

    // ── Efficacité conditionnelle ──────────────────────────────────────
    /// <summary>
    /// Si true, spawnsPerSecond est multiplié par (oxygène_consommé / oxygenRequiredPerSecond),
    /// plafonné à [0..1]. Le bâtiment ralentit proportionnellement au manque d'oxygène.
    /// </summary>
    public bool   scalesWithOxygen;

    /// <summary>Oxygène nécessaire (u/s) pour fonctionner à 100%.</summary>
    public float  oxygenRequiredPerSecond;
}

/// <summary>Paire ressource/quantité utilisée dans consumes[] et produces[].</summary>
[Serializable]
public class ResourceAmount
{
    /// <summary>Nom de la ressource : "oxygen", "glucose", "iron".</summary>
    public string resource;

    /// <summary>Quantité par seconde (pour consumes/produces passifs).</summary>
    public float  amount;
}
