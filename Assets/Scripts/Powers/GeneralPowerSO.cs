using UnityEngine;

/// <summary>Hoe een power gericht wordt. Bepaalt hoeveel tikken de speler nodig heeft.</summary>
public enum PowerTargeting
{
    /// <summary>Één tik op het terrein. Bijvoorbeeld een spy drone.</summary>
    Point,

    /// <summary>Twee tikken: start en eind van een pad. Bijvoorbeeld een luchtaanval langs een lijn.</summary>
    Line,

    /// <summary>Geen tik. De power is een globaal effect dat direct start.</summary>
    Passive,
}

/// <summary>
/// Data van één Generals' Power: naam, icoon, cooldown, vereiste ster-rang, tech-tree-voorwaarden
/// en het effect. Het effect is een eigen ScriptableObject, zodat nieuwe powers zonder code kunnen.
/// </summary>
[CreateAssetMenu(fileName = "NewGeneralPower", menuName = "Generals/General Power")]
public sealed class GeneralPowerSO : ScriptableObject
{
    public string displayName = "Power";
    public Sprite icon;

    [Tooltip("Minimale ster-rang van de generaal om deze power te ontgrendelen (1 t/m 5).")]
    [Range(1, 5)] public int requiredStar = 1;

    [Tooltip("Promotion Points die het ontgrendelen kost.")]
    [Min(0)] public int promotionCost = 1;

    [Tooltip("Seconden tussen twee activaties.")]
    [Min(0f)] public float cooldown = 240f;

    public PowerTargeting targeting = PowerTargeting.Point;

    [Tooltip("Powers die eerst ontgrendeld moeten zijn. Dit vormt de tech-tree.")]
    public GeneralPowerSO[] prerequisites = new GeneralPowerSO[0];

    public GeneralPowerEffect effect;
}
