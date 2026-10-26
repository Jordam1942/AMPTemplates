using UnityEngine;

/// <summary>
/// Cluster-mijn. Ontploft bij fysiek contact met een vijandelijke unit, via een Physics-trigger.
/// Onzichtbaar voor de tegenstander, tenzij een vijand dichtbij komt. Dan wordt de mijn zichtbaar
/// voor het volgende deel van de seconde. De eigen speler ziet de mijn altijd.
/// Een mijn ontploft één keer en ruimt zichzelf dan op.
/// </summary>
[RequireComponent(typeof(Collider))]
[DisallowMultipleComponent]
public sealed class LandMine : MonoBehaviour
{
    private const float DetectionInterval = 0.5f;
    private const float RevealDuration = 3f;

    private int ownerTeam;
    private float explosionRadius;
    private int damage;
    private float detectionRadius;
    private float lifeTimer;
    private float detectionTimer;
    private float revealTimer;
    private bool armed = true;
    private Renderer[] renderers;

    private void Awake()
    {
        Collider col = GetComponent<Collider>();
        col.isTrigger = true;

        // Een kinematic Rigidbody is nodig om trigger-events te krijgen bij een andere kinematic body.
        if (!TryGetComponent(out Rigidbody body)) body = gameObject.AddComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;

        renderers = GetComponentsInChildren<Renderer>(true);
    }

    /// <summary>Stelt de mijn in. Wordt direct na het spawnen aangeroepen door de dropper.</summary>
    public void Initialize(int team, float blastRadius, int blastDamage, float detectRadius, float lifetime)
    {
        ownerTeam = team;
        explosionRadius = blastRadius;
        damage = blastDamage;
        detectionRadius = detectRadius;
        lifeTimer = lifetime;
        ApplyVisibility();
    }

    private void Update()
    {
        lifeTimer -= Time.deltaTime;
        if (lifeTimer <= 0f)
        {
            Destroy(gameObject);
            return;
        }

        detectionTimer -= Time.deltaTime;
        if (detectionTimer <= 0f)
        {
            detectionTimer = DetectionInterval;
            ScanForEnemies();
        }

        if (revealTimer > 0f)
        {
            revealTimer -= Time.deltaTime;
            if (revealTimer <= 0f) ApplyVisibility();
        }
    }

    private void ScanForEnemies()
    {
        float radiusSqr = detectionRadius * detectionRadius;
        IReadOnlyList<UnitController> units = UnitController.Registry;

        for (int i = 0; i < units.Count; i++)
        {
            UnitController u = units[i];
            if (u.Team == ownerTeam || !u.IsAlive) continue;
            if ((u.Position - transform.position).sqrMagnitude > radiusSqr) continue;

            // Een vijand in de buurt maakt de mijn zichtbaar, zodat die te ontwijken is.
            revealTimer = RevealDuration;
            ApplyVisibility();
            return;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!armed) return;

        UnitController unit = other.GetComponentInParent<UnitController>();
        if (unit == null || unit.Team == ownerTeam || !unit.IsAlive) return;

        armed = false;
        CombatUtility.DamageArea(transform.position, explosionRadius, damage, ownerTeam);
        Destroy(gameObject);
    }

    private void ApplyVisibility()
    {
        bool visibleToLocal = ownerTeam == UnitController.LocalPlayerTeam || revealTimer > 0f;

        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null) renderers[i].enabled = visibleToLocal;
        }
    }
}
