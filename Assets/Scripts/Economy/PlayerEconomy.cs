using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Economie voor één team: credits, cashflow over een venster, en de stroombalans.
/// Meerdere teams hebben elk een eigen component (de speler op team 0, de AI op team 1).
/// Gebouwen en supply centers zoeken hun economie via ForTeam, zodat er geen spellogica dubbel is.
/// Stroom: de productie en het verbruik van alle operationele gebouwen van het team worden opgeteld.
/// Bij een tekort (verbruik > productie) staat het team op Low Power en produceert het op 50% snelheid.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerEconomy : MonoBehaviour
{
    private readonly struct CashEntry
    {
        public readonly float time;
        public readonly int amount;

        public CashEntry(float time, int amount)
        {
            this.time = time;
            this.amount = amount;
        }
    }

    private static readonly List<PlayerEconomy> economies = new List<PlayerEconomy>(4);

    /// <summary>De economie van de lokale speler (team 0). Het HUD gebruikt deze.</summary>
    public static PlayerEconomy Instance => ForTeam(UnitController.LocalPlayerTeam);

    /// <summary>De economie van het opgegeven team, of null als dat team geen economie heeft.</summary>
    public static PlayerEconomy ForTeam(int team)
    {
        for (int i = 0; i < economies.Count; i++)
        {
            if (economies[i].teamId == team) return economies[i];
        }

        return null;
    }

    [SerializeField] private int teamId = UnitController.LocalPlayerTeam;
    [SerializeField, Min(0)] private int startingMoney = 5000;

    [Tooltip("Productiesnelheid bij een stroomtekort. 0.5 = 50%.")]
    [SerializeField, Range(0.05f, 1f)] private float lowPowerSpeed = 0.5f;

    [Tooltip("Venster in seconden voor de cashflow-berekening.")]
    [SerializeField, Min(1f)] private float cashflowWindow = 60f;

    private readonly List<CashEntry> cashLog = new List<CashEntry>(128);

    public int TeamId => teamId;
    public int Money { get; private set; }
    public int PowerProduced { get; private set; }
    public int PowerConsumed { get; private set; }
    public bool IsLowPower => PowerConsumed > PowerProduced;
    public float ProductionSpeedMultiplier => IsLowPower ? lowPowerSpeed : 1f;

    public event Action<int> MoneyChanged;
    public event Action<bool> PowerStateChanged;

    private void Awake()
    {
        if (ForTeam(teamId) != null)
        {
            Debug.LogWarning($"Er bestaat al een economie voor team {teamId}. Deze wordt genegeerd.", this);
            Destroy(this);
            return;
        }

        economies.Add(this);
        Money = startingMoney;
    }

    private void Start()
    {
        MoneyChanged?.Invoke(Money);
        RefreshPower();
    }

    private void OnDestroy()
    {
        economies.Remove(this);
    }

    private void Update()
    {
        // Entries staan chronologisch, dus alleen vooraan verwijderen is genoeg.
        float cutoff = Time.time - cashflowWindow;
        int expired = 0;
        while (expired < cashLog.Count && cashLog[expired].time < cutoff) expired++;

        if (expired > 0) cashLog.RemoveRange(0, expired);
    }

    // ---------- Credits ----------

    public bool CanAfford(int amount) => Money >= amount;

    /// <summary>Schrijft kosten af. Geeft false terug als het team het niet kan betalen.</summary>
    public bool TrySpend(int amount)
    {
        if (amount <= 0) return true;
        if (!CanAfford(amount)) return false;

        ApplyDelta(-amount, record: true);
        return true;
    }

    /// <summary>Inkomsten, bijvoorbeeld van supply-levering.</summary>
    public void Deposit(int amount)
    {
        if (amount > 0) ApplyDelta(amount, record: true);
    }

    /// <summary>Terugbetaling van een geannuleerde bestelling. Telt niet mee als inkomsten.</summary>
    public void Refund(int amount)
    {
        if (amount > 0) ApplyDelta(amount, record: false);
    }

    private void ApplyDelta(int delta, bool record)
    {
        Money += delta;
        if (record) cashLog.Add(new CashEntry(Time.time, delta));
        MoneyChanged?.Invoke(Money);
    }

    /// <summary>Som van de inkomsten binnen het cashflow-venster.</summary>
    public int IncomeInWindow()
    {
        int sum = 0;
        for (int i = 0; i < cashLog.Count; i++)
        {
            if (cashLog[i].amount > 0) sum += cashLog[i].amount;
        }

        return sum;
    }

    /// <summary>Som van de uitgaven binnen het cashflow-venster, als positief getal.</summary>
    public int SpendingInWindow()
    {
        int sum = 0;
        for (int i = 0; i < cashLog.Count; i++)
        {
            if (cashLog[i].amount < 0) sum -= cashLog[i].amount;
        }

        return sum;
    }

    // ---------- Stroom ----------

    /// <summary>Herberekent de stroombalans uit alle operationele gebouwen van het team.</summary>
    public void RefreshPower()
    {
        int produced = 0;
        int consumed = 0;

        IReadOnlyList<BuildingController> buildings = BuildingController.Registry;
        for (int i = 0; i < buildings.Count; i++)
        {
            BuildingController building = buildings[i];
            if (building.Team != teamId || !building.IsOperational) continue;

            produced += building.Data.powerProduced;
            consumed += building.Data.powerConsumed;
        }

        bool wasLow = IsLowPower;
        PowerProduced = produced;
        PowerConsumed = consumed;

        if (wasLow != IsLowPower) PowerStateChanged?.Invoke(IsLowPower);
    }
}
