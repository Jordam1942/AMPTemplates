using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

/// <summary>
/// Touch-selectie en commando's.
/// - Tik op eigen unit: selecteren.
/// - Tik op vijandelijke unit of gebouw met selectie: aanval.
/// - Tik op terrein met selectie: verplaatsen in formatie.
/// - Stil vasthouden (0,3 s) en daarna slepen: marquee-selectie.
/// Vereist dat MobileRTSCamera de EnhancedTouch-support inschakelt.
/// </summary>
[DisallowMultipleComponent]
public sealed class TouchSelectionHandler : MonoBehaviour
{
    private enum GestureState { None, Pressing, Marquee, Ignored }

    [Header("References")]
    [SerializeField] private Camera worldCamera;
    [SerializeField] private MobileRTSCamera rtsCamera;

    [Header("Gestures")]
    [Tooltip("Zelfde waarde als MobileRTSCamera.dragThreshold.")]
    [SerializeField, Min(0f)] private float moveThreshold = 18f;
    [SerializeField, Min(0f)] private float holdToMarquee = 0.3f;

    [Header("Layers")]
    [SerializeField] private LayerMask selectableMask;
    [SerializeField] private LayerMask groundMask;

    [Header("Formatie")]
    [SerializeField, Min(0.5f)] private float formationSpacing = 1.4f;

    private readonly List<UnitController> selection = new List<UnitController>(64);
    private readonly List<UnitController> scratch = new List<UnitController>(64);

    private GestureState state;
    private Finger primaryFinger;
    private float pressStartTime;
    private Vector2 marqueeStart;
    private Vector2 marqueeEnd;
    private Texture2D pixel;

    private void Awake()
    {
        if (worldCamera == null) worldCamera = Camera.main;

        // 1x1 witte textuur voor het tekenen van de marquee in OnGUI.
        pixel = new Texture2D(1, 1);
        pixel.SetPixel(0, 0, Color.white);
        pixel.Apply();
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
        if (pixel != null) Destroy(pixel);
    }

    private void Update()
    {
        if (primaryFinger == null) return;

        Touch t = primaryFinger.currentTouch;
        switch (state)
        {
            case GestureState.Pressing:
                // Beweegt de vinger te ver? Dan is het een camera-pan, geen selectie.
                if (Vector2.Distance(t.screenPosition, t.startScreenPosition) > moveThreshold)
                    state = GestureState.Ignored;
                // Lang stil gehouden? Dan is het een marquee.
                else if (Time.unscaledTime - pressStartTime >= holdToMarquee)
                    BeginMarquee(t.screenPosition);
                break;

            case GestureState.Marquee:
                marqueeEnd = t.screenPosition;
                break;
        }
    }

    private void OnFingerDown(Finger finger)
    {
        // Tijdens bouwplaatsing, rally-keuze of een tik op de HUD is een ander systeem de eigenaar van de input.
        if (ConstructionSystem.IsPlacing || TargetPicker.IsActive || UIInputGuard.IsOverUI(finger)) return;

        // Een tweede vinger hoort bij de camera (pinch/twist). Lopende selectie stopt.
        if (Touch.activeTouches.Count > 1)
        {
            if (state == GestureState.Marquee) CancelMarquee();
            state = GestureState.Ignored;
            return;
        }

        primaryFinger = finger;
        pressStartTime = Time.unscaledTime;
        state = GestureState.Pressing;
    }

    private void OnFingerUp(Finger finger)
    {
        if (finger != primaryFinger) return;

        Touch t = finger.currentTouch;
        switch (state)
        {
            case GestureState.Pressing:
                HandleTap(t.screenPosition);
                break;
            case GestureState.Marquee:
                CompleteMarquee();
                break;
        }

        primaryFinger = null;
        state = GestureState.None;
    }

    // ---------- Tikken ----------

    private void HandleTap(Vector2 screenPos)
    {
        Ray ray = worldCamera.ScreenPointToRay(screenPos);
        if (!Physics.Raycast(ray, out RaycastHit hit, 1000f, selectableMask | groundMask, QueryTriggerInteraction.Ignore))
            return;

        int hitLayerBit = 1 << hit.collider.gameObject.layer;

        if ((selectableMask.value & hitLayerBit) != 0)
        {
            IDamageable target = hit.collider.GetComponentInParent<IDamageable>();
            if (target == null) return;

            if (target.Team == UnitController.LocalPlayerTeam)
            {
                if (target is UnitController unit) SelectSingle(unit);
                else if (target is BuildingController building) SelectBuilding(building);
            }
            else
            {
                IssueAttack(target);
            }
        }
        else if ((groundMask.value & hitLayerBit) != 0)
        {
            // Met een selectie van units is de grondtik een bevel. Anders sluit het gebouwpaneel.
            if (selection.Count > 0) IssueMove(hit.point);
            else SelectionState.Clear();
        }
    }

