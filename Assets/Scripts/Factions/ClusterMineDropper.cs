using UnityEngine;

/// <summary>
/// Voert de Cluster Mines-uitzending uit. Een bommenwerper vliegt over het doelgebied, en mijnen
/// vallen op vaste posities binnen de cirkel. De posities liggen op een gouden-hoek-spiraal, zodat ze
/// gelijkmatig verdeeld zijn en bij elke uitzending hetzelfde patroon hebben.
/// De mijnen worden direct op de grond geplaatst. Het object ruimt zichzelf op als alles is gevallen.
/// </summary>
public sealed class ClusterMineDropper : MonoBehaviour
{
    private const float GoldenAngle = 2.39996323f;
    private const float Cleanup = 3f;

    private ClusterMinesEffect config;
    private int team;
    private Vector3 center;
    private Vector3 bomberFrom;
    private Vector3 bomberTo;
    private float flightTime;
    private float elapsed;
    private float dropTimer;
    private int dropped;
    private Transform bomber;

    public void Begin(ClusterMinesEffect effect, int ownerTeam, Vector3 target, Vector3 direction)
    {
        config = effect;
        team = ownerTeam;
        center = target;
        transform.position = target;

        // De bommenwerper komt van buiten het doelgebied en vliegt er doorheen.
        Vector3 dir = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
        bomberFrom = target - dir * config.BomberApproach + Vector3.up * config.BomberAltitude;
        bomberTo = target + dir * config.BomberApproach + Vector3.up * config.BomberAltitude;
        flightTime = 2f * config.BomberApproach / config.BomberSpeed;

        if (config.BomberPrefab != null)
        {
            bomber = Instantiate(config.BomberPrefab, bomberFrom, Quaternion.LookRotation(dir)).transform;
        }
    }

    private void Update()
    {
        if (config == null) return;

        elapsed += Time.deltaTime;
        float t = Mathf.Clamp01(elapsed / flightTime);

        if (bomber != null) bomber.position = Vector3.Lerp(bomberFrom, bomberTo, t);

        // Mijnen vallen verspreid over de vluchttijd.
        dropTimer -= Time.deltaTime;
        while (dropped < config.MineCount && dropTimer <= 0f)
        {
            DropMine(dropped);
            dropped++;
            dropTimer += config.DropInterval;
        }

        if (dropped >= config.MineCount && elapsed >= flightTime)
        {
            if (bomber != null) Destroy(bomber.gameObject);
            Destroy(gameObject, Cleanup);
            enabled = false;
        }
    }

    private void DropMine(int index)
    {
        Vector3 offset = SpiralOffset(index, config.MineCount, config.DropRadius);
        Vector3 probe = center + offset + Vector3.up * 20f;

        Vector3 position = center + offset;
        if (Physics.Raycast(probe, Vector3.down, out RaycastHit hit, 40f, config.GroundMask))
            position = hit.point;

        GameObject mine = config.MinePrefab != null
            ? Instantiate(config.MinePrefab, position, Quaternion.identity)
            : CreateDefaultMine(position);

        if (mine.TryGetComponent(out LandMine landMine))
        {
            landMine.Initialize(team, config.ExplosionRadius, config.ExplosionDamage,
                config.DetectionRadius, config.MineLifetime);
        }
    }

    /// <summary>Gouden-hoek-spiraal binnen een cirkel. Gelijkmatige verdeling zonder toeval.</summary>
    private static Vector3 SpiralOffset(int index, int count, float radius)
    {
        float r = radius * Mathf.Sqrt((index + 0.5f) / count);
        float angle = index * GoldenAngle;
        return new Vector3(Mathf.Cos(angle) * r, 0f, Mathf.Sin(angle) * r);
    }

    private static GameObject CreateDefaultMine(Vector3 position)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "ClusterMine";
        go.transform.position = position + Vector3.up * 0.1f;
        go.transform.localScale = Vector3.one * 0.6f;

        SphereCollider col = go.GetComponent<SphereCollider>();
        col.isTrigger = true;

        go.AddComponent<LandMine>();
        return go;
    }
}
