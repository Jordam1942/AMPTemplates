using UnityEngine;

/// <summary>
/// Bewegingsbackend voor een gatherer. Ground-units gebruiken de NavMesh via UnitController.
/// Chinooks vliegen via ChinookFlight. SupplyGatherer kent alleen dit contract.
/// </summary>
public interface IGatherMovement
{
    /// <summary>Stuurt naar een doel. Landingsgedrag en route zijn aan de implementatie.</summary>
    void MoveTo(Vector3 destination);

    /// <summary>True als de gatherer stilstaat en op een plek is geland of gestopt.</summary>
    bool IsIdle { get; }
}
