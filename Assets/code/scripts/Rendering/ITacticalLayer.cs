using UnityEngine;

/// <summary>
/// Interface pour toute couche de rendu qui s'active en mode tactique (zoom proche).
/// Enregistre-toi auprès de ZoomLevelController.Instance.RegisterLayer(this) dans Start().
/// </summary>
public interface ITacticalLayer
{
    /// <summary>Appelé une fois quand le mode tactique s'active.</summary>
    void OnEnterTactical(Vector4 cameraBounds);

    /// <summary>Appelé une fois quand on quitte le mode tactique.</summary>
    void OnExitTactical();

    /// <summary>Appelé chaque frame en mode tactique avec les bounds caméra mises à jour.</summary>
    void OnCameraBoundsChanged(Vector4 cameraBounds);
}
