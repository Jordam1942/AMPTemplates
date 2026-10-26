using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

public enum PlacementResult { Valid, Unaffordable, NotWalkable, Blocked }

/// <summary>
/// Bouwplaatsing. Een ghost volgt de vinger op het grondvlak, snapt op het grid en kleurt
/// groen (vrij en begaanbaar) of rood (geblokkeerd, niet begaanbaar of onbetaalbaar).
/// Loslaten commit de plaatsing: kosten worden afgeschreven, het gebouw verschijnt in opbouwstand
/// en de eerstvolgende vrije bouwer krijgt de opdracht.
/// </summary>
[DisallowMultipleComponent]
public sealed class ConstructionSystem : MonoBehaviour
{
    public static ConstructionSystem Instance { get; private set; }

    /// <summary>True zolang een gebouw geplaatst wordt. Andere input-systemen moeten dan wijken.</summary>
    public static bool IsPlacing => Instance != null && Instance.placingData != null;

    // URP Lit gebruikt _BaseColor. Voor de Built-in pipeline is dit _Color.
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly Color ValidColor = new Color(0.2f, 1f, 0.3f, 0.6f);
    private static readonly Color InvalidColor = new Color(1f, 0.2f, 0.2f, 0.6f);

    private const int SampleGrid = 3;
    private const float NavMeshSampleDistance = 0.5f;
    private const float MaxFlatnessError = 0.5f;

    [Header("References")]
    [SerializeField] private Camera worldCamera;
    [SerializeField] private MobileRTSCamera rtsCamera;

    [Header("Layers")]
    [SerializeField] private LayerMask groundMask;

    [Tooltip("Gebouwen en statische obstakels. Units horen hier niet bij; die worden later weggeduwd.")]
    [SerializeField] private LayerMask blockingMask;

    [Header("Grid")]
    [SerializeField, Min(0.25f)] private float gridSize = 1f;

    [Header("Bouwers")]
    [SerializeField, Min(0.1f)] private float assignInterval = 0.5f;

    [Header("Ghost")]
    [SerializeField, Min(0.05f)] private float ghostHeight = 0.2f;

