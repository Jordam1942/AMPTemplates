using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

/// <summary>Zichtbaarheid van een wereldpunt voor het lokale team.</summary>
public enum FogState : byte
{
    /// <summary>Nooit verkend. Volledig zwart.</summary>
    Unexplored = FogStates.Unexplored,

    /// <summary>Verkend maar geen actief zicht. Terrein en gebouwen zichtbaar, units verborgen.</summary>
    Explored = FogStates.Explored,

    /// <summary>Actief zicht. Volledig verlicht.</summary>
    Visible = FogStates.Visible,
}

/// <summary>
/// Lichtgewicht fog of war op een laag-resolutie grid (standaard 128x128 cellen).
/// Units en gebouwen van het lokale team onthullen cellen binnen hun sightRange.
/// De berekening draait in Burst-jobs. Het resultaat wordt elke updateInterval bijgewerkt, niet per frame.
/// </summary>
[DisallowMultipleComponent]
public sealed class FogOfWar : MonoBehaviour
{
    public static FogOfWar Instance { get; private set; }

    [SerializeField] private MobileRTSCamera rtsCamera;

    [Tooltip("Aantal cellen langs de langste as. 128 is een goede balans voor mobiel.")]
    [SerializeField, Range(32, 256)] private int resolution = 128;

    [SerializeField, Min(0.02f)] private float updateInterval = 0.1f;
    [SerializeField, Min(16)] private int maxViewers = 512;

    private Rect bounds;
    private int width;
    private int height;
    private float cellSize;
    private NativeArray<byte> states;
    private NativeArray<float3> viewers;
    private NativeArray<Color32> pixels;
    private float timer;

    public int Width => width;
    public int Height => height;
    public bool IsReady => states.IsCreated;

    /// <summary>Gekleurde cellen, na elke update. Lees alleen, en alleen na de update van dit frame.</summary>
    public NativeArray<Color32> Pixels => pixels;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        bounds = rtsCamera != null ? rtsCamera.MapBounds : new Rect(-100f, -100f, 200f, 200f);

        // Vierkante cellen. De langste as heeft precies 'resolution' cellen.
        cellSize = Mathf.Max(bounds.width, bounds.height) / resolution;
        width = Mathf.CeilToInt(bounds.width / cellSize);
        height = Mathf.CeilToInt(bounds.height / cellSize);

        int cells = width * height;
        states = new NativeArray<byte>(cells, Allocator.Persistent, NativeArrayOptions.ClearMemory);
        pixels = new NativeArray<Color32>(cells, Allocator.Persistent, NativeArrayOptions.ClearMemory);
        viewers = new NativeArray<float3>(maxViewers, Allocator.Persistent, NativeArrayOptions.ClearMemory);

        // Beginstaat: volledig onverkend.
        timer = 0f;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;

