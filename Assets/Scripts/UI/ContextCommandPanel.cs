using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Contextueel commandopaneel, duimvriendelijk rechtsonder. Toont één modus, afhankelijk van de selectie:
///  - Bouwmenu: een geselecteerde Builder (dozer of worker).
///  - Productie: een geselecteerd gebouw met wachtrij, voortgang en rally point.
///  - Unit: een gevechtseenheid met stats en Stop/Guard.
/// Verversen gebeurt op een vast interval, niet elk frame. Knoppen reageren op touch-down.
/// </summary>
[DisallowMultipleComponent]
public sealed class ContextCommandPanel : MonoBehaviour
{
    private enum Mode { None, Build, Production, Unit }

    [Header("Panelen")]
    [SerializeField] private GameObject buildRoot;
    [SerializeField] private GameObject productionRoot;
    [SerializeField] private GameObject unitRoot;

    [Header("Bouwmenu")]
    [Tooltip("Gebouwen die de dozer kan plaatsen. Volgorde = volgorde van de slots.")]
    [SerializeField] private BuildingDataSO[] buildMenu = new BuildingDataSO[0];
    [SerializeField] private OptionSlot[] buildSlots = new OptionSlot[0];

    [Header("Productie")]
    [SerializeField] private OptionSlot[] productionSlots = new OptionSlot[0];
    [SerializeField] private OptionSlot[] queueSlots = new OptionSlot[0];
    [SerializeField] private TouchButton cancelButton;
    [SerializeField] private TouchButton rallyButton;

    [Header("Unit")]
    [SerializeField] private UnitStatsView unitStats;
    [SerializeField] private TouchButton stopButton;
    [SerializeField] private TouchButton guardButton;

    [Header("Verversen")]
    [SerializeField, Min(0.02f)] private float refreshInterval = 0.1f;

    private Mode mode = Mode.None;
    private BuildingController building;
    private PlayerEconomy economy;
    private float refreshTimer;

    private void Awake()
    {
        for (int i = 0; i < buildSlots.Length; i++)
        {
            int index = i;
            buildSlots[i].Pressed += () => OnBuildPressed(index);
        }

        for (int i = 0; i < productionSlots.Length; i++)
        {
            int index = i;
            productionSlots[i].Pressed += () => OnProductionPressed(index);
        }

        cancelButton.Pressed += OnCancelPressed;
        rallyButton.Pressed += OnRallyPressed;
        stopButton.Pressed += OnStopPressed;
        guardButton.Pressed += OnGuardPressed;
    }

    private void OnEnable()
    {
        SelectionState.Changed += Rebuild;
        Rebuild();
    }

    private void OnDisable()
    {
        SelectionState.Changed -= Rebuild;
    }

    private void Update()
    {
        refreshTimer -= Time.unscaledDeltaTime;
        if (refreshTimer > 0f) return;

        refreshTimer = refreshInterval;
        Refresh();
    }

    // ---------- Modus bepalen ----------

    private void Rebuild()
    {
        building = SelectionState.Building;
        IReadOnlyList<UnitController> units = SelectionState.Units;

        if (building != null)
        {
            SetMode(Mode.Production);
            ConfigureProduction();
        }
        else if (units.Count > 0)
        {
            bool isBuilder = units[0].TryGetComponent(out Builder _);
            SetMode(isBuilder ? Mode.Build : Mode.Unit);
            if (isBuilder) ConfigureBuildMenu();
        }
        else
        {
            SetMode(Mode.None);
        }

        Refresh();
    }

    private void SetMode(Mode next)
    {
        mode = next;
        if (buildRoot != null) buildRoot.SetActive(next == Mode.Build);
        if (productionRoot != null) productionRoot.SetActive(next == Mode.Production);
        if (unitRoot != null) unitRoot.SetActive(next == Mode.Unit);
    }

    // ---------- Configuratie (eenmalig per selectie) ----------

    private void ConfigureBuildMenu()
    {
        for (int i = 0; i < buildSlots.Length; i++)
        {
            bool visible = i < buildMenu.Length;
            buildSlots[i].gameObject.SetActive(visible);
            if (!visible) continue;

            BuildingDataSO data = buildMenu[i];
            buildSlots[i].SetContent(data.icon, data.displayName, data.cost);
        }
    }

