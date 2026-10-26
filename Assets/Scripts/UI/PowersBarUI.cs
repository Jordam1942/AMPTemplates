using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Uitklapbare balk met de Generals' Powers in de HUD.
///  - Het toggle-knopje klapt de rij met powers uit of in.
///  - Een ontgrendelde power met een tik start de targeting. Daarna volgt één of twee grondtikken.
///  - Een vergrendelde power die ontgrendeld kan worden, ontgrendelt bij een tik en kost Promotion Points.
///  - De cooldown-overlay is de resterende cooldown als fractie.
/// De balk ververst op een vast interval. Acties worden gemeld via de StateChanged- en Changed-events.
/// </summary>
[DisallowMultipleComponent]
public sealed class PowersBarUI : MonoBehaviour
{
    [SerializeField] private GeneralPowerManager powers;
    [SerializeField] private GeneralRankManager ranks;

    [Tooltip("De uitklapbare rij met power-slots.")]
    [SerializeField] private RectTransform expandedRow;

    [SerializeField] private TouchButton toggleButton;
    [SerializeField] private TouchButton cancelButton;
    [SerializeField] private OptionSlot[] slots = new OptionSlot[0];
    [SerializeField] private TMP_Text rankText;

    [SerializeField, Min(0.02f)] private float refreshInterval = 0.1f;

    private bool expanded;
    private float refreshTimer;

    private void Awake()
    {
        toggleButton.Pressed += ToggleExpanded;
        cancelButton.Pressed += OnCancelPressed;

        for (int i = 0; i < slots.Length; i++)
        {
            int index = i;
            slots[i].Pressed += () => OnSlotPressed(index);
        }

        expandedRow.gameObject.SetActive(false);
    }

    private void OnEnable()
    {
        powers.StateChanged += Refresh;
        ranks.Changed += Refresh;
        Refresh();
    }

    private void OnDisable()
    {
        powers.StateChanged -= Refresh;
        ranks.Changed -= Refresh;
    }

    private void Update()
    {
        refreshTimer -= Time.unscaledDeltaTime;
        if (refreshTimer > 0f) return;

        refreshTimer = refreshInterval;
        Refresh();
    }

    private void ToggleExpanded()
    {
        expanded = !expanded;
        expandedRow.gameObject.SetActive(expanded);
        Refresh();
    }

    private void OnSlotPressed(int index)
    {
        if (index >= powers.Powers.Count) return;

        GeneralPowerSO power = powers.Powers[index];
        if (ranks.IsUnlocked(power)) powers.Activate(index);
        else ranks.TryUnlock(power);

        Refresh();
    }

    private void OnCancelPressed()
    {
        powers.CancelTargeting();
        Refresh();
    }

    private void Refresh()
    {
        int count = powers.Powers.Count;

        for (int i = 0; i < slots.Length; i++)
        {
            bool visible = expanded && i < count;
            slots[i].gameObject.SetActive(visible);
            if (!visible) continue;

            GeneralPowerSO power = powers.Powers[i];
            slots[i].SetContent(power.icon, power.displayName, -1);
            slots[i].SetProgress(powers.CooldownFraction(i));

            // Ontgrendeld: beschikbaar als de cooldown voorbij is. Vergrendeld: beschikbaar als het kan.
            bool available = ranks.IsUnlocked(power) ? powers.IsReady(i) : ranks.CanUnlock(power);
            slots[i].SetAvailable(available);
        }

        cancelButton.gameObject.SetActive(powers.IsTargeting);
        if (rankText != null) rankText.text = $"★ {ranks.Stars}   PP {ranks.PromotionPoints}";
    }
}
