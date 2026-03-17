using System;

// Données pures pour une technologie (utilisées pour le chargement)
[Serializable]
public struct TechData {
    public string id;
    public string nameKey; // Référence au fichier de langue
    public float speedMultiplier;
    public int energyCost;
}
