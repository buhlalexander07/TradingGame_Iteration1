using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using UnityEngine.EventSystems;

/// <summary>
/// The trade panel (uGUI, built entirely in code — no editor UI setup).
/// Shows when the selected ship is docked at a city:
///  - every resource with live prices (what the city pays you / charges you)
///  - saturation indicator (▼ penalty %) so the mechanic is visible
///  - the city's FIFO sales log
///  - ship purchase buttons (replaces the 1/2/3 debug keys)
/// Plus always-on-screen: gold/turn/cargo readout and an End Turn button.
/// </summary>
public class TradePanelUI : MonoBehaviour
{
    private Canvas canvas;
    private GameObject panel;
    private Text headerText;
    private Text statsText;
    private Text logText;

    private class ResourceRow
    {
        public ResourceDef def;
        public Text nameText;
        public Text priceText;
        public Text satText;
        public Button buyButton;
        public Button sellButton;
    }
    private readonly List<ResourceRow> rows = new List<ResourceRow>();

    private class ShipRow
    {
        public ShipDef def;
        public Button button;
        public Text label;
    }
    private readonly List<ShipRow> shipRows = new List<ShipRow>();

    // ---------- BUILD ----------

    void Start()
    {
        GameState.Init();
        EnsureEventSystem();
        BuildUI();
    }

    void Update()
    {
        Refresh();
    }

