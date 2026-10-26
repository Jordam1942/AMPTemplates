using System.Collections.Generic;
using TMPro;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>
/// Bouwt met één menu-item de volledige skirmish-scène op: camera-rig, grond met NavMesh, licht,
/// EventSystem, HUD-canvas, fog of war, systemen, economieën, AI en de startbases.
/// Menu: Tools > Generals > Setup Skirmish Scene.
/// Prefabs en data komen uit SkirmishSetupConfig. Ontbrekende onderdelen krijgen een waarschuwing.
/// </summary>
public static class EditorSceneSetup
{
    private const string ScenesFolder = "Assets/Scenes";
    private const string ScenePath = "Assets/Scenes/SkirmishTest.unity";
    private const string ConfigPath = "Assets/Settings/SkirmishSetupConfig.asset";
    private const float MapSize = 200f;

    private static readonly Color PanelColor = new Color(0.05f, 0.07f, 0.09f, 0.85f);
    private static readonly Color ButtonColor = new Color(0.16f, 0.2f, 0.24f, 0.95f);
    private static readonly Color SlotColor = new Color(0.12f, 0.15f, 0.18f, 0.95f);

    [MenuItem("Tools/Generals/Setup Skirmish Scene")]
    public static void SetupSkirmishScene()
    {
        EnsureFolder("Assets", "Scenes");
        EnsureFolder("Assets", "Settings");

        int groundLayer = EnsureLayer("Ground");
        int unitsLayer = EnsureLayer("Units");
        int buildingsLayer = EnsureLayer("Buildings");

        int groundMask = 1 << groundLayer;
        int selectableMask = (1 << unitsLayer) | (1 << buildingsLayer);
        int blockingMask = 1 << buildingsLayer;

        // Het grafische profiel moet bestaan voordat het volume-profiel wordt gekoppeld.
        if (AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.VolumeProfile>(PixelGraphicsProfile.VolumeProfilePath) == null)
            PixelGraphicsProfile.Apply();

        SkirmishSetupConfig config = LoadOrCreateConfig();
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        GameObject ground = CreateGround(groundLayer);
        CreateLight();
        CreateVolume();

        MobileRTSCamera rig = CreateCameraRig();
        CreateFogOfWar(rig);
        CreateEventSystem();

        // Systemen: gedeeld door speler en HUD.
        GameObject systems = new GameObject("Systems");
        systems.AddComponent<AudioSource>();
        AudioManager audio = systems.AddComponent<AudioManager>();
        ConstructionSystem construction = systems.AddComponent<ConstructionSystem>();
        TouchSelectionHandler selection = systems.AddComponent<TouchSelectionHandler>();
        TargetPicker picker = systems.AddComponent<TargetPicker>();
        systems.AddComponent<ChinaHordeSystem>();

        Camera cam = rig.GetComponentInChildren<Camera>();
        SetRef(construction, "worldCamera", cam);
        SetRef(construction, "rtsCamera", rig);
        SetInt(construction, "groundMask", groundMask);
        SetInt(construction, "blockingMask", blockingMask);

        SetRef(selection, "worldCamera", cam);
        SetRef(selection, "rtsCamera", rig);
        SetInt(selection, "selectableMask", selectableMask);
        SetInt(selection, "groundMask", groundMask);

        SetRef(picker, "worldCamera", cam);
        SetInt(picker, "groundMask", groundMask);

        // Economieën en generaal: één per team.
        CreateEconomy("Economy_Player", 0, 10000);
        CreateEconomy("Economy_AI", 1, 10000);

        GameObject generals = new GameObject("Generals");
        GeneralRankManager ranks = generals.AddComponent<GeneralRankManager>();
        GeneralPowerManager powers = generals.AddComponent<GeneralPowerManager>();
        SetRef(powers, "rankManager", ranks);

        // HUD onder een canvas met SafeArea. De koppelingen gaan naar de managers hierboven.
        BuildHud(config, rig, powers, ranks);

        // Startbases en AI.
        BuildBases(config, out BuildingController aiCommandCenter, out Transform aiRally);
        CreateAi(config, aiCommandCenter, aiRally, groundMask, blockingMask);

        // NavMesh bakken na het plaatsen van alle obstakels. Anders ontbreken er routes.
        ground.GetComponent<NavMeshSurface>().BuildNavMesh();

        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        AssetDatabase.SaveAssets();

        Debug.Log($"Skirmish-scène opgebouwd: {ScenePath}. Speler, AI en HUD staan klaar.");
    }

