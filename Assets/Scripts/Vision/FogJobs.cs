using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

/// <summary>Toestandswaarden van de fog-grid. Als constanten zodat Burst ze inline kan gebruiken.</summary>
public static class FogStates
{
    public const byte Unexplored = 0;
    public const byte Explored = 1;
    public const byte Visible = 2;
}

/// <summary>
/// Berekent het zicht per rij van de grid. Elke rij wordt door één thread geschreven, dus er zijn
/// geen races: de rijen zijn disjunct. Eerst verliezen zichtbare cellen hun zicht (wordt Explored),
/// daarna markeert elke viewer een cirkel als Visible.
/// </summary>
[BurstCompile(FloatPrecision.Standard, FloatMode.Fast)]
public struct RevealJob : IJobParallelFor
{
    /// <summary>xy = celcoördinaten van de bron, z = zichtradius in cellen.</summary>
    [ReadOnly] public NativeArray<float3> viewers;
    public int viewerCount;
    public int width;

    [NativeDisableParallelForRestriction] public NativeArray<byte> states;

    public void Execute(int row)
    {
        int rowStart = row * width;

        for (int x = 0; x < width; x++)
        {
            if (states[rowStart + x] == FogStates.Visible) states[rowStart + x] = FogStates.Explored;
        }

        float rowCenter = row + 0.5f;

        for (int v = 0; v < viewerCount; v++)
        {
            float3 viewer = viewers[v];
            float radius = viewer.z;
            float dz = rowCenter - viewer.y;
            float spanSqr = radius * radius - dz * dz;
            if (spanSqr < 0f) continue;

            float span = math.sqrt(spanSqr);
            int x0 = math.max(0, (int)math.floor(viewer.x - span));
            int x1 = math.min(width - 1, (int)math.ceil(viewer.x + span));

            for (int x = x0; x <= x1; x++)
            {
                float dx = (x + 0.5f) - viewer.x;
                if (dx * dx + dz * dz <= radius * radius)
                    states[rowStart + x] = FogStates.Visible;
            }
        }
    }
}

/// <summary>Zet de fog-toestand om naar pixels voor de minimap-textuur. Puur per cel, dus parallel.</summary>
[BurstCompile(FloatPrecision.Standard, FloatMode.Fast)]
public struct ColorizeJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<byte> states;
    [WriteOnly] public NativeArray<Color32> pixels;

    public void Execute(int i)
    {
        byte s = states[i];

        if (s == FogStates.Visible) pixels[i] = new Color32(120, 140, 110, 255);
        else if (s == FogStates.Explored) pixels[i] = new Color32(55, 60, 66, 255);
        else pixels[i] = new Color32(0, 0, 0, 255);
    }
}
