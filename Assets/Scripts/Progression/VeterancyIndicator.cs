using UnityEngine;

/// <summary>
/// Chevron-weergave boven een geselecteerde unit. Eén chevron voor Veteran, twee voor Elite, drie voor Heroic.
/// Toont alleen als de unit geselecteerd is en een rang heeft. De chevrons kijken altijd naar de camera.
/// Plaats dit als kind boven het model, met de chevrons als kinderen in volgorde.
/// </summary>
[DisallowMultipleComponent]
public sealed class VeterancyIndicator : MonoBehaviour
{
    [SerializeField] private UnitVeterancy source;
    [SerializeField] private UnitController unit;

    [Tooltip("Chevron-objecten, laag naar hoog. Index 0 = Veteran.")]
    [SerializeField] private GameObject[] chevrons = new GameObject[3];

    private Camera cam;
    private int shownCount = -1;

    private void Awake()
    {
        if (source == null) source = GetComponentInParent<UnitVeterancy>();
        if (unit == null) unit = GetComponentInParent<UnitController>();

        cam = Camera.main;
        SetVisibleCount(0);
    }

    private void OnEnable()
    {
        if (source != null) source.RankChanged += OnRankChanged;
        Refresh();
    }

    private void OnDisable()
    {
        if (source != null) source.RankChanged -= OnRankChanged;
    }

    private void LateUpdate()
    {
        // Gebruik de camera-rotatie zodat de chevrons leesbaar blijven vanuit elke hoek.
        if (cam != null) transform.rotation = cam.transform.rotation;

        Refresh();
    }

    private void OnRankChanged(VeterancyRank _) => Refresh();

    private void Refresh()
    {
        if (source == null || unit == null) return;

        int count = unit.IsSelected ? (int)source.Rank : 0;
        SetVisibleCount(count);
    }

    private void SetVisibleCount(int count)
    {
        // Schrijf alleen bij wijzigingen. De indicator wordt elke frame gecontroleerd.
        if (count == shownCount) return;

        shownCount = count;
        for (int i = 0; i < chevrons.Length; i++)
        {
            if (chevrons[i] != null) chevrons[i].SetActive(i < count);
        }
    }
}
