using System;
using UnityEngine;

/// <summary>
/// Entrée espèce dans la définition d'un joueur : speciesId + couleur.
/// </summary>
[Serializable]
public class PlayerSpeciesEntry
{
    /// <summary>Id espèce (minuscules) : "globulerouge", "bacterie"…</summary>
    public string speciesId;

    /// <summary>Couleur RGB normalisée [0..1]. Array de 3 floats pour compatibilité JsonUtility.</summary>
    public float[] color = new float[3];

    public Color ToColor() => color != null && color.Length >= 3
        ? new Color(color[0], color[1], color[2])
        : Color.white;

    public Vector4 ToVector4() => color != null && color.Length >= 3
        ? new Vector4(color[0], color[1], color[2], 1f)
        : Vector4.one;
}

/// <summary>
/// Définition d'un joueur chargée depuis StreamingAssets/Players/*.json.
/// Un joueur peut contrôler plusieurs espèces simultanément.
/// </summary>
[Serializable]
public class PlayerDefinition
{
    /// <summary>Identifiant unique (minuscules) : "corps_humain", "infection"…</summary>
    public string id;

    /// <summary>Nom affiché dans l'UI.</summary>
    public string displayName;

    /// <summary>
    /// Indique si ce joueur est ennemi de tous les autres par défaut.
    /// Pour configurer finement, utiliser la section warsWith.
    /// </summary>
    public bool defaultEnemy = false;

    /// <summary>Liste des ids joueur avec qui ce joueur est en guerre.</summary>
    public string[] warsWith = new string[0];

    /// <summary>Liste des espèces (et leur couleur) que ce joueur contrôle.</summary>
    public PlayerSpeciesEntry[] species = new PlayerSpeciesEntry[0];
}
