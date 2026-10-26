using UnityEngine;

/// <summary>
/// Optioneel onderdeel op units en gebouwen dat de zichtbaarheid van de renderers beheert.
/// FogOfWar schakelt het in en uit. Zonder dit onderdeel blijft een object altijd zichtbaar.
/// Renderers worden één keer gecachet, dus er zijn geen zoekopdrachten tijdens het spelen.
/// </summary>
[DisallowMultipleComponent]
public sealed class FogRenderTarget : MonoBehaviour
{
    private Renderer[] renderers;
    private bool revealed = true;

    private void Awake()
    {
        renderers = GetComponentsInChildren<Renderer>(true);
    }

    public void SetRevealed(bool value)
    {
        // Schrijf alleen bij wijzigingen. Dat houdt het bijwerken per tick bijna gratis.
        if (revealed == value) return;

        revealed = value;
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null) renderers[i].enabled = value;
        }
    }
}
