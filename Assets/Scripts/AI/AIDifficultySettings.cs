using System;
using UnityEngine;

public enum AIDifficulty { Easy, Medium, Hard }

/// <summary>
/// Alle instelbare parameters van de AI per moeilijkheidsgraad, op één plek om te balanceren.
/// Easy beslist langzamer, bouwt minder en valt later aan. Hard is het tegenovergestelde.
/// </summary>
[Serializable]
public struct AIDifficultySettings
{
    [Tooltip("Seconden tussen twee beslisrondes. Lager = reactiever.")]
    public float decisionInterval;

    [Tooltip("Maximaal aantal gebouwen tegelijk in aanbouw.")]
    public int maxConcurrentBuilds;

    public int targetGatherers;
    public int targetBuilders;
    public int targetBarracks;
    public int targetWarFactories;
    public int targetSupplyCenters;

    [Tooltip("Aantal gevechtsunits in de squad voordat een aanvalsgolf start.")]
    public int attackThreshold;

    [Tooltip("Seconden tussen twee aanvalsgolven.")]
    public float waveCooldown;

    public bool canRetreat;

    [Tooltip("Het aandeel van de golf dat verloren moet gaan voordat de AI terugtrekt. Lager = voorzichtiger.")]
    [Range(0f, 1f)] public float retreatAtLossRatio;

    public static AIDifficultySettings For(AIDifficulty difficulty)
    {
        switch (difficulty)
        {
            case AIDifficulty.Easy:
                return new AIDifficultySettings
                {
                    decisionInterval = 2.5f,
                    maxConcurrentBuilds = 1,
                    targetGatherers = 2,
                    targetBuilders = 1,
                    targetBarracks = 1,
                    targetWarFactories = 0,
                    targetSupplyCenters = 1,
                    attackThreshold = 10,
                    waveCooldown = 240f,
                    canRetreat = false,
                    retreatAtLossRatio = 0.5f,
                };

            case AIDifficulty.Medium:
                return new AIDifficultySettings
                {
                    decisionInterval = 1.5f,
                    maxConcurrentBuilds = 2,
                    targetGatherers = 4,
                    targetBuilders = 2,
                    targetBarracks = 2,
                    targetWarFactories = 1,
                    targetSupplyCenters = 2,
                    attackThreshold = 8,
                    waveCooldown = 150f,
                    canRetreat = true,
                    retreatAtLossRatio = 0.5f,
                };

            default:
                return new AIDifficultySettings
                {
                    decisionInterval = 0.8f,
                    maxConcurrentBuilds = 3,
                    targetGatherers = 6,
                    targetBuilders = 3,
                    targetBarracks = 2,
                    targetWarFactories = 2,
                    targetSupplyCenters = 3,
                    attackThreshold = 6,
                    waveCooldown = 90f,
                    canRetreat = true,
                    retreatAtLossRatio = 0.35f,
                };
        }
    }
}
