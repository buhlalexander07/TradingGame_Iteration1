using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// Instruction screen: shows the rules before the first turn, then removes itself.
/// Setup: empty GameObject named "InstructionScreen" with this component. Nothing else.
/// Blocks all map clicks until dismissed (fullscreen raycast blocker).
/// </summary>
public class InstructionScreen : MonoBehaviour
{
    private GameObject root;

    private static Font BuiltinFont()
    {
        return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }

    void Start()
    {
        BuildScreen();
    }

    void BuildScreen()
    {
        // Own canvas, sorted on top of the trade panel / win screen
        GameObject canvasGo = new GameObject("InstructionCanvas");
        canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200; // above everything
        canvasGo.AddComponent<GraphicRaycaster>();
        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
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
        prt.sizeDelta = new Vector2(900, 780);
        Stretch(prt);

        // Title
        Text title = MakeText(panelGo.transform, "Trading Game", 42, FontStyle.Bold,
            new Vector2(0, 330), new Vector2(840, 70));
        title.alignment = TextAnchor.MiddleCenter;
        title.color = new Color(0.95f, 0.85f, 0.55f);

        // Body
        Text body = MakeText(panelGo.transform,
            "You command a small trading charter. Turn your 12 gold into 200 gold as fast as you can.\n\n" +
            "YOUR TURN\n" +
            "  - Move your ship (up to 5 hexes for a Sloop), then trade freely at any city you dock at.\n" +
            "  - Click a ship to select it, then click a hex to sail there. TAB cycles ships, SPACE ends the turn.\n" +
            "  - Ships can only sail on water (blue) and city ports (gold) - never over land (green).\n\n" +
            "TRADING\n" +
            "  - Each city SHOWS live prices: what it pays you | what it charges you.\n" +
            "  - You can only BUY goods a city produces (the 'produces' tag). You can SELL anything, anywhere.\n" +
            "  - Cities pay a PREMIUM for goods they don't produce - that's where the money is.\n\n" +
            "THE MARKET REMEMBERS\n" +
            "  - Every sale is logged. Dump the same good in one city and its price CRASHES (red arrow),\n" +
            "    recovering a little each turn. Rotate goods and routes - your own success ruins your best run.\n\n" +
            "GROWING\n" +
            "  - While docked, buy ships: Sloop 10g (fast), Caravel 15g, Galleon 20g (big hold).\n" +
            "  - New ships cannot move the turn they are purchased. Click a city's gold hex to dock.",
            24, FontStyle.Normal, new Vector2(0, -10), new Vector2(840, 620));
        body.alignment = TextAnchor.UpperLeft;
        body.color = new Color(0.9f, 0.9f, 0.9f);

        // Set Sail button
        Button start = MakeButton(panelGo.transform, "SET SAIL", new Vector2(0, -330), new Vector2(300, 70), SetSail);
        var st = start.GetComponentInChildren<Text>();
        if (st != null) st.fontSize = 30;
    }

    void SetSail()
    {
        Debug.Log("Instructions dismissed - set sail Captain.");
        Destroy(root);
        Destroy(gameObject); // remove this component too; the game proceeds
    }

    // ---------- helpers (same patterns as TradePanelUI) ----------

    private Canvas canvas;

    void EnsureEventSystem()
    {
        if (FindFirstObjectByType<EventSystem>() == null)
        {
            GameObject es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }
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

