using UnityEngine;

/// <summary>
/// A-10 Strike (USA). Drie straaljagers vliegen in formatie langs het gekozen pad en bombarderen
/// de lijn met raketten en kanonvuur. Elke jager start buiten het doelgebied en vliegt er doorheen.
/// </summary>
[CreateAssetMenu(fileName = "A10StrikeEffect", menuName = "Generals/Effects/A-10 Strike")]
public sealed class AirStrikeEffect : GeneralPowerEffect
{
    [Header("Formatie")]
    [SerializeField, Range(1, 5)] private int jetCount = 3;
    [SerializeField, Min(1f)] private float formationSpacing = 6f;
    [Tooltip("Afstand buiten het doelgebied waar de jagers beginnen en eindigen.")]
    [SerializeField, Min(0f)] private float approachDistance = 40f;
    [SerializeField, Min(0f)] private float altitude = 30f;
    [SerializeField, Min(1f)] private float jetSpeed = 70f;

    [Header("Bombardement")]
    [Tooltip("Afstand in meter tussen twee vuurpassen langs de lijn.")]
    [SerializeField, Min(0.5f)] private float strikeSpacing = 4f;

    [SerializeField, Min(0)] private int rocketDamage = 120;
    [SerializeField, Min(0.5f)] private float rocketRadius = 2.5f;
    [SerializeField, Min(0)] private int cannonDamage = 35;
    [SerializeField, Min(0.5f)] private float cannonRadius = 5f;

    [Tooltip("Optioneel model van de straaljager. Zonder prefab is er alleen de logica.")]
    [SerializeField] private GameObject jetPrefab;

    public float ApproachDistance => approachDistance;
    public float Altitude => altitude;
    public float JetSpeed => jetSpeed;
    public float StrikeSpacing => strikeSpacing;
    public int RocketDamage => rocketDamage;
    public float RocketRadius => rocketRadius;
    public int CannonDamage => cannonDamage;
    public float CannonRadius => cannonRadius;
    public GameObject JetPrefab => jetPrefab;

    public override void Execute(GeneralPowerContext context)
    {
        Vector3 flat = context.end - context.start;
        flat.y = 0f;

        float lineLength = flat.magnitude;
        Vector3 dir = lineLength > 0.01f ? flat / lineLength : Vector3.forward;

        // Zijwaarts voor de formatie. Elke jager krijgt een eigen offset.
        Vector3 side = Vector3.Cross(Vector3.up, dir);

        for (int i = 0; i < jetCount; i++)
        {
            float offset = (i - (jetCount - 1) * 0.5f) * formationSpacing;
            Vector3 origin = context.start + side * offset - dir * approachDistance;

            GameObject runnerObject = new GameObject($"A10_Jet_{i}");
            runnerObject.AddComponent<AirStrikeRunner>().Begin(this, context.team, origin, dir, lineLength);
        }
    }
}
