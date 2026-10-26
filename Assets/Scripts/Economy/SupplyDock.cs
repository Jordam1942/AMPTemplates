using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Eindige supply-bron met een vaste totaalvoorraad. Gatherers halen er kratten uit.
/// Een leeg dock wordt uitgesloten van zoekopdrachten en blijft als object op de map staan.
/// </summary>
[DisallowMultipleComponent]
public sealed class SupplyDock : MonoBehaviour
{
    private static readonly List<SupplyDock> registry = new List<SupplyDock>(16);

    public static IReadOnlyList<SupplyDock> Registry => registry;

    [SerializeField, Min(1)] private int totalSupply = 25000;

    [Tooltip("Optioneel: schaalt in Y mee met de resterende voorraad.")]
    [SerializeField] private Transform fillVisual;

    [SerializeField, Min(0.5f)] private float approachRadius = 3f;

    public int Remaining { get; private set; }
    public bool IsDepleted => Remaining <= 0;
    public Vector3 Position => transform.position;
    public float ApproachRadius => approachRadius;

    public event Action<SupplyDock> Depleted;

    private void Awake()
    {
        Remaining = totalSupply;
        UpdateVisual();
    }

    private void OnEnable()
    {
        if (!registry.Contains(this)) registry.Add(this);
    }

    private void OnDisable()
    {
        registry.Remove(this);
    }

    /// <summary>Neemt maximaal 'requested' uit de voorraad. Geeft de werkelijk genomen hoeveelheid terug.</summary>
    public int Withdraw(int requested)
    {
        if (requested <= 0 || IsDepleted) return 0;

        int taken = Mathf.Min(requested, Remaining);
        Remaining -= taken;
        UpdateVisual();

        if (IsDepleted) Depleted?.Invoke(this);
        return taken;
    }

    /// <summary>Dichtstbijzijnde dock dat nog voorraad heeft.</summary>
    public static SupplyDock FindNearest(Vector3 from)
    {
        SupplyDock best = null;
        float bestSqr = float.MaxValue;

        for (int i = 0; i < registry.Count; i++)
        {
            SupplyDock dock = registry[i];
            if (dock.IsDepleted) continue;

            float distSqr = (dock.Position - from).sqrMagnitude;
            if (distSqr < bestSqr)
            {
                bestSqr = distSqr;
                best = dock;
            }
        }

        return best;
    }

    private void UpdateVisual()
    {
        if (fillVisual == null) return;

        float fraction = totalSupply > 0 ? (float)Remaining / totalSupply : 0f;
        fillVisual.localScale = new Vector3(1f, Mathf.Max(0.01f, fraction), 1f);
    }
}