    private BuildingDataSO placingData;
    private int rotationSteps;
    private Finger placingFinger;
    private Transform ghostTransform;
    private Renderer ghostRenderer;
    private readonly MaterialPropertyBlock ghostBlock = new MaterialPropertyBlock();
    private readonly Collider[] overlapBuffer = new Collider[32];
    private Vector3 snappedPosition;
    private PlacementResult lastResult = PlacementResult.Blocked;
    private bool ghostDirty = true;
    private float assignTimer;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        if (worldCamera == null) worldCamera = Camera.main;
        CreateGhost();
    }

    private void OnEnable()
    {
        Touch.onFingerDown += OnFingerDown;
        Touch.onFingerUp += OnFingerUp;
    }

    private void OnDisable()
    {
        Touch.onFingerDown -= OnFingerDown;
        Touch.onFingerUp -= OnFingerUp;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (ghostTransform != null) Destroy(ghostTransform.gameObject);
    }

    private void Update()
    {
        if (placingData != null && placingFinger != null
            && TryGroundPoint(placingFinger.currentTouch.screenPosition, out Vector3 point))
        {
            RefreshPlacement(point);
        }

        assignTimer -= Time.deltaTime;
        if (assignTimer <= 0f)
        {
            assignTimer = assignInterval;
            AssignBuilders();
        }
    }

    // ---------- Publieke API (aangeroepen door UI) ----------

    /// <summary>Start plaatsingsmodus voor het gebouw. Een volgende vingerdruk plaatst het.</summary>
    public void BeginPlacement(BuildingDataSO data)
    {
        if (data == null || data.prefab == null) return;

        placingData = data;
        rotationSteps = 0;
        ghostTransform.rotation = Quaternion.identity;
        ghostTransform.localScale = new Vector3(data.footprint.x, ghostHeight, data.footprint.y);
        ghostTransform.gameObject.SetActive(true);
        ghostDirty = true;
    }

    public void CancelPlacement()
    {
        if (placingData == null) return;

        if (placingFinger != null)
        {
            placingFinger = null;
            if (rtsCamera != null) rtsCamera.InputLocked = false;
        }

        ClearPlacingState();
    }

    /// <summary>Draait de ghost 90 graden.</summary>
    public void RotateGhost()
    {
        if (placingData == null) return;

        rotationSteps = (rotationSteps + 1) & 3;
        ghostTransform.rotation = Quaternion.Euler(0f, rotationSteps * 90f, 0f);
        ghostDirty = true;
    }

    // ---------- Touch ----------

    private void OnFingerDown(Finger finger)
    {
        // Eén vinger plaatst. Een tweede vinger hoort bij de camera.
        if (placingData == null || placingFinger != null || Touch.activeTouches.Count > 1) return;
        if (UIInputGuard.IsOverUI(finger)) return;

        placingFinger = finger;
        ghostDirty = true;
        if (rtsCamera != null) rtsCamera.InputLocked = true;
    }

    private void OnFingerUp(Finger finger)
    {
        if (finger != placingFinger) return;

        // Ververs met de laatste vingerpositie. Een snelle tik kan Update overslaan.
        if (TryGroundPoint(finger.currentTouch.screenPosition, out Vector3 point))
            RefreshPlacement(point);

        placingFinger = null;
        if (rtsCamera != null) rtsCamera.InputLocked = false;

        if (lastResult == PlacementResult.Valid && Commit(snappedPosition, ghostTransform.rotation))
            ClearPlacingState();
    }

    // ---------- Ghost en validatie ----------

    private void RefreshPlacement(Vector3 groundPoint)
    {
        Vector3 snapped = SnapToGrid(groundPoint);
        if (!ghostDirty && snapped == snappedPosition) return;

        snappedPosition = snapped;
        ghostDirty = false;

        ghostTransform.position = snapped + Vector3.up * (ghostHeight * 0.5f);
        lastResult = Evaluate(snapped, ghostTransform.rotation);

        ghostBlock.SetColor(BaseColorId, lastResult == PlacementResult.Valid ? ValidColor : InvalidColor);
        ghostRenderer.SetPropertyBlock(ghostBlock);
    }

    /// <summary>
    /// Controleert kosten, begaanbaarheid op de NavMesh en overlap met blokkerende colliders.
    /// Begaanbaarheid wordt bemonsterd op een raster over de voetafdruk, niet alleen in het midden.
    /// </summary>
    private PlacementResult Evaluate(Vector3 center, Quaternion rotation)
    {
        PlayerEconomy economy = PlayerEconomy.Instance;
        if (economy == null || !economy.CanAfford(placingData.cost)) return PlacementResult.Unaffordable;

        Vector2 half = placingData.footprint * 0.5f;

        for (int ix = 0; ix < SampleGrid; ix++)
        {
            for (int iz = 0; iz < SampleGrid; iz++)
            {
                float tx = ix / (float)(SampleGrid - 1);
                float tz = iz / (float)(SampleGrid - 1);
                Vector3 local = new Vector3(Mathf.Lerp(-half.x, half.x, tx), 0f, Mathf.Lerp(-half.y, half.y, tz));
                Vector3 sample = center + rotation * local;

                if (!NavMesh.SamplePosition(sample, out NavMeshHit hit, NavMeshSampleDistance, NavMesh.AllAreas))
                    return PlacementResult.NotWalkable;

                // Vlakte: een punt op een andere hoogte betekent een helling of een trede.
                if (Mathf.Abs(hit.position.y - center.y) > MaxFlatnessError)
                    return PlacementResult.NotWalkable;
            }
        }

        // Overlap-box iets kleiner dan de voetafdruk, zodat aangrenzend bouwen mogelijk blijft.
        Vector3 boxCenter = center + Vector3.up;
        Vector3 boxHalf = new Vector3(half.x * 0.95f, 1f, half.y * 0.95f);
        int hits = Physics.OverlapBoxNonAlloc(
            boxCenter, boxHalf, overlapBuffer, rotation, blockingMask.value, QueryTriggerInteraction.Ignore);

        return hits > 0 ? PlacementResult.Blocked : PlacementResult.Valid;
    }

    private Vector3 SnapToGrid(Vector3 p)
    {
        // Snappen gebeurt op de werkelijke wereldafmetingen, dus na rotatie zijn X en Z gewisseld.
        bool swapped = (rotationSteps & 1) == 1;
        Vector2 fp = swapped
            ? new Vector2(placingData.footprint.y, placingData.footprint.x)
            : placingData.footprint;

        return new Vector3(SnapAxis(p.x, fp.x), p.y, SnapAxis(p.z, fp.y));
    }

    private float SnapAxis(float value, float footprintSize)
    {
        int cells = Mathf.RoundToInt(footprintSize / gridSize);

        // Oneven aantal cellen: het midden ligt in een cel. Even aantal: het midden ligt op een gridlijn.
        float offset = (cells & 1) == 1 ? gridSize * 0.5f : 0f;
        return Mathf.Round((value - offset) / gridSize) * gridSize + offset;
    }

    private bool TryGroundPoint(Vector2 screenPos, out Vector3 point)
    {
        Ray ray = worldCamera.ScreenPointToRay(screenPos);
        bool hit = Physics.Raycast(ray, out RaycastHit info, 1000f, groundMask, QueryTriggerInteraction.Ignore);
        point = hit ? info.point : Vector3.zero;
        return hit;
    }

    // ---------- Commit en toewijzing ----------

    private bool Commit(Vector3 position, Quaternion rotation)
    {
        PlayerEconomy economy = PlayerEconomy.Instance;
        if (economy == null || !economy.TrySpend(placingData.cost)) return false;

        GameObject go = Instantiate(placingData.prefab, position, rotation);
        if (!go.TryGetComponent(out BuildingController site))
        {
            Destroy(go);
            economy.Refund(placingData.cost);
            return false;
        }

        // Er is geen aparte bouwwachtrij. Bouwers zoeken zelf naar gebouwen in opbouw via BuildingController.Registry.
        site.BeginConstruction(placingData, economy.TeamId);
        return true;
    }

    /// <summary>
    /// Wijst vrije bouwers toe aan gebouwen in opbouw. Door de registry te doorlopen, herpakt een
    /// gebouw automatisch een bouwer als de vorige is gesneuveld.
    /// </summary>
    private void AssignBuilders()
    {
        IReadOnlyList<BuildingController> sites = BuildingController.Registry;

        for (int i = 0; i < sites.Count; i++)
        {
            BuildingController site = sites[i];
            if (!site.IsUnderConstruction || site.HasBuilder || !site.IsAlive) continue;

            Builder builder = Builder.FindNearestIdle(site.Team, site.Position);
            if (builder != null) builder.Assign(site);
        }
    }

    // ---------- Ghost-opbouw en opruimen ----------

    private void CreateGhost()
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "PlacementGhost";

        // De ghost mag geen raycasts of overlaps blokkeren.
        Destroy(go.GetComponent<Collider>());

        ghostTransform = go.transform;
        ghostRenderer = go.GetComponent<Renderer>();
        go.SetActive(false);
    }

    private void ClearPlacingState()
    {
        placingData = null;
        ghostTransform.gameObject.SetActive(false);
    }
}
