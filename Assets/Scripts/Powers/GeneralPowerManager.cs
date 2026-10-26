using System;
using UnityEngine;

/// <summary>
/// Beheert de powers van de generaal: cooldowns, targeting en uitvoering.
/// Targeting loopt via TargetPicker, dezelfde one-shot grondtik als het rally point. Line-powers
/// vragen twee tikken, start en eind. Een marker toont de gekozen punten.
/// Activeren kan alleen als de power ontgrendeld en niet in cooldown is.
/// </summary>
[DisallowMultipleComponent]
public sealed class GeneralPowerManager : MonoBehaviour
{
    [SerializeField] private int teamId = UnitController.LocalPlayerTeam;
    [SerializeField] private GeneralRankManager rankManager;

    [Tooltip("Alle powers van deze generaal, in de volgorde van de HUD-slots.")]
    [SerializeField] private GeneralPowerSO[] powers = new GeneralPowerSO[0];

    [Tooltip("Optionele marker die op de gekozen punten verschijnt.")]
    [SerializeField] private GameObject markerPrefab;

    [SerializeField, Min(0.1f)] private float markerLifetime = 1.5f;

    private float[] cooldowns;
    private GeneralPowerSO pending;
    private int pendingIndex;
    private Vector3 pendingStart;

    /// <summary>Wordt aangeroepen bij een wijziging van cooldowns of targeting. Niet per frame.</summary>
    public event Action StateChanged;

    public IReadOnlyList<GeneralPowerSO> Powers => powers;
    public bool IsTargeting => pending != null;

    private void Awake()
    {
        cooldowns = new float[powers.Length];
        if (rankManager == null) rankManager = GeneralRankManager.ForTeam(teamId);
    }

    private void Update()
    {
        bool changed = false;
        for (int i = 0; i < cooldowns.Length; i++)
        {
            if (cooldowns[i] <= 0f) continue;

            cooldowns[i] = Mathf.Max(0f, cooldowns[i] - Time.deltaTime);
            changed |= cooldowns[i] == 0f;
        }

        // Een tik naast het terrein breekt de picker af zonder dat de power dat meldt. Maak de wachtstand dan ongedaan.
        if (IsTargeting && !TargetPicker.IsActive)
        {
            pending = null;
            changed = true;
        }

        if (changed) StateChanged?.Invoke();
    }

    /// <summary>Resterende cooldown als fractie van de totale cooldown, 0 tot 1. Voor de overlay in de HUD.</summary>
    public float CooldownFraction(int index)
    {
        float total = powers[index].cooldown;
        if (total <= 0f) return 0f;
        return Mathf.Clamp01(cooldowns[index] / total);
    }

    public bool IsReady(int index)
    {
        if (index < 0 || index >= powers.Length) return false;
        if (rankManager == null || !rankManager.IsUnlocked(powers[index])) return false;
        return cooldowns[index] <= 0f && !IsTargeting;
    }

    // ---------- Activeren en targeting ----------

    public void Activate(int index)
    {
        if (!IsReady(index)) return;

        GeneralPowerSO power = powers[index];
        if (power.targeting == PowerTargeting.Passive)
        {
            Execute(power, index, Vector3.zero, Vector3.zero);
            return;
        }

        pending = power;
        pendingIndex = index;
        TargetPicker.Begin(OnFirstPointPicked);
        StateChanged?.Invoke();
    }

    public void CancelTargeting()
    {
        if (!IsTargeting) return;

        TargetPicker.Cancel();
        pending = null;
        StateChanged?.Invoke();
    }

    private void OnFirstPointPicked(Vector3 point)
    {
        if (pending == null) return;

        ShowMarker(point);

        if (pending.targeting == PowerTargeting.Point)
        {
            Execute(pending, pendingIndex, point, point);
            return;
        }

        // Line: het eerste punt is het begin. Het tweede punt start het effect.
        pendingStart = point;
        TargetPicker.Begin(OnLineEndPicked);
    }

    private void OnLineEndPicked(Vector3 end)
    {
        if (pending == null) return;

        ShowMarker(end);
        Execute(pending, pendingIndex, pendingStart, end);
    }

    private void Execute(GeneralPowerSO power, int index, Vector3 start, Vector3 end)
    {
        pending = null;
        cooldowns[index] = power.cooldown;

        power.effect?.Execute(new GeneralPowerContext(teamId, start, end));
        StateChanged?.Invoke();
    }

    private void ShowMarker(Vector3 point)
    {
        if (markerPrefab == null) return;

        GameObject marker = Instantiate(markerPrefab, point, Quaternion.identity);
        Destroy(marker, markerLifetime);
    }
}
