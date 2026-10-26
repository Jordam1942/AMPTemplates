using UnityEngine;

/// <summary>
/// Balans van het GLA-schroot-systeem op één plek: drop-kans, oprapen, reparatie, wapenupgrades en de bounty.
/// Eén asset wordt gedeeld door de voertuigen, de crates en het oprapen.
/// </summary>
[CreateAssetMenu(fileName = "ScrapSettings", menuName = "Generals/Factions/Scrap Settings")]
public sealed class ScrapSettingsSO : ScriptableObject
{
    [Tooltip("Kans dat een gesneuveld voertuig een scrap-crate achterlaat.")]
    [Range(0f, 1f)] public float dropChance = 0.7f;

    [Tooltip("Straal waarbinnen een unit de crate oprapt.")]
    [Min(0.5f)] public float pickupRadius = 2.5f;

    [Tooltip("Seconden voordat een ongeraapte crate verdwijnt.")]
    [Min(1f)] public float lifetime = 60f;

    [Tooltip("Reparatie bij niveau 1, als fractie van het maximum HP.")]
    [Range(0f, 1f)] public float repairFraction = 0.25f;

    [Min(1f)] public float levelOneDamageMultiplier = 1.25f;
    [Min(1f)] public float levelTwoDamageMultiplier = 1.5f;

    [Tooltip("Credits per crate als er geen upgrade meer mogelijk is, of als de unit niet kan upgraden.")]
    [Min(0)] public int bounty = 150;

    public int MaxLevel => 2;

    public float DamageMultiplierForLevel(int level)
    {
        if (level >= 2) return levelTwoDamageMultiplier;
        return level == 1 ? levelOneDamageMultiplier : 1f;
    }
}
