using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// Automatic sailing + fleet management.
///
/// Automation model:
///  - The ONLY automation is a destination city. Press AUTO on a ship (or R
///    with the ship selected), then click a city on the map.
///  - An automatic ship spends ALL of its movement every turn sailing toward
///    that city, and keeps going across turns until it arrives or you cancel.
///  - When it reaches a city the player is told and makes the trade decision;
///    control returns to the player when it reaches the destination.
///  - HOLD tells a ship to sit out; held ships do not block the end of turn.
///
/// End-of-turn gate: the turn may only end when every ship is out of
/// movement, set to automatic, or holding.
///
/// This file is the single source of truth. Replace any previous copy.
/// Requires Ship.cs v5 (which calls Ensure, ShowBanner and IsModalOpen).
/// </summary>
public class AutoShipSystem : MonoBehaviour
{
    public static AutoShipSystem Instance;

    /// <summary>True while automatic ships are sailing.</summary>
    public bool Resolving { get; private set; }

    /// <summary>True while a modal owns the screen.</summary>
    public bool IsModalOpen { get; private set; }

    // --- fleet panel ---
    private GameObject fleetCanvas;
    private RectTransform fleetPanel;
    private readonly List<FleetRow> rows = new List<FleetRow>();
    private int rowsBuiltForFleetSize = -1;

    // --- modal ---
    private GameObject modalCanvas;
    private RectTransform modalPanel;
    private Text modalBody;
    private Button modalPrimary;
    private Text modalPrimaryLabel;
    private Button modalSecondary;
    private Text modalSecondaryLabel;

    // --- banner ---
    private GameObject bannerCanvas;
    private Text bannerText;
    private float bannerUntil;

    private readonly Dictionary<Ship, LineRenderer> routeLines = new Dictionary<Ship, LineRenderer>();

    private System.Action modalPrimaryAction;
    private System.Action modalSecondaryAction;

    private static readonly Color manualColor = new Color(0.85f, 0.85f, 0.85f);
    private static readonly Color autoColor = new Color(0.35f, 0.85f, 1f);
    private static readonly Color holdColor = new Color(1f, 0.78f, 0.3f);
    private static readonly Color pickColor = new Color(1f, 0.92f, 0.25f);

    private class FleetRow
    {
        public Ship ship;
        public Text label;
        public Button autoBtn;
        public Button holdBtn;
        public Button cancelBtn;
    }

