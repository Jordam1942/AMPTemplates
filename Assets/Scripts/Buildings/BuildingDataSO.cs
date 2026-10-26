using System;
using UnityEngine;

public enum ProductionKind { Unit, Upgrade }

/// <summary>Eén bestelbare optie in de productiewachtrij van een gebouw: een unit of een upgrade.</summary>
[Serializable]
public struct ProductionOption
{
    public ProductionKind kind;
    public string label;
    [Min(0)] public int cost;
    [Min(0.1f)] public float time;

    [Tooltip("Alleen voor Unit: prefab met UnitController.")]
    public GameObject unitPrefab;

    [Tooltip("Alleen voor Upgrade.")]
    public UpgradeDataSO upgrade;
}

/// <summary>
/// Statische eigenschappen van een gebouw: kosten, bouwtijd, voetafdruk, stroom en productie-opties.
/// Eén asset per gebouwtype.
/// </summary>
[CreateAssetMenu(fileName = "NewBuildingData", menuName = "Generals/Building Data")]
public sealed class BuildingDataSO : ScriptableObject
{
    [Header("Identiteit")]
    public string displayName = "Building";
    [Tooltip("Icoon voor het bouwmenu.")]
    public Sprite icon;
    public Faction faction = Faction.USA;

    [Tooltip("Prefab met BuildingController, een collider op de Buildings-laag en optioneel SupplyCenter. " +
             "Zet startsBuilt op false op bouwbare prefabs.")]
    public GameObject prefab;

    [Header("Bouwen")]
    [Min(0)] public int cost = 1000;
    [Tooltip("Seconden die één bouwer nodig heeft voor 100%.")]
    [Min(0.5f)] public float buildTime = 10f;
    [Min(1)] public int maxHp = 1000;

    [Tooltip("Voetafdruk in meters (X, Z) in onrotatiestand.")]
    public Vector2 footprint = new Vector2(6f, 6f);

    [Header("Stroom")]
    [Tooltip("Stroom die dit gebouw levert zodra het operationeel is.")]
    [Min(0)] public int powerProduced;

    [Tooltip("Stroom die dit gebouw verbruikt zodra het operationeel is.")]
    [Min(0)] public int powerConsumed;

    [Header("Productie")]
    [Min(1)] public int maxQueueLength = 5;
    public ProductionOption[] productionOptions = new ProductionOption[0];
}
