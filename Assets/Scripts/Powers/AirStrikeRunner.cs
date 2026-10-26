using UnityEngine;

/// <summary>
/// Één straaljager van de A-10 Strike. Vliegt een rechte lijn af op hoogte, en vuurt op vaste afstanden
/// op het doellijnstuk. Elke vuurpas bestaat uit raketten (smal, zwaar) en kanonvuur (breed, licht).
/// Ruimt zichzelf op als de lijn voorbij is.
/// </summary>
public sealed class AirStrikeRunner : MonoBehaviour
{
    private AirStrikeEffect config;
    private int attackerTeam;
    private Vector3 origin;
    private Vector3 direction;
    private float lineLength;
    private float travelled;
    private float totalLength;
    private float nextStrikeAt;

    /// <summary>Initialiseert de runner. origin is het begin van de vlucht, direction is genormaliseerd.</summary>
    public void Begin(AirStrikeEffect effect, int team, Vector3 flightOrigin, Vector3 flightDirection, float targetLength)
    {
        config = effect;
        attackerTeam = team;
        origin = flightOrigin;
        direction = flightDirection;
        lineLength = targetLength;

        // Vlucht: aanloop, doellijn, en uitloop met dezelfde lengte als de aanloop.
        totalLength = config.ApproachDistance * 2f + lineLength;
        nextStrikeAt = config.ApproachDistance;

        transform.position = origin + Vector3.up * config.Altitude;
        transform.rotation = Quaternion.LookRotation(direction, Vector3.up);

        if (config.JetPrefab != null) Instantiate(config.JetPrefab, transform);
    }

    private void Update()
    {
        if (config == null) return;

        travelled += config.JetSpeed * Time.deltaTime;

        Vector3 ground = origin + direction * Mathf.Min(travelled, totalLength);
        transform.position = ground + Vector3.up * config.Altitude;

        float strikeEnd = config.ApproachDistance + lineLength;
        while (nextStrikeAt <= travelled && nextStrikeAt <= strikeEnd)
        {
            Strike(origin + direction * nextStrikeAt);
            nextStrikeAt += config.StrikeSpacing;
        }

        if (travelled >= totalLength) Destroy(gameObject);
    }

    private void Strike(Vector3 point)
    {
        // Raketten: smalle, zware inslag. Kanonvuur: brede, lichtere spreiding.
        CombatUtility.DamageArea(point, config.RocketRadius, config.RocketDamage, attackerTeam);
        CombatUtility.DamageArea(point, config.CannonRadius, config.CannonDamage, attackerTeam);
    }
}
