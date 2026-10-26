using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Gedeelde selectiestatus voor de HUD. Wordt gevuld door TouchSelectionHandler.
/// Eén selectie tegelijk: óf een groep units, óf één gebouw.
/// </summary>
public static class SelectionState
{
    private static readonly List<UnitController> units = new List<UnitController>(64);

    public static IReadOnlyList<UnitController> Units => units;
    public static BuildingController Building { get; private set; }

    /// <summary>Wordt aangeroepen bij elke wijziging van de selectie. Niet per frame.</summary>
    public static event Action Changed;

    public static void SetUnits(IList<UnitController> newUnits)
    {
        units.Clear();
        for (int i = 0; i < newUnits.Count; i++) units.Add(newUnits[i]);

        Building = null;
        Changed?.Invoke();
    }

    public static void SetBuilding(BuildingController building)
    {
        units.Clear();
        Building = building;
        Changed?.Invoke();
    }

    public static void Clear()
    {
        if (units.Count == 0 && Building == null) return;

        units.Clear();
        Building = null;
        Changed?.Invoke();
    }

    // Voorkomt dat de statische staat tussen Play-sessies blijft hangen als domain reload uit staat.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetOnLoad()
    {
        units.Clear();
        Building = null;
        Changed = null;
    }
}
