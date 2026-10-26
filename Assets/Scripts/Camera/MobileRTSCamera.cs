using UnityEngine;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

/// <summary>
/// Touch-RTS-camera. Root = pivot op het grondvlak (yaw + vaste pitch), child = camera op -afstand.
/// 1 vinger (na drempel): pan. 2 vingers: pan via het midden, pinch-zoom en twist-rotatie.
/// Alle beweging is inertie met exponentiële demping: frame-rate onafhankelijk en zonder snap bij loslaten.
/// </summary>
[DisallowMultipleComponent]
public sealed class MobileRTSCamera : MonoBehaviour
{
    public enum ProjectionMode { Perspective, Orthographic }

    [Header("References")]
    [SerializeField] private Camera rtsCamera;

    [Header("Projectie")]
    [SerializeField] private ProjectionMode projection = ProjectionMode.Perspective;
    [SerializeField, Range(20f, 80f)] private float lensFov = 45f;

    [Header("Pan")]
    [SerializeField, Min(0.1f)] private float panSpeed = 1f;
    [Tooltip("Hoger = sneller stoppen na loslaten.")]
    [SerializeField, Min(0f)] private float panDamping = 5f;
    [Tooltip("Hoger = strakker volgen van de vinger.")]
    [SerializeField, Min(0f)] private float followSharpness = 25f;
    [Tooltip("Pixels die een vinger moet bewegen voordat een drag start. Houdt tikken schoon.")]
    [SerializeField, Min(0f)] private float dragThreshold = 18f;

    [Header("Zoom")]
    [SerializeField] private float minDistance = 12f;
    [SerializeField] private float maxDistance = 60f;
    [SerializeField, Min(0f)] private float zoomSharpness = 10f;

    [Header("Rotatie")]
    [SerializeField, Range(20f, 80f)] private float pitch = 55f;
    [SerializeField, Min(0f)] private float twistDamping = 6f;

    [Header("Bounds (XZ, wereldruimte)")]
    [SerializeField] private Rect mapBounds = new Rect(-100f, -100f, 200f, 200f);
    [Tooltip("Hoe zacht de camera terugveert naar de map als hij erbuiten raakt.")]
    [SerializeField, Min(0f)] private float boundsSpring = 10f;

    /// <summary>Zet op true om camerabeweging te blokkeren, bijvoorbeeld tijdens marquee-selectie.</summary>
    public bool InputLocked { get; set; }

    private float yaw;
    private float currentDistance;
    private float targetDistance;
    private Vector3 panVelocity;
    private float twistVelocity;

    private void Awake()
    {
        if (rtsCamera == null) rtsCamera = GetComponentInChildren<Camera>();
        yaw = transform.eulerAngles.y;
        currentDistance = targetDistance = (minDistance + maxDistance) * 0.5f;
        ApplyProjection();
    }

    private void OnEnable() => EnhancedTouchSupport.Enable();
    private void OnDisable() => EnhancedTouchSupport.Disable();

    private void Update()
    {
        // Unscaled: de camera blijft werken als de simulatie gepauzeerd is (menu's, pauze).
        float dt = Time.unscaledDeltaTime;
        if (dt <= 0f) return;

        var touches = Touch.activeTouches;
        bool panning = false;
        bool twisting = false;
        Vector3 panTargetVelocity = Vector3.zero;
        float twistTargetVelocity = 0f;

        if (!InputLocked && touches.Count >= 2)
        {
            Touch a = touches[0];
            Touch b = touches[1];

            Vector2 curA = a.screenPosition;
            Vector2 curB = b.screenPosition;
            Vector2 prevA = curA - a.delta;
            Vector2 prevB = curB - b.delta;

            Vector2 curVec = curB - curA;
            Vector2 prevVec = prevB - prevA;
            float curDist = curVec.magnitude;
            float prevDist = prevVec.magnitude;

            // Pinch: de verhouding van de vingerafstand is frame-onafhankelijk.
            if (curDist > 1f && prevDist > 1f)
            {
                targetDistance = Mathf.Clamp(targetDistance * prevDist / curDist, minDistance, maxDistance);

                // Twist: hoekverschil van de vingerlijn tussen twee frames, in graden per seconde.
                float prevAngle = Mathf.Atan2(prevVec.y, prevVec.x) * Mathf.Rad2Deg;
                float curAngle = Mathf.Atan2(curVec.y, curVec.x) * Mathf.Rad2Deg;
                twistTargetVelocity = Mathf.DeltaAngle(prevAngle, curAngle) / dt;
                twisting = true;
            }

            // Het midden van de twee vingers pant de camera.
            Vector2 midDelta = (curA + curB - prevA - prevB) * 0.5f;
            panTargetVelocity = ScreenDeltaToWorld(midDelta) / dt;
            panning = true;
        }
        else if (!InputLocked && touches.Count == 1)
        {
            Touch t = touches[0];
            // Drempel: een tik mag de camera niet verschuiven.
            if (Vector2.Distance(t.screenPosition, t.startScreenPosition) >= dragThreshold)
            {
                panTargetVelocity = ScreenDeltaToWorld(t.delta) / dt;
                panning = true;
            }
        }

        // Pan: volg de vinger strak, of laat de inertie uitdoven na loslaten.
        if (panning)
            panVelocity = Vector3.Lerp(panVelocity, panTargetVelocity, 1f - Mathf.Exp(-followSharpness * dt));
        else
            panVelocity *= Mathf.Exp(-panDamping * dt);

        // Rotatie: zelfde principe als pan.
        if (twisting)
            twistVelocity = Mathf.Lerp(twistVelocity, twistTargetVelocity, 1f - Mathf.Exp(-followSharpness * dt));
        else
            twistVelocity *= Mathf.Exp(-twistDamping * dt);

        currentDistance = Mathf.Lerp(currentDistance, targetDistance, 1f - Mathf.Exp(-zoomSharpness * dt));
        yaw += twistVelocity * dt;
        transform.position += panVelocity * dt;
        ClampToBounds(dt);

        transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        rtsCamera.transform.localPosition = new Vector3(0f, 0f, -currentDistance);
        rtsCamera.transform.localRotation = Quaternion.identity;

        if (projection == ProjectionMode.Orthographic)
            rtsCamera.orthographicSize = currentDistance * 0.5f;
    }

