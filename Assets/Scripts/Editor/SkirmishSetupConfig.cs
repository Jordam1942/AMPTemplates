using UnityEngine;

/// <summary>
/// Eenmalige configuratie voor EditorSceneSetup. Vul de prefabs en data één keer in.
/// Het menu-item bouwt daarna de volledige scène met deze koppelingen.
/// Asset: Assets/Settings/SkirmishSetupConfig.asset (wordt aangemaakt door het menu-item).
/// </summary>
public sealed class SkirmishSetupConfig : ScriptableObject
{
    [Header("Basislocaties (wereld, y = 0 op het grondvlak)")]
    public Vector3 playerBasePosition = new Vector3(-70f, 0f, -70f);
    public Vector3 aiBasePosition = new Vector3(70f, 0f, 70f);

    [Tooltip("Plekken waar supply docks worden geplaatst. Gebruik grondposities binnen de map.")]
    public Vector3[] dockPositions =
    {
        new Vector3(-40f, 0f, -10f),
        new Vector3(40f, 0f, 10f),
        new Vector3(-10f, 0f, 40f),
        new Vector3(10f, 0f, -40f),
    };

    [Header("Speler (team 0)")]
    public GameObject playerCommandCenterPrefab;
    public GameObject playerBuilderPrefab;
    public GameObject playerGathererPrefab;

    [Header("AI (team 1)")]
    public GameObject aiCommandCenterPrefab;
    public GameObject aiBuilderPrefab;
    public GameObject aiGathererPrefab;

    [Header("Gedeeld")]
    public GameObject supplyDockPrefab;

    [Header("Gebouwdata voor de AI-planner")]
    public BuildingDataSO powerPlantData;
    public BuildingDataSO supplyCenterData;
    public BuildingDataSO barracksData;
    public BuildingDataSO warFactoryData;

    [Tooltip("Gebouwen die het bouwmenu van de speler toont, in slotvolgorde.")]
    public BuildingDataSO[] playerBuildMenu = new BuildingDataSO[0];
}