    private void ConfigureProduction()
    {
        ProductionOption[] options = building.Data != null ? building.Data.productionOptions : null;

        for (int i = 0; i < productionSlots.Length; i++)
        {
            bool visible = options != null && i < options.Length;
            productionSlots[i].gameObject.SetActive(visible);
            if (!visible) continue;

            ProductionOption option = options[i];
            productionSlots[i].SetContent(IconFor(option), option.label, option.cost);
        }
    }

    // ---------- Verversen ----------

    private void Refresh()
    {
        if (economy == null) economy = PlayerEconomy.Instance;

        // Het gebouw is vernield: wis de selectie zodat het paneel en de selectie in sync blijven.
        if (mode == Mode.Production && building == null)
        {
            SelectionState.Clear();
            return;
        }

        switch (mode)
        {
            case Mode.Build: RefreshBuildMenu(); break;
            case Mode.Production: RefreshProduction(); break;
            case Mode.Unit: RefreshUnit(); break;
        }
    }

    private void RefreshBuildMenu()
    {
        for (int i = 0; i < buildMenu.Length && i < buildSlots.Length; i++)
            buildSlots[i].SetAvailable(BuildRules.CanBuild(buildMenu[i], economy));
    }

    private void RefreshProduction()
    {
        ProductionQueue queue = building.Queue;
        bool operational = building.IsOperational && queue != null;
        bool canQueueMore = operational && !queue.IsFull;
        ProductionOption[] options = building.Data != null ? building.Data.productionOptions : null;

        for (int i = 0; i < productionSlots.Length && options != null && i < options.Length; i++)
        {
            bool canPay = economy != null && economy.CanAfford(options[i].cost);
            productionSlots[i].SetAvailable(canQueueMore && canPay);
        }

        // Wachtrij: slot 0 is het voorste item met de voortgangsbalk.
        for (int i = 0; i < queueSlots.Length; i++)
        {
            if (queue != null && queue.TryGetEntry(i, out ProductionOption entry, out float progress))
            {
                queueSlots[i].gameObject.SetActive(true);
                queueSlots[i].SetContent(IconFor(entry), entry.label, -1);
                queueSlots[i].SetProgress(progress);
            }
            else
            {
                queueSlots[i].gameObject.SetActive(false);
            }
        }

        cancelButton.Interactable = queue != null && queue.Count > 0;
        rallyButton.Interactable = operational;
    }

    private void RefreshUnit()
    {
        IReadOnlyList<UnitController> units = SelectionState.Units;
        unitStats.Show(units);

        bool any = units.Count > 0;
        stopButton.Interactable = any;
        guardButton.Interactable = any;
    }

    // ---------- Knoppen ----------

    private void OnBuildPressed(int index)
    {
        if (index >= buildMenu.Length || ConstructionSystem.Instance == null) return;

        ConstructionSystem.Instance.BeginPlacement(buildMenu[index]);
    }

    private void OnProductionPressed(int index)
    {
        if (building == null) return;

        building.TryQueue(index);
        Refresh();
    }

    private void OnCancelPressed()
    {
        if (building == null) return;

        building.TryCancelLast();
        Refresh();
    }

    private void OnRallyPressed()
    {
        if (building == null) return;

        // De volgende grondtik zet het rally point. De picker blokkeert de selectie-handler tot die tik.
        TargetPicker.Begin(point =>
        {
            if (building != null) building.SetRallyPoint(point);
        });
    }

    private void OnStopPressed()
    {
        IReadOnlyList<UnitController> units = SelectionState.Units;
        for (int i = 0; i < units.Count; i++) units[i].CommandStop();
    }

    private void OnGuardPressed()
    {
        IReadOnlyList<UnitController> units = SelectionState.Units;
        for (int i = 0; i < units.Count; i++) units[i].CommandGuard();
    }

    // ---------- Hulpfuncties ----------

    private static Sprite IconFor(ProductionOption option)
    {
        if (option.kind == ProductionKind.Upgrade)
            return option.upgrade != null ? option.upgrade.icon : null;

        if (option.unitPrefab == null) return null;

        UnitController unit = option.unitPrefab.GetComponent<UnitController>();
        return unit != null ? unit.Icon : null;
    }
}
