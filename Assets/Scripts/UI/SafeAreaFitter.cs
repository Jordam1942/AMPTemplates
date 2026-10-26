using UnityEngine;

/// <summary>
/// Laat de RectTransform precies binnen Screen.safeArea vallen: punch-hole camera, afgeronde hoeken
/// en de gebarenbalk onderaan. De achtergrond hoort op een ouder-element dat het hele scherm vult,
/// zodat alleen de inhoud wordt ingesprongen.
/// Vereist een Canvas in Screen Space - Overlay.
/// Pixel-instelling: Player Settings > Android > Resolution and Presentation > "Render Outside Safe Area" uit.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public sealed class SafeAreaFitter : MonoBehaviour
{
    private RectTransform rectTransform;
    private Rect appliedArea;
    private int appliedWidth;
    private int appliedHeight;

    private void Awake()
    {
        rectTransform = (RectTransform)transform;
        Apply();
    }

    private void Update()
    {
        // Rotatie en multi-window veranderen het schermformaat. Dan opnieuw toepassen.
        if (Screen.safeArea != appliedArea || Screen.width != appliedWidth || Screen.height != appliedHeight)
            Apply();
    }

    private void Apply()
    {
        Rect safe = Screen.safeArea;
        appliedArea = safe;
        appliedWidth = Screen.width;
        appliedHeight = Screen.height;

        if (appliedWidth <= 0 || appliedHeight <= 0) return;

        // Ankers zijn genormaliseerd naar het scherm. Het canvas in Overlay-modus heeft diezelfde afmeting.
        rectTransform.anchorMin = new Vector2(safe.xMin / appliedWidth, safe.yMin / appliedHeight);
        rectTransform.anchorMax = new Vector2(safe.xMax / appliedWidth, safe.yMax / appliedHeight);
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;
    }
}
