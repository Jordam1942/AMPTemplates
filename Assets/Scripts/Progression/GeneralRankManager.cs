using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Generaal-voortgang van één team. Verdient General-XP door kills en opgebouwde gebouwen.
/// Elke ster geeft één Promotion Point. Points worden besteed om Generals' Powers te ontgrendelen,
/// in tech-tree volgorde: eerst de vereiste sterren, dan de voorgaande powers.
/// </summary>
[DisallowMultipleComponent]
public sealed class GeneralRankManager : MonoBehaviour
{
    private const int MaxStars = 5;

    private static readonly List<GeneralRankManager> managers = new List<GeneralRankManager>(2);

    /// <summary>De manager van het lokale team (team 0). Het HUD gebruikt deze.</summary>
    public static GeneralRankManager Instance => ForTeam(UnitController.LocalPlayerTeam);

    public static GeneralRankManager ForTeam(int team)
    {
        for (int i = 0; i < managers.Count; i++)
        {
            if (managers[i].teamId == team) return managers[i];
        }

        return null;
    }

    [SerializeField] private int teamId = UnitController.LocalPlayerTeam;

    [Tooltip("XP-drempel per ster, van ster 1 tot 5. Index 0 = ster 1 en moet 0 zijn.")]
    [SerializeField] private int[] starThresholds = { 0, 1000, 2500, 5000, 9000 };

    [Tooltip("XP per vernietigde vijandelijke unit = kosten x deze factor.")]
    [SerializeField, Min(0f)] private float killXpFactor = 1f;

    [Tooltip("XP per opgebouwd gebouw = kosten x deze factor.")]
    [SerializeField, Min(0f)] private float constructionXpFactor = 0.5f;

    private readonly HashSet<GeneralPowerSO> unlocked = new HashSet<GeneralPowerSO>();

    public event Action Changed;

    public int TeamId => teamId;
    public int Xp { get; private set; }
    public int Stars { get; private set; } = 1;
    public int PromotionPoints { get; private set; } = 1;

    private void Awake()
    {
        if (ForTeam(teamId) != null)
        {
            Debug.LogWarning($"Er bestaat al een generaal-manager voor team {teamId}.", this);
            Destroy(this);
            return;
        }

        managers.Add(this);
    }

    private void OnDestroy()
    {
        managers.Remove(this);
    }

    private void OnEnable()
    {
        UnitController.UnitKilled += OnUnitKilled;
        BuildingController.ConstructionCompletedGlobal += OnConstructionCompleted;
    }

    private void OnDisable()
    {
        UnitController.UnitKilled -= OnUnitKilled;
        BuildingController.ConstructionCompletedGlobal -= OnConstructionCompleted;
    }

    // ---------- XP en sterren ----------

    public void AddXp(int amount)
    {
        if (amount <= 0) return;

        Xp += amount;

        int stars = StarsForXp(Xp);
        if (stars > Stars)
        {
            // Elke nieuwe ster levert één Promotion Point op.
            PromotionPoints += stars - Stars;
            Stars = stars;
        }

        Changed?.Invoke();
    }

    private int StarsForXp(int xp)
    {
        int stars = 1;
        for (int i = 0; i < starThresholds.Length && i < MaxStars; i++)
        {
            if (xp >= starThresholds[i]) stars = i + 1;
        }

        return stars;
    }

    private void OnUnitKilled(UnitController victim, UnitController killer)
    {
        if (killer == null || killer.Team != teamId || victim.Data == null) return;
        AddXp(Mathf.RoundToInt(victim.Data.cost * killXpFactor));
    }

    private void OnConstructionCompleted(BuildingController building)
    {
        if (building.Team != teamId || building.Data == null) return;
        AddXp(Mathf.RoundToInt(building.Data.cost * constructionXpFactor));
    }

    // ---------- Powers ontgrendelen ----------

    public bool IsUnlocked(GeneralPowerSO power) => unlocked.Contains(power);

    /// <summary>Kan deze power ontgrendeld worden? Vereist ster-rang, voorgaande powers en voldoende Promotion Points.</summary>
    public bool CanUnlock(GeneralPowerSO power)
    {
        if (power == null || unlocked.Contains(power)) return false;
        if (power.requiredStar > Stars || PromotionPoints < power.promotionCost) return false;

        for (int i = 0; i < power.prerequisites.Length; i++)
        {
            if (!unlocked.Contains(power.prerequisites[i])) return false;
        }

        return true;
    }

    public bool TryUnlock(GeneralPowerSO power)
    {
        if (!CanUnlock(power)) return false;

        PromotionPoints -= power.promotionCost;
        unlocked.Add(power);
        Changed?.Invoke();
        return true;
    }
}
