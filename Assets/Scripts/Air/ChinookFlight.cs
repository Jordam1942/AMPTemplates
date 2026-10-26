using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Vliegende gatherer (Chinook). Vliegt rechtstreeks over terrein en obstakels. De NavMesh wordt
/// alleen gebruikt om het landingspunt te bepalen en de agent weer te activeren.
/// Drie fasen: opstijgen naar vaste kruishoogte, kruisen, dalen op de NavMesh.
/// Het model kantelt mee: pitch bij versnellen en vertragen, roll bij draaien.
/// </summary>
[RequireComponent(typeof(UnitController))]
[DisallowMultipleComponent]
public sealed class ChinookFlight : MonoBehaviour, IGatherMovement
{
    private enum Phase { Grounded, Ascending, Cruising, Descending }

    [Header("Vlucht")]
    [Tooltip("Vaste wereldhoogte van de kruisvlucht. Daardoor wordt het terrein eronder overgevlogen.")]
    [SerializeField, Min(0f)] private float cruiseAltitude = 18f;

    [SerializeField, Min(0.1f)] private float ascendSpeed = 6f;
    [SerializeField, Min(0.1f)] private float descendSpeed = 4f;
    [SerializeField, Min(0.1f)] private float cruiseSpeed = 22f;
    [SerializeField, Min(0.1f)] private float acceleration = 10f;
    [Tooltip("Graden per seconde.")]
    [SerializeField, Min(0.1f)] private float turnSpeed = 120f;

    [Tooltip("XZ-afstand tot het doel waarbij het dalen begint.")]
    [SerializeField, Min(0.1f)] private float descentStartDistance = 10f;

    [SerializeField, Min(0f)] private float hoverAboveNavMesh = 0.05f;

    [Header("Model")]
    [Tooltip("Kind-transform die kantelt. De root draait alleen om de verticale as.")]
    [SerializeField] private Transform bodyRoot;

    [SerializeField, Min(0f)] private float maxPitch = 12f;
    [SerializeField, Min(0f)] private float maxRoll = 18f;
    [SerializeField, Min(0f)] private float tiltSharpness = 4f;

    private UnitController unit;
    private Phase phase = Phase.Grounded;
    private Vector3 target;
    private Vector3 landingPoint;
    private Vector3 velocity;
    private float yawRate;
    private float pitch;
    private float roll;

    public bool IsIdle => phase == Phase.Grounded;

    private void Awake()
    {
        unit = GetComponent<UnitController>();
    }

    public void MoveTo(Vector3 destination)
    {
        if (!unit.IsAlive) return;

        target = destination;

        switch (phase)
        {
            case Phase.Grounded:
                // Opstijgen: de NavMesh-agent maakt plaats voor de vlucht.
                unit.BeginExternalControl();
                velocity = Vector3.zero;
                phase = Phase.Ascending;
                break;

            case Phase.Descending:
                // Tijdens het dalen een nieuw doel: weer op kruishoogte gaan.
                phase = Phase.Cruising;
                break;
        }
    }

    private void Update()
    {
        if (phase == Phase.Grounded) return;

        // Gesneuveld tijdens de vlucht: de unit stopt met sturen. Vallen komt later.
        if (!unit.IsAlive)
        {
            phase = Phase.Grounded;
            return;
        }

        float dt = Time.deltaTime;
        switch (phase)
        {
            case Phase.Ascending: TickAscend(dt); break;
            case Phase.Cruising: TickCruise(dt); break;
            case Phase.Descending: TickDescend(dt); break;
        }

        ApplyTilt(dt);
    }

    // ---------- Fasen ----------

    private void TickAscend(float dt)
    {
        Vector3 pos = transform.position;
        float newY = Mathf.MoveTowards(pos.y, cruiseAltitude, ascendSpeed * dt);
        transform.position = new Vector3(pos.x, newY, pos.z);

        // Tijdens het opstijgen al richten op het doel, zodat de vlucht niet met een zijwaartse start begint.
        TurnToward(FlatDirection(pos, target), dt);
        velocity = Vector3.zero;

        if (Mathf.Abs(newY - cruiseAltitude) < 0.01f) phase = Phase.Cruising;
    }

