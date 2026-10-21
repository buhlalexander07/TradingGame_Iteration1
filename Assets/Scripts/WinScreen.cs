using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;

/// <summary>
/// Win condition + victory screen (locked design: reach 50g, score = turns taken).
/// Also records the playtest stats breakdown: gold earned per city, goods traded.
/// Builds its own UI in code — one empty GameObject with this component is the only setup.
/// </summary>
public class WinScreen : MonoBehaviour
{
    public const int WIN_GOLD = 200;   // placeholder target — tune after playtest

    // ---- playtest stats ----
    private static Dictionary<string, int> goldPerCity = new Dictionary<string, int>();
    private static int unitsTraded = 0;
    private static int totalEarned = 0;
    private static bool won = false;

    /// <summary>Call from SellOne after gold is added: records where and how much.</summary>
    public static void RecordSale(string cityName, int earned)
    {
        if (won) return;
        if (!goldPerCity.ContainsKey(cityName)) goldPerCity[cityName] = 0;
        goldPerCity[cityName] += earned;
        totalEarned += earned;
        unitsTraded++;
    }

    private RectTransform victoryPanel;
    private Text victoryText;

    private GameObject dimmer;

    void Awake()
    {
        // Reset statics so a scene reload (Restart button) starts a fresh run.
        goldPerCity = new Dictionary<string, int>();
        unitsTraded = 0;
        totalEarned = 0;
        won = false;
    }

    void Start()
    {
        BuildVictoryPanel();
    }

    void Update()
    {
        if (!won && GameState.gold >= WIN_GOLD)
        {
            won = true;
            ShowVictory();
        }
    }

    // ---------------- UI (built in code, uGUI) ----------------

    void BuildVictoryPanel()
    {
        // Reuse the trade panel's canvas if one exists; otherwise make our own.
        Canvas canvas = FindObjectOfType<Canvas>();
        GameObject canvasGo;
        if (canvas != null)
        {
            canvasGo = canvas.gameObject;
        }
        else
        {
            canvasGo = new GameObject("VictoryCanvas");
            canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGo.AddComponent<CanvasScaler>().uiScaleMode =
                CanvasScaler.ScaleMode.ScaleWithScreenSize;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();
            if (FindObjectOfType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<EventSystem>();
                es.AddComponent<StandaloneInputModule>();
            }
        }

        // Fullscreen dimmer
        GameObject dim = new GameObject("VictoryDim");
        dim.transform.SetParent(canvasGo.transform, false);
        Image dimImg = dim.AddComponent<Image>();
        dimImg.color = new Color(0f, 0f, 0f, 0.85f);
        dimImg.raycastTarget = true;   // block clicks behind the victory screen
        RectTransform dimRt = dim.GetComponent<RectTransform>();
        Stretch(dimRt);
        dimmer = dim;

        // Center panel
        GameObject panel = new GameObject("VictoryPanel");
        panel.transform.SetParent(dim.transform, false);
        Image panelImg = panel.AddComponent<Image>();
        panelImg.color = new Color(0.13f, 0.15f, 0.12f);
        victoryPanel = panel.GetComponent<RectTransform>();
        victoryPanel.sizeDelta = new Vector2(700, 760);

        // Title
        MakeText(panel.transform, "VictoryTitle",
            $"<b>VICTORY</b>", 44, new Vector2(0, 300), TextAnchor.MiddleCenter,
            new Color(0.85f, 0.72f, 0.3f));

        // Body — filled in by ShowVictory
        victoryText = MakeText(panel.transform, "VictoryBody", "", 26,
            new Vector2(0, -40), TextAnchor.UpperLeft, Color.white);

        // Restart button
        Button restart = MakeButton(panel.transform, "RestartButton", "Restart Run",
            new Vector2(0, -320), new Vector2(260, 64),
            new Color(0.25f, 0.4f, 0.25f), () =>
            {
                Time.timeScale = 1f;
                SceneManager.LoadScene(SceneManager.GetActiveScene().name);
            });

        dimmer.SetActive(false);
        victoryPanel.gameObject.SetActive(false);
    }

    void ShowVictory()
    {
        Debug.Log($"=== VICTORY: reached {WIN_GOLD}g on turn {GameState.turn} ===");

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"You reached {GameState.gold}g on turn {GameState.turn}.");
        sb.AppendLine();
        sb.AppendLine($"<b>Run stats</b>");
        sb.AppendLine($"Gold earned (sales): {totalEarned}g");
        sb.AppendLine($"Goods traded: {unitsTraded} units");
        sb.AppendLine($"Ships in fleet: {GameState.fleet.Count}");
        sb.AppendLine();
        sb.AppendLine("<b>Gold earned per city</b>");
        if (goldPerCity.Count == 0)
        {
            sb.AppendLine("(no sales recorded)");
        }
        else
        {
            foreach (var kv in goldPerCity)
                sb.AppendLine($"{kv.Key}: {kv.Value}g");
        }

        victoryText.text = sb.ToString();
        dimmer.SetActive(true);
        victoryPanel.gameObject.SetActive(true);

        // Freeze the world behind the victory screen.
        Time.timeScale = 0f;
    }

    // ---------------- helpers ----------------

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    static Text MakeText(Transform parent, string name, string content, int size,
        Vector2 pos, TextAnchor anchor, Color color)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        Text t = go.AddComponent<Text>();
        t.text = content;
        t.color = color;
        t.alignment = anchor;
        t.fontSize = size;
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.raycastTarget = false;
        RectTransform rt = t.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(620, 520);
        rt.anchoredPosition = pos;
        return t;
    }

    static Button MakeButton(Transform parent, string name, string label,
        Vector2 pos, Vector2 size, Color color, UnityEngine.Events.UnityAction onClick)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        Image img = go.AddComponent<Image>();
        img.color = color;
        Button b = go.AddComponent<Button>();
        b.targetGraphic = img;
        if (onClick != null) b.onClick.AddListener(onClick);

        GameObject lbl = new GameObject("Label");
        lbl.transform.SetParent(go.transform, false);
        Text t = lbl.AddComponent<Text>();
        t.text = label;
        t.color = Color.white;
        t.alignment = TextAnchor.MiddleCenter;
        t.fontSize = 24;
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.raycastTarget = false;
        RectTransform lrt = lbl.GetComponent<RectTransform>();
        Stretch(lrt);

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;
        return b;
    }
}

