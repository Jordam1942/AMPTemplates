using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Één eenheid met een lichte FSM: Idle → Moving / Attacking → Dying.
/// Doelzoeken gebeurt staggered en via een statische registry, niet via scene-queries.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(Collider))]
public sealed class UnitController : MonoBehaviour
{
    public enum State { Idle, Moving, Attacking, Dying }

    public const int LocalPlayerTeam = 0;

    private const float RetargetInterval = 0.25f;
    private const float TurnSpeed = 10f;

    private static readonly List<UnitController> registry = new List<UnitController>(256);

    /// <summary>Alle actieve units, voor selectie en doelzoeken.</summary>
    public static IReadOnlyList<UnitController> Registry => registry;

    [Header("Data")]
    [SerializeField] private UnitDataSO data;
    [SerializeField] private int team = LocalPlayerTeam;

    [Header("Visueel")]
    [SerializeField] private GameObject selectionIndicator;
    [SerializeField, Min(0f)] private float corpseDuration = 2f;

    public UnitDataSO Data => data;
    public int Team => team;
    public ArmorType Armor => data != null ? data.armorType : ArmorType.Vehicle;
    public State CurrentState { get; private set; } = State.Idle;
    public int CurrentHp { get; private set; }
    public bool IsAlive => CurrentHp > 0 && CurrentState != State.Dying;
    public bool IsSelected { get; private set; }
    public Vector3 Position => transform.position;

    /// <summary>
    /// Uitgeschakeld voor gespecialiseerde units (dozers, gatherers). Die sturen hun eigen bewegingen
    /// en moeten niet zelf van route wijzigen om vijanden te achtervolgen.
    /// </summary>
    public bool AutoAcquire { get; set; } = true;

    private NavMeshAgent agent;
    private Collider bodyCollider;
    private IDamageable attackTarget;
    private float retargetTimer;
    private float nextShotTime;
    private float corpseTimer;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        bodyCollider = GetComponent<Collider>();

        if (data == null)
        {
            Debug.LogError($"{name}: UnitDataSO ontbreekt.", this);
            enabled = false;
            return;
        }

        agent.speed = data.speed;
        CurrentHp = data.maxHp;

        // Spreid de retarget-timers, zodat niet alle units in hetzelfde frame zoeken.
        retargetTimer = Random.Range(0f, RetargetInterval);

        if (selectionIndicator != null) selectionIndicator.SetActive(false);
    }

    private void OnEnable()
    {
        if (!registry.Contains(this)) registry.Add(this);
    }

    private void OnDisable()
    {
        registry.Remove(this);
    }

    private void Update()
    {
        retargetTimer -= Time.deltaTime;
        bool retargetNow = retargetTimer <= 0f;
        if (retargetNow) retargetTimer = RetargetInterval;

        switch (CurrentState)
        {
            case State.Idle: TickIdle(retargetNow); break;
            case State.Moving: TickMoving(); break;
            case State.Attacking: TickAttacking(retargetNow); break;
            case State.Dying: TickDying(); break;
        }
    }

    // ---------- Commando's (aangeroepen door de selectie-handler en systemen) ----------

    public void CommandMove(Vector3 destination)
    {
        if (!IsAlive) return;

        attackTarget = null;
        agent.isStopped = false;
        agent.stoppingDistance = 0.25f;
        agent.SetDestination(destination);
        SetState(State.Moving);
    }

    public void CommandAttack(IDamageable target)
    {
        if (!IsAlive || target == null || !target.IsAlive || target.Team == team) return;

        attackTarget = target;
        retargetTimer = 0f; // Direct herpad in het volgende frame.
        SetState(State.Attacking);
    }

    /// <summary>Plaatst de agent direct op een positie, bijvoorbeeld bij het verlaten van een gebouw.</summary>
    public void WarpTo(Vector3 position)
    {
        // Warp faalt stil als de positie niet op de NavMesh ligt. Sample die positie vooraf.
        agent.Warp(position);
    }

    public void SetSelected(bool value)
    {
        IsSelected = value;
        if (selectionIndicator != null) selectionIndicator.SetActive(value);
    }

    public void TakeDamage(int amount)
    {
        if (!IsAlive || amount <= 0) return;

        CurrentHp = Mathf.Max(0, CurrentHp - amount);
        if (CurrentHp == 0) EnterDying();
    }

    // ---------- FSM-states ----------

    private void TickIdle(bool retargetNow)
    {
        if (!AutoAcquire || !retargetNow) return;

        UnitController enemy = FindNearestEnemy();
        if (enemy != null) CommandAttack(enemy);
    }

    private void TickMoving()
    {
        if (agent.pathPending) return;

        if (!agent.hasPath || agent.remainingDistance <= agent.stoppingDistance)
            SetState(State.Idle);
    }

    private void TickAttacking(bool retargetNow)
    {
        if (attackTarget == null || !attackTarget.IsAlive)
        {
            attackTarget = null;
            SetState(State.Idle);
            return;
        }

        float range = data.attackRange;
        bool inRange = (attackTarget.Position - transform.position).sqrMagnitude <= range * range;

        if (!inRange)
        {
            // Achtervolgen: herpad alleen periodiek, niet elk frame.
            if (retargetNow)
            {
                agent.isStopped = false;
                agent.stoppingDistance = range * 0.9f;
                agent.SetDestination(attackTarget.Position);
            }
            return;
        }

        if (!agent.isStopped) agent.isStopped = true;
        FaceTowards(attackTarget.Position);

        if (Time.time >= nextShotTime)
        {
            Fire();
            nextShotTime = Time.time + 1f / data.fireRate;
        }
    }

    private void TickDying()
    {
        corpseTimer -= Time.deltaTime;
        // Bij veel units: vervang door een object pool.
        if (corpseTimer <= 0f) Destroy(gameObject);
    }

    // ---------- Interne hulpfuncties ----------

    private void Fire()
    {
        float multiplier = data.GetDamageMultiplier(attackTarget.Armor);
        attackTarget.TakeDamage(Mathf.RoundToInt(data.weaponDamage * multiplier));
    }

    private void EnterDying()
    {
        SetState(State.Dying);

        agent.enabled = false;
        bodyCollider.enabled = false; // Niet meer raakbaar of selecteerbaar.
        SetSelected(false);
        corpseTimer = corpseDuration;
    }

    private UnitController FindNearestEnemy()
    {
        UnitController best = null;
        float bestSqr = data.sightRange * data.sightRange;
        Vector3 pos = transform.position;

        for (int i = 0; i < registry.Count; i++)
        {
            UnitController other = registry[i];
            if (other.team == team || !other.IsAlive) continue;

            float distSqr = (other.Position - pos).sqrMagnitude;
            if (distSqr < bestSqr)
            {
                bestSqr = distSqr;
                best = other;
            }
        }

        return best;
    }

    private void FaceTowards(Vector3 worldPosition)
    {
        Vector3 dir = worldPosition - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return;

        Quaternion target = Quaternion.LookRotation(dir);
        transform.rotation = Quaternion.Slerp(transform.rotation, target, 1f - Mathf.Exp(-TurnSpeed * Time.deltaTime));
    }

    private void SetState(State next)
    {
        if (CurrentState == next) return;

        CurrentState = next;
        // Aansluitpunt voor Animator-parameters en VFX.
    }
}