    private void TickCruise(float dt)
    {
        Vector3 pos = transform.position;
        Vector3 flat = FlatVector(pos, target);
        float dist = flat.magnitude;

        if (dist <= descentStartDistance)
        {
            BeginDescent();
            return;
        }

        // Afremmen naarmate het doel dichterbij komt, zodat de landing niet overschiet.
        Vector3 dir = flat / dist;
        float speed = Mathf.Min(cruiseSpeed, dist * 2f);
        velocity = Vector3.MoveTowards(velocity, dir * speed, acceleration * dt);

        Vector3 next = pos + velocity * dt;
        next.y = Mathf.MoveTowards(pos.y, cruiseAltitude, ascendSpeed * dt);
        transform.position = next;

        TurnToward(dir, dt);
    }

    private void BeginDescent()
    {
        // Het landingspunt moet op de NavMesh liggen, zodat de agent daarna weer kan lopen.
        // Lukt het zoeken niet, dan landt de chinook op de huidige XZ-positie.
        landingPoint = NavMesh.SamplePosition(target, out NavMeshHit hit, 8f, NavMesh.AllAreas)
            ? hit.position
            : new Vector3(transform.position.x, 0f, transform.position.z);

        phase = Phase.Descending;
    }

    private void TickDescend(float dt)
    {
        Vector3 pos = transform.position;
        Vector3 flat = FlatVector(pos, landingPoint);
        float dist = flat.magnitude;

        float groundY = landingPoint.y + hoverAboveNavMesh;
        float newY = Mathf.MoveTowards(pos.y, groundY, descendSpeed * dt);

        Vector3 dir = dist > 0.001f ? flat / dist : Vector3.zero;
        float speed = Mathf.Min(cruiseSpeed * 0.5f, dist * 2f);
        velocity = Vector3.MoveTowards(velocity, dir * speed, acceleration * dt);

        Vector3 next = pos + velocity * dt;
        next.y = newY;
        transform.position = next;

        if (dist > 0.001f) TurnToward(dir, dt);

        if (Mathf.Abs(newY - groundY) < 0.01f && dist < 1f) Land();
    }

    private void Land()
    {
        velocity = Vector3.zero;
        phase = Phase.Grounded;

        // Zet de agent terug op het NavMesh-punt. Het model rechtzetten gebeurt in ApplyTilt.
        unit.EndExternalControl(landingPoint);
        ResetTilt();
    }

    // ---------- Oriëntatie en kanteling ----------

    /// <summary>Draait de root richting dir (alleen horizontaal) en meet de draaisnelheid voor de roll.</summary>
    private void TurnToward(Vector3 dir, float dt)
    {
        if (dir.sqrMagnitude < 0.0001f) return;

        Quaternion targetRot = Quaternion.LookRotation(dir, Vector3.up);
        float before = transform.eulerAngles.y;
        transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, turnSpeed * dt);
        yawRate = Mathf.DeltaAngle(before, transform.eulerAngles.y) / dt;
    }

    private void ApplyTilt(float dt)
    {
        if (bodyRoot == null) return;

        // Lokale snelheid: vooruit versnellen laat de neus zakken, vertragen laat hem stijgen.
        Vector3 local = transform.InverseTransformDirection(velocity);
        float targetPitch = Mathf.Clamp(local.z / cruiseSpeed * maxPitch, -maxPitch, maxPitch);

        // Draaien naar rechts (positieve yaw) laat de rechterkant zakken: negatieve Z-roll.
        float targetRoll = Mathf.Clamp(-yawRate / turnSpeed * maxRoll, -maxRoll, maxRoll);

        float k = 1f - Mathf.Exp(-tiltSharpness * dt);
        pitch = Mathf.Lerp(pitch, targetPitch, k);
        roll = Mathf.Lerp(roll, targetRoll, k);

        bodyRoot.localRotation = Quaternion.Euler(pitch, 0f, roll);
        yawRate = Mathf.Lerp(yawRate, 0f, k);
    }

    private void ResetTilt()
    {
        pitch = 0f;
        roll = 0f;
        yawRate = 0f;
        if (bodyRoot != null) bodyRoot.localRotation = Quaternion.identity;
    }

    // ---------- Hulpfuncties ----------

    private static Vector3 FlatVector(Vector3 from, Vector3 to)
    {
        Vector3 v = to - from;
        v.y = 0f;
        return v;
    }

    private static Vector3 FlatDirection(Vector3 from, Vector3 to)
    {
        Vector3 v = FlatVector(from, to);
        return v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.zero;
    }
}
