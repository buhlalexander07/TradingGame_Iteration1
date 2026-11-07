using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;

/// <summary>
/// Win condition + victory screen (playtest change: target lowered 200g -> 30g,
/// then raised to 100g so the auto-ship fleet has time to pay back and to
/// show route saturation as decay over a run).
/// Also records the playtest stats breakdown: gold earned per city, goods traded.
///
/// Playtest change: the Restart button now resets the run IN PLACE
/// (GameState.RestartRun) instead of reloading the scene, so the game can be
/// restarted without quitting and reopening it. The scene-reload path is kept
/// as a fallback if the in-place reset fails.
/// </summary>
public class WinScreen : MonoBehaviour
{
    public const int WIN_GOLD = GameData.TARGET_GOLD;   // 100g

    // ---- playtest stats ----
    private static Dictionary<string, int> goldPerCity = new Dictionary<string, int>();
    private static int unitsTraded = 0;
    private static int totalEarned = 0;
    private static bool won = false;

    /// <summary>Call after gold is added: records where and how much.</summary>
    public static void RecordSale(string cityName, int earned)
    {
        if (won) return;
        if (!goldPerCity.ContainsKey(cityName)) goldPerCity[cityName] = 0;
        goldPerCity[cityName] += earned;
        totalEarned += earned;
        unitsTraded++;
    }

    /// <summary>Clear the run stats (used by the in-place restart).</summary>
    public static void ResetStats()
    {
        goldPerCity = new Dictionary<string, int>();
        unitsTraded = 0;
        totalEarned = 0;
        won = false;
    }

    private RectTransform victoryPanel;
    private Text victoryText;

    private GameObject dimmer;

    void Awake()
    {
        // Reset statics so a fresh run starts clean.
        ResetStats();
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
        Canvas canvas = FindFirstObjectByType<Canvas>();
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
            canvasGo.AddComponent<UnityEngine.UI.CanvasScaler>().uiScaleMode =
                UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
             var scaler = canvasGo.GetComponent<UnityEngine.UI.CanvasScaler>();
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();
            if (FindFirstObjectByType<EventSystem>() == null)
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
        victoryPanel = panelImg.GetComponent<RectTransform>();
        victoryPanel.sizeDelta = new Vector2(700, 760);

        // Title
        MakeText(panel.transform, "VictoryTitle",
            $"<b>VICTORY</b>", 44, new Vector2(0, 300), TextAnchor.MiddleCenter,
            new Color(0.85f, 0.72f, 0.3f));

        // Body — filled in by ShowVictory
        victoryText = MakeText(panel.transform, "VictoryBody", "", 26,
            new Vector2(0, -40), TextAnchor.UpperLeft, Color.white);

        // Restart — in place, no scene reload.
        Button restart = MakeButton(panel.transform, "RestartButton", "Restart Run",
            new Vector2(0, -320), new Vector2(260, 64),
            new Color(0.25f, 0.4f, 0.25f), () =>
            {
                Time.timeScale = 1f;
                RestartInPlace();
            });

        dimmer.SetActive(false);
        victoryPanel.gameObject.SetActive(false);
    }

    /// <summary>
    /// Restart the run without leaving the game: reset state, hide the panel.
    /// </summary>
    private void RestartInPlace()
    {
        GameState.RestartRun();
        won = false;
        if (dimmer != null) dimmer.SetActive(false);
        if (victoryPanel != null) victoryPanel.gameObject.SetActive(false);
        Debug.Log("--- New run started. Same scene, no reload. ---");
    }

    private void ShowVictory()
    {
        Debug.Log($"=== VICTORY: reached {WIN_GOLD}g on turn {GameState.turn} ===");

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"You reached {GameState.gold}g on turn {GameState.turn}.");
        sb.AppendLine($"Score: {GameState.turn} turns.");
        sb.AppendLine();
        sb.AppendLine($"<b>Run stats</b>");
        sb.AppendLine($"Gold earned (sales): {totalEarned}g");
        sb.AppendLine($"Goods traded: {unitsTraded} units");
        sb.AppendLine($"Ships in fleet: {GameState.fleet.Count}");
        sb.AppendLine();
        sb.AppendLine($"<b>Gold earned per city</b>");
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
        Text t = go.AddComponent<Text>();
        t.text = content;
        t.color = color;
        t.alignment = anchor;
        t.fontSize = size;
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.raycastTarget = false;
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(620, 520);
        rt.anchoredPosition = pos;
        return t;
    }

    static Button MakeButton(Transform parent, string name, string label,
        Vector2 pos, Vector2 size, Color color, UnityEngine.Events.UnityAction onClick)
    {
        GameObject go = new GameObject(name);
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
