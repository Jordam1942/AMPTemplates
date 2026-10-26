using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Dozer- of worker-gedrag: rijdt naar een gebouw in opbouw, bouwt het op, en zoekt daarna opnieuw werk.
/// Gebruikt UnitController voor beweging. Auto-acquire staat uit, zodat een bouwer niet van taak wisselt.
/// </summary>
[RequireComponent(typeof(UnitController))]
[DisallowMultipleComponent]
public sealed class Builder : MonoBehaviour
{
    private static readonly List<Builder> registry = new List<Builder>(16);

    public static IReadOnlyList<Builder> Registry => registry;

    [Tooltip("Vermenigvuldigt de bouwsnelheid. 1 = de basisbouwtijd van het gebouw.")]
    [SerializeField, Min(0.01f)] private float buildSpeed = 1f;

    [Tooltip("Hoe vaak een bouwer die niet bij het gebouw komt, een nieuwe route krijgt.")]
    [SerializeField, Min(0.1f)] private float repathInterval = 1f;

    private UnitController unit;
    private BuildingController site;
    private float repathTimer;

    public bool IsBusy => site != null;

    private void Awake()
    {
        unit = GetComponent<UnitController>();
        unit.AutoAcquire = false;
    }

    private void OnEnable()
    {
        if (!registry.Contains(this)) registry.Add(this);
    }

    private void OnDisable()
    {
        registry.Remove(this);
    }

    /// <summary>Dichtstbijzijnde vrije bouwer van het team.</summary>
    public static Builder FindNearestIdle(int team, Vector3 position)
    {
        Builder best = null;
        float bestSqr = float.MaxValue;

        for (int i = 0; i < registry.Count; i++)
        {
            Builder candidate = registry[i];
            if (candidate.IsBusy || candidate.unit.Team != team || !candidate.unit.IsAlive) continue;

            float distSqr = (candidate.unit.Position - position).sqrMagnitude;
            if (distSqr < bestSqr)
            {
                bestSqr = distSqr;
                best = candidate;
            }
        }

        return best;
    }

    public void Assign(BuildingController target)
    {
        site = target;
        site.SetBuilderAssigned(true);
        repathTimer = repathInterval;
        MoveToSite();
    }

    private void Update()
    {
        // Gebouw is verdwenen, opgebouwd of de bouwer is gesneuveld: stop de opdracht.
        if (site == null || !site.IsUnderConstruction || !site.IsAlive || !unit.IsAlive)
        {
            Release();
            return;
        }

        if (IsInWorkRange())
        {
            site.AddConstructionWork(Time.deltaTime * buildSpeed / site.BuildTime);
            return;
        }

        // Niet in bereik: herpad als de eerdere route is afgelopen zonder aankomst (bijvoorbeeld door een blokkade).
        repathTimer -= Time.deltaTime;
        if (repathTimer <= 0f && unit.CurrentState == UnitController.State.Idle)
        {
            repathTimer = repathInterval;
            MoveToSite();
        }
    }

    private bool IsInWorkRange()
    {
        Vector3 d = site.Position - unit.Position;
        d.y = 0f;
        return d.sqrMagnitude <= site.ApproachRadius * site.ApproachRadius;
    }

    /// <summary>Route naar de zijde van het gebouw die het dichtst bij de bouwer ligt.</summary>
    private void MoveToSite()
    {
        Vector3 offset = unit.Position - site.Position;
        offset.y = 0f;
        if (offset.sqrMagnitude < 0.01f) offset = Vector3.back;

        Vector3 approach = site.Position + offset.normalized * site.ApproachRadius;
        unit.CommandMove(approach);
    }

    private void Release()
    {
        if (site != null) site.SetBuilderAssigned(false);
        site = null;
    }
}
