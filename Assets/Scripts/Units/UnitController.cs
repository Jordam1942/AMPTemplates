using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Één eenheid met een lichte FSM: Idle → Moving / Attacking → Dying.
/// Doelzoeken gebeurt staggered en via een statische registry, niet via scene-queries.
/// Stop: stopt direct en blijft idle. Guard: blijft op de huidige plek, vuurt op vijanden in bereik
/// en keert daarna terug naar de bewaakpositie.
/// External control: een chinook neemt de beweging over. De NavMesh-agent is dan uitgeschakeld.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(Collider))]
public sealed class UnitController : MonoBehaviour, IDamageable, IGatherMovement
{
    public enum State { Idle, Moving, Attacking, Dying }

    public const int LocalPlayerTeam = 0;

    private const float RetargetInterval = 0.25f;
    private const float TurnSpeed = 10f;
    private const float GuardReturnDistance = 1.5f;

    private static readonly List<UnitController> registry = new List<UnitController>(256);

    /// <summary>Alle actieve units, voor selectie en doelzoeken.</summary>
    public static IReadOnlyList<UnitController> Registry => registry;

    /// <summary>Wordt aangeroepen als een unit een andere unit doodt. Args: slachtoffer, schutter.</summary>
    public static event Action<UnitController, UnitController> UnitKilled;

    [Header("Data")]
    [SerializeField] private UnitDataSO data;
    [SerializeField] private int team = LocalPlayerTeam;

    [Header("Visueel")]
    [SerializeField] private GameObject selectionIndicator;
    [SerializeField, Min(0f)] private float corpseDuration = 2f;

    public UnitDataSO Data => data;
    public int Team => team;
    public ArmorType Armor => data != null ? data.armorType : ArmorType.Vehicle;
    public Sprite Icon => data != null ? data.icon : null;
    public State CurrentState { get; private set; } = State.Idle;
    public int CurrentHp { get; private set; }
    public bool IsAlive => CurrentHp > 0 && CurrentState != State.Dying;
    public bool IsSelected { get; private set; }
    public bool IsGuarding { get; private set; }
    public bool IsUnderExternalControl { get; private set; }
    public Vector3 Position => transform.position;

    /// <summary>Maximum HP inclusief het rangbonus van de veterancy.</summary>
    public int MaxHp => data != null ? Mathf.RoundToInt(data.maxHp * hpBonusMultiplier) : 0;

    /// <summary>True als de unit stilstaat en niet door een extern systeem wordt aangestuurd.</summary>
    public bool IsIdle => CurrentState == State.Idle && !IsUnderExternalControl;

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
    private Vector3 guardPoint;
    private UnitVeterancy veterancy;
    private float hpBonusMultiplier = 1f;
    private float fireRateMultiplier = 1f;
    private float hordeFireMultiplier = 1f;
    private float hordeSpeedMultiplier = 1f;
    private float damageMultiplier = 1f;

    /// <summary>Wordt aangeroepen zodra deze unit sterft. Gebruikt door scrap-drops.</summary>
    public event Action<UnitController> Died;

    /// <summary>True zolang de China Horde Bonus actief is.</summary>
    public bool HasHordeBonus { get; private set; }

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        bodyCollider = GetComponent<Collider>();
        veterancy = GetComponent<UnitVeterancy>();

        // Een kinematic Rigidbody maakt trigger-contact mogelijk (mijnen). Zonder die body stopt de detectie.
        if (!TryGetComponent(out Rigidbody body))
        {
            body = gameObject.AddComponent<Rigidbody>();
        }

        body.isKinematic = true;
        body.useGravity = false;

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
        // Een gesneuveld object moet altijd het sterfproces afmaken, ook tijdens een vlucht.
        if (CurrentState == State.Dying)
        {
            TickDying();
            return;
        }

        // Een extern systeem (chinook) bestuurt deze unit. De FSM rust tot de release.
        if (IsUnderExternalControl) return;

        retargetTimer -= Time.deltaTime;
        bool retargetNow = retargetTimer <= 0f;
        if (retargetNow) retargetTimer = RetargetInterval;

