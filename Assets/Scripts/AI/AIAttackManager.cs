using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Productie en legers van de AI.
///  - Productie: kiest per productiegebouw de volgende unit-rol op basis van de ratio infanterie/voertuigen
///    en het aantal gatherers. Gerekend wordt met units én wat er al in de wachtrijen staat.
///  - Squad: units die bij het verzamelpunt staan, vormen de squad.
///  - Aanvalsgolf: zodra de squad de drempel haalt, start een golf met doelprioritering
///    (gatherers en supply centers, dan productiegebouwen, dan overige troepen).
///    Bij zwaar verlies trekt de golf zich terug, als de moeilijkheidsgraad dat toestaat.
/// </summary>
[DisallowMultipleComponent]
public sealed class AIAttackManager : MonoBehaviour
{
    [Header("Verzamelen")]
    [Tooltip("Defensief verzamelpunt. Leeg = het hoofdgebouw van de AI.")]
    [SerializeField] private Transform rallyPoint;

    [Tooltip("Units binnen deze straal van het verzamelpunt horen bij de squad.")]
    [SerializeField, Min(1f)] private float squadRadius = 16f;

    [Header("Samenstelling")]
    [Tooltip("Aandeel infanterie tegenover voertuigen. 0 = alleen voertuigen, 1 = alleen infanterie.")]
    [SerializeField, Range(0f, 1f)] private float infantryRatio = 0.6f;

    private readonly List<UnitController> squad = new List<UnitController>(32);
    private readonly List<UnitController> attackGroup = new List<UnitController>(32);
    private readonly int[] queuedCounts = new int[4];

    private bool attacking;
    private IDamageable waveTarget;
    private int waveStartCount;
    private float nextWaveTime;

    public void Plan(SkirmishAIController ctx)
    {
        ApplyRallyPoints(ctx);
        ProduceCombatUnits(ctx);
        CollectSquad(ctx);

        if (attacking) TickWave(ctx);
        else TryStartWave(ctx);
    }

    private Vector3 Rally(SkirmishAIController ctx) => rallyPoint != null ? rallyPoint.position : ctx.BaseCenter;

    // ---------- Productie ----------

    private void ApplyRallyPoints(SkirmishAIController ctx)
    {
        Vector3 rally = Rally(ctx);

        IReadOnlyList<BuildingController> buildings = BuildingController.Registry;
        for (int i = 0; i < buildings.Count; i++)
        {
            BuildingController b = buildings[i];
            if (b.Team != ctx.Team || !b.IsOperational || b == ctx.CommandCenter) continue;
            if (b.Data.productionOptions.Length == 0) continue;

            // Alleen opnieuw zetten als het rally point echt is verschoven. Dat scheelt NavMesh-sampling.
            if ((b.RallyPoint - rally).sqrMagnitude > 1f) b.SetRallyPoint(rally);
        }
    }

    private void ProduceCombatUnits(SkirmishAIController ctx)
    {
        // Tellingen: levende units plus wat al in een wachtrij staat.
        int infantry = AIUnitClassifier.CountAlive(ctx, AIUnitClass.Infantry) + AIUnitClassifier.CountPending(ctx, AIUnitClass.Infantry);
        int vehicles = AIUnitClassifier.CountAlive(ctx, AIUnitClass.Vehicle) + AIUnitClassifier.CountPending(ctx, AIUnitClass.Vehicle);
        int gatherers = AIUnitClassifier.CountAlive(ctx, AIUnitClass.Gatherer) + AIUnitClassifier.CountPending(ctx, AIUnitClass.Gatherer);

        queuedCounts[(int)AIUnitClass.Infantry] = infantry;
        queuedCounts[(int)AIUnitClass.Vehicle] = vehicles;
        queuedCounts[(int)AIUnitClass.Gatherer] = gatherers;

        IReadOnlyList<BuildingController> buildings = BuildingController.Registry;
        for (int i = 0; i < buildings.Count; i++)
        {
            BuildingController b = buildings[i];
            if (b.Team != ctx.Team || !b.IsOperational || b == ctx.CommandCenter) continue;
            if (b.Queue == null || b.Queue.IsFull) continue;

            AIUnitClass want = DesiredClass(ctx.Settings, queuedCounts);
            int index = AIUnitClassifier.FindOption(b.Data, want);

            // Dit gebouw kan de gewenste rol niet maken. Dan valt het terug op de andere gevechtsrol.
            if (index < 0 && want != AIUnitClass.Gatherer)
                index = AIUnitClassifier.FindOption(b.Data, want == AIUnitClass.Infantry ? AIUnitClass.Vehicle : AIUnitClass.Infantry);

            if (index < 0 || !b.TryQueue(index)) continue;

            AIUnitClass queued = AIUnitClassifier.Of(b.Data.productionOptions[index]);
            queuedCounts[(int)queued]++;
        }
    }

    /// <summary>Eerst genoeg gatherers, daarna de infanterie/voertuig-ratio.</summary>
    private AIUnitClass DesiredClass(AIDifficultySettings s, int[] counts)
    {
        if (counts[(int)AIUnitClass.Gatherer] < s.targetGatherers) return AIUnitClass.Gatherer;

        int infantry = counts[(int)AIUnitClass.Infantry];
        int total = infantry + counts[(int)AIUnitClass.Vehicle];
        return infantry < (total + 1) * infantryRatio ? AIUnitClass.Infantry : AIUnitClass.Vehicle;
    }

    // ---------- Squad ----------

