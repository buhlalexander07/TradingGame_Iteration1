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
///  - the SELL PREVIEW: what you get now vs. what the next unit would fetch
///  - the city's FIFO sales log
///  - ship purchase buttons
/// Plus always-on-screen: gold/turn/cargo readout, the goal line, and End Turn.
///
/// Playtest changes in this file:
///  - Added the goal readout (testers were uniformly unclear what to do).
///  - Added the sell preview (testers saw prices move but did not plan
///    around them — the price had to be stated before the button).
///  - Fixed the log label: it said "RECENTLY BOUGHT" but the log records SALES.
///  - A city will not buy a resource it grows itself (rule enforced in City.cs).
///  - Exactly one EventSystem is enforced here (duplicates break click routing).
///  - CanvasScaler fully qualified: the bare name resolves to
///    UnityEngine.UIElements.CanvasScaler in Unity 6 and fails to compile.
/// </summary>
public class TradePanelUI : MonoBehaviour
{
    private Canvas canvas;
    private GameObject panel;
    private Text headerText;
    private Text statsText;
    private Text goalText;
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

    /// <summary>
    /// There must be EXACTLY ONE EventSystem in the scene. Two of them each
    /// process input every frame, so which one wins is arbitrary — that is a
    /// classic cause of "the button only works sometimes".
    /// </summary>
    private static void EnsureEventSystem()
    {
        EventSystem[] all = Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None);

        if (all.Length == 0)
        {
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            return;
        }

        EventSystem keep = all[0];
        if (keep.GetComponent<BaseInputModule>() == null)
            keep.gameObject.AddComponent<StandaloneInputModule>();

        for (int i = 1; i < all.Length; i++)
        {
            if (all[i] == null || all[i] == keep) continue;
            Object.Destroy(all[i].gameObject);
        }
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
        RectTransform rt = t.GetComponent<RectTransform>();
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
        RectTransform lrt = lbl.GetComponent<RectTransform>();
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
        UnityEngine.UI.CanvasScaler scaler = canvasGo.AddComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        // ---- Always-on HUD: stats top-left, End Turn top-right ----
        statsText = MakeText(canvas.transform, "", 15,
            new Vector2(0, 1), new Vector2(0, 1), new Vector2(12, -34), new Vector2(560, -8), TextAnchor.MiddleLeft);

        // The goal line — the single clearest playtest fix.
        goalText = MakeText(canvas.transform, "", 17,
            new Vector2(0, 1), new Vector2(0, 1), new Vector2(12, -60), new Vector2(700, -36), TextAnchor.MiddleLeft);
        goalText.color = new Color(0.95f, 0.85f, 0.45f);

        MakeButton(canvas.transform, "End Turn (Space)",
            new Vector2(1, 1), new Vector2(-110, -30), new Vector2(200, 36),
            () => GameState.EndTurn());

        // ---- Docked panel (left side) ----
        panel = new GameObject("DockedPanel", typeof(Image));
        Image pimg = panel.GetComponent<Image>();
        pimg.color = new Color(0, 0, 0, 0.88f);
        RectTransform prt = pimg.rectTransform;
        prt.SetParent(canvas.transform, false);
        prt.anchorMin = new Vector2(0, 0.5f);
        prt.anchorMax = new Vector2(0, 0.5f);
        prt.pivot = new Vector2(0, 0.5f);
        prt.anchoredPosition = new Vector2(10, 0);
        prt.sizeDelta = new Vector2(540, 700);
        panel.SetActive(false);

        // Header
        headerText = MakeText(panel.transform, "", 17,
            new Vector2(0, 1), new Vector2(1, 1), new Vector2(12, -36), new Vector2(-12, -8), TextAnchor.MiddleLeft);

        // Column headers
        MakeText(panel.transform, "GOOD", 12, new Vector2(0, 1), new Vector2(0, 1), new Vector2(12, -58), new Vector2(110, -40), TextAnchor.MiddleLeft);
        MakeText(panel.transform, "PAYS | SELLS", 12, new Vector2(0, 1), new Vector2(0, 1), new Vector2(115, -58), new Vector2(235, -40), TextAnchor.MiddleLeft);
        MakeText(panel.transform, "SELL PREVIEW", 12, new Vector2(0, 1), new Vector2(0, 1), new Vector2(240, -58), new Vector2(360, -40), TextAnchor.MiddleLeft);

        // One row per resource
        float y = 66;
        foreach (var res in GameData.Resources)
        {
            var row = new ResourceRow { def = res };

            row.nameText = MakeText(panel.transform, res.name, 14,
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(12, -y - 22), new Vector2(110, -y), TextAnchor.MiddleLeft);

            row.priceText = MakeText(panel.transform, "", 14,
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(115, -y - 22), new Vector2(235, -y), TextAnchor.MiddleLeft);

            row.satText = MakeText(panel.transform, "", 12,
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(240, -y - 22), new Vector2(360, -y), TextAnchor.MiddleLeft);

            row.sellButton = MakeButton(panel.transform, "SELL",
                new Vector2(0, 1), new Vector2(368, -y - 11), new Vector2(58, 20), null);
            row.buyButton = MakeButton(panel.transform, "BUY",
                new Vector2(0, 1), new Vector2(432, -y - 11), new Vector2(58, 20), null);

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
                new Vector2(0, 1), new Vector2(bx, -y - 12), new Vector2(150, 24), null);
            string s = sd.name; // capture for the closure
            sr.button.onClick.AddListener(() =>
            {
                Ship sel = GameState.selected;
                if (sel == null) return;
                Ship bought = GameState.BuyShip(s, sel.coord, Ship.prefab);
                if (bought != null) bought.boughtThisRun = true;
            });
            sr.label = sr.button.GetComponentInChildren<Text>();
            shipRows.Add(sr);
            bx += 155;
        }
        y += 34;

        // Saturation log
        MakeText(panel.transform, "RECENT SALES HERE (newest first — prices recover as entries age):", 11,
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
        if (!city.CanSellTo(resource))
        {
            Debug.Log($"{city.name} grows {resource} — it won't buy its own goods.");
            return;
        }

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
                         (s != null ? $"    Move: {s.remainingMovement}/{s.def.movement}" : "") +
                         (s != null && s.isAuto ? "    [AUTO]" : "");

        // The goal — always visible, always spelled out.
        int goal = GameData.TARGET_GOLD;
        int remaining = Mathf.Max(0, goal - GameState.gold);
        goalText.text = $"GOAL: reach {goal}g gold  ({GameState.gold}/{goal}, {remaining} to go)  " +
                        $"— fewer turns is a better score.";

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

            // Sell preview: what this unit fetches now, and what the NEXT
            // unit would fetch after this sale lands at the front of the log.
            int after = city.GetBuyPriceAfterSale(r);
            if (penalty > 0f)
            {
                row.satText.text = $"{cityPays}g → {after}g";
                row.satText.color = new Color(1f, 0.45f, 0.35f);
            }
            else
            {
                row.satText.text = produces ? "grows it - no sell" : "premium";
                row.satText.color = produces
                    ? new Color(0.6f, 0.75f, 1f)
                    : new Color(0.55f, 1f, 0.55f);
            }

            row.sellButton.interactable = inHold > 0 && !produces;
            row.buyButton.interactable = produces && GameState.gold >= citySells && s.cargo.Count < s.def.storage;
        }

        foreach (var sr in shipRows)
        {
            sr.button.interactable = GameState.gold >= sr.def.cost;
        }

        logText.text = city.DebugLog();
    }
}
