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
    Vegetal     = 6, // Stationnaire, émet traînée en disque (arbre, fleur, pollen)
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

    // ── Répulsion et densité ──────────────────────────────────────────
    /// <summary>Force de répulsion intra-espèce courte portée. 0 = désactivé.</summary>
    public float repulsionStrength;

    /// <summary>Rayon de détection pour la répulsion (pixels). Ex: 5.</summary>
    public float repulsionRadius = 5f;

    /// <summary>Seuil de densité locale avant affaiblissement de l'attraction. 0 = désactivé.</summary>
    public float densityLimit;

    /// <summary>Rayon visuel de l'agent en pixels (disque dans AgentMap). 0 = pixel unique (défaut). 1 = croix 5px. 3 = disque ~29px.</summary>
    public int agentRadius = 0;

    /// <summary>Rayon d'émission de traînée en pixels. 0 = pixel unique. >0 = disque de pollen (recommandé pour Vegetal).</summary>
    public int trailEmitRadius = 0;

    // ── Germination (Végétal) ──────────────────────────────────────────────
    /// <summary>Probabilité de germination quand traînée = 1.0 (ex: 0.01 = 1%). 0 = désactivé.</summary>
    public float seedProbHigh = 0f;
    /// <summary>Probabilité de germination quand traînée = 0.25 (ex: 0.0025 = 0.25%).</summary>
    public float seedProbLow = 0f;
    /// <summary>Secondes entre chaque scan de germination (C# uniquement, pas dans le GPU struct).</summary>
    public float seedInterval = 5f;

    // ── Sprite visuel ─────────────────────────────────────────────────
    /// <summary>Nom du fichier sprite dans StreamingAssets/Sprites/ (sans .png). Vide = pas de sprite.</summary>
    public string spriteName    = "";
    /// <summary>Largeur du sprite en tiles (0 = pas de sprite).</summary>
    public int    spriteTilesW  = 0;
    /// <summary>Hauteur du sprite en tiles.</summary>
    public int    spriteTilesH  = 0;
    /// <summary>Colonnes d'animation dans le sprite sheet (1 par défaut).</summary>
    public int    spriteFramesW = 1;
    /// <summary>Lignes d'animation (direction de vue) dans le sprite sheet (1 par défaut).</summary>
    public int    spriteFramesH = 1;
    /// <summary>Ancre horizontale : 0=gauche, 0.5=centre, 1=droite (fraction du sprite).</summary>
    public float  spriteAnchorX = 0.5f;
    /// <summary>Ancre verticale : 0=bas, 1=haut (fraction du sprite).</summary>
    public float  spriteAnchorY = 0f;

    // ── Zone bloquante rectangulaire ───────────────────────────────────
    /// <summary>Offset X depuis l'ancre en tiles (gauche=négatif). Utilisé si blockTilesW > 0.</summary>
    public float  blockOffsetX  = 0f;
    /// <summary>Offset Y depuis l'ancre en tiles (bas=0).</summary>
    public float  blockOffsetY  = 0f;
    /// <summary>Largeur du rectangle bloquant en tiles. 0 = utiliser agentRadius (disque).</summary>
    public float  blockTilesW   = 0f;
    /// <summary>Hauteur du rectangle bloquant en tiles.</summary>
    public float  blockTilesH   = 0f;

    /// <summary>
    /// Si true, cette unité bloque la carte de déplacement (WalkabilityGrid + flow fields).
    /// Les agents ne peuvent pas traverser les cellules occupées par ces unités.
    /// Mis à jour dynamiquement à l'apparition/mort de l'unité. CPU uniquement — pas dans le GPU struct.
    /// </summary>
    public bool blocksMovement = false;

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
        repulsionStrength     = repulsionStrength,
        repulsionRadius       = repulsionRadius,
        densityLimit          = densityLimit,
        agentRadius           = Mathf.Max(0, agentRadius),
        trailEmitRadius       = Mathf.Max(0, trailEmitRadius),
        seedProbHigh          = seedProbHigh,
        seedProbLow           = seedProbLow,
    };
}