    private void CollectSquad(SkirmishAIController ctx)
    {
        RemoveDead(squad);
        RemoveDead(attackGroup);

        Vector3 rally = Rally(ctx);
        float radiusSqr = squadRadius * squadRadius;

        IReadOnlyList<UnitController> units = UnitController.Registry;
        for (int i = 0; i < units.Count; i++)
        {
            UnitController u = units[i];
            if (u.Team != ctx.Team || !u.IsIdle) continue;
            if (squad.Contains(u) || attackGroup.Contains(u)) continue;

            // Alleen gevechtsunits. Gatherers en dozers hebben hun eigen taak.
            AIUnitClass unitClass = AIUnitClassifier.Of(u);
            if (unitClass != AIUnitClass.Infantry && unitClass != AIUnitClass.Vehicle) continue;

            if ((u.Position - rally).sqrMagnitude <= radiusSqr) squad.Add(u);
        }
    }

    // ---------- Aanvalsgolven ----------

    private void TryStartWave(SkirmishAIController ctx)
    {
        if (squad.Count < ctx.Settings.attackThreshold || Time.time < nextWaveTime) return;

        IDamageable target = ChooseTarget(ctx, Centroid(squad));
        if (target == null)
        {
            // Geen vijand bekend. Wacht even voordat er opnieuw gezocht wordt.
            nextWaveTime = Time.time + 10f;
            return;
        }

        attackGroup.Clear();
        attackGroup.AddRange(squad);
        squad.Clear();

        waveStartCount = attackGroup.Count;
        waveTarget = target;
        attacking = true;

        IssueAttack();
    }

    private void TickWave(SkirmishAIController ctx)
    {
        RemoveDead(attackGroup);
        AIDifficultySettings s = ctx.Settings;

        if (attackGroup.Count == 0)
        {
            EndWave(ctx, retreated: false);
            return;
        }

        if (s.canRetreat && attackGroup.Count <= waveStartCount * (1f - s.retreatAtLossRatio))
        {
            EndWave(ctx, retreated: true);
            return;
        }

        // Het doel is dood of verdwenen: kies het volgende doel volgens de prioriteit.
        if (waveTarget == null || !waveTarget.IsAlive)
        {
            waveTarget = ChooseTarget(ctx, Centroid(attackGroup));
            if (waveTarget == null)
            {
                EndWave(ctx, retreated: false);
                return;
            }

            IssueAttack();
        }
    }

    private void EndWave(SkirmishAIController ctx, bool retreated)
    {
        attacking = false;
        waveTarget = null;
        nextWaveTime = Time.time + ctx.Settings.waveCooldown;

        if (retreated)
        {
            Vector3 rally = Rally(ctx);
            for (int i = 0; i < attackGroup.Count; i++)
                attackGroup[i].CommandMove(rally);
        }

        // De units gaan terug naar idle. CollectSquad neemt ze op zodra ze bij het verzamelpunt zijn.
        attackGroup.Clear();
    }

    private void IssueAttack()
    {
        for (int i = 0; i < attackGroup.Count; i++)
            attackGroup[i].CommandAttack(waveTarget);
    }

    /// <summary>
    /// Doelprioritering: 0 = vijandelijke gatherers en supply centers, 1 = productiegebouwen,
    /// 2 = overige units, 3 = overige gebouwen. Binnen één prioriteit wint de dichtstbijzijnde.
    /// </summary>
    private static IDamageable ChooseTarget(SkirmishAIController ctx, Vector3 from)
    {
        IDamageable best = null;
        int bestTier = int.MaxValue;
        float bestSqr = float.MaxValue;

        IReadOnlyList<UnitController> units = UnitController.Registry;
        for (int i = 0; i < units.Count; i++)
        {
            UnitController u = units[i];
            if (u.Team == ctx.Team || !u.IsAlive) continue;

            int tier = AIUnitClassifier.Of(u) == AIUnitClass.Gatherer ? 0 : 2;
            Consider(u, tier, from, ref best, ref bestTier, ref bestSqr);
        }

        IReadOnlyList<BuildingController> buildings = BuildingController.Registry;
        for (int i = 0; i < buildings.Count; i++)
        {
            BuildingController b = buildings[i];
            if (b.Team == ctx.Team || !b.IsAlive) continue;

            int tier;
            if (b.TryGetComponent(out SupplyCenter _)) tier = 0;
            else if (b.Data != null && b.Data.productionOptions.Length > 0) tier = 1;
            else tier = 3;

            Consider(b, tier, from, ref best, ref bestTier, ref bestSqr);
        }

        return best;
    }

    private static void Consider(IDamageable candidate, int tier, Vector3 from,
        ref IDamageable best, ref int bestTier, ref float bestSqr)
    {
        float sq = (candidate.Position - from).sqrMagnitude;
        if (tier < bestTier || (tier == bestTier && sq < bestSqr))
        {
            best = candidate;
            bestTier = tier;
            bestSqr = sq;
        }
    }

    // ---------- Hulpfuncties ----------

    private static void RemoveDead(List<UnitController> list)
    {
        for (int i = list.Count - 1; i >= 0; i--)
        {
            if (!list[i].IsAlive) list.RemoveAt(i);
        }
    }

    private static Vector3 Centroid(List<UnitController> list)
    {
        if (list.Count == 0) return Vector3.zero;

        Vector3 sum = Vector3.zero;
        for (int i = 0; i < list.Count; i++) sum += list[i].Position;
        return sum / list.Count;
    }
}
