using System;
using UnityEngine;

/// <summary>
/// XP en rang van één unit. Verzamelt XP per kill en past bij een rangstijging de bonussen toe op
/// UnitController. Elite en Heroic herstellen HP per seconde.
/// Voeg dit component toe aan unit-prefabs die veterancy moeten hebben.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(UnitController))]
public sealed class UnitVeterancy : MonoBehaviour
{
    private UnitController unit;
    private float healAccumulator;

    public int Xp { get; private set; }
    public VeterancyRank Rank { get; private set; }

    /// <summary>Wordt aangeroepen bij een rangstijging. Gebruikt door de chevron-weergave.</summary>
    public event Action<VeterancyRank> RankChanged;

    private void Awake()
    {
        unit = GetComponent<UnitController>();
    }

    public void AddKillXp(int amount)
    {
        if (!unit.IsAlive || amount <= 0) return;

        Xp += amount;
        VeterancyRank next = VeterancyRules.RankForXp(Xp);
        if (next == Rank) return;

        Rank = next;
        unit.ApplyVeterancy(VeterancyRules.MaxHpMultiplier(next), VeterancyRules.FireRateMultiplier(next));
        RankChanged?.Invoke(next);
    }

    private void Update()
    {
        float heal = VeterancyRules.HealPerSecond(Rank);
        if (heal <= 0f || !unit.IsAlive) return;

        // Breuken optellen en pas gehele HP toepassen. Anders loopt het herstel bij lage waarden stil.
        healAccumulator += heal * Time.deltaTime;
        int whole = Mathf.FloorToInt(healAccumulator);
        if (whole <= 0) return;

        healAccumulator -= whole;
        unit.Heal(whole);
    }
}