        switch (CurrentState)
        {
            case State.Idle: TickIdle(retargetNow); break;
            case State.Moving: TickMoving(); break;
            case State.Attacking: TickAttacking(retargetNow); break;
        }
    }

    // ---------- Commando's (aangeroepen door de selectie-handler, de HUD en systemen) ----------

    public void CommandMove(Vector3 destination)
    {
        if (!IsAlive || IsUnderExternalControl) return;

        IsGuarding = false;
        attackTarget = null;
        agent.isStopped = false;
        agent.stoppingDistance = 0.25f;
        agent.SetDestination(destination);
        SetState(State.Moving);
    }

    /// <summary>Implementatie van IGatherMovement voor grondvoertuigen.</summary>
    public void MoveTo(Vector3 destination) => CommandMove(destination);

    public void CommandAttack(IDamageable target)
    {
        // Een expliciet aanvalscommando beëindigt de bewaking.
        IsGuarding = false;
        BeginAttack(target);
    }

    /// <summary>Stopt direct. De unit blijft staan en wacht op een nieuw commando.</summary>
    public void CommandStop()
    {
        if (!IsAlive || IsUnderExternalControl) return;

        IsGuarding = false;
        attackTarget = null;
        agent.isStopped = true;
        agent.ResetPath();
        SetState(State.Idle);
    }

    /// <summary>Bewaakt de huidige positie. Vijanden in bereik worden aangevallen, daarna keert de unit terug.</summary>
    public void CommandGuard()
    {
        if (!IsAlive || IsUnderExternalControl) return;

        IsGuarding = true;
        guardPoint = transform.position;
        attackTarget = null;
        agent.isStopped = true;
        agent.ResetPath();
        SetState(State.Idle);
    }

    /// <summary>Plaatst de agent direct op een positie, bijvoorbeeld bij het verlaten van een gebouw.</summary>
    public void WarpTo(Vector3 position)
    {
        // Warp faalt stil als de positie niet op de NavMesh ligt. Sample die positie vooraf.
        agent.Warp(position);
    }

    /// <summary>Geeft de beweging aan een extern systeem. De NavMesh-agent wordt uitgeschakeld.</summary>
    public void BeginExternalControl()
    {
        if (IsUnderExternalControl) return;

        IsGuarding = false;
        attackTarget = null;
        if (agent.enabled)
        {
            agent.ResetPath();
            agent.enabled = false;
        }

        IsUnderExternalControl = true;
        SetState(State.Idle);
    }

    /// <summary>Geeft de beweging terug. navMeshPosition moet op de NavMesh liggen.</summary>
    public void EndExternalControl(Vector3 navMeshPosition)
    {
        if (!IsUnderExternalControl) return;

        IsUnderExternalControl = false;
        agent.enabled = true;
        agent.Warp(navMeshPosition);
        SetState(State.Idle);
    }

    /// <summary>
    /// Past de rangbonussen toe. Het huidige HP groeit mee met het verschil in maximum, zodat een
    /// rangstijging de unit niet ineens half beschadigd maakt.
    /// </summary>
    public void ApplyVeterancy(float hpMultiplier, float fireRateBonus)
    {
        int oldMax = MaxHp;
        hpBonusMultiplier = hpMultiplier;
        fireRateMultiplier = fireRateBonus;
        CurrentHp += MaxHp - oldMax;
    }

    /// <summary>Zet de China Horde Bonus aan of uit. Snelheid wordt direct op de agent toegepast.</summary>
    public void SetHordeBonus(bool active, float fireMultiplier, float speedMultiplier)
    {
        HasHordeBonus = active;
        hordeFireMultiplier = active ? fireMultiplier : 1f;
        hordeSpeedMultiplier = active ? speedMultiplier : 1f;

        if (agent != null && agent.enabled && data != null) agent.speed = data.speed * hordeSpeedMultiplier;
    }

    /// <summary>Schadefactor van het wapen. Gebruikt door de GLA-scrap-upgrades.</summary>
    public void SetDamageMultiplier(float multiplier)
    {
        damageMultiplier = Mathf.Max(0f, multiplier);
    }

    public void Heal(int amount)
    {
        if (!IsAlive || amount <= 0) return;

        CurrentHp = Mathf.Min(MaxHp, CurrentHp + amount);
    }

    /// <summary>Wijst de unit toe aan een team. Gebruikt door gebouwen, zodat één prefab voor elke partij werkt.</summary>
    public void SetTeam(int newTeam)
    {
        team = newTeam;
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
        if (enemy != null)
        {
            BeginAttack(enemy);
            return;
        }

        if (IsGuarding) ReturnToGuardPoint();
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
            nextShotTime = Time.time + 1f / (data.fireRate * fireRateMultiplier * hordeFireMultiplier);
        }
    }

    private void TickDying()
    {
        corpseTimer -= Time.deltaTime;
        // Bij veel units: vervang door een object pool.
        if (corpseTimer <= 0f) Destroy(gameObject);
    }

    // ---------- Interne hulpfuncties ----------

    private void BeginAttack(IDamageable target)
    {
        if (!IsAlive || IsUnderExternalControl || target == null || !target.IsAlive || target.Team == team) return;

        attackTarget = target;
        retargetTimer = 0f; // Direct herpad in het volgende frame.
        SetState(State.Attacking);
    }

    private void ReturnToGuardPoint()
    {
        if ((guardPoint - transform.position).sqrMagnitude <= GuardReturnDistance * GuardReturnDistance) return;

        agent.isStopped = false;
        agent.stoppingDistance = 0.25f;
        agent.SetDestination(guardPoint);
        SetState(State.Moving);
    }

    private void Fire()
    {
        float multiplier = data.GetDamageMultiplier(attackTarget.Armor);
        bool wasAlive = attackTarget.IsAlive;
        attackTarget.TakeDamage(Mathf.RoundToInt(data.weaponDamage * multiplier * damageMultiplier));

        // Alleen een echte kill telt voor veterancy en de generaal. Gebouwen leveren hier geen XP.
        UnitController victim = attackTarget as UnitController;
        if (!wasAlive || attackTarget.IsAlive || victim == null) return;

        if (veterancy != null && victim.Data != null) veterancy.AddKillXp(victim.Data.cost);
        UnitKilled?.Invoke(victim, this);
    }

    private void EnterDying()
    {
        SetState(State.Dying);
        Died?.Invoke(this);

        if (agent.enabled) agent.enabled = false;
        bodyCollider.enabled = false; // Niet meer raakbaar of selecteerbaar.
        SetSelected(false);
        corpseTimer = corpseDuration;
    }

    private UnitController FindNearestEnemy()
    {
        UnitController best = null;
        float bestSqr = data.sightRange * data.sightRange;
        Vector3 pos = transform.position;
        FogOfWar fog = FogOfWar.Instance;

        for (int i = 0; i < registry.Count; i++)
        {
            UnitController other = registry[i];
            if (other.team == team || !other.IsAlive) continue;

            // De speler-units kunnen alleen vijanden zien die in het zicht zijn.
            if (team == LocalPlayerTeam && fog != null && !fog.IsVisible(other.Position)) continue;

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
