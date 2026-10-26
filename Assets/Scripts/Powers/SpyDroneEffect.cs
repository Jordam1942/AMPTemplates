using UnityEngine;

/// <summary>
/// Spy Drone (USA). Een onzichtbare scoutdrone die tijdelijk een zichtbel legt over het gekozen punt.
/// Het zicht loopt via de fog of war, dus vijanden in de cirkel komen in actief zicht.
/// De drone zelf is onzichtbaar, en er is geen model om te beschermen.
/// </summary>
[CreateAssetMenu(fileName = "SpyDroneEffect", menuName = "Generals/Effects/Spy Drone")]
public sealed class SpyDroneEffect : GeneralPowerEffect
{
    [SerializeField, Min(5f)] private float revealRadius = 40f;
    [Tooltip("Seconden dat de drone het gebied verlicht.")]
    [SerializeField, Min(1f)] private float duration = 45f;

    public override void Execute(GeneralPowerContext context)
    {
        TemporaryVision.Add(context.team, context.start, revealRadius, duration);
    }
}
