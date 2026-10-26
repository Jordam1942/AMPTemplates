using System;
using UnityEngine;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

/// <summary>
/// Vraagt de volgende grondtik op, bijvoorbeeld om een rally point te zetten.
/// Zolang deze actief is, negeert de selectie-handler en de bouwplaatsing de input.
/// De keuze komt pas bij loslaten en alleen van dezelfde vinger die ook is neergezet.
/// Een tik op de HUD telt niet mee.
/// </summary>
[DisallowMultipleComponent]
public sealed class TargetPicker : MonoBehaviour
{
    public static TargetPicker Instance { get; private set; }

    /// <summary>True zolang de picker een punt aan het wachten is.</summary>
    public static bool IsActive => Instance != null && Instance.callback != null;

    [SerializeField] private Camera worldCamera;
    [SerializeField] private LayerMask groundMask;

    private Action<Vector3> callback;
    private Finger pickingFinger;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        if (worldCamera == null) worldCamera = Camera.main;
    }

    private void OnEnable()
    {
        Touch.onFingerDown += OnFingerDown;
        Touch.onFingerUp += OnFingerUp;
    }

    private void OnDisable()
    {
        Touch.onFingerDown -= OnFingerDown;
        Touch.onFingerUp -= OnFingerUp;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>Wacht op de volgende grondtik en roep onPicked aan met het punt.</summary>
    public static void Begin(Action<Vector3> onPicked)
    {
        if (Instance == null) return;

        Instance.callback = onPicked;
        Instance.pickingFinger = null;
    }

    public static void Cancel()
    {
        if (Instance == null) return;

        Instance.callback = null;
        Instance.pickingFinger = null;
    }

    private void OnFingerDown(Finger finger)
    {
        if (!IsActive || Touch.activeTouches.Count > 1 || UIInputGuard.IsOverUI(finger)) return;
        pickingFinger = finger;
    }

    private void OnFingerUp(Finger finger)
    {
        if (finger != pickingFinger) return;

        pickingFinger = null;
        Action<Vector3> pending = callback;
        callback = null;

        if (pending == null) return;

        Ray ray = worldCamera.ScreenPointToRay(finger.currentTouch.screenPosition);
        if (Physics.Raycast(ray, out RaycastHit hit, 1000f, groundMask, QueryTriggerInteraction.Ignore))
            pending(hit.point);
    }
}
