using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Stats van de geselecteerde groep: icoon, naam, aantal en HP van de eerste levende unit.
/// Schrijft alleen bij wijzigingen. Het paneel zelf bepaalt wanneer dit wordt ververst.
/// </summary>
[DisallowMultipleComponent]
public sealed class UnitStatsView : MonoBehaviour
{
    [SerializeField] private Image icon;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text countText;
    [SerializeField] private Image hpFill;
    [SerializeField] private TMP_Text hpText;

    [SerializeField] private Color healthyColor = new Color(0.4f, 0.95f, 0.45f);
    [SerializeField] private Color damagedColor = new Color(0.95f, 0.3f, 0.25f);

    private UnitController shown;
    private int shownCount = -1;
    private int shownHp = -1;

    public void Show(IReadOnlyList<UnitController> units)
    {
        UnitController lead = FirstAlive(units);
        if (lead == null)
        {
            shown = null;
            return;
        }

        if (lead != shown)
        {
            shown = lead;
            shownHp = -1;
            if (icon != null) icon.sprite = lead.Icon;
            if (nameText != null && lead.Data != null) nameText.text = lead.Data.displayName;
        }

        int count = units.Count;
        if (count != shownCount)
        {
            shownCount = count;
            if (countText != null) countText.text = count > 1 ? "x" + count : string.Empty;
        }

        if (lead.Data == null) return;

        int hp = lead.CurrentHp;
        if (hp == shownHp) return;

        shownHp = hp;
        float ratio = lead.Data.maxHp > 0 ? hp / (float)lead.Data.maxHp : 0f;

        if (hpFill != null)
        {
            hpFill.fillAmount = ratio;
            hpFill.color = Color.Lerp(damagedColor, healthyColor, ratio);
        }

        if (hpText != null) hpText.text = hp + " / " + lead.Data.maxHp;
    }

    private static UnitController FirstAlive(IReadOnlyList<UnitController> units)
    {
        for (int i = 0; i < units.Count; i++)
        {
            if (units[i].IsAlive) return units[i];
        }

        return null;
    }
}