    private void SelectSingle(UnitController unit)
    {
        scratch.Clear();
        scratch.Add(unit);
        SetSelection(scratch);

        if (AudioManager.Instance != null) AudioManager.Instance.PlaySelectionVoice(selection);
        HapticFeedback.Tick();
    }

    private void SelectBuilding(BuildingController building)
    {
        scratch.Clear();
        SetSelection(scratch); // Leeg de unitselectie, anders commandeert de handler verborgen units.

        SelectionState.SetBuilding(building);
        HapticFeedback.Tick();
    }

    // ---------- Marquee ----------

    private void BeginMarquee(Vector2 start)
    {
        state = GestureState.Marquee;
        marqueeStart = start;
        marqueeEnd = start;
        rtsCamera.InputLocked = true;
        HapticFeedback.Tick();
    }

    private void CancelMarquee()
    {
        rtsCamera.InputLocked = false;
        state = GestureState.None;
    }

    private void CompleteMarquee()
    {
        rtsCamera.InputLocked = false;

        Rect rect = ScreenRect(marqueeStart, marqueeEnd);

        // Een te kleine box is geen selectie. Een lange stille druk selecteert dus niets.
        if (rect.width < moveThreshold && rect.height < moveThreshold) return;

        // Projectie in plaats van per-unit raycasts: sneller, en units achter andere units worden ook gevonden.
        scratch.Clear();
        foreach (UnitController u in UnitController.Registry)
        {
            if (u.Team != UnitController.LocalPlayerTeam || !u.IsAlive) continue;

            Vector3 sp = worldCamera.WorldToScreenPoint(u.Position);
            if (sp.z > 0f && rect.Contains(new Vector2(sp.x, sp.y)))
                scratch.Add(u);
        }

        SetSelection(scratch);

        if (selection.Count > 0)
        {
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySelectionVoice(selection);
            HapticFeedback.Tick();
        }
    }

    private void OnGUI()
    {
        if (state != GestureState.Marquee) return;

        Rect r = ScreenRect(marqueeStart, marqueeEnd);
        // OnGUI heeft de oorsprong linksboven, input linksonder.
        r.y = Screen.height - r.yMax;

        Color previous = GUI.color;
        const float border = 2f;

        GUI.color = new Color(0.2f, 1f, 0.4f, 0.15f);
        GUI.DrawTexture(r, pixel);

        GUI.color = new Color(0.2f, 1f, 0.4f, 0.9f);
        GUI.DrawTexture(new Rect(r.xMin, r.yMin, r.width, border), pixel);
        GUI.DrawTexture(new Rect(r.xMin, r.yMax - border, r.width, border), pixel);
        GUI.DrawTexture(new Rect(r.xMin, r.yMin, border, r.height), pixel);
        GUI.DrawTexture(new Rect(r.xMax - border, r.yMin, border, r.height), pixel);

        GUI.color = previous;
    }

    // ---------- Commando's ----------

    private void IssueMove(Vector3 target)
    {
        PruneDead();
        if (selection.Count == 0) return;

        // Dichtstbijzijnde units krijgen de dichtstbijzijnde plekken in de formatie.
        selection.Sort((a, b) =>
            (a.Position - target).sqrMagnitude.CompareTo((b.Position - target).sqrMagnitude));

        Vector3[] offsets = FormationUtility.BuildGridOffsets(selection.Count, formationSpacing);

        for (int i = 0; i < selection.Count; i++)
        {
            Vector3 slot = target + offsets[i];

            // Plek op de NavMesh forceren, anders kan een slot op een muur of water vallen.
            if (NavMesh.SamplePosition(slot, out NavMeshHit hit, formationSpacing * 3f, NavMesh.AllAreas))
                slot = hit.position;

            selection[i].CommandMove(slot);
        }

        if (AudioManager.Instance != null) AudioManager.Instance.PlayMoveVoice(selection);
    }

    private void IssueAttack(IDamageable target)
    {
        PruneDead();
        if (selection.Count == 0 || !target.IsAlive) return;

        for (int i = 0; i < selection.Count; i++)
            selection[i].CommandAttack(target);

        if (AudioManager.Instance != null) AudioManager.Instance.PlayAttackVoice(selection);
        HapticFeedback.Impact();
    }

    // ---------- Hulpfuncties ----------

    private void SetSelection(IList<UnitController> units)
    {
        for (int i = 0; i < selection.Count; i++)
            selection[i].SetSelected(false);

        selection.Clear();

        for (int i = 0; i < units.Count; i++)
        {
            selection.Add(units[i]);
            units[i].SetSelected(true);
        }

        SelectionState.SetUnits(selection);
    }

    private void PruneDead()
    {
        bool removed = false;

        for (int i = selection.Count - 1; i >= 0; i--)
        {
            if (selection[i].IsAlive) continue;

            selection[i].SetSelected(false);
            selection.RemoveAt(i);
            removed = true;
        }

        // De HUD moet weten dat de selectie is gekrompen.
        if (removed) SelectionState.SetUnits(selection);
    }

    private static Rect ScreenRect(Vector2 a, Vector2 b) =>
        Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
}
