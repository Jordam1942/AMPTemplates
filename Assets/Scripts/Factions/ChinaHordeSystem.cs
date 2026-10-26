using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// China Horde Bonus. Groepen van minimaal 'threshold' identieke Chinese gevechtsunits van hetzelfde team
/// binnen 'radius' krijgen samen +fire rate en +bewegingssnelheid. De leidende unit toont een status-icoon.
///
/// Controle is periodiek (checkInterval), niet per frame. Per controle loopt het over de units met
/// een nabijheidscheck. Alleen units met dezelfde data en hetzelfde team tellen mee. Dat houdt het
/// aantal vergelijkingen klein: bij enkele tientallen units is dit binnen één frame-budget.
/// </summary>
[DisallowMultipleComponent]
public sealed class ChinaHordeSystem : MonoBehaviour
{
    [SerializeField, Min(2)] private int threshold = 5;
    [SerializeField, Min(1f)] private float radius = 12f;
    [SerializeField, Min(0.05f)] private float checkInterval = 0.3f;

    [Tooltip("Vermenigvuldiger op de vuursnelheid van leden van een actieve horde.")]
    [SerializeField, Min(1f)] private float fireRateBonus = 1.25f;

    [Tooltip("Vermenigvuldiger op de bewegingssnelheid van leden van een actieve horde.")]
    [SerializeField, Min(1f)] private float speedBonus = 1.25f;

    private readonly List<UnitController> candidates = new List<UnitController>(128);
    private bool[] isMember = new bool[0];
    private bool[] isLeader = new bool[0];
    private float timer;

    private void Update()
    {
        timer -= Time.deltaTime;
        if (timer > 0f) return;

        timer = checkInterval;
        Evaluate();
    }

    private void Evaluate()
    {
        // Verzamel de China-gevechtsunits. Gatherers en dozers doen niet mee.
        candidates.Clear();
        IReadOnlyList<UnitController> units = UnitController.Registry;
        for (int i = 0; i < units.Count; i++)
        {
            UnitController u = units[i];
            if (u.Data == null || u.Data.faction != Faction.China || !u.IsAlive) continue;
            if (IsNonCombat(u)) continue;

            candidates.Add(u);
        }

        int n = candidates.Count;
        EnsureCapacity(n);

        // Eerste pas: welke units hebben minimaal threshold-1 buren van hetzelfde type en team?
        float radiusSqr = radius * radius;
        for (int i = 0; i < n; i++)
        {
            int neighbours = 0;
            for (int j = 0; j < n; j++)
            {
                if (i == j || !SameGroup(candidates[i], candidates[j])) continue;
                if ((candidates[i].Position - candidates[j].Position).sqrMagnitude <= radiusSqr) neighbours++;
            }

            isMember[i] = neighbours + 1 >= threshold;
        }

        // Tweede pas: de leider is het lidmaatschap met het laagste index. Alleen dat icoon wordt getoond.
        for (int i = 0; i < n; i++)
        {
            isLeader[i] = isMember[i];
            if (!isMember[i]) continue;

            for (int j = 0; j < i; j++)
            {
                if (!isMember[j] || !SameGroup(candidates[i], candidates[j])) continue;
                if ((candidates[i].Position - candidates[j].Position).sqrMagnitude > radiusSqr) continue;

                isLeader[i] = false;
                break;
            }
        }

        ApplyBonuses(n);
    }

    private void ApplyBonuses(int n)
    {
        // Eerst alle bonussen en iconen uit, daarna alleen de huidige leden aan. Zo blijft er geen verouderde status.
        IReadOnlyList<UnitController> units = UnitController.Registry;
        for (int i = 0; i < units.Count; i++)
        {
            UnitController u = units[i];
            if (u.HasHordeBonus) u.SetHordeBonus(false, 1f, 1f);
            // Het icoon staat op een kindobject boven het model. GetComponentInChildren alloceert niet.
            u.GetComponentInChildren<HordeIndicator>(true)?.SetVisible(false);
        }

        for (int i = 0; i < n; i++)
        {
            if (!isMember[i]) continue;

            UnitController u = candidates[i];
            u.SetHordeBonus(true, fireRateBonus, speedBonus);

            if (isLeader[i]) u.GetComponentInChildren<HordeIndicator>(true)?.SetVisible(true);
        }
    }

    private static bool SameGroup(UnitController a, UnitController b) =>
        a.Team == b.Team && a.Data == b.Data;

    private static bool IsNonCombat(UnitController u) =>
        u.TryGetComponent(out SupplyGatherer _) || u.TryGetComponent(out Builder _);

    private void EnsureCapacity(int n)
    {
        if (isMember.Length >= n) return;

        // Groeit alleen zelden, bij meer units dan ooit tevoren. Verder geen allocaties.
        isMember = new bool[n * 2];
        isLeader = new bool[n * 2];
    }
}