    /// <summary>Speelkaart in wereldruimte (XZ). Gedeeld met de minimap en de fog of war.</summary>
    public Rect MapBounds => mapBounds;

    /// <summary>
    /// Zet de pivot direct op een wereldpunt, bijvoorbeeld vanaf de minimap.
    /// De kaartgrenzen worden in het volgende frame toegepast.
    /// </summary>
    public void SetPivot(Vector3 worldPoint)
    {
        transform.position = new Vector3(worldPoint.x, transform.position.y, worldPoint.z);
        panVelocity = Vector3.zero;
    }

    /// <summary>Zet een schermverplaatsing in pixels om naar een wereldverplaatsing op het grondvlak.</summary>
    private Vector3 ScreenDeltaToWorld(Vector2 pixels)
    {
        float worldPerPixel = ViewHeight() / Screen.height * panSpeed;
        Vector3 right = transform.right;
        Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;

        // Verticale pixels worden door de pitch op het grondvlak uitgerekt. Compenseer dat.
        float verticalCompensation = 1f / Mathf.Sin(pitch * Mathf.Deg2Rad);
        return (-right * pixels.x - forward * (pixels.y * verticalCompensation)) * worldPerPixel;
    }

    /// <summary>Hoogte van het zichtveld op de pivot, in wereldeenheden.</summary>
    private float ViewHeight()
    {
        return projection == ProjectionMode.Perspective
            ? 2f * currentDistance * Mathf.Tan(lensFov * 0.5f * Mathf.Deg2Rad)
            : currentDistance; // orthographicSize = afstand * 0.5, dus de hoogte is de afstand
    }

    /// <summary>
    /// Begrenst de pivot zo dat het zichtveld binnen de map blijft.
    /// Bij een overschrijding veert de camera zacht terug in plaats van hard te stoppen.
    /// </summary>
    private void ClampToBounds(float dt)
    {
        float halfHeight = ViewHeight() * 0.5f;
        float halfWidth = halfHeight * rtsCamera.aspect;
        float halfDepth = halfHeight / Mathf.Sin(pitch * Mathf.Deg2Rad);

        Vector3 p = transform.position;
        Vector3 clamped = new Vector3(
            ClampAxis(p.x, mapBounds.xMin, mapBounds.xMax, halfWidth),
            p.y,
            ClampAxis(p.z, mapBounds.yMin, mapBounds.yMax, halfDepth));

        if (clamped == p) return;

        float k = 1f - Mathf.Exp(-boundsSpring * dt);
        transform.position = Vector3.Lerp(p, clamped, k);
        panVelocity *= Mathf.Exp(-boundsSpring * dt);
    }

    private static float ClampAxis(float value, float min, float max, float margin)
    {
        float lo = min + margin;
        float hi = max - margin;

        // Het zicht is groter dan de map: centreer.
        if (lo > hi) return (min + max) * 0.5f;
        return Mathf.Clamp(value, lo, hi);
    }

    private void ApplyProjection()
    {
        rtsCamera.orthographic = projection == ProjectionMode.Orthographic;
        rtsCamera.fieldOfView = lensFov;
    }
}
