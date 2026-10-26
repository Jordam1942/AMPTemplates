using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Afleverpunt voor supply. Hoort bij een operationeel BuildingController-gebouw.
/// Gatherers leveren hier hun lading af en de credits gaan naar de economie van het team.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(BuildingController))]
public sealed class SupplyCenter : MonoBehaviour
{
    private static readonly List<SupplyCenter> registry = new List<SupplyCenter>(8);

    public static IReadOnlyList<SupplyCenter> Registry => registry;

    private BuildingController building;

    public BuildingController Building => building;
    public Vector3 Position => transform.position;

    private void Awake()
    {
        building = GetComponent<BuildingController>();
    }

    private void OnEnable()
    {
        if (!registry.Contains(this)) registry.Add(this);
    }

    private void OnDisable()
    {
        registry.Remove(this);
    }

    /// <summary>Dichtstbijzijnde operationele supply center van het team.</summary>
    public static SupplyCenter FindNearest(Vector3 from, int team)
    {
        SupplyCenter best = null;
        float bestSqr = float.MaxValue;

        for (int i = 0; i < registry.Count; i++)
        {
            SupplyCenter center = registry[i];
            if (center.building.Team != team || !center.building.IsOperational) continue;

            float distSqr = (center.Position - from).sqrMagnitude;
            if (distSqr < bestSqr)
            {
                bestSqr = distSqr;
                best = center;
            }
        }

        return best;
    }

    /// <summary>Levert credits af bij de economie van het team van dit centrum.</summary>
    public void Deliver(int amount)
    {
        // De economie van het team van dit centrum. Een AI-center stort dus in de AI-kas.
        PlayerEconomy economy = PlayerEconomy.ForTeam(building.Team);
        if (economy != null) economy.Deposit(amount);
    }
}
