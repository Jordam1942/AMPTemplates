using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

/// <summary>
/// Zoekt per unit de dichtstbijzijnde vijand binnen sightSqr. Draait parallel op alle worker-threads.
/// Dode units krijgen team 255 en worden overgeslagen. Output-index = index in de input-arrays.
/// </summary>
[BurstCompile(FloatPrecision.Standard, FloatMode.Fast)]
public struct NearestEnemyJob : IJobParallelFor
{
    private const byte DeadTeam = 255;

    [ReadOnly] public NativeArray<float3> positions;
    [ReadOnly] public NativeArray<byte> teams;
    [ReadOnly] public NativeArray<float> sightSqr;
    [WriteOnly] public NativeArray<int> nearestEnemy;

    public void Execute(int i)
    {
        float3 p = positions[i];
        byte myTeam = teams[i];
        float best = sightSqr[i];
        int bestIndex = -1;

        for (int j = 0; j < positions.Length; j++)
        {
            byte otherTeam = teams[j];
            if (otherTeam == myTeam || otherTeam == DeadTeam) continue;

            float d = math.distancesq(p, positions[j]);
            if (d < best)
            {
                best = d;
                bestIndex = j;
            }
        }

        nearestEnemy[i] = bestIndex;
    }
}

public static class TargetingJobs
{
    /// <summary>Plant de job. Batchgrootte 16 houdt de scheduling-overhead laag.</summary>
    public static JobHandle Schedule(
        NativeArray<float3> positions,
        NativeArray<byte> teams,
        NativeArray<float> sightSqr,
        NativeArray<int> nearestEnemy,
        JobHandle dependency = default)
    {
        var job = new NearestEnemyJob
        {
            positions = positions,
            teams = teams,
            sightSqr = sightSqr,
            nearestEnemy = nearestEnemy,
        };

        return job.Schedule(positions.Length, 16, dependency);
    }
}
