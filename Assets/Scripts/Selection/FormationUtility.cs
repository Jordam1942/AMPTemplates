using UnityEngine;

/// <summary>Berekent grid-offsets voor formaties. Het centrum van het rooster ligt op (0,0,0).</summary>
public static class FormationUtility
{
    public static Vector3[] BuildGridOffsets(int count, float spacing)
    {
        var offsets = new Vector3[count];
        if (count == 0) return offsets;

        // Bijna vierkant rooster: kolommen = ceil(sqrt(n)).
        int columns = Mathf.CeilToInt(Mathf.Sqrt(count));
        int rows = Mathf.CeilToInt((float)count / columns);
        float xOrigin = (columns - 1) * 0.5f;
        float zOrigin = (rows - 1) * 0.5f;

        for (int i = 0; i < count; i++)
        {
            int col = i % columns;
            int row = i / columns;
            offsets[i] = new Vector3((col - xOrigin) * spacing, 0f, (row - zOrigin) * spacing);
        }

        return offsets;
    }
}
