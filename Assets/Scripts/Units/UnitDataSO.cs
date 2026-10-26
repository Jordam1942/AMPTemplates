using UnityEngine;

public enum Faction { USA, China, GLA }

public enum ArmorType { Infantry, Vehicle, Structure, Aircraft }

/// <summary>
/// Statische eigenschappen van één unit-type. Eén asset per unit, geen runtime-state.
/// Balanceren gebeurt hier, niet in code.
/// </summary>
[CreateAssetMenu(fileName = "NewUnitData", menuName = "Generals/Unit Data")]
public sealed class UnitDataSO : ScriptableObject
{
    [Header("Identiteit")]
    public string displayName = "Unit";
    public Faction faction = Faction.USA;
    public ArmorType armorType = ArmorType.Vehicle;
    [Tooltip("Icoon voor de HUD en productiewachtrij.")]
    public Sprite icon;

    [Header("Overleven en beweging")]
    [Min(1)] public int maxHp = 100;
    [Min(0.1f)] public float speed = 4f;
    [Tooltip("Bereik waarbinnen een inactieve unit zelf vijanden aanvalt.")]
    [Min(1f)] public float sightRange = 25f;

    [Header("Wapen")]
    [Min(0)] public int weaponDamage = 10;
    [Min(0.5f)] public float attackRange = 6f;
    [Tooltip("Schoten per seconde.")]
    [Min(0.01f)] public float fireRate = 1f;

    [Tooltip("Schadefactor wanneer het doel dit pantsertype heeft.")]
    [Range(0f, 3f)] public float vsInfantry = 1f;
    [Range(0f, 3f)] public float vsVehicle = 1f;
    [Range(0f, 3f)] public float vsStructure = 1f;
    [Range(0f, 3f)] public float vsAircraft = 1f;

    [Header("Voice-lines (één clip per selectie-actie)")]
    public AudioClip[] selectVoices = new AudioClip[0];
    public AudioClip[] moveVoices = new AudioClip[0];
    public AudioClip[] attackVoices = new AudioClip[0];

    /// <summary>Schadefactor voor deze wapen tegen een doel met het opgegeven pantsertype.</summary>
    public float GetDamageMultiplier(ArmorType target) => target switch
    {
        ArmorType.Infantry => vsInfantry,
        ArmorType.Vehicle => vsVehicle,
        ArmorType.Structure => vsStructure,
        ArmorType.Aircraft => vsAircraft,
        _ => 1f,
    };
}
