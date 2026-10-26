using UnityEngine;

/// <summary>
/// Status-icoon van de China Horde Bonus. Staat alleen aan bij de leidende unit van een groep.
/// Het object moet als kind boven het model staan. Het wordt door ChinaHordeSystem aangestuurd.
/// </summary>
[DisallowMultipleComponent]
public sealed class HordeIndicator : MonoBehaviour
{
    [Tooltip("Het zichtbare icoon, bijvoorbeeld een rode vlag of een gloed.")]
    [SerializeField] private GameObject flag;

    public void SetVisible(bool visible)
    {
        if (flag != null && flag.activeSelf != visible) flag.SetActive(visible);
    }
}