    // ---------- Wereld ----------

    private static GameObject CreateGround(int layer)
    {
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.layer = layer;

        // Een standaard-plane is 10 x 10 m. Schaal naar de kaartgrootte.
        ground.transform.localScale = new Vector3(MapSize / 10f, 1f, MapSize / 10f);

        NavMeshSurface surface = ground.AddComponent<NavMeshSurface>();
        surface.collectObjects = CollectObjects.All;
        return ground;
    }

    private static void CreateLight()
    {
        GameObject go = new GameObject("Directional Light");
        Light light = go.AddComponent<Light>();
        light.type = LightType.Directional;
        light.shadows = LightShadows.Soft;
        go.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
    }

    private static void CreateVolume()
    {
        GameObject go = new GameObject("GlobalVolume");
        Volume volume = go.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(PixelGraphicsProfile.VolumeProfilePath);
    }

    private static MobileRTSCamera CreateCameraRig()
    {
        GameObject rig = new GameObject("CameraRig");
        MobileRTSCamera rts = rig.AddComponent<MobileRTSCamera>();

        GameObject camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        camGo.transform.SetParent(rig.transform, false);

        Camera cam = camGo.AddComponent<Camera>();
        cam.nearClipPlane = 0.3f;
        cam.farClipPlane = 300f;
        camGo.AddComponent<AudioListener>();

        // Anti-aliasing per camera: FXAA, zie PixelGraphicsProfile.md.
        UniversalAdditionalCameraData camData = camGo.AddComponent<UniversalAdditionalCameraData>();
        camData.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
        camData.renderPostProcessing = true;

        SetRef(rts, "rtsCamera", cam);
        SetRect(rts, "mapBounds", new Rect(-MapSize * 0.5f, -MapSize * 0.5f, MapSize, MapSize));
        return rts;
    }

    private static void CreateFogOfWar(MobileRTSCamera rig)
    {
        GameObject go = new GameObject("FogOfWar");
        FogOfWar fog = go.AddComponent<FogOfWar>();
        SetRef(fog, "rtsCamera", rig);
    }

    private static void CreateEventSystem()
    {
        GameObject go = new GameObject("EventSystem");
        go.AddComponent<EventSystem>();
        go.AddComponent<InputSystemUIInputModule>();
    }

    private static void CreateEconomy(string name, int team, int startingMoney)
    {
        GameObject go = new GameObject(name);
        PlayerEconomy economy = go.AddComponent<PlayerEconomy>();
        SetInt(economy, "teamId", team);
        SetInt(economy, "startingMoney", startingMoney);
    }

    // ---------- Bases en AI ----------

    private static void BuildBases(SkirmishSetupConfig config, out BuildingController aiCommandCenter, out Transform aiRally)
    {
        SpawnBase(config.playerCommandCenterPrefab, config.playerBuilderPrefab, config.playerGathererPrefab,
            config.playerBasePosition, 0, "Player");
        SpawnBase(config.aiCommandCenterPrefab, config.aiBuilderPrefab, config.aiGathererPrefab,
            config.aiBasePosition, 1, "AI");

        for (int i = 0; i < config.dockPositions.Length; i++)
        {
            GameObject dock = config.supplyDockPrefab != null
                ? PrefabUtility.InstantiatePrefab(config.supplyDockPrefab) as GameObject
                : CreateDefaultDock();

            dock.name = $"SupplyDock_{i}";
            dock.transform.position = config.dockPositions[i];
        }

        GameObject aiCc = FindCommandCenter(config.aiBasePosition);
        aiCommandCenter = aiCc != null ? aiCc.GetComponent<BuildingController>() : null;

        GameObject rally = new GameObject("AIRally");
        rally.transform.position = config.aiBasePosition;
        aiRally = rally.transform;
    }

