using System;
using UnityEngine;

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

    /// <summary>Slot GPU 0-5 occupé par cette espèce.</summary>
    public int slotIndex;

    // ── Mouvement ─────────────────────────────────────────────────────
    public float moveSpeed;
    public float turnSpeed;

    /// <summary>Angle du senseur en degrés (converti en radians à l'usage).</summary>
    public float sensorAngleDeg;
    public float sensorOffsetDst;
    public int   sensorSize;

    // ── Cycle de vie ──────────────────────────────────────────────────
    public float maxAge;
    public float trailWeight;
    public float decayRate;
    public float diffuseRate;
    public float warDamageRate;

    // ── Comportement ──────────────────────────────────────────────────
    /// <summary>Libellé libre, pour documentation JSON uniquement (ex: "GlobuleRouge").</summary>
    public string behaviorType;

    /// <summary>
    /// Code GPU du comportement, défini directement dans le JSON de l'espèce.
    /// 0=défaut, 1=Bacterie, 2=GlobuleRouge, 3=GlobuleBlanc, 4=Virus, 5=Plaquette, …
    /// Ajouter un nouveau comportement = ajouter un case dans UpdateAgents.compute + choisir un entier libre ici.
    /// </summary>
    public int behaviorTypeInt;

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

    public int BehaviorTypeInt => behaviorTypeInt;

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
