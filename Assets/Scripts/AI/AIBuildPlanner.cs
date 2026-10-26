using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Bouwplanner van de AI. Eén beslisronde kiest hoogstens één bouwactie, in deze volgorde:
///  1. Stroomcentrale, als de AI op Low Power staat en er geen centrale in aanbouw is.
///  2. Supply center bij een vrij dock, en dozers bijbestellen.
///  3. Productiegebouwen (Barracks, War Factory) rondom de basis.
/// Plaatsing gebruikt PlacementRules, dezelfde regels als voor de speler, plus een marge tussen gebouwen.
/// </summary>
[DisallowMultipleComponent]
public sealed class AIBuildPlanner : MonoBehaviour
{
    [Header("Gebouwdata")]
    [SerializeField] private BuildingDataSO powerPlantData;
    [SerializeField] private BuildingDataSO supplyCenterData;
    [SerializeField] private BuildingDataSO barracksData;
    [SerializeField] private BuildingDataSO warFactoryData;

    [Header("Plaatsing")]
    [SerializeField] private LayerMask groundMask;

    [Tooltip("Zelfde laag als voor de speler: gebouwen en statische obstakels.")]
    [SerializeField] private LayerMask blockingMask;

    [SerializeField, Min(0f)] private float baseMinRadius = 8f;
    [SerializeField, Min(1f)] private float baseMaxRadius = 24f;

    [Tooltip("Minimale vrije ruimte in meter rond elk AI-gebouw. Houdt ruimte voor looproutes.")]
    [SerializeField, Min(0f)] private float buildingPadding = 1.5f;

    [Tooltip("Een nieuw supply center mag niet binnen deze straal van een bestaand AI-center liggen.")]
    [SerializeField, Min(1f)] private float supplyCenterClaimRadius = 18f;

    [SerializeField, Min(4)] private int candidatesPerRing = 12;
    [SerializeField, Min(0.5f)] private float ringStep = 3f;

    private readonly Collider[] overlapBuffer = new Collider[32];

    public void Plan(SkirmishAIController ctx)
    {
        PlayerEconomy economy = ctx.Economy;
        AIDifficultySettings s = ctx.Settings;
        if (economy == null || ctx.CountPendingConstructionTotal() >= s.maxConcurrentBuilds) return;

        // Prioriteit 1: stroom. Maximaal één stroomcentrale tegelijk in aanbouw.
        if (powerPlantData != null && economy.IsLowPower && ctx.CountPendingConstruction(powerPlantData) == 0
            && TryPlaceNearBase(ctx, powerPlantData))
            return;

        // Prioriteit 2: supply centers bij vrije docks, daarna dozers bijbestellen.
        if (supplyCenterData != null && ctx.CountOwned(supplyCenterData) < s.targetSupplyCenters
            && TryPlaceSupplyCenter(ctx))
            return;

        if (EnsureBuilders(ctx)) return;

        // Prioriteit 3: productiegebouwen rondom de basis.
        if (barracksData != null && ctx.CountOwned(barracksData) < s.targetBarracks
            && TryPlaceNearBase(ctx, barracksData))
            return;

        if (warFactoryData != null && ctx.CountOwned(warFactoryData) < s.targetWarFactories
            && TryPlaceNearBase(ctx, warFactoryData))
            return;
    }

    // ---------- Prioriteit 2: dozers ----------

    /// <summary>Bestelt een dozer bij het hoofdgebouw als er te weinig zijn, inclusief de dozers in de wachtrij.</summary>
    private static bool EnsureBuilders(SkirmishAIController ctx)
    {
        BuildingController cc = ctx.CommandCenter;
        if (cc == null || !cc.IsOperational) return false;

        int have = AIUnitClassifier.CountAlive(ctx, AIUnitClass.Builder)
                   + AIUnitClassifier.CountPending(ctx, AIUnitClass.Builder);
        if (have >= ctx.Settings.targetBuilders) return false;

        int index = AIUnitClassifier.FindOption(cc.Data, AIUnitClass.Builder);
        return index >= 0 && cc.TryQueue(index);
    }

    // ---------- Plaatsing ----------

    private bool TryPlaceNearBase(SkirmishAIController ctx, BuildingDataSO data)
    {
        return TrySearch(ctx, data, ctx.BaseCenter, baseMinRadius, baseMaxRadius);
    }

    /// <summary>
    /// Zoekt een supply center naast het dichtstbijzijnde vrije dock. "Vrij" betekent: niet depleted
    /// en zonder eigen center in de buurt.
    /// </summary>
    private bool TryPlaceSupplyCenter(SkirmishAIController ctx)
    {
        SupplyDock dock = FindFreeDock(ctx);
        if (dock == null) return false;

        return TrySearch(ctx, supplyCenterData, dock.Position,
            dock.ApproachRadius + 3f, dock.ApproachRadius + 12f);
    }

    private SupplyDock FindFreeDock(SkirmishAIController ctx)
    {
        SupplyDock best = null;
        float bestSqr = float.MaxValue;
        Vector3 basePos = ctx.BaseCenter;

        IReadOnlyList<SupplyDock> docks = SupplyDock.Registry;
        for (int i = 0; i < docks.Count; i++)
        {
            SupplyDock dock = docks[i];
            if (dock.IsDepleted || HasOwnCenterNear(ctx, dock.Position)) continue;

            float sq = (dock.Position - basePos).sqrMagnitude;
            if (sq < bestSqr)
            {
                bestSqr = sq;
                best = dock;
            }
        }

        return best;
    }

    private bool HasOwnCenterNear(SkirmishAIController ctx, Vector3 position)
    {
        float claimSqr = supplyCenterClaimRadius * supplyCenterClaimRadius;
        IReadOnlyList<SupplyCenter> centers = SupplyCenter.Registry;

        for (int i = 0; i < centers.Count; i++)
        {
            SupplyCenter c = centers[i];
            if (c.Building.Team == ctx.Team && (c.Position - position).sqrMagnitude <= claimSqr) return true;
        }

        return false;
    }

    /// <summary>
    /// Zoekt in concentrische ringen een geldige plek. Begint bij een willekeurige hoek, zodat de
    /// basis niet altijd in dezelfde richting groeit. Stopt bij de eerste plek die PlacementRules goedkeurt.
    /// </summary>
    private bool TrySearch(SkirmishAIController ctx, BuildingDataSO data, Vector3 origin, float minRadius, float maxRadius)
    {
        if (data == null) return false;

        float angleOffset = Random.value * 360f;
        float angleStep = 360f / candidatesPerRing;

        for (float r = minRadius; r <= maxRadius; r += ringStep)
        {
            for (int i = 0; i < candidatesPerRing; i++)
            {
                float angle = (angleOffset + i * angleStep) * Mathf.Deg2Rad;
                Vector3 probe = origin + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * r;

                if (!TryGround(probe, out Vector3 ground)) continue;

                PlacementResult result = PlacementRules.Evaluate(
                    data, ground, Quaternion.identity, buildingPadding,
                    blockingMask.value, overlapBuffer, ctx.Economy);

                if (result != PlacementResult.Valid) continue;
                if (PlacementRules.TryPlace(data, ground, Quaternion.identity, ctx.Economy, out _)) return true;
            }
        }

        return false;
    }

    private bool TryGround(Vector3 probe, out Vector3 ground)
    {
        Vector3 from = probe + Vector3.up * 100f;
        bool hit = Physics.Raycast(from, Vector3.down, out RaycastHit info, 200f, groundMask, QueryTriggerInteraction.Ignore);
        ground = hit ? info.point : Vector3.zero;
        return hit;
    }
}
