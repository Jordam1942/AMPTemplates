using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Minimap op een RawImage in het minimap-kader van de TopBarUI.
/// Tekent per update op één texture: de fog of war als basis, daarna docks en gebouwen, units en
/// de camera-frustum. Tikken of slepen verplaatst de camera naar het aangewezen punt.
/// De texture-resolutie is gelijk aan de fog-grid, zodat elke cel precies één pixel is.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(RawImage))]
public sealed class MinimapSystem : MonoBehaviour, IPointerDownHandler, IDragHandler
{
    private static readonly Vector2[] ViewportCorners =
    {
        new Vector2(0f, 0f),
        new Vector2(1f, 0f),
        new Vector2(1f, 1f),
        new Vector2(0f, 1f),
    };

    [SerializeField] private FogOfWar fog;
    [SerializeField] private MobileRTSCamera rtsCamera;
    [SerializeField] private Camera worldCamera;

    [SerializeField, Min(0.02f)] private float refreshInterval = 0.1f;

    [Header("Kleuren")]
    [SerializeField] private Color friendlyUnitColor = new Color(0.3f, 1f, 0.35f);
    [SerializeField] private Color enemyUnitColor = new Color(1f, 0.25f, 0.25f);
    [SerializeField] private Color friendlyBuildingColor = new Color(0.6f, 1f, 0.7f);
    [SerializeField] private Color enemyBuildingColor = new Color(1f, 0.5f, 0.4f);
    [SerializeField] private Color supplyDockColor = new Color(1f, 0.85f, 0.2f);
    [SerializeField] private Color frustumColor = Color.white;

    private RawImage image;
    private RectTransform rectTransform;
    private Texture2D texture;
    private Color32[] buffer;
    private float timer;
    private readonly Plane groundPlane = new Plane(Vector3.up, Vector3.zero);
    private readonly Vector2[] frustumPixels = new Vector2[4];

    private void Awake()
    {
        image = GetComponent<RawImage>();
        rectTransform = (RectTransform)transform;
        if (worldCamera == null) worldCamera = Camera.main;
    }

    private void Start()
    {
        if (fog == null) fog = FogOfWar.Instance;
        if (fog == null || !fog.IsReady)
        {
            enabled = false;
            return;
        }

        int w = fog.Width;
        int h = fog.Height;
        texture = new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
        };

        // Eén buffer voor het hele spel. Geen allocatie per update.
        buffer = new Color32[w * h];
        image.texture = texture;
    }

    private void OnDestroy()
    {
        if (texture != null) Destroy(texture);
    }

    private void Update()
    {
        timer -= Time.unscaledDeltaTime;
        if (timer > 0f || texture == null) return;

        timer = refreshInterval;
        Rebuild();
    }

    // ---------- Opbouw van de minimap ----------

    private void Rebuild()
    {
        // 1. Basis: de fog-grid, al gekleurd door de ColorizeJob.
        fog.Pixels.CopyTo(buffer);

        int team = PlayerEconomy.Instance != null ? PlayerEconomy.Instance.TeamId : UnitController.LocalPlayerTeam;

        // 2. Structuren eerst, zodat units erbovenop komen.
        IReadOnlyList<SupplyDock> docks = SupplyDock.Registry;
        for (int i = 0; i < docks.Count; i++)
        {
            SupplyDock dock = docks[i];
            if (!dock.IsDepleted && fog.GetState(dock.Position) != FogState.Unexplored)
                DrawDot(dock.Position, 3, supplyDockColor);
        }

        IReadOnlyList<BuildingController> buildings = BuildingController.Registry;
        for (int i = 0; i < buildings.Count; i++)
        {
            BuildingController b = buildings[i];
            if (!b.IsAlive) continue;

            if (b.Team == team)
                DrawDot(b.Position, 3, friendlyBuildingColor);
            else if (fog.GetState(b.Position) != FogState.Unexplored)
                DrawDot(b.Position, 3, enemyBuildingColor);
        }

        // 3. Units: vijanden alleen in actief zicht.
        IReadOnlyList<UnitController> units = UnitController.Registry;
        for (int i = 0; i < units.Count; i++)
        {
            UnitController u = units[i];
            if (!u.IsAlive) continue;

            if (u.Team == team)
                DrawDot(u.Position, 2, friendlyUnitColor);
            else if (fog.IsVisible(u.Position))
                DrawDot(u.Position, 2, enemyUnitColor);
        }

        // 4. Camera-kader.
        DrawFrustum();

        texture.SetPixels32(buffer);
        texture.Apply(false);
    }

    private void DrawDot(Vector3 world, int size, Color color)
    {
        Vector2 uv = fog.WorldToUV(world);
        int cx = Mathf.FloorToInt(uv.x * fog.Width);
        int cy = Mathf.FloorToInt(uv.y * fog.Height);
        int half = size / 2;

        for (int dy = -half; dy < size - half; dy++)
        {
            for (int dx = -half; dx < size - half; dx++)
                SetPixel(cx + dx, cy + dy, color);
        }
    }

    /// <summary>
    /// Tekent het zichtveld als vier lijnen op de grond. Het snijpunt van elke hoek met het grondvlak
    /// bepaalt de hoeken. Hoeken die boven de horizon uitkomen, worden overgeslagen.
    /// </summary>
    private void DrawFrustum()
    {
        if (worldCamera == null) return;

        for (int i = 0; i < 4; i++)
        {
            Ray ray = worldCamera.ViewportPointToRay(ViewportCorners[i]);
            if (!groundPlane.Raycast(ray, out float enter)) return;

            Vector2 uv = fog.WorldToUV(ray.GetPoint(enter));
            frustumPixels[i] = new Vector2(uv.x * fog.Width, uv.y * fog.Height);
        }

        for (int i = 0; i < 4; i++)
            DrawLine(frustumPixels[i], frustumPixels[(i + 1) % 4], frustumColor);
    }

    private void DrawLine(Vector2 a, Vector2 b, Color color)
    {
        int steps = Mathf.CeilToInt(Vector2.Distance(a, b));
        if (steps <= 0) return;

        for (int s = 0; s <= steps; s++)
        {
            Vector2 p = Vector2.Lerp(a, b, s / (float)steps);
            SetPixel(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y), color);
        }
    }

    private void SetPixel(int x, int y, Color color)
    {
        if (x < 0 || y < 0 || x >= fog.Width || y >= fog.Height) return;

        Color32 c = color;
        buffer[y * fog.Width + x] = c;
    }

    // ---------- Input ----------

    public void OnPointerDown(PointerEventData eventData) => MoveCameraTo(eventData);

    public void OnDrag(PointerEventData eventData) => MoveCameraTo(eventData);

    private void MoveCameraTo(PointerEventData eventData)
    {
        if (rtsCamera == null || fog == null || !fog.IsReady) return;

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                rectTransform, eventData.position, eventData.pressEventCamera, out Vector2 local))
            return;

        Rect r = rectTransform.rect;
        Vector2 uv = new Vector2((local.x - r.xMin) / r.width, (local.y - r.yMin) / r.height);
        uv = Vector2.Min(Vector2.one, Vector2.Max(Vector2.zero, uv));

        rtsCamera.SetPivot(fog.UVToWorld(uv));
    }
}
