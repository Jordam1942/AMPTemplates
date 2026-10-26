using System.Collections.Generic;
using UnityEngine;

public enum AIUnitClass { Infantry, Vehicle, Gatherer, Builder }

/// <summary>
/// Classificeert units en productie-opties in rollen. Een gatherer heeft een SupplyGatherer,
/// een dozer heeft een Builder. De rest is infanterie of voertuig, op basis van het pantsertype.
/// </summary>
public static class AIUnitClassifier
{
    public static AIUnitClass Of(UnitController unit)
    {
        if (unit.TryGetComponent(out SupplyGatherer _)) return AIUnitClass.Gatherer;
        if (unit.TryGetComponent(out Builder _)) return AIUnitClass.Builder;
        return unit.Armor == ArmorType.Infantry ? AIUnitClass.Infantry : AIUnitClass.Vehicle;
    }

    public static AIUnitClass Of(ProductionOption option)
    {
        if (option.unitPrefab == null) return AIUnitClass.Vehicle;

        if (option.unitPrefab.TryGetComponent(out SupplyGatherer _)) return AIUnitClass.Gatherer;
        if (option.unitPrefab.TryGetComponent(out Builder _)) return AIUnitClass.Builder;

        UnitController prefabUnit = option.unitPrefab.GetComponent<UnitController>();
        bool infantry = prefabUnit != null && prefabUnit.Armor == ArmorType.Infantry;
        return infantry ? AIUnitClass.Infantry : AIUnitClass.Vehicle;
    }

    /// <summary>Index van de eerste unit-optie van deze rol, of -1 als het gebouw die niet kan maken.</summary>
    public static int FindOption(BuildingDataSO data, AIUnitClass unitClass)
    {
        if (data == null) return -1;

        ProductionOption[] options = data.productionOptions;
        for (int i = 0; i < options.Length; i++)
        {
            if (options[i].kind != ProductionKind.Unit || options[i].unitPrefab == null) continue;
            if (Of(options[i]) == unitClass) return i;
        }

        return -1;
    }

    /// <summary>Aantal levende units van de rol in het team van de AI.</summary>
    public static int CountAlive(SkirmishAIController ctx, AIUnitClass unitClass)
    {
        int count = 0;
        IReadOnlyList<UnitController> units = UnitController.Registry;

        for (int i = 0; i < units.Count; i++)
        {
            UnitController u = units[i];
            if (u.Team == ctx.Team && u.IsAlive && Of(u) == unitClass) count++;
        }

        return count;
    }

    /// <summary>Aantal units van de rol die in een wachtrij van de AI staan.</summary>
    public static int CountPending(SkirmishAIController ctx, AIUnitClass unitClass)
    {
        int count = 0;
        IReadOnlyList<BuildingController> buildings = BuildingController.Registry;

        for (int i = 0; i < buildings.Count; i++)
        {
            BuildingController b = buildings[i];
            if (b.Team != ctx.Team || b.Queue == null) continue;

            for (int e = 0; e < b.Queue.Count; e++)
            {
                if (b.Queue.TryGetEntry(e, out ProductionOption option, out _) && Of(option) == unitClass)
                    count++;
            }
        }

        return count;
    }
}
