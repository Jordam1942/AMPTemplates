using UnityEngine;

/// <summary>
/// Gemeenschappelijk contract voor alles wat aangevallen kan worden: units en gebouwen.
/// </summary>
public interface IDamageable
{
    int Team { get; }
    ArmorType Armor { get; }
    Vector3 Position { get; }
    bool IsAlive { get; }

    void TakeDamage(int amount);
}
