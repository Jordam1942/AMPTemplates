using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Bovenste HUD-balk: geanimeerde credits met een verschilpopup, stroomstatus met waarschuwing,
/// en een minimap-placeholder aan de linker- of rechterkant.
/// Leest de economie elk frame uit. Dat zijn een paar ints en een bool, dus er is geen event-koppeling
/// nodig en geen risico op een verkeerde initialisatievolgorde.
/// </summary>
[DisallowMultipleComponent]
public sealed class TopBarUI : MonoBehaviour
{
    [Header("Credits")]
    [SerializeField] private TMP_Text creditsText;
    [SerializeField] private TMP_Text deltaText;

    [Tooltip("Hoe snel de teller de werkelijke waarde volgt. Hoger = sneller.")]
    [SerializeField, Min(0.1f)] private float countSpeed = 8f;

    [SerializeField] private Color gainColor = new Color(0.45f, 1f, 0.5f);
    [SerializeField] private Color lossColor = new Color(1f, 0.45f, 0.4f);
    [SerializeField, Min(0.1f)] private float deltaLifetime = 1.5f;

    [Header("Stroom")]
    [Tooltip("Balk met de gedekte stroomvraag (productie / verbruik, begrensd op 1).")]
    [SerializeField] private Image powerFill;
    [SerializeField] private TMP_Text powerText;

    [Tooltip("Waarschuwing die knippert bij Low Power. Wordt verborgen als er geen tekort is.")]
    [SerializeField] private Graphic lowPowerWarning;
    [SerializeField] private Color powerOkColor = new Color(0.3f, 0.85f, 1f);
    [SerializeField] private Color powerLowColor = new Color(1f, 0.3f, 0.2f);
    [SerializeField, Min(0.1f)] private float warningPulseSpeed = 4f;

    [Header("Minimap-placeholder")]
    [SerializeField] private RectTransform minimapFrame;
    [SerializeField] private bool minimapOnLeft;
    [SerializeField, Min(0f)] private float minimapMargin = 24f;

    private PlayerEconomy economy;
    private float displayedMoney;
    private int shownMoney = int.MinValue;
    private int lastMoney;
    private int pendingDelta;
    private float deltaTimer;

    private int shownProduced = int.MinValue;
    private int shownConsumed = int.MinValue;
    private float shownFill = -1f;

    private void Awake()
    {
        ApplyMinimapSide();
        if (lowPowerWarning != null) lowPowerWarning.gameObject.SetActive(false);
        if (deltaText != null) deltaText.text = string.Empty;
    }

    private void Update()
    {
        if (economy == null)
        {
            economy = PlayerEconomy.Instance;
            if (economy == null) return;

            lastMoney = economy.Money;
            displayedMoney = lastMoney;
        }

        float dt = Time.unscaledDeltaTime;
        UpdateCredits(dt);
        UpdatePower();
    }

    private void UpdateCredits(float dt)
    {
        int money = economy.Money;
        int change = money - lastMoney;

        if (change != 0)
        {
            // Opeenvolgende wijzigingen binnen de levensduur worden opgeteld tot één popup.
            if (deltaTimer <= 0f) pendingDelta = 0;
            pendingDelta += change;
            deltaTimer = deltaLifetime;
            lastMoney = money;

            if (deltaText != null)
                deltaText.text = (pendingDelta > 0 ? "+" : string.Empty) + pendingDelta.ToString("N0");
        }

        // Exponentiële demping: de teller loopt soepel naar de werkelijke waarde.
        displayedMoney += (money - displayedMoney) * (1f - Mathf.Exp(-countSpeed * dt));
        if (Mathf.Abs(money - displayedMoney) < 0.5f) displayedMoney = money;

        int rounded = Mathf.RoundToInt(displayedMoney);
        if (rounded != shownMoney && creditsText != null)
        {
            shownMoney = rounded;
            creditsText.text = rounded.ToString("N0");
        }

        UpdateDeltaPopup(dt);
    }

    private void UpdateDeltaPopup(float dt)
    {
        if (deltaText == null || deltaTimer <= 0f) return;

        deltaTimer -= dt;
        Color c = pendingDelta >= 0 ? gainColor : lossColor;
        c.a = Mathf.Clamp01(deltaTimer / deltaLifetime);
        deltaText.color = c;

        if (deltaTimer <= 0f) deltaText.text = string.Empty;
    }

    private void UpdatePower()
    {
        int produced = economy.PowerProduced;
        int consumed = economy.PowerConsumed;
        bool low = economy.IsLowPower;

        // Gedekte vraag: 1 als er geen verbruik is, anders productie / verbruik, begrensd op 1.
        float fill = consumed <= 0 ? 1f : Mathf.Clamp01(produced / (float)consumed);
        if (powerFill != null)
        {
            if (!Mathf.Approximately(fill, shownFill))
            {
                shownFill = fill;
                powerFill.fillAmount = fill;
            }

            powerFill.color = low ? powerLowColor : powerOkColor;
        }

        if (powerText != null && (produced != shownProduced || consumed != shownConsumed))
        {
            shownProduced = produced;
            shownConsumed = consumed;
            powerText.text = produced + " / " + consumed;
        }

        if (lowPowerWarning == null) return;

        lowPowerWarning.gameObject.SetActive(low);
        if (!low) return;

        // Knipperen zonder allocatie: alleen de alfa van de bestaande kleur wijzigen.
        Color w = lowPowerWarning.color;
        w.a = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * warningPulseSpeed);
        lowPowerWarning.color = w;
    }

    private void ApplyMinimapSide()
    {
        if (minimapFrame == null) return;

        // Linksboven of rechtsboven, binnen de bovenste balk.
        float x = minimapOnLeft ? 0f : 1f;
        minimapFrame.anchorMin = new Vector2(x, 1f);
        minimapFrame.anchorMax = new Vector2(x, 1f);
        minimapFrame.pivot = new Vector2(x, 1f);
        minimapFrame.anchoredPosition = new Vector2(minimapOnLeft ? minimapMargin : -minimapMargin, -minimapMargin);
    }
}