        if (states.IsCreated) states.Dispose();
        if (pixels.IsCreated) pixels.Dispose();
        if (viewers.IsCreated) viewers.Dispose();
    }

    private void Update()
    {
        if (!IsReady) return;

        timer -= Time.deltaTime;
        if (timer > 0f) return;

        timer = updateInterval;
        Refresh();
        ApplyRenderTargets();
    }

    // ---------- Publieke API ----------

    public FogState GetState(Vector3 worldPosition)
    {
        // Zonder fog-grid blijft alles zichtbaar. Dan verbergt niets de wereld.
        if (!IsReady) return FogState.Visible;

        if (!TryWorldToCell(worldPosition, out int x, out int y)) return FogState.Unexplored;
        return (FogState)states[y * width + x];
    }

    public bool IsVisible(Vector3 worldPosition) => GetState(worldPosition) == FogState.Visible;

    /// <summary>Wereldpositie naar een UV binnen de speelkaart, van (0,0) tot (1,1).</summary>
    public Vector2 WorldToUV(Vector3 p) =>
        new Vector2((p.x - bounds.xMin) / bounds.width, (p.z - bounds.yMin) / bounds.height);

    /// <summary>UV binnen de speelkaart naar een wereldpunt op het grondvlak.</summary>
    public Vector3 UVToWorld(Vector2 uv) =>
        new Vector3(bounds.xMin + uv.x * bounds.width, 0f, bounds.yMin + uv.y * bounds.height);

    private bool TryWorldToCell(Vector3 p, out int x, out int y)
    {
        x = Mathf.FloorToInt((p.x - bounds.xMin) / cellSize);
        y = Mathf.FloorToInt((p.z - bounds.yMin) / cellSize);
        return x >= 0 && x < width && y >= 0 && y < height;
    }

    // ---------- Updates ----------

    private void Refresh()
    {
        int count = CollectViewers();

        new RevealJob
        {
            viewers = viewers,
            viewerCount = count,
            width = width,
            states = states,
        }.Schedule(height, 4).Complete();

        new ColorizeJob
        {
            states = states,
            pixels = pixels,
        }.Schedule(states.Length, 256).Complete();
    }

    /// <summary>Verzamelt zichtbronnen van het lokale team: operationele gebouwen en levende units.</summary>
    private int CollectViewers()
    {
        int team = LocalTeam();
        int count = 0;

        IReadOnlyList<UnitController> units = UnitController.Registry;
        for (int i = 0; i < units.Count; i++)
        {
            UnitController u = units[i];
            if (u.Team != team || !u.IsAlive || u.Data == null) continue;
            AddViewer(u.Position, u.Data.sightRange, ref count);
        }

        IReadOnlyList<BuildingController> buildings = BuildingController.Registry;
        for (int i = 0; i < buildings.Count; i++)
        {
            BuildingController b = buildings[i];
            if (b.Team != team || !b.IsOperational) continue;
            AddViewer(b.Position, b.Data.sightRange, ref count);
        }

        // Tijdelijke bronnen, bijvoorbeeld een spy drone. Verlopen bronnen worden eerst opgeruimd.
        TemporaryVision.Prune();
        IReadOnlyList<TemporaryVision.VisionSource> temporary = TemporaryVision.Active;
        for (int i = 0; i < temporary.Count; i++)
        {
            if (temporary[i].team != team) continue;
            AddViewer(temporary[i].position, temporary[i].radius, ref count);
        }

        return count;
    }

    private void AddViewer(Vector3 worldPosition, float sightRange, ref int count)
    {
        if (count >= viewers.Length) return;

        float x = (worldPosition.x - bounds.xMin) / cellSize;
        float y = (worldPosition.z - bounds.yMin) / cellSize;
        viewers[count++] = new float3(x, y, sightRange / cellSize);
    }

    /// <summary>
    /// Stuurt de renderers van vijandelijke objecten aan. Units zijn alleen zichtbaar in actief zicht.
    /// Gebouwen zijn zichtbaar zodra ze verkend zijn. Eigen objecten staan altijd aan.
    /// </summary>
    private void ApplyRenderTargets()
    {
        int team = LocalTeam();

        IReadOnlyList<UnitController> units = UnitController.Registry;
        for (int i = 0; i < units.Count; i++)
        {
            UnitController u = units[i];
            if (!u.TryGetComponent(out FogRenderTarget target)) continue;

            bool show = u.Team == team || GetState(u.Position) == FogState.Visible;
            target.SetRevealed(show);
        }

        IReadOnlyList<BuildingController> buildings = BuildingController.Registry;
        for (int i = 0; i < buildings.Count; i++)
        {
            BuildingController b = buildings[i];
            if (!b.TryGetComponent(out FogRenderTarget target)) continue;

            bool show = b.Team == team || GetState(b.Position) != FogState.Unexplored;
            target.SetRevealed(show);
        }
    }

    private static int LocalTeam() =>
        PlayerEconomy.Instance != null ? PlayerEconomy.Instance.TeamId : UnitController.LocalPlayerTeam;
}
