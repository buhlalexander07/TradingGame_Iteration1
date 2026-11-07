using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// Instruction screen: shows the rules before the first turn, then can be
/// re-opened at any time by pressing I (playtest fix — two of four testers
/// asked for the instructions to be available while playing).
///
/// NOTE: the help key is I, NOT H. H is the HOLD key (a ship skips the
/// turn), and both scripts read the keyboard, so sharing the key toggled
/// the ship and the screen at the same time.
///
/// Setup: empty GameObject named "InstructionScreen" with this component.
/// Blocks all map clicks while open (fullscreen raycast blocker).
/// </summary>
public class InstructionScreen : MonoBehaviour
{
    private GameObject root;
    private Canvas canvas;

    void Start()
    {
        BuildScreen();
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.I))
        {
            if (root != null && root.activeSelf) CloseScreen();
            else ShowScreen();
        }
    }

    private static Font BuiltinFont()
    {
        return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }

    void BuildScreen()
    {
        // Own canvas, sorted on top of the trade panel / win screen
        GameObject canvasGo = new GameObject("InstructionCanvas");
        canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200; // above everything
        canvasGo.AddComponent<GraphicRaycaster>();
        UnityEngine.UI.CanvasScaler scaler = canvasGo.AddComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        EnsureEventSystem();

        // Fullscreen blocker — swallows every click so the map can't be touched
        root = new GameObject("InstructionBlocker");
        root.transform.SetParent(canvasGo.transform, false);
        Image block = root.AddComponent<Image>();
        block.color = new Color(0f, 0f, 0f, 0.88f);
        block.raycastTarget = true;
        Stretch(block.rectTransform);

        // Centered content panel
        GameObject panelGo = new GameObject("Panel");
        panelGo.transform.SetParent(root.transform, false);
        Image panelImg = panelGo.AddComponent<Image>();
        panelImg.color = new Color(0.12f, 0.12f, 0.14f, 0.98f);
        RectTransform prt = panelImg.rectTransform;
        prt.sizeDelta = new Vector2(1000, 880);
        Stretch(prt);

        // Title
        Text title = MakeText(panelGo.transform, "Trading Game", 42, FontStyle.Bold,
            new Vector2(0, 390), new Vector2(940, 70));
        title.alignment = TextAnchor.MiddleCenter;
        title.color = new Color(0.95f, 0.85f, 0.55f);

        // Body
        Text body = MakeText(panelGo.transform,
            "GOAL: turn your 12 gold into 100 gold. Fewer turns = a better score.\n\n" +
            "YOUR TURN\n" +
            "  - Click a ship to select it, then click a hex to sail there. A Sloop sails up to 7 hexes.\n" +
            "  - TAB cycles ships. SPACE ends the turn.\n" +
            "  - Ships sail on water (blue) and city ports (gold) - never over land (green).\n" +
            "  - You may trade as much as you like while docked at a city.\n\n" +
            "TRADING\n" +
            "  - BUY only what a city grows (the 'produces' tag).\n" +
            "  - A city will NOT buy what it grows itself. Sell it somewhere else - that is the whole game.\n" +
            "  - Cities pay a 50% PREMIUM for goods they don't grow. That is where the money is.\n\n" +
            "THE MARKET REMEMBERS\n" +
            "  - Every SALE is logged in that city (the last 8 sales). Sell the same good twice in one\n" +
            "    city and its price CRASHES - the newest sale costs you 50%.\n" +
            "  - Each turn the log ages one step and prices recover.\n" +
            "  - The SELL PREVIEW column shows what you get now and what the next unit would fetch.\n" +
            "  - Rotate goods - your own success ruins your best run.\n\n" +
            "AUTOMATIC SAILING\n" +
            "  - Select a ship, press R, then click a CITY. That ship will sail there on its own.\n" +
            "  - It uses all its movement every turn until it arrives" +
            "  - The moment it ARRIVES, automatic mode turns off and you have the helm again.\n" +
            "  - A ship on automatic never blocks ending the turn.\n" +
            "  - Press C to cancel a voyage and take the wheel back early.\n\n" +
            "HOLDING\n" +
            "  - Press H to tell a ship to skip the turn. It sits out and will not block ending the turn.\n\n" +
            "ENDING THE TURN\n" +
            "  - You cannot end the turn while a ship still has movement, unless that ship is\n" +
            "    automatic or holding. If it is blocked, you can hold the ships and end the turn anyway.\n\n" +
            "GROWING\n" +
            "  - While docked, buy ships: Sloop 8g (7 hexes, holds 3), Caravel 14g (5 hexes, holds 5),\n" +
            "    Galleon 20g (5 hexes, holds 8).\n" +
            "  - A new ship cannot move the turn it is bought. A second ship runs a second route -\n" +
            "    but watch the income: a ship selling the same good every trip wears its market out.\n\n" +
            "F1 opens and closes this screen at any time.",
            19, FontStyle.Normal, new Vector2(0, -8), new Vector2(940, 740));
        body.alignment = TextAnchor.UpperLeft;
        body.color = new Color(0.9f, 0.9f, 0.9f);

        // Set Sail button
        Button start = MakeButton(panelGo.transform, "SET SAIL", new Vector2(0, -395), new Vector2(300, 66), SetSail);
        var st = start.GetComponentInChildren<Text>();
        if (st != null) st.fontSize = 28;
    }

    void ShowScreen()
    {
        if (root != null) root.SetActive(true);
    }

    void CloseScreen()
    {
        if (root != null) root.SetActive(false);
    }

    void SetSail()
    {
        Debug.Log("Instructions dismissed - set sail Captain.");
        CloseScreen();
        // The component stays alive so F1 can reopen the screen later.
    }

    // ---------- helpers (same patterns as TradePanelUI) ----------

    void EnsureEventSystem()
    {
        // There must be EXACTLY ONE EventSystem in the scene.
        UiEventSystem.EnsureExactlyOne();
    }

    Text MakeText(Transform parent, string content, int size, FontStyle style, Vector2 pos, Vector2 sizeDelta)
    {
        GameObject go = new GameObject("Text");
        go.transform.SetParent(parent, false);
        Text t = go.AddComponent<Text>();
        t.font = BuiltinFont();
        t.fontSize = size;
        t.fontStyle = style;
        t.text = content;
        t.raycastTarget = false;
        RectTransform rt = t.rectTransform;
        rt.sizeDelta = sizeDelta;
        rt.anchoredPosition = pos;
        return t;
    }

    Button MakeButton(Transform parent, string label, Vector2 pos, Vector2 size, UnityEngine.Events.UnityAction onClick)
    {
        GameObject go = new GameObject(label + "Button");
        go.transform.SetParent(parent, false);
        Image img = go.AddComponent<Image>();
        img.color = new Color(0.25f, 0.42f, 0.28f);
        Button b = go.AddComponent<Button>();
        if (onClick != null) b.onClick.AddListener(onClick);
        RectTransform rt = img.rectTransform;
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;
        Text t = MakeText(go.transform, label, 24, FontStyle.Bold, Vector2.zero, size);
        t.alignment = TextAnchor.MiddleCenter;
        t.color = Color.white;
        return b;
    }

    void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}
