using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Knop die reageert op touch-down, niet op loslaten. Geen click-vertraging.
/// Vibreert kort bij elke druk en schaalt visueel mee. Een niet-interactieve knop negeert de druk
/// en vibreert ook niet.
/// </summary>
[DisallowMultipleComponent]
public sealed class TouchButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    public event Action Pressed;

    [Header("Feedback")]
    [SerializeField] private RectTransform scaleTarget;
    [SerializeField, Range(0.8f, 1f)] private float pressedScale = 0.92f;
    [SerializeField, Min(1f)] private float scaleSpeed = 30f;
    [SerializeField] private bool hapticOnPress = true;

    [Header("Staat")]
    [SerializeField] private Graphic tintTarget;
    [SerializeField] private Color enabledTint = Color.white;
    [SerializeField] private Color disabledTint = new Color(0.35f, 0.35f, 0.35f, 0.7f);

    private bool interactable = true;
    private bool pressed;
    private float currentScale = 1f;

    public bool Interactable
    {
        get => interactable;
        set
        {
            if (interactable == value) return;

            interactable = value;
            ApplyTint();
        }
    }

    private void Awake()
    {
        if (scaleTarget == null) scaleTarget = (RectTransform)transform;
        if (tintTarget == null) tintTarget = GetComponent<Graphic>();
        ApplyTint();
    }

    private void Update()
    {
        float target = pressed ? pressedScale : 1f;
        if (Mathf.Approximately(currentScale, target)) return;

        // Alleen schrijven als er iets verandert. Zo kost een rustende knop niets.
        currentScale = Mathf.Lerp(currentScale, target, 1f - Mathf.Exp(-scaleSpeed * Time.unscaledDeltaTime));
        if (Mathf.Abs(currentScale - target) < 0.001f) currentScale = target;

        scaleTarget.localScale = new Vector3(currentScale, currentScale, 1f);
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (!interactable) return;

        pressed = true;
        if (hapticOnPress) HapticFeedback.Tick();
        Pressed?.Invoke();
    }

    public void OnPointerUp(PointerEventData eventData) => pressed = false;

    public void OnPointerExit(PointerEventData eventData) => pressed = false;

    private void ApplyTint()
    {
        if (tintTarget != null) tintTarget.color = interactable ? enabledTint : disabledTint;
    }
}
