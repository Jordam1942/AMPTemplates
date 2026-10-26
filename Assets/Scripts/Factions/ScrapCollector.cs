using UnityEngine;

/// <summary>
/// Schroot-upgrades van een GLA-voertuig. Niveau 1 repareert het voertuig en verhoogt de schade.
/// Niveau 2 is de zwaarste wapenupgrade (bijv. raketwerper). Hogere niveaus zijn niet mogelijk,
/// en de volgende crates geven dan alleen credits.
/// Wapenvisuals zijn kinderen die per niveau aan- of uitgaan.
/// </summary>
[RequireComponent(typeof(UnitController))]
[DisallowMultipleComponent]
public sealed class ScrapCollector : MonoBehaviour
{
    [Tooltip("Wapenvisuals per niveau. Index 0 = niveau 1 (bijv. dubbele loop), index 1 = niveau 2 (bijv. raketwerper).")]
    [SerializeField] private GameObject[] weaponVisuals = new GameObject[2];

    private UnitController unit;

    public int Level { get; private set; }

    private void Awake()
    {
        unit = GetComponent<UnitController>();
        ShowVisual(0);
    }

    /// <summary>Verhoogt het niveau met één. Geeft false terug als het al maximaal is.</summary>
    public bool Upgrade(ScrapSettingsSO settings)
    {
        if (settings == null || Level >= settings.MaxLevel) return false;

        Level++;

        // Reparatie alleen bij niveau 1. Niveau 2 is puur offensief.
        if (Level == 1)
            unit.Heal(Mathf.RoundToInt(unit.MaxHp * settings.repairFraction));

        unit.SetDamageMultiplier(settings.DamageMultiplierForLevel(Level));
        ShowVisual(Level);
        return true;
    }

    private void ShowVisual(int level)
    {
        for (int i = 0; i < weaponVisuals.Length; i++)
        {
            if (weaponVisuals[i] != null) weaponVisuals[i].SetActive(i == level - 1);
        }
    }
}