    private static void SpawnBase(GameObject commandCenterPrefab, GameObject builderPrefab, GameObject gathererPrefab,
        Vector3 basePos, int team, string label)
    {
        if (commandCenterPrefab == null)
        {
            Debug.LogWarning($"{label}: geen Command Center-prefab in SkirmishSetupConfig. De basis wordt overgeslagen.");
            return;
        }

        GameObject cc = PrefabUtility.InstantiatePrefab(commandCenterPrefab) as GameObject;
        cc.name = $"CommandCenter_{label}";
        cc.transform.position = basePos;

        // Scène-gebouwen moeten direct voltooid zijn. Het prefab-veld startsBuilt staat daarvoor op true.
        SetBool(cc.GetComponent<BuildingController>(), "startsBuilt", true);
        SetInt(cc.GetComponent<BuildingController>(), "team", team);

        SpawnUnit(builderPrefab, basePos + new Vector3(6f, 0f, 0f), team, $"Builder_{label}", label);
        SpawnUnit(gathererPrefab, basePos + new Vector3(-6f, 0f, 0f), team, $"Gatherer_{label}", label);
    }

    private static void SpawnUnit(GameObject prefab, Vector3 position, int team, string name, string label)
    {
        if (prefab == null)
        {
            Debug.LogWarning($"{label}: geen prefab voor {name}. Die unit wordt overgeslagen.");
            return;
        }

        GameObject go = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
        go.name = name;
        go.transform.position = position;
        SetInt(go.GetComponent<UnitController>(), "team", team);
    }

    private static GameObject FindCommandCenter(Vector3 basePos)
    {
        foreach (BuildingController b in Object.FindObjectsByType<BuildingController>(FindObjectsSortMode.None))
        {
            if ((b.transform.position - basePos).sqrMagnitude < 0.01f) return b.gameObject;
        }

        return null;
    }

