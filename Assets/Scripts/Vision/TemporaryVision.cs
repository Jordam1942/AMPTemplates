using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tijdelijke zichtbronnen, bijvoorbeeld een spy drone. FogOfWar leest deze lijst bij elke update mee
/// als extra viewers van het team. Verlopen bronnen worden opgeruimd voordat de lijst gelezen wordt.
/// </summary>
public static class TemporaryVision
{
    public struct VisionSource
    {
        public int team;
        public Vector3 position;
        public float radius;
        public float expiresAt;
    }

    private static readonly List<VisionSource> sources = new List<VisionSource>(8);

    public static IReadOnlyList<VisionSource> Active => sources;

    public static void Add(int team, Vector3 position, float radius, float duration)
    {
        sources.Add(new VisionSource
        {
            team = team,
            position = position,
            radius = radius,
            expiresAt = Time.time + duration,
        });
    }

    /// <summary>Verwijdert verlopen bronnen. Achteraan beginnen, zodat RemoveAt goedkoop blijft.</summary>
    public static void Prune()
    {
        float now = Time.time;
        for (int i = sources.Count - 1; i >= 0; i--)
        {
            if (sources[i].expiresAt <= now) sources.RemoveAt(i);
        }
    }

    // Voorkomt dat bronnen tussen Play-sessies blijven staan als domain reload uit staat.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetOnLoad() => sources.Clear();
}
