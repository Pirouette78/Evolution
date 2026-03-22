using System;

/// <summary>
/// Définition d'un niveau diplomatique chargé depuis StreamingAssets/Diplomacy/levels.json.
/// Chaque niveau définit : une valeur float pour la matrice d'interaction GPU,
/// une couleur UI, et un flag isWar qui active warDamageRate + trailErasePower.
/// </summary>
[Serializable]
public class DiplomacyLevelDefinition
{
    /// <summary>Identifiant stable : "guerre", "neutre", "paix", "allie".</summary>
    public string id;

    /// <summary>Nom affiché en UI et dans la légende.</summary>
    public string displayName;

    /// <summary>Valeur injectée dans interactionMatrix[from*16+to] pour le sensing GPU.</summary>
    public float value;

    /// <summary>Couleur UI [R, G, B] en 0-1.</summary>
    public float[] color = new float[3];

    /// <summary>Si true, active warMask → warDamageRate et trailErasePower contre cet ennemi.</summary>
    public bool isWar;
}
