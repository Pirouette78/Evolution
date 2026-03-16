using Unity.Entities;
// using Unity.NetCode; // Uncomment when Unity.NetCode package is fully installed
using Unity.Mathematics;
using System;
using System.Collections.Generic;

// Données pures pour une technologie (utilisées pour le chargement)
[Serializable]
public struct TechData {
    public string id;
    public string nameKey; // Référence au fichier de langue
    public float speedMultiplier;
    public int energyCost;
}

// Marker pour Netcode for Entities (peut être remplacé par le composant natif UnityNetCode plus tard)
public struct GhostInstance : IComponentData { }
