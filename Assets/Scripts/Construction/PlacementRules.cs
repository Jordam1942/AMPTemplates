using UnityEngine;
using UnityEngine.AI;

public enum PlacementResult { Valid, Unaffordable, NotWalkable, Blocked }

/// <summary>
/// Gedeelde plaatsingsregels voor de speler (ghost) en de AI. Eén implementatie voor beide.
/// Evaluate controleert alleen; TryPlace voert de plaatsing uit.
/// </summary>
public static class PlacementRules
{
    private const int SampleGrid = 3;
    private const float NavMeshSampleDistance = 0.5f;
    private const float MaxFlatnessError = 0.5f;

    /// <summary>
    /// Controleert kosten, begaanbaarheid op de NavMesh en overlap met blokkerende colliders.
    /// padding is extra vrije ruimte in meters rond het gebouw. De speler gebruikt 0, de AI een marge.
    /// </summary>
    public static PlacementResult Evaluate(
        BuildingDataSO data,
        Vector3 center,
        Quaternion rotation,
        float padding,
        int blockingMask,
        Collider[] overlapBuffer,
        PlayerEconomy economy)
    {
        if (data == null || economy == null || !economy.CanAfford(data.cost)) return PlacementResult.Unaffordable;

        Vector2 half = data.footprint * 0.5f;

        // Bemonstering op een raster over de voetafdruk, niet alleen in het midden.
        for (int ix = 0; ix < SampleGrid; ix++)
        {
            for (int iz = 0; iz < SampleGrid; iz++)
            {
                float tx = ix / (float)(SampleGrid - 1);
                float tz = iz / (float)(SampleGrid - 1);
                Vector3 local = new Vector3(Mathf.Lerp(-half.x, half.x, tx), 0f, Mathf.Lerp(-half.y, half.y, tz));
                Vector3 sample = center + rotation * local;

                if (!NavMesh.SamplePosition(sample, out NavMeshHit hit, NavMeshSampleDistance, NavMesh.AllAreas))
                    return PlacementResult.NotWalkable;

                // Vlakte: een punt op een andere hoogte betekent een helling of een trede.
                if (Mathf.Abs(hit.position.y - center.y) > MaxFlatnessError)
                    return PlacementResult.NotWalkable;
            }
        }

        // Overlap-box iets kleiner dan de voetafdruk, zodat aangrenzend bouwen mogelijk blijft.
        Vector3 boxCenter = center + Vector3.up;
        Vector3 boxHalf = new Vector3(half.x * 0.95f + padding, 1f, half.y * 0.95f + padding);
        int hits = Physics.OverlapBoxNonAlloc(
            boxCenter, boxHalf, overlapBuffer, rotation, blockingMask, QueryTriggerInteraction.Ignore);

        return hits > 0 ? PlacementResult.Blocked : PlacementResult.Valid;
    }

    /// <summary>Voert de plaatsing uit: kosten, spawn in opbouwstand. Geeft false terug bij te weinig geld.</summary>
    public static bool TryPlace(
        BuildingDataSO data,
        Vector3 position,
        Quaternion rotation,
        PlayerEconomy economy,
        out BuildingController site)
    {
        site = null;
        if (data == null || data.prefab == null || economy == null) return false;
        if (!economy.TrySpend(data.cost)) return false;

        GameObject go = Object.Instantiate(data.prefab, position, rotation);
        if (!go.TryGetComponent(out site))
        {
            Object.Destroy(go);
            economy.Refund(data.cost);
            return false;
        }

        // Er is geen aparte bouwwachtrij. Bouwers zoeken zelf naar gebouwen in opbouw via BuildingController.Registry.
        site.BeginConstruction(data, economy.TeamId);
        return true;
    }
}
