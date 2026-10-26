using UnityEngine;

/// <summary>
/// Cluster Mines (China). Een bommenwerper vliegt over het gekozen punt en dropt een mijnenveld in een cirkel.
/// De mijnen zijn onzichtbaar voor de tegenstander, tot een vijand dichtbij komt. Ze ontploffen bij contact.
/// Het effect is een GeneralPowerEffect, dus het hangt aan een GeneralPowerSO met targeting Point.
/// </summary>
[CreateAssetMenu(fileName = "ClusterMinesEffect", menuName = "Generals/Effects/Cluster Mines")]
public sealed class ClusterMinesEffect : GeneralPowerEffect
{
    [Header("Uitzending")]
    [SerializeField, Range(1, 30)] private int mineCount = 12;
    [SerializeField, Min(1f)] private float dropRadius = 8f;
    [Tooltip("Seconden tussen twee mijnen.")]
    [SerializeField, Min(0.01f)] private float dropInterval = 0.12f;
    [SerializeField, Min(1f)] private float mineLifetime = 180f;

    [Header("Explosie en detectie")]
    [SerializeField, Min(0)] private int explosionDamage = 150;
    [SerializeField, Min(0.5f)] private float explosionRadius = 3f;
    [Tooltip("Afstand waarbinnen een vijand de mijn zichtbaar maakt.")]
    [SerializeField, Min(0.5f)] private float detectionRadius = 6f;

    [Header("Bommenwerper")]
    [SerializeField, Min(5f)] private float bomberAltitude = 30f;
    [SerializeField, Min(5f)] private float bomberApproach = 40f;
    [SerializeField, Min(1f)] private float bomberSpeed = 40f;

    [Header("Prefabs en ondergrond")]
    [Tooltip("Optioneel model van de bommenwerper.")]
    [SerializeField] private GameObject bomberPrefab;

    [Tooltip("Optioneel mijnmodel. Zonder prefab wordt een bol met een trigger gebruikt.")]
    [SerializeField] private GameObject minePrefab;

    [SerializeField] private LayerMask groundMask;

    public int MineCount => mineCount;
    public float DropRadius => dropRadius;
    public float DropInterval => dropInterval;
    public float MineLifetime => mineLifetime;
    public int ExplosionDamage => explosionDamage;
    public float ExplosionRadius => explosionRadius;
    public float DetectionRadius => detectionRadius;
    public float BomberAltitude => bomberAltitude;
    public float BomberApproach => bomberApproach;
    public float BomberSpeed => bomberSpeed;
    public GameObject BomberPrefab => bomberPrefab;
    public GameObject MinePrefab => minePrefab;
    public LayerMask GroundMask => groundMask;

    public override void Execute(GeneralPowerContext context)
    {
        // De richting van de uitzending is hier onbekend. Vlieg langs de X-as, zodat het resultaat deterministisch is.
        GameObject go = new GameObject("ClusterMineDropper");
        go.AddComponent<ClusterMineDropper>().Begin(this, context.team, context.start, Vector3.right);
    }
}
