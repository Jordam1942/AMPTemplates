using UnityEngine;

/// <summary>
/// Autonome supply-loop: dock → laden → supply center → lossen → herhalen, tot alle docks leeg zijn.
/// De beweging komt van een IGatherMovement: NavMesh voor grondvoertuigen, ChinookFlight voor helikopters.
/// Deze klasse beheert alleen de fasen en de lading.
/// </summary>
[RequireComponent(typeof(UnitController))]
[DisallowMultipleComponent]
public sealed class SupplyGatherer : MonoBehaviour
{
    private enum Phase { Idle, ToDock, Loading, ToCenter, Unloading }

    [Tooltip("Krediet per trip. Een Supply Truck of Chinook laadt dit per rit.")]
    [SerializeField, Min(1)] private int carryCapacity = 300;

    [SerializeField, Min(0f)] private float loadTime = 2f;
    [SerializeField, Min(0f)] private float unloadTime = 1.5f;

    [Tooltip("Interval waarmee een inactieve gatherer opnieuw zoekt naar werk.")]
    [SerializeField, Min(0.5f)] private float idleRetryInterval = 3f;

    [Tooltip("Interval waarmee een vastgelopen gatherer een nieuwe route krijgt. Geldt niet tijdens een vlucht.")]
    [SerializeField, Min(0.5f)] private float repathInterval = 1f;

    private UnitController unit;
    private IGatherMovement movement;
    private SupplyDock dock;
    private SupplyCenter center;
    private Phase phase = Phase.Idle;
    private float timer;
    private float repathTimer;
    private int cargo;

    public int Cargo => cargo;

    private void Awake()
    {
        unit = GetComponent<UnitController>();
        unit.AutoAcquire = false;

        // Een ChinookFlight heeft voorrang. Anders valt de gatherer terug op grondbeweging.
        ChinookFlight flight = GetComponent<ChinookFlight>();
        movement = flight != null ? (IGatherMovement)flight : unit;
    }

    private void Start() => StartTrip();

    private void Update()
    {
        if (!unit.IsAlive) return;
        float dt = Time.deltaTime;

        switch (phase)
        {
            case Phase.Idle:
                timer -= dt;
                if (timer <= 0f)
                {
                    timer = idleRetryInterval;
                    StartTrip();
                }
                break;

            case Phase.ToDock:
                if (dock == null || dock.IsDepleted)
                {
                    StartTrip();
                    break;
                }

                if (HasArrived(dock.Position, dock.ApproachRadius)) BeginLoading();
                else RepathIfStuck(dock.Position, dt);
                break;

            case Phase.Loading:
                timer -= dt;
                if (timer <= 0f) FinishLoading();
                break;

            case Phase.ToCenter:
                if (center == null || !center.Building.IsOperational)
                {
                    RouteToCenterOrIdle();
                    break;
                }

                if (HasArrived(center.Position, center.Building.ApproachRadius)) BeginUnloading();
                else RepathIfStuck(center.Position, dt);
                break;

            case Phase.Unloading:
                timer -= dt;
                if (timer <= 0f) FinishUnloading();
                break;
        }
    }

    // ---------- Fasen ----------

    /// <summary>Kies het volgende werk. Met lading gaat de gatherer eerst naar een center.</summary>
    private void StartTrip()
    {
        if (cargo > 0)
        {
            RouteToCenterOrIdle();
            return;
        }

        dock = SupplyDock.FindNearest(unit.Position);
        if (dock == null)
        {
            SetIdle();
            return;
        }

        phase = Phase.ToDock;
        repathTimer = repathInterval;
        movement.MoveTo(dock.Position);
    }

    private void RouteToCenterOrIdle()
    {
        center = SupplyCenter.FindNearest(unit.Position, unit.Team);
        if (center == null)
        {
            // Geen operationeel center: houd de lading vast en probeer het later opnieuw.
            SetIdle();
            return;
        }

        phase = Phase.ToCenter;
        repathTimer = repathInterval;
        movement.MoveTo(center.Position);
    }

    private void BeginLoading()
    {
        phase = Phase.Loading;
        timer = loadTime;
    }

    private void FinishLoading()
    {
        cargo = dock.Withdraw(carryCapacity);

        // Het dock is leeg geraakt tijdens het laden. Zoek dan naar ander werk.
        if (cargo <= 0)
        {
            StartTrip();
            return;
        }

        RouteToCenterOrIdle();
    }

    private void BeginUnloading()
    {
        phase = Phase.Unloading;
        timer = unloadTime;
    }

    private void FinishUnloading()
    {
        // Als het center in de tussentijd is vernield, blijft de lading bewaard voor het volgende center.
        if (center != null && center.Building.IsOperational)
        {
            center.Deliver(cargo);
            cargo = 0;
        }

        StartTrip();
    }

    private void SetIdle()
    {
        phase = Phase.Idle;
        timer = idleRetryInterval;
    }

    // ---------- Hulpfuncties ----------

    private bool HasArrived(Vector3 target, float radius)
    {
        Vector3 d = target - unit.Position;
        d.y = 0f;
        return d.sqrMagnitude <= radius * radius;
    }

    /// <summary>Als de beweging stilvalt zonder aankomst (blokkade), geef periodiek een nieuwe route.</summary>
    private void RepathIfStuck(Vector3 target, float dt)
    {
        if (!movement.IsIdle) return;

        repathTimer -= dt;
        if (repathTimer > 0f) return;

        repathTimer = repathInterval;
        movement.MoveTo(target);
    }
}
