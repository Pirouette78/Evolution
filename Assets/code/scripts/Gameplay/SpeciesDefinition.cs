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
///
/// Multi-catégorie : une même espèce peut définir plusieurs sous-types (catégories)
/// via le tableau "categories". Chaque catégorie partage la même tranche TrailMap GPU
/// (trailSliceIndex) mais obtient son propre slot GPU (speciesIdIndex).
/// Les champs scalaires (moveSpeed, behaviorType…) sont des valeurs par défaut pour
/// les espèces sans catégories. Pour les espèces multi-catégories, utiliser les
/// variantes _arr : moveSpeed_arr[i] donne la valeur pour categories[i].
///
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
    /// Noms des sous-catégories. Null/vide = espèce simple (pas de multi-catégorie).
    /// Ex: ["Soldier", "Defender", "Bucheron"]
    /// </summary>
    public string[] categories;

    // ── Variantes par catégorie (parallèles à categories[]) ───────────
    // Si categories est défini, ces tableaux remplacent les scalaires correspondants.
    // color_arr est aplati : 3 floats par catégorie [R0,G0,B0, R1,G1,B1, …]
    public float[]  color_arr;          // 3 × CategoryCount floats
    public float[]  moveSpeed_arr;
    public float[]  turnSpeed_arr;
    public string[] behaviorType_arr;
    public float[]  sensorAngleDeg_arr;
    public float[]  sensorOffsetDst_arr;
    public float[]  trailWeight_arr;
    public float[]  trailEmitRadius_arr; // int values stored as float for JsonUtility

    /// <summary>
    /// Slot GPU assigné à cette espèce (espèce simple) ou à la catégorie 0.
    /// NE PAS mettre dans le JSON — calculé à l'exécution par PlayerLibrary.ApplyToRenderer().
    /// </summary>
    [System.NonSerialized] public int slotIndex = -1;

    /// <summary>
    /// Index de la tranche TrailMap GPU partagée par toutes les catégories de cette espèce.
    /// Assigné par PlayerLibrary. -1 = pas encore assigné.
    /// </summary>
    [System.NonSerialized] public int trailSliceIndex = -1;

    /// <summary>
    /// Index de base dans le tableau global des speciesId GPU.
    /// La catégorie k de cette espèce a speciesIdIndex = speciesIdBase + k.
    /// Assigné par PlayerLibrary. -1 = pas encore assigné.
    /// </summary>
    [System.NonSerialized] public int speciesIdBase = -1;

    /// <summary>
    /// Canal RGBA dans la tranche TrailMap partagée (0=R, 1=G, 2=B, 3=A).
    /// Assigné par PlayerLibrary selon la position de la catégorie dans son groupe de 4.
    /// </summary>
    [System.NonSerialized] public int trailChannel = 0;

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

    // ── Particle Life (interaction directe agent-à-agent) ─────────────
    /// <summary>Rayon du disque de balayage Particle Life autour de l'agent (pixels terrain). Ex: 20.</summary>
    public float particleLifeScanRadius = 20f;
    /// <summary>Pas d'échantillonnage dans le disque Particle Life (pixels terrain). Plus grand = plus rapide mais moins précis. Ex: 4.</summary>
    public float particleLifeStepSize = 4f;

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
    /// <summary>Largeur en pixels image d'une frame du sprite (0 = toute la texture).</summary>
    public int    spriteFramePixelW = 0;
    /// <summary>Hauteur en pixels image d'une frame du sprite (0 = toute la texture).</summary>
    public int    spriteFramePixelH = 0;

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

    // ── Multi-catégorie : helpers ──────────────────────────────────────

    /// <summary>Nombre de catégories. 1 pour une espèce simple.</summary>
    public int CategoryCount => (categories != null && categories.Length > 0) ? categories.Length : 1;

    /// <summary>
    /// Retourne une SpeciesDefinition aplatie pour la catégorie catIndex.
    /// Pour une espèce simple (CategoryCount==1), retourne this directement.
    /// Pour une espèce multi-catégorie, copie les champs partagés et surcharge
    /// avec les valeurs spécifiques à la catégorie.
    /// </summary>
    public SpeciesDefinition GetCategoryDefinition(int catIndex)
    {
        if (CategoryCount == 1) return this;

        var cat = new SpeciesDefinition
        {
            // Identité dérivée
            id          = id + "_" + catIndex,
            displayName = (categories != null && catIndex < categories.Length)
                              ? categories[catIndex]
                              : displayName + "_" + catIndex,

            // Champs partagés (valeurs scalaires de base)
            sensorSize            = sensorSize,
            maxAge                = maxAge,
            maxHealth             = maxHealth,
            decayRate             = decayRate,
            diffuseRate           = diffuseRate,
            warDamageRate         = warDamageRate,
            trailErasePower       = trailErasePower,
            energyConsumptionRate = energyConsumptionRate,
            energyReward          = energyReward,
            startingEnergy        = startingEnergy,
            arrivalRadius         = arrivalRadius,
            loadingTime           = loadingTime,
            unloadingTime         = unloadingTime,
            payloadCapacity       = payloadCapacity,
            waitForStock          = waitForStock,
            repulsionStrength     = repulsionStrength,
            repulsionRadius       = repulsionRadius,
            densityLimit          = densityLimit,
            particleLifeScanRadius = particleLifeScanRadius,
            particleLifeStepSize  = particleLifeStepSize,
            agentRadius           = agentRadius,
            seedProbHigh          = seedProbHigh,
            seedProbLow           = seedProbLow,
            seedInterval          = seedInterval,
            spriteName            = spriteName,
            spriteTilesW          = spriteTilesW,
            spriteTilesH          = spriteTilesH,
            spriteFramesW         = spriteFramesW,
            spriteFramesH         = spriteFramesH,
            spriteAnchorX         = spriteAnchorX,
            spriteAnchorY         = spriteAnchorY,
            spriteFramePixelW     = spriteFramePixelW,
            spriteFramePixelH     = spriteFramePixelH,
            blockOffsetX          = blockOffsetX,
            blockOffsetY          = blockOffsetY,
            blockTilesW           = blockTilesW,
            blockTilesH           = blockTilesH,
            blocksMovement        = blocksMovement,

            // Indices GPU — copiés depuis le parent après assignation
            trailSliceIndex = trailSliceIndex,
            speciesIdBase   = speciesIdBase,
            trailChannel    = trailChannel,
        };

        // Couleur par catégorie (color_arr aplati : 3 floats par catégorie)
        int ci = catIndex * 3;
        if (color_arr != null && ci + 2 < color_arr.Length)
            cat.color = new float[] { color_arr[ci], color_arr[ci + 1], color_arr[ci + 2] };
        else
            cat.color = color;

        // Champs surchargés par catégorie
        cat.moveSpeed      = GetFloat(moveSpeed_arr,      catIndex, moveSpeed);
        cat.turnSpeed      = GetFloat(turnSpeed_arr,      catIndex, turnSpeed);
        cat.sensorAngleDeg = GetFloat(sensorAngleDeg_arr, catIndex, sensorAngleDeg);
        cat.sensorOffsetDst= GetFloat(sensorOffsetDst_arr,catIndex, sensorOffsetDst);
        cat.trailWeight    = GetFloat(trailWeight_arr,    catIndex, trailWeight);
        cat.trailEmitRadius= (int)GetFloat(trailEmitRadius_arr, catIndex, trailEmitRadius);

        cat.behaviorType   = GetString(behaviorType_arr, catIndex, behaviorType);

        return cat;
    }

    // ── Conversion ────────────────────────────────────────────────────

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
        waitForStock              = waitForStock ? 1f : 0f,
        repulsionStrength         = repulsionStrength,
        repulsionRadius           = repulsionRadius,
        densityLimit              = densityLimit,
        agentRadius               = Mathf.Max(0, agentRadius),
        trailEmitRadius           = Mathf.Max(0, trailEmitRadius),
        seedProbHigh              = seedProbHigh,
        seedProbLow               = seedProbLow,
        particleLifeScanRadius    = Mathf.Max(1f, particleLifeScanRadius),
        particleLifeStepSize      = Mathf.Max(1f, particleLifeStepSize),
        navDensityLimit           = densityLimit,
        trailSliceIndex           = trailSliceIndex,
        speciesId                 = speciesIdBase >= 0 ? speciesIdBase : slotIndex,
        trailChannel              = trailChannel,
    };

    // ── Helpers privés ────────────────────────────────────────────────

    private static float GetFloat(float[] arr, int idx, float fallback)
        => (arr != null && idx < arr.Length) ? arr[idx] : fallback;

    private static string GetString(string[] arr, int idx, string fallback)
        => (arr != null && idx < arr.Length && !string.IsNullOrEmpty(arr[idx])) ? arr[idx] : fallback;
}