    private static GameObject CreateDefaultDock()
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.transform.localScale = new Vector3(3f, 0.2f, 3f);
        go.AddComponent<SupplyDock>();
        return go;
    }

    private static void CreateAi(SkirmishSetupConfig config, BuildingController aiCommandCenter, Transform rally,
        int groundMask, int blockingMask)
    {
        GameObject ai = new GameObject("SkirmishAI");
        SkirmishAIController controller = ai.AddComponent<SkirmishAIController>();
        AIBuildPlanner planner = ai.AddComponent<AIBuildPlanner>();
        AIAttackManager attack = ai.AddComponent<AIAttackManager>();

        SetInt(controller, "team", 1);
        SetRef(controller, "commandCenter", aiCommandCenter);
        SetRef(controller, "buildPlanner", planner);
        SetRef(controller, "attackManager", attack);

        SetRef(planner, "powerPlantData", config.powerPlantData);
        SetRef(planner, "supplyCenterData", config.supplyCenterData);
        SetRef(planner, "barracksData", config.barracksData);
        SetRef(planner, "warFactoryData", config.warFactoryData);
        SetInt(planner, "groundMask", groundMask);
        SetInt(planner, "blockingMask", blockingMask);

        SetRef(attack, "rallyPoint", rally);
    }

    // ---------- HUD ----------

    private static void BuildHud(SkirmishSetupConfig config, MobileRTSCamera rig, GeneralPowerManager powers, GeneralRankManager ranks)
    {
        GameObject canvasGo = new GameObject("HUD", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080f, 2400f);
        scaler.matchWidthOrHeight = 0.5f;

        RectTransform safe = Stretch(canvasGo.transform, "SafeArea");
        safe.gameObject.AddComponent<SafeAreaFitter>();

        Camera cam = rig.GetComponentInChildren<Camera>();
        BuildTopBar(safe, cam, rig);
        BuildContextPanel(config, safe);
        BuildPowersBar(safe, powers, ranks);
    }

    private static void BuildTopBar(RectTransform safe, Camera cam, MobileRTSCamera rig)
    {
        RectTransform topBar = Anchored(safe, "TopBar", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
            Vector2.zero, new Vector2(0f, 240f));
        AddImage(topBar, PanelColor);

        TMP_Text credits = AddText(Child(topBar, "Credits", 24f, 16f, 420f, 64f), 46, "$ 0");
        TMP_Text delta = AddText(Child(topBar, "Delta", 24f, 84f, 320f, 40f), 30, string.Empty);

        RectTransform powerBg = Anchored(topBar, "PowerBg", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f), new Vector2(0f, -72f), new Vector2(380f, 22f));
        AddImage(powerBg, SlotColor);
        Image powerFill = AddImage(Stretch(powerBg, "PowerFill"), new Color(0.3f, 0.85f, 1f));
        powerFill.type = Image.Type.Filled;
        powerFill.fillMethod = Image.FillMethod.Horizontal;

        TMP_Text powerText = AddText(Anchored(topBar, "PowerText", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f), new Vector2(0f, -28f), new Vector2(380f, 36f)), 28, "0 / 0");

        TMP_Text lowPower = AddText(Anchored(topBar, "LowPower", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f), new Vector2(0f, -112f), new Vector2(380f, 36f)), 28, "LAAG VERMOGEN");
        lowPower.color = new Color(1f, 0.3f, 0.2f);

        // Minimap: rechtsboven in de balk. TopBarUI zet de anchor zelf, dus het frame moet een kind van TopBar zijn.
        RectTransform frame = Anchored(topBar, "MinimapFrame", new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(1f, 1f), new Vector2(-20f, -20f), new Vector2(200f, 200f));
        AddImage(frame, PanelColor);

        RawImage raw = Stretch(frame, "MinimapView").gameObject.AddComponent<RawImage>();
        raw.raycastTarget = true;
        MinimapSystem minimap = raw.gameObject.AddComponent<MinimapSystem>();
        SetRef(minimap, "fog", FindFog());
        SetRef(minimap, "rtsCamera", rig);
        SetRef(minimap, "worldCamera", cam);

        TopBarUI topBarUi = topBar.gameObject.AddComponent<TopBarUI>();
        SetRef(topBarUi, "creditsText", credits);
        SetRef(topBarUi, "deltaText", delta);
        SetRef(topBarUi, "powerFill", powerFill);
        SetRef(topBarUi, "powerText", powerText);
        SetRef(topBarUi, "lowPowerWarning", lowPower);
        SetRef(topBarUi, "minimapFrame", frame);
    }

    private static void BuildContextPanel(SkirmishSetupConfig config, RectTransform safe)
    {
        RectTransform panel = Anchored(safe, "ContextPanel", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
            new Vector2(-24f, 24f), new Vector2(600f, 460f));
        AddImage(panel, PanelColor);

        RectTransform buildRoot = Stretch(panel, "BuildRoot");
        RectTransform productionRoot = Stretch(panel, "ProductionRoot");
        RectTransform unitRoot = Stretch(panel, "UnitRoot");

        // Bouwmenu: 6 slots in 2 rijen van 3.
        OptionSlot[] buildSlots = new OptionSlot[6];
        for (int i = 0; i < buildSlots.Length; i++)
        {
            int col = i % 3;
            int row = i / 3;
            buildSlots[i] = MakeSlot(Child(buildRoot, $"Build_{i}", 20f + col * 190f, 20f + row * 150f, 170f, 135f));
        }

        // Productie: 5 optiesloten, 5 wachtrijslots, en de knoppen Annuleer en Rally.
        OptionSlot[] productionSlots = new OptionSlot[5];
        OptionSlot[] queueSlots = new OptionSlot[5];
        for (int i = 0; i < 5; i++)
        {
            productionSlots[i] = MakeSlot(Child(productionRoot, $"Prod_{i}", 20f + i * 115f, 20f, 105f, 105f));
            queueSlots[i] = MakeSlot(Child(productionRoot, $"Queue_{i}", 20f + i * 115f, 140f, 105f, 105f));
        }

        TouchButton cancel = MakeButton(Child(productionRoot, "Cancel", 20f, 270f, 160f, 70f), "Annuleer");
        TouchButton rally = MakeButton(Child(productionRoot, "Rally", 200f, 270f, 160f, 70f), "Rally");

        // Unit-stats: icoon, naam, aantal, HP-balk en Stop/Guard.
        Image icon = AddImage(Child(unitRoot, "Icon", 20f, 20f, 120f, 120f), Color.white);
        TMP_Text unitName = AddText(Child(unitRoot, "Name", 160f, 20f, 420f, 50f), 34, string.Empty);
        TMP_Text count = AddText(Child(unitRoot, "Count", 160f, 70f, 420f, 40f), 28, string.Empty);
        RectTransform hpBg = Child(unitRoot, "HpBg", 160f, 120f, 400f, 30f);
        AddImage(hpBg, SlotColor);
        Image hpFill = AddImage(Stretch(hpBg, "HpFill"), new Color(0.4f, 0.95f, 0.45f));
        hpFill.type = Image.Type.Filled;
        hpFill.fillMethod = Image.FillMethod.Horizontal;
        TMP_Text hpText = AddText(Child(unitRoot, "HpText", 160f, 160f, 400f, 36f), 26, string.Empty);
        TouchButton stop = MakeButton(Child(unitRoot, "Stop", 20f, 300f, 160f, 70f), "Stop");
        TouchButton guard = MakeButton(Child(unitRoot, "Guard", 200f, 300f, 160f, 70f), "Guard");

        // Alles koppelen. De componenten staan in de volgorde van het menu.
        ContextCommandPanel contextPanel = panel.gameObject.AddComponent<ContextCommandPanel>();
        SetRef(contextPanel, "buildRoot", buildRoot.gameObject);
        SetRef(contextPanel, "productionRoot", productionRoot.gameObject);
        SetRef(contextPanel, "unitRoot", unitRoot.gameObject);
        SetRefArray(contextPanel, "buildMenu", ToObjects(config.playerBuildMenu));
        SetRefArray(contextPanel, "buildSlots", ToObjects(buildSlots));
        SetRefArray(contextPanel, "productionSlots", ToObjects(productionSlots));
        SetRefArray(contextPanel, "queueSlots", ToObjects(queueSlots));
        SetRef(contextPanel, "cancelButton", cancel);
        SetRef(contextPanel, "rallyButton", rally);
        SetRef(contextPanel, "stopButton", stop);
        SetRef(contextPanel, "guardButton", guard);

        UnitStatsView stats = unitRoot.gameObject.AddComponent<UnitStatsView>();
        SetRef(stats, "icon", icon);
        SetRef(stats, "nameText", unitName);
        SetRef(stats, "countText", count);
        SetRef(stats, "hpFill", hpFill);
        SetRef(stats, "hpText", hpText);
        SetRef(contextPanel, "unitStats", stats);
    }

    private static void BuildPowersBar(RectTransform safe, GeneralPowerManager powers, GeneralRankManager ranks)
    {
        RectTransform bar = Anchored(safe, "PowersBar", new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
            new Vector2(24f, 24f), new Vector2(560f, 80f));

        TouchButton toggle = MakeButton(Bottom(bar, "Toggle", 0f, 0f, 160f, 70f), "Powers");
        TouchButton cancel = MakeButton(Bottom(bar, "Cancel", 180f, 0f, 160f, 70f), "Annuleer");
        TMP_Text rank = AddText(Bottom(bar, "Rank", 360f, 0f, 200f, 70f), 26, string.Empty);

        RectTransform row = Bottom(bar, "Row", 0f, 90f, 560f, 140f);
        AddImage(row, PanelColor);

        OptionSlot[] slots = new OptionSlot[4];
        for (int i = 0; i < slots.Length; i++)
            slots[i] = MakeSlot(Bottom(row, $"Power_{i}", 10f + i * 135f, 5f, 125f, 130f));

        PowersBarUI ui = bar.gameObject.AddComponent<PowersBarUI>();
        SetRef(ui, "powers", powers);
        SetRef(ui, "ranks", ranks);
        SetRef(ui, "expandedRow", row);
        SetRef(ui, "toggleButton", toggle);
        SetRef(ui, "cancelButton", cancel);
        SetRef(ui, "rankText", rank);
        SetRefArray(ui, "slots", ToObjects(slots));
    }

    // ---------- Widget-helpers ----------

    private static RectTransform Stretch(Transform parent, string name)
    {
        RectTransform rt = NewRect(name, parent);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        return rt;
    }

    /// <summary>Kind met linksboven als oorsprong. x en y zijn vanaf de linker- en bovenrand van de parent.</summary>
    private static RectTransform Child(Transform parent, string name, float x, float y, float w, float h)
    {
        return Anchored(parent, name, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(x, -y), new Vector2(w, h));
    }

    /// <summary>Kind met linksonder als oorsprong.</summary>
    private static RectTransform Bottom(Transform parent, string name, float x, float y, float w, float h)
    {
        return Anchored(parent, name, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(x, y), new Vector2(w, h));
    }

    private static RectTransform Anchored(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
        Vector2 pivot, Vector2 anchoredPos, Vector2 size)
    {
        RectTransform rt = NewRect(name, parent);
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;
        return rt;
    }

    private static RectTransform NewRect(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static Image AddImage(RectTransform rt, Color color)
    {
        Image image = rt.gameObject.AddComponent<Image>();
        image.color = color;
        return image;
    }

    private static TMP_Text AddText(RectTransform rt, int fontSize, string text)
    {
        TextMeshProUGUI tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;
        return tmp;
    }

    private static TouchButton MakeButton(RectTransform rt, string label)
    {
        AddImage(rt, ButtonColor);
        TouchButton button = rt.gameObject.AddComponent<TouchButton>();
        AddText(Stretch(rt, "Label"), 28, label);
        return button;
    }

    private static OptionSlot MakeSlot(RectTransform rt)
    {
        AddImage(rt, SlotColor);
        TouchButton button = rt.gameObject.AddComponent<TouchButton>();

        Image icon = AddImage(Anchored(rt, "Icon", new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, 14f), new Vector2(-20f, -28f)), Color.white);
        icon.raycastTarget = false;

        TMP_Text title = AddText(Anchored(rt, "Title", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0f, 2f), new Vector2(-8f, 24f)), 18, string.Empty);
        TMP_Text cost = AddText(Anchored(rt, "Cost", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(-4f, -4f), new Vector2(80f, 24f)), 18, string.Empty);

        Image progress = AddImage(Stretch(rt, "Progress"), new Color(0f, 0f, 0f, 0.6f));
        progress.type = Image.Type.Filled;
        progress.fillMethod = Image.FillMethod.Vertical;
        progress.raycastTarget = false;
        progress.fillAmount = 0f;

        OptionSlot slot = rt.gameObject.AddComponent<OptionSlot>();
        SetRef(slot, "button", button);
        SetRef(slot, "icon", icon);
        SetRef(slot, "titleText", title);
        SetRef(slot, "costText", cost);
        SetRef(slot, "progressFill", progress);
        return slot;
    }

    private static FogOfWar FindFog() => Object.FindFirstObjectByType<FogOfWar>();

    // ---------- Config en assets ----------

    private static SkirmishSetupConfig LoadOrCreateConfig()
    {
        EnsureFolder("Assets", "Settings");

        SkirmishSetupConfig config = AssetDatabase.LoadAssetAtPath<SkirmishSetupConfig>(ConfigPath);
        if (config != null) return config;

        config = ScriptableObject.CreateInstance<SkirmishSetupConfig>();
        AssetDatabase.CreateAsset(config, ConfigPath);
        Debug.Log($"Nieuwe configuratie aangemaakt: {ConfigPath}. Vul de prefabs in en draai het menu opnieuw.");
        return config;
    }

    private static int EnsureLayer(string layerName)
    {
        SerializedObject tagManager = new SerializedObject(
            AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        SerializedProperty layers = tagManager.FindProperty("layers");

        int free = -1;
        // Lagen 0 t/m 7 zijn ingebouwd. Eigen lagen beginnen bij 8.
        for (int i = 8; i < layers.arraySize; i++)
        {
            SerializedProperty layer = layers.GetArrayElementAtIndex(i);
            if (layer.stringValue == layerName) return i;
            if (free < 0 && string.IsNullOrEmpty(layer.stringValue)) free = i;
        }

        if (free < 0)
        {
            Debug.LogError($"Geen vrije laag voor '{layerName}'. Verwijder een ongebruikte laag en draai het menu opnieuw.");
            return 0;
        }

        layers.GetArrayElementAtIndex(free).stringValue = layerName;
        tagManager.ApplyModifiedProperties();
        return free;
    }

    private static void EnsureFolder(string parent, string name)
    {
        if (!AssetDatabase.IsValidFolder($"{parent}/{name}"))
            AssetDatabase.CreateFolder(parent, name);
    }

    private static Object[] ToObjects<T>(IList<T> items) where T : Object
    {
        Object[] result = new Object[items.Count];
        for (int i = 0; i < items.Count; i++) result[i] = items[i];
        return result;
    }

    // ---------- SerializedObject-koppelingen ----------

    private static void SetRef(Object target, string property, Object value)
    {
        SerializedProperty prop = FindProperty(target, property);
        if (prop == null) return;

        prop.objectReferenceValue = value;
        prop.serializedObject.ApplyModifiedProperties();
    }

    private static void SetRefArray(Object target, string property, Object[] values)
    {
        SerializedProperty prop = FindProperty(target, property);
        if (prop == null) return;

        prop.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++)
            prop.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        prop.serializedObject.ApplyModifiedProperties();
    }

    private static void SetInt(Object target, string property, int value)
    {
        SerializedProperty prop = FindProperty(target, property);
        if (prop == null) return;

        prop.intValue = value;
        prop.serializedObject.ApplyModifiedProperties();
    }

    private static void SetBool(Object target, string property, bool value)
    {
        SerializedProperty prop = FindProperty(target, property);
        if (prop == null) return;

        prop.boolValue = value;
        prop.serializedObject.ApplyModifiedProperties();
    }

    private static void SetRect(Object target, string property, Rect value)
    {
        SerializedProperty prop = FindProperty(target, property);
        if (prop == null) return;

        prop.rectValue = value;
        prop.serializedObject.ApplyModifiedProperties();
    }

    /// <summary>Zoekt een serialized property. Geeft een waarschuwing als die niet bestaat, zodat de rest doorgaat.</summary>
    private static SerializedProperty FindProperty(Object target, string property)
    {
        if (target == null)
        {
            Debug.LogWarning($"Koppeling '{property}' overgeslagen: het doelobject ontbreekt.");
            return null;
        }

        SerializedObject so = new SerializedObject(target);
        SerializedProperty prop = so.FindProperty(property);
        if (prop == null)
            Debug.LogWarning($"{target.GetType().Name}: onbekende property '{property}'. Controleer het veld.");

        return prop;
    }
}