    // ---------------------------------------------------------------- setup

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        EnsureEventSystem();
        BuildFleetPanel();
        BuildBanner();
        BuildModal();
        HideModal();
        RefreshFleetPanel();
    }

    /// <summary>
    /// A Canvas needs a GraphicRaycaster for its buttons to be clickable, and
    /// something needs to drive the EventSystem. Without both, UI buttons
    /// render normally but never fire.
    /// </summary>
    private void EnsureEventSystem()
    {
        // There must be EXACTLY ONE EventSystem in the scene. This script
        // builds three canvases, and other scripts build more, so they all
        // route through the shared helper.
        UiEventSystem.EnsureExactlyOne();
    }

    void Update()
    {
        // ESC dismisses a modal that has a secondary action (e.g. GO BACK).
        if (IsModalOpen && modalSecondary != null && modalSecondary.gameObject.activeSelf)
        {
            if (Input.GetKeyDown(KeyCode.Escape)) RunModalSecondary();
        }
        // SPACE / ENTER continue a modal that has only a primary action.
        // This is a safety net so a port call can never soft-lock the game
        // if the button is hard to click.
        else if (IsModalOpen)
        {
            if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return)
                || Input.GetKeyDown(KeyCode.KeypadEnter))
                RunModalPrimary();
        }

        RefreshFleetPanel();
        RefreshRouteLines();

        if (bannerCanvas != null && bannerCanvas.activeSelf && Time.time > bannerUntil)
            bannerCanvas.SetActive(false);
    }

    /// <summary>Create the system if it is missing from the scene.</summary>
    public static AutoShipSystem Ensure()
    {
        if (Instance != null) return Instance;
        GameObject go = new GameObject("AutoShipSystem");
        return go.AddComponent<AutoShipSystem>();
    }

    // ------------------------------------------------------------------ UI

    private void BuildFleetPanel()
    {
        if (fleetCanvas != null) return;

        fleetCanvas = new GameObject("FleetCanvas", typeof(Canvas));
        fleetCanvas.transform.SetParent(transform, false);
        Canvas canvas = fleetCanvas.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 150;
        if (fleetCanvas.GetComponent<GraphicRaycaster>() == null)
            fleetCanvas.AddComponent<GraphicRaycaster>();

        UnityEngine.UI.CanvasScaler scaler = fleetCanvas.AddComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        GameObject panel = new GameObject("FleetPanel", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(fleetCanvas.transform, false);
        Image bg = panel.GetComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.62f);
        RectTransform rt = panel.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(1f, 0.5f);
        rt.anchorMax = new Vector2(1f, 0.5f);
        rt.pivot = new Vector2(1f, 0.5f);
        rt.anchoredPosition = new Vector2(-10f, 0f);
        rt.sizeDelta = new Vector2(330f, 470f);
        fleetPanel = rt;

        Text header = MakeText(fleetPanel, "Header", 20, TextAnchor.MiddleLeft);
        RectTransform hrt = header.GetComponent<RectTransform>();
        hrt.anchorMin = new Vector2(0f, 1f);
        hrt.anchorMax = new Vector2(1f, 1f);
        hrt.pivot = new Vector2(0.5f, 1f);
        hrt.anchoredPosition = new Vector2(0f, -14f);
        hrt.sizeDelta = new Vector2(-24f, 30f);
        header.text = "FLEET";
    }

    private void BuildBanner()
    {
        if (bannerCanvas != null) return;

        bannerCanvas = new GameObject("BannerCanvas", typeof(Canvas));
        bannerCanvas.transform.SetParent(transform, false);
        Canvas canvas = bannerCanvas.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 240;
        if (bannerCanvas.GetComponent<GraphicRaycaster>() == null)
            bannerCanvas.AddComponent<GraphicRaycaster>();

        UnityEngine.UI.CanvasScaler scaler = bannerCanvas.AddComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        GameObject strip = new GameObject("BannerStrip", typeof(RectTransform), typeof(Image));
        strip.transform.SetParent(bannerCanvas.transform, false);
        strip.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.78f);
        RectTransform rt = strip.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 1f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -40f);
        rt.sizeDelta = new Vector2(1100f, 52f);

        bannerText = MakeText(rt, "BannerText", 22, TextAnchor.MiddleCenter);
        RectTransform brt = bannerText.GetComponent<RectTransform>();
        brt.anchorMin = Vector2.zero;
        brt.anchorMax = Vector2.one;
        brt.offsetMin = new Vector2(16f, 0f);
        brt.offsetMax = new Vector2(-16f, 0f);
        bannerText.color = new Color(1f, 0.88f, 0.35f);
        bannerText.fontStyle = FontStyle.Bold;

        bannerCanvas.SetActive(false);
    }

    private void BuildModal()
    {
        if (modalCanvas != null) return;

        modalCanvas = new GameObject("ModalCanvas", typeof(Canvas));
        modalCanvas.transform.SetParent(transform, false);
        Canvas canvas = modalCanvas.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 260;
        if (modalCanvas.GetComponent<GraphicRaycaster>() == null)
            modalCanvas.AddComponent<GraphicRaycaster>();

        UnityEngine.UI.CanvasScaler scaler = modalCanvas.AddComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        // Dimmer so the modal reads as a modal.
        GameObject dim = new GameObject("Dimmer", typeof(RectTransform), typeof(Image));
        dim.transform.SetParent(modalCanvas.transform, false);
        Image dimImg = dim.GetComponent<Image>();
        dimImg.color = new Color(0f, 0f, 0f, 0.55f);
        // Non-blocking: the player is meant to trade in the panel behind the
        // modal during a port call, so the dimmer must not eat those clicks.
        dimImg.raycastTarget = false;
        RectTransform drt = dim.GetComponent<RectTransform>();
        drt.anchorMin = Vector2.zero;
        drt.anchorMax = Vector2.one;
        drt.offsetMin = Vector2.zero;
        drt.offsetMax = Vector2.zero;

        GameObject panel = new GameObject("ModalPanel", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(modalCanvas.transform, false);
        panel.GetComponent<Image>().color = new Color(0.06f, 0.07f, 0.10f, 0.98f);
        RectTransform rt = panel.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(760f, 340f);
        modalPanel = rt;

        modalBody = MakeText(modalPanel, "Body", 22, TextAnchor.UpperLeft);
        RectTransform bodyRt = modalBody.GetComponent<RectTransform>();
        bodyRt.anchorMin = new Vector2(0f, 1f);
        bodyRt.anchorMax = new Vector2(1f, 1f);
        bodyRt.pivot = new Vector2(0.5f, 1f);
        bodyRt.offsetMin = new Vector2(30f, 0f);
        bodyRt.offsetMax = new Vector2(-30f, -30f);
        bodyRt.anchoredPosition = Vector2.zero;
        bodyRt.sizeDelta = new Vector2(-60f, 200f);

        modalPrimary = MakeButton(modalPanel, "Primary", "OK", new Vector2(-160f, -140f), out modalPrimaryLabel);
        modalPrimary.onClick.AddListener(RunModalPrimary);
        modalSecondary = MakeButton(modalPanel, "Secondary", "CANCEL", new Vector2(160f, -140f), out modalSecondaryLabel);
        modalSecondary.onClick.AddListener(RunModalSecondary);

        // Start hidden. Without this the modal sits on screen from frame one
        // with dead buttons.
        modalCanvas.SetActive(false);
        IsModalOpen = false;
    }

    private Text MakeText(RectTransform parent, string name, int size, TextAnchor anchor)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        Text t = go.GetComponent<Text>();
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (t.font == null) t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        t.fontSize = size;
        t.alignment = anchor;
        t.color = Color.white;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        return t;
    }

    private Button MakeButton(RectTransform parent, string name, string label, Vector2 pos, out Text labelText)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        Image img = go.GetComponent<Image>();
        img.color = new Color(0.2f, 0.35f, 0.5f, 1f);
        Button b = go.GetComponent<Button>();
        b.targetGraphic = img;
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0f);
        rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(280f, 56f);

        labelText = MakeText(rt, "Label", 20, TextAnchor.MiddleCenter);
        RectTransform lrt = labelText.GetComponent<RectTransform>();
        lrt.anchorMin = Vector2.zero;
        lrt.anchorMax = Vector2.one;
        lrt.offsetMin = Vector2.zero;
        lrt.offsetMax = Vector2.zero;
        labelText.text = label;
        return b;
    }

    // ---------------------------------------------------------- fleet list

    private void RefreshFleetPanel()
    {
        if (fleetPanel == null) return;

        // Rebuild the row OBJECTS only when the fleet size changes. Rebuilding
        // every frame destroyed the buttons between mouse-down and mouse-up,
        // so their clicks never fired.
        if (rowsBuiltForFleetSize != GameState.fleet.Count)
        {
            RebuildRows();
            rowsBuiltForFleetSize = GameState.fleet.Count;
        }

        int i = 0;
        foreach (Ship s in GameState.fleet)
        {
            if (s == null) continue;
            if (i >= rows.Count) break;
            FleetRow row = rows[i];

            if (row.ship != s) BindRow(row, s);

            bool selected = s == GameState.selected;
            string status;
            Color color;
            if (s.isAuto && s.hasAutoTarget)
            {
                City t = s.TargetCity;
                status = "AUTO -> " + (t != null ? t.name : "?");
                color = autoColor;
            }
            else if (s.destMode)
            {
                status = "PICK A CITY";
                color = pickColor;
            }
            else if (s.holdTurn)
            {
                status = "HOLDING";
                color = holdColor;
            }
            else
            {
                status = "MANUAL";
                color = manualColor;
            }

            row.label.text = (selected ? "> " : "  ") + s.def.name + "   " + status +
                             "\n     move " + s.remainingMovement + "/" + s.def.movement +
                             "   cargo " + s.cargo.Count + "/" + s.def.storage;
            row.label.color = color;

            row.autoBtn.gameObject.SetActive(!s.holdTurn);
            row.holdBtn.gameObject.SetActive(true);
            row.cancelBtn.gameObject.SetActive(s.hasAutoTarget || s.destMode);

            i++;
        }

        for (int j = i; j < rows.Count; j++)
        {
            FleetRow r = rows[j];
            if (r.label != null) r.label.gameObject.SetActive(false);
            if (r.autoBtn != null) r.autoBtn.gameObject.SetActive(false);
            if (r.holdBtn != null) r.holdBtn.gameObject.SetActive(false);
            if (r.cancelBtn != null) r.cancelBtn.gameObject.SetActive(false);
        }
    }

    private void RebuildRows()
    {
        // Remove old rows (buttons included).
        foreach (FleetRow r in rows)
        {
            if (r.label != null) Destroy(r.label.gameObject);
        }
        rows.Clear();

        float y = -50f;
        for (int i = 0; i < 12; i++)
        {
            FleetRow row = new FleetRow();

            row.label = MakeText(fleetPanel, "Ship", 17, TextAnchor.UpperLeft);
            RectTransform lrt = row.label.GetComponent<RectTransform>();
            lrt.anchorMin = new Vector2(0f, 1f);
            lrt.anchorMax = new Vector2(1f, 1f);
            lrt.pivot = new Vector2(0.5f, 1f);
            lrt.anchoredPosition = new Vector2(0f, y);
            lrt.sizeDelta = new Vector2(-24f, 40f);

            row.autoBtn = MakeRowButton(fleetPanel, "Auto", "AUTO", new Vector2(-200f, y - 44f), 110f);
            row.holdBtn = MakeRowButton(fleetPanel, "Hold", "HOLD", new Vector2(-80f, y - 44f), 110f);
            row.cancelBtn = MakeRowButton(fleetPanel, "Cancel", "CANCEL", new Vector2(40f, y - 44f), 110f);

            row.autoBtn.onClick.AddListener(() => ToggleAuto(row.ship));
            row.holdBtn.onClick.AddListener(() => ToggleHold(row.ship));
            row.cancelBtn.onClick.AddListener(() => CancelAuto(row.ship));

            rows.Add(row);
            y -= 74f;
        }
    }

    private void BindRow(FleetRow row, Ship s)
    {
        row.ship = s;
        row.label.gameObject.SetActive(true);
        row.autoBtn.gameObject.SetActive(true);
        row.holdBtn.gameObject.SetActive(true);
        row.cancelBtn.gameObject.SetActive(true);
    }

    private Button MakeRowButton(RectTransform parent, string name, string label, Vector2 pos, float width)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        Image img = go.GetComponent<Image>();
        img.color = new Color(0.25f, 0.28f, 0.34f, 0.95f);
        Button b = go.GetComponent<Button>();
        b.targetGraphic = img;
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 1f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(width, 28f);

        Text t = MakeText(rt, "Label", 14, TextAnchor.MiddleCenter);
        RectTransform trt = t.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = Vector2.zero;
        trt.offsetMax = Vector2.zero;
        t.text = label;
        return b;
    }

    private void ToggleAuto(Ship s)
    {
        if (s == null) return;
        GameState.selected = s;

        if (s.isAuto && s.hasAutoTarget)
        {
            s.ClearDestination();
            ShowBanner(s.def.name + " is manual again.");
            return;
        }

        s.destMode = true;
        s.holdTurn = false;
        s.UpdateVisualMode();
        Ship.RefreshAllVisuals();
        ShowBanner("Click a CITY on the map to set " + s.def.name + "'s destination.");
        Debug.Log("Click a city on the map: " + s.def.name + " will sail there by itself.");
    }

    private void ToggleHold(Ship s)
    {
        if (s == null) return;
        s.SetHold(!s.holdTurn);
        Ship.RefreshAllVisuals();
        ShowBanner(s.holdTurn
            ? s.def.name + " is HOLDING — it sits out and will not block ending the turn."
            : s.def.name + " is no longer holding.");
    }

    private void CancelAuto(Ship s)
    {
        if (s == null) return;
        if (s.hasAutoTarget || s.destMode)
        {
            s.ClearDestination();
            Ship.RefreshAllVisuals();
            ShowBanner("Automatic sailing cancelled for " + s.def.name + ".");
        }
    }

    // --------------------------------------------------------- route lines

    private void RefreshRouteLines()
    {
        foreach (Ship s in GameState.fleet)
        {
            if (s == null || !s.isAuto || !s.hasAutoTarget)
                continue;
            City target = s.TargetCity;
            if (target == null) continue;

            LineRenderer lr;
            if (!routeLines.TryGetValue(s, out lr) || lr == null)
            {
                GameObject go = new GameObject("RouteLine");
                go.transform.SetParent(transform, false);
                lr = go.AddComponent<LineRenderer>();
                lr.widthMultiplier = 0.12f;
                Shader sh = Shader.Find("Sprites/Default");
                if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
                if (sh != null) lr.material = new Material(sh);
                lr.material.color = new Color(0.3f, 0.85f, 1f, 0.85f);
                lr.positionCount = 2;
                lr.useWorldSpace = true;
                routeLines[s] = lr;
            }

            lr.SetPosition(0, s.transform.position + Vector3.up * 0.6f);
            lr.SetPosition(1, HexGrid.Instance.AxialToWorld(target.coord.x, target.coord.y) + Vector3.up * 0.6f);
        }
    }

    // ------------------------------------------------------ end-turn gate

    public void RequestEndTurn()
    {
        if (Resolving) return;

        List<Ship> blocking = new List<Ship>();
        foreach (Ship s in GameState.fleet)
        {
            if (s == null) continue;
            if (s.remainingMovement > 0 && !s.isAuto && !s.holdTurn)
                blocking.Add(s);
        }

        if (blocking.Count == 0)
        {
            StartCoroutine(ResolveAndAdvance());
            return;
        }

        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.AppendLine("These ships still have movement and have not been set to automatic:");
        sb.AppendLine();
        foreach (Ship s in blocking)
            sb.AppendLine("   " + s.def.name + "   (" + s.remainingMovement + " movement left)");
        sb.AppendLine();
        sb.AppendLine("Move them, set them to AUTO, or HOLD them to sit out this turn.");

        ShowModal(sb.ToString(), "HOLD THEM & END TURN", "GO BACK", () =>
        {
            foreach (Ship s in blocking) s.SetHold(true);
            HideModal();
            StartCoroutine(ResolveAndAdvance());
        }, HideModal);
    }

    // ------------------------------------------------------------- resolve

    private IEnumerator ResolveAndAdvance()
    {
        Resolving = true;

        // A voyage can take several turns. Keep sailing until every automatic
        // ship has arrived or been cancelled.
        while (true)
        {
            bool anyStillAtSea = false;

            foreach (Ship s in GameState.fleet)
            {
                if (s == null || !s.isAuto || !s.hasAutoTarget) continue;
                yield return SailAutomatic(s);
                if (s != null && s.isAuto && s.hasAutoTarget) anyStillAtSea = true;
            }

            if (!anyStillAtSea) break;

            // Spend a turn: markets age, movement refills, turn counter moves on.
            GameState.AdvanceTurn();
        }

        Resolving = false;
        GameState.AdvanceTurn();
        Ship.RefreshAllVisuals();
    }

    private IEnumerator SailAutomatic(Ship s)
    {
        while (s != null && s.isAuto && s.hasAutoTarget && s.remainingMovement > 0)
        {
            City target = s.TargetCity;
            if (target == null) break;

            if (s.coord == target.coord)
            {
                s.ClearDestination();
                ShowPortCall(s, target, "arrived at " + target.name + ". Control returned to you.");
                yield return WaitForModalDismissed();
                yield break;
            }

            List<Vector2Int> path = HexGrid.Instance.FindPath(s.coord, target.coord);
            if (path == null || path.Count < 2)
            {
                s.ClearDestination();
                ShowModal(s.def.name + " cannot reach " + target.name + " — automatic sailing cancelled.",
                          "OK", null, HideModal, null);
                yield return WaitForModalDismissed();
                yield break;
            }

            // FindPath is INCLUSIVE: path[0] is the hex we are standing on.
            int budget = Mathf.Min(s.remainingMovement, path.Count - 1);
            for (int i = 1; i <= budget; i++)
            {
                Vector2Int next = path[i];
                yield return s.SailStep(next);

                // Intermediate cities are passed straight through. A voyage has
                // exactly one stop: its destination.
                if (s == null || !s.isAuto || !s.hasAutoTarget) yield break;   // cancelled mid-step
                if (s.coord == target.coord) break;
            }
        }
    }

    private void ShowPortCall(Ship s, City city, string note)
    {
        GameState.selected = s;
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.AppendLine(s.def.name + " " + note);
        sb.AppendLine();
        sb.AppendLine("Trade here if you wish, then continue the voyage.");
        ShowModal(sb.ToString(), "CONTINUE", null, HideModal, null);
    }

    private IEnumerator WaitForModalDismissed()
    {
        while (IsModalOpen) yield return null;
    }

    // --------------------------------------------------------------- modal

    public void ShowModal(string body, string primary, string secondary,
                          System.Action primaryAction, System.Action secondaryAction)
    {
        if (modalCanvas == null) BuildModal();
        modalBody.text = body;
        modalPrimaryAction = primaryAction;
        modalSecondaryAction = secondaryAction;

        modalPrimary.gameObject.SetActive(primary != null);
        if (primary != null) modalPrimaryLabel.text = primary;

        modalSecondary.gameObject.SetActive(secondary != null);
        if (secondary != null) modalSecondaryLabel.text = secondary;

        modalCanvas.SetActive(true);
        IsModalOpen = true;
    }

    public void HideModal()
    {
        if (modalCanvas != null) modalCanvas.SetActive(false);
        modalPrimaryAction = null;
        modalSecondaryAction = null;
        IsModalOpen = false;
    }

    private void RunModalPrimary()
    {
        System.Action a = modalPrimaryAction;
        HideModal();
        if (a != null) a();
    }

    private void RunModalSecondary()
    {
        System.Action a = modalSecondaryAction;
        HideModal();
        if (a != null) a();
    }

    // -------------------------------------------------------------- banner

    public void ShowBanner(string message)
    {
        if (bannerCanvas == null) BuildBanner();
        bannerText.text = message;
        bannerCanvas.SetActive(true);
        bannerUntil = Time.time + 4f;
    }

    // ------------------------------------------------------------- restart

    public void ResetUI()
    {
        HideModal();
        if (bannerCanvas != null) bannerCanvas.SetActive(false);
        foreach (var kv in routeLines)
            if (kv.Value != null) Destroy(kv.Value.gameObject);
        routeLines.Clear();
        rowsBuiltForFleetSize = -1;
        foreach (FleetRow r in rows)
            if (r.label != null) Destroy(r.label.gameObject);
        rows.Clear();
        Resolving = false;
    }
}
