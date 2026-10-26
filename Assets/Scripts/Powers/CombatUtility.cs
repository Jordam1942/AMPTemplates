using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Gedeelde schade-helpers voor powers. Schade gaat direct, zonder armor, en telt alleen vijandelijke
/// doelen. Loopt over de registries met een index, zodat er geen enumerator-allocaties zijn.
/// </summary>
public static class CombatUtility
{
    /// <summary>Brengt schade toe aan alle vijandelijke units en gebouwen binnen radius van center.</summary>
    public static void DamageArea(Vector3 center, float radius, int damage, int attackerTeam)
    {
        if (damage <= 0 || radius <= 0f) return;

        float radiusSqr = radius * radius;

        IReadOnlyList<UnitController> units = UnitController.Registry;
        for (int i = 0; i < units.Count; i++)
        {
            UnitController u = units[i];
            if (u.Team == attackerTeam || !u.IsAlive) continue;
            if ((u.Position - center).sqrMagnitude <= radiusSqr) u.TakeDamage(damage);
        }

        IReadOnlyList<BuildingController> buildings = BuildingController.Registry;
        for (int i = 0; i < buildings.Count; i++)
        {
            BuildingController b = buildings[i];
            if (b.Team == attackerTeam || !b.IsAlive) continue;
            if ((b.Position - center).sqrMagnitude <= radiusSqr) b.TakeDamage(damage);
        }
    }
}
