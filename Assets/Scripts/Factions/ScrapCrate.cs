using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Scrap-crate op het slagveld. Een unit die in de buurt komt, rapt de crate op.
///  - GLA-voertuig met ScrapCollector en nog niet op maximaal niveau: upgrade.
///  - Alle andere units, of een volledig geüpgraded GLA-voertuig: credits voor het team van de unit.
/// Pickup gebeurt via een afstandscheck, periodiek. Crates zijn statisch en hebben geen Rigidbody nodig.
/// </summary>
[DisallowMultipleComponent]
public sealed class ScrapCrate : MonoBehaviour
{
    private const float PickupCheckInterval = 0.2f;

    private ScrapSettingsSO settings;
    private float lifeTimer;
    private float checkTimer;

    /// <summary>Wordt aangeroepen door ScrapOnDeath direct na het spawnen.</summary>
    public void Initialize(ScrapSettingsSO scrapSettings)
    {
        settings = scrapSettings;
        lifeTimer = settings != null ? settings.lifetime : 60f;
    }

    private void Update()
    {
        lifeTimer -= Time.deltaTime;
        if (lifeTimer <= 0f)
        {
            Destroy(gameObject);
            return;
        }

        checkTimer -= Time.deltaTime;
        if (checkTimer > 0f) return;

        checkTimer = PickupCheckInterval;
        TryPickup();
    }

    private void TryPickup()
    {
        if (settings == null) return;

        float radiusSqr = settings.pickupRadius * settings.pickupRadius;
        UnitController nearest = null;
        float nearestSqr = radiusSqr;

        IReadOnlyList<UnitController> units = UnitController.Registry;
        for (int i = 0; i < units.Count; i++)
        {
            UnitController u = units[i];
            if (!u.IsAlive) continue;

            float sq = (u.Position - transform.position).sqrMagnitude;
            if (sq <= nearestSqr)
            {
                nearestSqr = sq;
                nearest = u;
            }
        }

        if (nearest != null) Collect(nearest);
    }

    private void Collect(UnitController unit)
    {
        ScrapCollector collector = unit.GetComponent<ScrapCollector>();
        bool isGla = unit.Data != null && unit.Data.faction == Faction.GLA;
        bool canUpgrade = collector != null && isGla && collector.Level < settings.MaxLevel;

        if (canUpgrade)
        {
            collector.Upgrade(settings);
        }
        else
        {
            PlayerEconomy economy = PlayerEconomy.ForTeam(unit.Team);
            if (economy != null) economy.Deposit(settings.bounty);
        }

        Destroy(gameObject);
    }
}
