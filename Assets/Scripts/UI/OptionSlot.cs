using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Herbruikbaar optie-element: icoon, titel, kosten, optionele voortgangsbalk en een TouchButton.
/// Gebruikt voor het bouwmenu, de productieopties en de wachtrij. Stringwaarden worden alleen
/// geschreven als ze veranderen, zodat er geen onnodige allocaties of mesh-rebuilds zijn.
/// </summary>
[DisallowMultipleComponent]
public sealed class OptionSlot : MonoBehaviour
{
    [SerializeField] private TouchButton button;
    [SerializeField] private Image icon;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text costText;

    [Tooltip("Optioneel: Image met Type = Filled voor de voortgang.")]
    [SerializeField] private Image progressFill;

    private Sprite shownIcon;
    private string shownTitle;
    private int shownCost = int.MinValue;

    public event Action Pressed;

    private void Awake()
    {
        button.Pressed += HandlePressed;
    }

    private void OnDestroy()
    {
        if (button != null) button.Pressed -= HandlePressed;
    }

    /// <summary>Zet de inhoud. Een negatieve cost verbergt de kostentekst.</summary>
    public void SetContent(Sprite sprite, string title, int cost)
    {
        if (icon != null && sprite != shownIcon)
        {
            shownIcon = sprite;
            icon.sprite = sprite;
            icon.enabled = sprite != null;
        }

        if (titleText != null && title != shownTitle)
        {
            shownTitle = title;
            titleText.text = title;
        }

        if (costText != null && cost != shownCost)
        {
            shownCost = cost;
            costText.text = cost >= 0 ? cost.ToString("N0") : string.Empty;
        }
    }

    public void SetAvailable(bool available) => button.Interactable = available;

    /// <summary>Voortgang van 0 tot 1.</summary>
    public void SetProgress(float value)
    {
        if (progressFill != null) progressFill.fillAmount = value;
    }

    private void HandlePressed() => Pressed?.Invoke();
}
