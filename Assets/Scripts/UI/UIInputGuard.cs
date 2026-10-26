using UnityEngine.EventSystems;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

/// <summary>
/// Bepaalt of een vinger op de HUD ligt. Wereldsystemen (selectie, bouwen, rally) gebruiken dit
/// om HUD-tikken niet door te laten naar het terrein.
/// </summary>
public static class UIInputGuard
{
    public static bool IsOverUI(Finger finger)
    {
        EventSystem system = EventSystem.current;
        if (system == null) return false;

        // Touch-pointers in het Input System gebruiken de touchId als pointer-ID.
        // (verifiëren op een Pixel-apparaat)
        return system.IsPointerOverGameObject(finger.currentTouch.touchId);
    }
}
