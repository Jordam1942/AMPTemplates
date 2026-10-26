using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Gebouw met opbouwstand, HP, stroombijdrage en een productiewachtrij met rally point.
/// Operationeel = opgebouwd, niet vernield en voorzien van data. Alleen operationele gebouwen
/// leveren stroom en produceren. De economie wordt per team opgezocht (PlayerEconomy.ForTeam).
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public sealed class BuildingController : MonoBehaviour, IDamageable
{
    private static readonly List<BuildingController> registry = new List<BuildingController>(64);

    /// <summary>Alle gebouwen in de scène. Gebruikt voor stroomberekening en het zoeken naar bouwwerk en supply centers.</summary>
    public static IReadOnlyList<BuildingController> Registry => registry;

    [Header("Data")]
    [SerializeField] private BuildingDataSO data;
    [SerializeField] private int team = UnitController.LocalPlayerTeam;

    [Tooltip("True voor gebouwen die al in de scène staan. Door een plaatsing gespawnde gebouwen starten op 0%.")]
    [SerializeField] private bool startsBuilt = true;

    [Header("Scène")]
    [Tooltip("Dit onderdeel schaalt mee met de opbouw.")]
    [SerializeField] private Transform modelRoot;
    [SerializeField] private Transform spawnPoint;
    [SerializeField, Range(0.05f, 1f)] private float scaleAtStart = 0.1f;
    [SerializeField, Min(0f)] private float destroyDelay = 3f;
    [SerializeField, Min(0.1f)] private float rallySampleRadius = 3f;

    public BuildingDataSO Data => data;
    public int Team => team;
    public ArmorType Armor => ArmorType.Structure;
    public Vector3 Position => transform.position;

    public bool IsUnderConstruction { get; private set; }
    public float ConstructionProgress { get; private set; }
    public int CurrentHp { get; private set; }
    public bool IsAlive => CurrentHp > 0;
    public bool IsOperational => IsAlive && !IsUnderConstruction && data != null;

    /// <summary>True zodra een bouwer aan dit gebouw is toegewezen. Voorkomt dat twee bouwers hetzelfde doen.</summary>
    public bool HasBuilder { get; private set; }

    public Vector3 RallyPoint { get; private set; }
    public ProductionQueue Queue { get; private set; }

    /// <summary>Straal waarbinnen een bouwer of gatherer het gebouw als bereikt beschouwt.</summary>
    public float ApproachRadius => data != null ? Mathf.Max(data.footprint.x, data.footprint.y) * 0.5f + 1.5f : 3f;

    public float BuildTime => data != null ? data.buildTime : 1f;

    /// <summary>De economie van het team van dit gebouw. Null als dat team geen economie heeft.</summary>
    private PlayerEconomy Economy => PlayerEconomy.ForTeam(team);

    public event Action<UnitController> UnitProduced;
    public event Action<UpgradeDataSO> UpgradeCompleted;
    public event Action<BuildingController> Destroyed;

    /// <summary>Wordt aangeroepen zodra een gebouw van welk team dan ook is opgebouwd. Voor de generaal-XP.</summary>
    public static event Action<BuildingController> ConstructionCompletedGlobal;

    private bool isDead;
    private float destroyTimer;
    private Collider bodyCollider;

    private void Awake()
    {
        bodyCollider = GetComponent<Collider>();
        RallyPoint = spawnPoint != null ? spawnPoint.position : transform.position;

        if (data == null)
        {
            Debug.LogError($"{name}: BuildingDataSO ontbreekt.", this);
            return;
        }

        if (startsBuilt) InitializeAsBuilt();
    }

    private void OnEnable()
    {
        if (!registry.Contains(this)) registry.Add(this);
    }

    private void OnDisable()
    {
        registry.Remove(this);
        RefreshEconomyPower();
    }

    private void Update()
    {
        if (isDead)
        {
            destroyTimer -= Time.deltaTime;
            if (destroyTimer <= 0f) Destroy(gameObject);
            return;
        }

        if (!IsOperational || Queue == null || Queue.Count == 0) return;

        PlayerEconomy economy = Economy;
        float speed = economy != null ? economy.ProductionSpeedMultiplier : 1f;
        if (!Queue.Tick(Time.deltaTime, speed, out ProductionOption done)) return;

        if (done.kind == ProductionKind.Unit) SpawnUnit(done.unitPrefab);
        else UpgradeCompleted?.Invoke(done.upgrade);
    }

    // ---------- Constructie ----------

    /// <summary>Zet het gebouw in opbouwstand. Aangeroepen direct na Instantiate door PlacementRules.</summary>
    public void BeginConstruction(BuildingDataSO buildingData, int ownerTeam)
    {
        data = buildingData;
        team = ownerTeam;
        Queue = new ProductionQueue(data.maxQueueLength);

        IsUnderConstruction = true;
        ConstructionProgress = 0f;
        CurrentHp = 1;
        ApplyVisual();
    }

    /// <summary>Voegt bouwvoortgang toe. fraction is het deel van de totale bouwtijd, 0 tot 1.</summary>
    public void AddConstructionWork(float fraction)
    {
        if (!IsUnderConstruction || isDead) return;

        ConstructionProgress = Mathf.Min(1f, ConstructionProgress + fraction);

        // Tijdens de opbouw is HP gekoppeld aan voortgang. Schade wordt bij de volgende bouwstap overschreven.
        CurrentHp = Mathf.Max(1, Mathf.RoundToInt(data.maxHp * ConstructionProgress));
        ApplyVisual();

        if (ConstructionProgress >= 1f) CompleteConstruction();
    }

    public void SetBuilderAssigned(bool assigned) => HasBuilder = assigned;

    private void InitializeAsBuilt()
    {
        Queue = new ProductionQueue(data.maxQueueLength);
        IsUnderConstruction = false;
        ConstructionProgress = 1f;
        CurrentHp = data.maxHp;
        ApplyVisual();
    }

    private void CompleteConstruction()
    {
        IsUnderConstruction = false;
        HasBuilder = false;
        CurrentHp = data.maxHp;
        ApplyVisual();
        RefreshEconomyPower();
        ConstructionCompletedGlobal?.Invoke(this);
    }

    private void ApplyVisual()
    {
        if (modelRoot == null) return;

        float y = Mathf.Lerp(scaleAtStart, 1f, ConstructionProgress);
        modelRoot.localScale = new Vector3(1f, y, 1f);
    }

    private void RefreshEconomyPower()
    {
        PlayerEconomy economy = Economy;
        if (economy != null) economy.RefreshPower();
    }

    // ---------- Productie ----------

    /// <summary>Zet optie 'optionIndex' in de wachtrij. De kosten worden direct afgeschreven.</summary>
    public bool TryQueue(int optionIndex)
    {
        if (!IsOperational || Queue == null || Queue.IsFull) return false;

        ProductionOption[] options = data.productionOptions;
        if (optionIndex < 0 || optionIndex >= options.Length) return false;

        ProductionOption option = options[optionIndex];
        if (option.kind == ProductionKind.Unit && option.unitPrefab == null) return false;

        PlayerEconomy economy = Economy;
        if (economy == null || !economy.TrySpend(option.cost)) return false;

        Queue.TryEnqueue(option);
        return true;
    }

    /// <summary>Annuleert het laatst toegevoegde item en betaalt de kosten terug.</summary>
    public bool TryCancelLast()
    {
        if (Queue == null || !Queue.TryCancelLast(out ProductionOption cancelled)) return false;

        PlayerEconomy economy = Economy;
        if (economy != null) economy.Refund(cancelled.cost);
        return true;
    }

    /// <summary>Stelt het rally point in. Het punt wordt op de NavMesh gezet, zodat units altijd een route vinden.</summary>
    public void SetRallyPoint(Vector3 point)
    {
        if (NavMesh.SamplePosition(point, out NavMeshHit hit, rallySampleRadius, NavMesh.AllAreas))
            RallyPoint = hit.position;
    }

    private void SpawnUnit(GameObject prefab)
    {
        if (prefab == null) return;

        Vector3 spawnPos = spawnPoint != null
            ? spawnPoint.position
            : transform.position + transform.forward * ApproachRadius;

        GameObject go = Instantiate(prefab, spawnPos, Quaternion.LookRotation(transform.forward));
        if (!go.TryGetComponent(out UnitController unit))
        {
            Destroy(go);
            return;
        }

        // Het team komt van het gebouw, zodat één prefab voor beide kanten werkt.
        unit.SetTeam(team);

        // Warp faalt stil als spawnpos niet op de NavMesh ligt. Zorg dat spawnPoint op de NavMesh staat.
        unit.WarpTo(spawnPos);
        unit.CommandMove(RallyPoint);
        UnitProduced?.Invoke(unit);
    }

    // ---------- Schade ----------

    public void TakeDamage(int amount)
    {
        if (!IsAlive || amount <= 0) return;

        CurrentHp = Mathf.Max(0, CurrentHp - amount);
        if (CurrentHp == 0) Die();
    }

    private void Die()
    {
        isDead = true;
        destroyTimer = destroyDelay;
        HasBuilder = false;

        if (bodyCollider != null) bodyCollider.enabled = false;

        // Stroom valt direct weg. Het gebouw blijft nog even zichtbaar voor de vernielanimatie.
        RefreshEconomyPower();

        Destroyed?.Invoke(this);
    }
}