    private static Font BuiltinFont()
    {
        Font f = null;
        try { f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
        if (f == null) { try { f = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { } }
        return f;
    }

    private static void EnsureEventSystem()
    {
        if (Object.FindFirstObjectByType<EventSystem>() != null) return;
        new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
    }

    private Text MakeText(Transform parent, string text, int size, Vector2 anchorMin, Vector2 anchorMax,
                          Vector2 offsetMin, Vector2 offsetMax, TextAnchor align)
    {
        GameObject go = new GameObject("text", typeof(Text));
        Text t = go.GetComponent<Text>();
        t.font = BuiltinFont();
        t.text = text;
        t.fontSize = size;
        t.color = Color.white;
        t.alignment = align;
        t.raycastTarget = false; // texts must never eat button clicks
        RectTransform rt = t.rectTransform;
        rt.SetParent(parent, false);
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;
        return t;
    }

    private Button MakeButton(Transform parent, string label, Vector2 anchor, Vector2 pos, Vector2 size,
                              UnityAction onClick)
    {
        GameObject go = new GameObject("btn_" + label, typeof(Image), typeof(Button));
        Image img = go.GetComponent<Image>();
        img.color = new Color(0.25f, 0.35f, 0.25f, 1f);
        Button b = go.GetComponent<Button>();
        b.navigation = new Navigation { mode = Navigation.Mode.None }; // so Space never re-clicks it
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;

        GameObject lbl = new GameObject("label", typeof(Text));
        Text t = lbl.GetComponent<Text>();
        t.font = BuiltinFont();
        t.text = label;
        t.fontSize = 13;
        t.alignment = TextAnchor.MiddleCenter;
        t.color = Color.white;
        t.raycastTarget = false; // the button's Image handles the raycast
        RectTransform lrt = t.rectTransform;
        lrt.SetParent(rt, false);
        lrt.anchorMin = Vector2.zero;
        lrt.anchorMax = Vector2.one;
        lrt.offsetMin = Vector2.zero;
        lrt.offsetMax = Vector2.zero;

        if (onClick != null) b.onClick.AddListener(onClick);
        return b;
    }

    private void BuildUI()
    {
        // Canvas root
        GameObject canvasGo = new GameObject("TradeCanvas", typeof(Canvas));
        canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvasGo.AddComponent<GraphicRaycaster>(); // REQUIRED for button clicks to register
        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        // ---- Always-on HUD: stats top-left, End Turn top-right ----
        statsText = MakeText(canvas.transform, "", 15,
            new Vector2(0, 1), new Vector2(0, 1), new Vector2(12, -34), new Vector2(560, -8), TextAnchor.MiddleLeft);

        MakeButton(canvas.transform, "End Turn (Space)",
            new Vector2(1, 1), new Vector2(-110, -30), new Vector2(200, 36),
            () => GameState.EndTurn());

        // ---- Docked panel (left side) ----
        panel = new GameObject("DockedPanel", typeof(Image));
        Image pimg = panel.GetComponent<Image>();
        pimg.color = new Color(0, 0, 0, 0.88f);
        RectTransform prt = panel.GetComponent<RectTransform>();
        prt.SetParent(canvas.transform, false);
        prt.anchorMin = new Vector2(0, 0.5f);
        prt.anchorMax = new Vector2(0, 0.5f);
        prt.pivot = new Vector2(0, 0.5f);
        prt.anchoredPosition = new Vector2(10, 0);
        prt.sizeDelta = new Vector2(470, 660);
        panel.SetActive(false);

        // Header
        headerText = MakeText(panel.transform, "", 17,
            new Vector2(0, 1), new Vector2(1, 1), new Vector2(12, -36), new Vector2(-12, -8), TextAnchor.MiddleLeft);

        // Column headers
        MakeText(panel.transform, "GOOD", 12, new Vector2(0, 1), new Vector2(0, 1), new Vector2(12, -58), new Vector2(120, -40), TextAnchor.MiddleLeft);
        MakeText(panel.transform, "CITY PAYS YOU | CITY SELLS", 12, new Vector2(0, 1), new Vector2(0, 1), new Vector2(125, -58), new Vector2(370, -40), TextAnchor.MiddleLeft);

        // One row per resource
        float y = 66;
        foreach (var res in GameData.Resources)
        {
            var row = new ResourceRow { def = res };

            row.nameText = MakeText(panel.transform, res.name, 14,
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(12, -y - 22), new Vector2(125, -y), TextAnchor.MiddleLeft);

            row.priceText = MakeText(panel.transform, "", 14,
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(125, -y - 22), new Vector2(270, -y), TextAnchor.MiddleLeft);

            row.satText = MakeText(panel.transform, "", 13,
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(270, -y - 22), new Vector2(372, -y), TextAnchor.MiddleLeft);

            row.sellButton = MakeButton(panel.transform, "SELL",
                new Vector2(0, 1), new Vector2(376, -y - 11), new Vector2(42, 20), null);
            row.buyButton = MakeButton(panel.transform, "BUY",
                new Vector2(0, 1), new Vector2(422, -y - 11), new Vector2(42, 20), null);

            string r = res.name; // capture for the closure
            row.sellButton.onClick.AddListener(() => SellOne(r));
            row.buyButton.onClick.AddListener(() => BuyOne(r));

            rows.Add(row);
            y += 24;
        }

        // Ship purchase section
        MakeText(panel.transform, "BUY SHIPS", 12,
            new Vector2(0, 1), new Vector2(0, 1), new Vector2(12, -y - 18), new Vector2(200, -y), TextAnchor.MiddleLeft);
        y += 22;
        float bx = 85; 
        foreach (var sd in GameData.Ships)
        {
            var sr = new ShipRow { def = sd };
            sr.button = MakeButton(panel.transform, $"{sd.name} {sd.cost}g",
                new Vector2(0, 1), new Vector2(bx, -y - 12), new Vector2(145, 24), null);
            string s = sd.name; // capture for the closure
            sr.button.onClick.AddListener(() =>
            {
                Ship sel = GameState.selected;
                if (sel != null) GameState.BuyShip(s, sel.coord, Ship.prefab);
            });
            sr.label = sr.button.GetComponentInChildren<Text>();
            shipRows.Add(sr);
            bx += 150;
        }
        y += 34;

        // Saturation log
        MakeText(panel.transform, "RECENTLY BOUGHT (newest first — prices recover as entries age):", 11,
            new Vector2(0, 1), new Vector2(0, 1), new Vector2(12, -y - 16), new Vector2(-12, -y), TextAnchor.MiddleLeft);
        y += 18;
        logText = MakeText(panel.transform, "", 12,
            new Vector2(0, 0), new Vector2(1, 0), new Vector2(12, 8), new Vector2(-12, 70), TextAnchor.UpperLeft);
    }

    // ---------- ACTIONS ----------

    private void SellOne(string resource)
    {
        Ship s = GameState.selected;
        City city = s != null ? GameState.CityAt(s.coord) : null;
        if (s == null || city == null) return;
        if (!s.cargo.Contains(resource)) { Debug.Log($"No {resource} in the hold."); return; }

        int earned = city.PlayerSells(resource, 1);
        s.cargo.Remove(resource);
        GameState.gold += earned;
        Debug.Log($"Sold 1 {resource} at {city.name} for {earned}g. Gold: {GameState.gold}.");
        WinScreen.RecordSale(city.name, earned);
    }

    private void BuyOne(string resource)
    {
        Ship s = GameState.selected;
        City city = s != null ? GameState.CityAt(s.coord) : null;
        if (s == null || city == null) return;
        if (System.Array.IndexOf(city.produces, resource) < 0)
        {
            Debug.Log($"{city.name} doesn't produce {resource} — nothing to buy.");
            return;
        }
        if (s.cargo.Count >= s.def.storage) { Debug.Log("Hold is full."); return; }

        int price = city.GetSellPriceToPlayer(resource);
        if (GameState.gold < price) { Debug.Log($"Not enough gold: {resource} costs {price}g, you have {GameState.gold}g."); return; }

        GameState.gold -= price;
        s.cargo.Add(resource);
        Debug.Log($"Bought 1 {resource} at {city.name} for {price}g. Gold: {GameState.gold}.");
    }

    // ---------- REFRESH (every frame; cheap at this scale) ----------

    private void Refresh()
    {
        Ship s = GameState.selected;
        City city = s != null ? GameState.CityAt(s.coord) : null;

        // HUD always visible
        int storage = s != null ? s.def.storage : 0;
        statsText.text = $"Turn {GameState.turn}    Gold: {GameState.gold}g    Cargo: {(s != null ? s.cargo.Count : 0)}/{storage}" +
                         (s != null ? $"    Move: {s.remainingMovement}/{s.def.movement}" : "");

        // Docked panel only when docked
        bool docked = city != null;
        if (panel.activeSelf != docked) panel.SetActive(docked);
        if (!docked) return;

        headerText.text = $"Docked at {city.name}";

        foreach (var row in rows)
        {
            string r = row.def.name;
            int cityPays = city.GetBuyPrice(r);
            int citySells = city.GetSellPriceToPlayer(r);
            float penalty = city.GetSaturationPenalty(r);
            bool produces = System.Array.IndexOf(city.produces, r) >= 0;

            int inHold = 0;
            foreach (var c in s.cargo) if (c == r) inHold++;
            row.nameText.text = inHold > 0 ? $"{r} x{inHold}" : r;

            row.priceText.text = $"{cityPays}g | {citySells}g";

            if (penalty > 0f)
            {
                row.satText.text = $"▼ -{penalty:P0}";
                row.satText.color = new Color(1f, 0.45f, 0.35f);
            }
            else if (produces)
            {
                row.satText.text = "produces";
                row.satText.color = new Color(0.6f, 0.75f, 1f);
            }
            else
            {
                row.satText.text = "premium";
                row.satText.color = new Color(0.55f, 1f, 0.55f);
            }

            row.sellButton.interactable = inHold > 0;
            row.buyButton.interactable = produces && GameState.gold >= citySells && s.cargo.Count < s.def.storage;
        }

        foreach (var sr in shipRows)
        {
            sr.button.interactable = GameState.gold >= sr.def.cost;
        }

        logText.text = city.DebugLog();
    }
}

