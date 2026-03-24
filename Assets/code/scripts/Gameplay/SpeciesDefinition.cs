using System;
using UnityEngine;

/// <summary>
/// Comportement GPU d'un agent. La valeur int correspond au case dans UpdateAgents.compute.
/// Ajouter un comportement = ajouter une valeur ici + un case dans le shader.
/// </summary>
public enum AgentBehavior
{
    Wanderer    = 0, // errance phéromonale pure (suit/fuit les traînées)
    Scavenger   = 1, // consomme de l'énergie, absorbe les traînées des Transporters
    Transporter = 2, // navigue entre waypoints Source et Destination
    Defender    = 3, // (à implémenter)
    Parasite    = 4, // (à implémenter)
    Eraser      = 5, // (à implémenter)
}

/// <summary>
/// Paramètres complets d'une espèce d'agent.
/// Chargé depuis StreamingAssets/Species/*.json au démarrage via SpeciesLibrary.
/// Ajouter une espèce = créer un fichier JSON, sans modifier le code.
/// </summary>
[Serializable]
public class SpeciesDefinition
{
    // ── Identité ──────────────────────────────────────────────────────
    /// <summary>Identifiant stable (minuscules) : "globulerouge", "globuleblanc"…</summary>
    public string id;

    /// <summary>Nom affiché en UI.</summary>
    public string displayName;

    /// <summary>Couleur canonique UI [R, G, B] en 0-1. Utilisée pour les boutons et la matrice diplomatique.</summary>
    public float[] color = new float[3];

    /// <summary>
    /// Slot GPU assigné à cette espèce. NE PAS mettre dans le JSON —
    /// calculé automatiquement à l'exécution par PlayerLibrary.ApplyToRenderer().
    /// </summary>
    [System.NonSerialized] public int slotIndex = -1;

    // ── Mouvement ─────────────────────────────────────────────────────
    public float moveSpeed;
    public float turnSpeed;

    /// <summary>Angle du senseur en degrés (converti en radians à l'usage).</summary>
    public float sensorAngleDeg;
    public float sensorOffsetDst;
    public int   sensorSize;

    // ── Cycle de vie ──────────────────────────────────────────────────
    public float maxAge;
    public float maxHealth = 100f;  // Points de vie max (combat). 0 dans le JSON = utilise cette valeur par défaut.
    public float trailWeight;
    public float decayRate;
    public float diffuseRate;
    public float warDamageRate;
    public float trailErasePower;

    // ── Comportement ──────────────────────────────────────────────────
    /// <summary>
    /// Comportement de l'agent. Correspond à l'enum AgentBehavior.
    /// JSON : "behaviorType": "Transporter"  (nom de l'enum, insensible à la casse)
    /// </summary>
    public string behaviorType;

    public float energyConsumptionRate;
    public float energyReward;
    public float startingEnergy;

    // ── Navigation (agents à waypoints type GlobuleRouge) ─────────────
    public float arrivalRadius;
    public float loadingTime;
    public float unloadingTime;

    /// <summary>Quantité de ressource transportée par agent par voyage (0 = non-transporteur).</summary>
    public float payloadCapacity;

    /// <summary>Si true, l'agent attend en navState 2 jusqu'à ce que le stock soit disponible.</summary>
    public bool waitForStock;

    // ── Conversion ───────────────────────────────────────────────────

    public int BehaviorTypeInt => System.Enum.TryParse<AgentBehavior>(behaviorType, true, out var b) ? (int)b : 0;

    /// <summary>Construit le struct GPU correspondant.</summary>
    public SlimeMapRenderer.SpeciesSettings ToSpeciesSettings() => new SlimeMapRenderer.SpeciesSettings
    {
        moveSpeed             = moveSpeed,
        turnSpeed             = turnSpeed,
        sensorAngleRad        = sensorAngleDeg * Mathf.Deg2Rad,
        sensorOffsetDst       = sensorOffsetDst,
        sensorSize            = sensorSize,
        maxAge                = maxAge,
        trailWeight           = trailWeight,
        decayRate             = decayRate,
        diffuseRate           = diffuseRate,
        warDamageRate         = warDamageRate,
        trailErasePower       = trailErasePower,
        maxHealth             = maxHealth > 0 ? maxHealth : 100f,
        behaviorType          = BehaviorTypeInt,
        energyConsumptionRate = energyConsumptionRate,
        energyReward          = energyReward,
        startingEnergy        = startingEnergy,
        arrivalRadius         = arrivalRadius,
        loadingTime           = loadingTime,
        unloadingTime         = unloadingTime,
        waitForStock          = waitForStock ? 1f : 0f,
    };
}
