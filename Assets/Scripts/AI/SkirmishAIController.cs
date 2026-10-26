using UnityEngine;

/// <summary>
/// Regisseur van de skirmish-AI. Houdt de macro-economie bij en roept elke beslisronde de bouwplanner
/// en het aanvalsbeheer aan. Gebruikt dezelfde systemen als de speler (PlayerEconomy, BuildingController,
/// ProductionQueue, UnitController), via het eigen team-ID. Er is geen aparte spellogica.
/// </summary>
[DisallowMultipleComponent]
public sealed class SkirmishAIController : MonoBehaviour
{
    [Tooltip("Team-ID van de AI. Moet overeenkomen met de economie, gebouwen en units van de AI.")]
    [SerializeField] private int team = 1;

    [SerializeField] private AIDifficulty difficulty = AIDifficulty.Medium;

    [Tooltip("Het hoofdgebouw van de AI. Basis voor plaatsing en dozer-productie.")]
    [SerializeField] private BuildingController commandCenter;

    [SerializeField] private AIBuildPlanner buildPlanner;
    [SerializeField] private AIAttackManager attackManager;

    [Tooltip("Wachttijd voordat de AI voor het eerst beslist. Geeft de startopstelling de tijd om klaar te komen.")]
    [SerializeField, Min(0f)] private float startDelay = 5f;

    public int Team => team;
    public AIDifficulty Difficulty => difficulty;
    public AIDifficultySettings Settings { get; private set; }
    public BuildingController CommandCenter => commandCenter;

    /// <summary>De economie van de AI. Null als er geen PlayerEconomy met het AI-team in de scène staat.</summary>
    public PlayerEconomy Economy => PlayerEconomy.ForTeam(team);

    /// <summary>Het middelpunt van de basis: het hoofdgebouw, of anders dit object.</summary>
    public Vector3 BaseCenter => commandCenter != null ? commandCenter.Position : transform.position;

    private float decisionTimer;
    private float delayTimer;

    private void Awake()
    {
        Settings = AIDifficultySettings.For(difficulty);
        delayTimer = startDelay;

        if (buildPlanner == null) buildPlanner = GetComponent<AIBuildPlanner>();
        if (attackManager == null) attackManager = GetComponent<AIAttackManager>();
    }

    private void Update()
    {
        if (delayTimer > 0f)
        {
            delayTimer -= Time.deltaTime;
            return;
        }

        decisionTimer -= Time.deltaTime;
        if (decisionTimer > 0f) return;

        decisionTimer = Settings.decisionInterval;
        Think();
    }

    /// <summary>Wijzigt de moeilijkheidsgraad tijdens het spel. Geldt vanaf de volgende beslisronde.</summary>
    public void SetDifficulty(AIDifficulty value)
    {
        difficulty = value;
        Settings = AIDifficultySettings.For(value);
    }

    private void Think()
    {
        if (Economy == null) return;

        // Eerst bouwen, daarna pas de legers aansturen. Zo gaat productie niet ten koste van de basis.
        if (buildPlanner != null) buildPlanner.Plan(this);
        if (attackManager != null) attackManager.Plan(this);
    }

    // ---------- Queries voor de planner en het aanvalsbeheer ----------

    /// <summary>Aantal eigen gebouwen van dit type, inclusief gebouwen in aanbouw.</summary>
    public int CountOwned(BuildingDataSO data)
    {
        int count = 0;
        IReadOnlyList<BuildingController> buildings = BuildingController.Registry;

        for (int i = 0; i < buildings.Count; i++)
        {
            BuildingController b = buildings[i];
            if (b.Team == team && b.IsAlive && b.Data == data) count++;
        }

        return count;
    }

    /// <summary>Aantal eigen gebouwen van dit type dat nog in aanbouw is.</summary>
    public int CountPendingConstruction(BuildingDataSO data)
    {
        int count = 0;
        IReadOnlyList<BuildingController> buildings = BuildingController.Registry;

        for (int i = 0; i < buildings.Count; i++)
        {
            BuildingController b = buildings[i];
            if (b.Team == team && b.IsUnderConstruction && b.Data == data) count++;
        }

        return count;
    }

    /// <summary>Aantal eigen gebouwen in aanbouw, van elk type.</summary>
    public int CountPendingConstructionTotal()
    {
        int count = 0;
        IReadOnlyList<BuildingController> buildings = BuildingController.Registry;

        for (int i = 0; i < buildings.Count; i++)
        {
            BuildingController b = buildings[i];
            if (b.Team == team && b.IsUnderConstruction) count++;
        }

        return count;
    }
}
