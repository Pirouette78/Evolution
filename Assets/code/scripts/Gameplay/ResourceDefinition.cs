using System;

/// <summary>
/// Décrit un type de ressource (oxygène, glucose, fer…).
/// Chargé depuis StreamingAssets/Resources/resources.json au démarrage.
/// Ajouter une nouvelle ressource = ajouter une entrée dans ce fichier JSON, sans toucher au code.
/// </summary>
[Serializable]
public class ResourceDefinition
{
    /// <summary>Identifiant stable (minuscules) utilisé dans les JSON des bâtiments : "oxygen", "glucose"…</summary>
    public string id;

    /// <summary>Nom affiché en UI.</summary>
    public string displayName;
}
