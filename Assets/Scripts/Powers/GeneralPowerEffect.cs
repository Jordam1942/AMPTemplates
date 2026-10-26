using UnityEngine;

/// <summary>
/// Context die de manager meegeeft aan een effect. start en end zijn grondpunten.
/// Bij Point zijn ze gelijk. Bij Passive zijn ze nul.
/// </summary>
public readonly struct GeneralPowerContext
{
    public readonly int team;
    public readonly Vector3 start;
    public readonly Vector3 end;

    public GeneralPowerContext(int team, Vector3 start, Vector3 end)
    {
        this.team = team;
        this.start = start;
        this.end = end;
    }
}

/// <summary>
/// Basis voor het effect van een power. Subklassen zijn ScriptableObjects, zodat een ontwerper
/// ze in de editor kan instellen en koppelen aan een GeneralPowerSO.
/// </summary>
public abstract class GeneralPowerEffect : ScriptableObject
{
    public abstract void Execute(GeneralPowerContext context);
}
