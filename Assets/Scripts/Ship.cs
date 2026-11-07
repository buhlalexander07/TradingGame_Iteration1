using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Runtime game state: the player's gold, the turn counter, the fleet,
/// and the runtime City instances (separate from CityDef data).
/// </summary>
public static class GameState
{
    public static int gold = GameData.STARTING_GOLD;   // starting gold
    public static int turn = 1;
    public static List<City> Cities = new List<City>();
    public static List<Ship> fleet = new List<Ship>();
    public static Ship selected;                        // which ship receives move orders

    // Fleet caps from the rules doc: 26 total (13 sloops, 8 caravels, 5 galleons)
    private static readonly Dictionary<string, int> fleetCaps = new Dictionary<string, int>
    {
        { "Sloop", 13 }, { "Caravel", 8 }, { "Galleon", 5 }
    };

    /// <summary>Build runtime City instances from GameData defs. Call once.</summary>
    public static void Init()
    {
        if (Cities.Count > 0) return;
        foreach (var def in GameData.Cities)
            Cities.Add(new City(def));
    }

    public static City CityAt(Vector2Int coord)
    {
        foreach (var c in Cities)
            if (c != null && c.coord == coord) return c;
        return null;
    }

    /// <summary>
    /// The city nearest to a world point, within maxDistance.
    /// City markers float slightly above their tile, so converting the click
    /// point straight to a hex can land on a neighbouring tile. Searching by
    /// distance instead is immune to that offset.
    /// </summary>
    public static City NearestCityToWorld(Vector3 world, float maxDistance)
    {
        if (HexGrid.Instance == null) return null;
        City best = null;
        float bestSqr = maxDistance * maxDistance;
        foreach (var c in Cities)
        {
            if (c == null) continue;
            Vector3 p = HexGrid.Instance.AxialToWorld(c.coord.x, c.coord.y);
            float d = (p - world).sqrMagnitude;
            if (d < bestSqr) { bestSqr = d; best = c; }
        }
        return best;
    }

    public static Ship ShipAt(Vector2Int coord)
    {
        foreach (var s in fleet)
            if (s != null && s.coord == coord) return s;
        return null;
    }

    /// <summary>
    /// Try to buy a ship of the given type, spawning it at (or adjacent to) a city.
    /// Returns null and logs why if the purchase is impossible.
    /// </summary>
    public static Ship BuyShip(string shipName, Vector2Int cityCoord, Ship prefab)
    {
        ShipDef def = GameData.GetShip(shipName);
        if (def.name != shipName) { Debug.Log($"No ship def named {shipName}."); return null; }
        if (gold < def.cost) { Debug.Log($"Not enough gold: {shipName} costs {def.cost}g, you have {gold}g."); return null; }

        // Fleet cap check (per type, and 26 total)
        int ofType = 0;
        foreach (var s in fleet) if (s != null && s.def.name == def.name) ofType++;
        if (ofType >= fleetCaps[def.name]) { Debug.Log($"Fleet cap reached for {def.name} ({fleetCaps[def.name]})."); return null; }
        if (fleet.Count >= 26) { Debug.Log("Total fleet cap reached (26)."); return null; }

        // Find a free hex: the city's hex, or the nearest adjacent one
        // (no two ships share a tile — locked in design)
        Vector2Int spot = FindFreeHexNear(cityCoord);
        if (spot == cityCoord && ShipAt(cityCoord) != null)
        { Debug.Log("No free hex adjacent to the city to place the new ship."); return null; }

        gold -= def.cost;
        Ship ship = Object.Instantiate(prefab);
        ship.name = def.name;
        ship.def = def;
        ship.coord = spot;
        ship.transform.position = HexGrid.Instance.AxialToWorld(spot.x, spot.y);
        ship.ResetToStart(spot);
        ship.boughtThisRun = true;
        ship.UpdateVisualMode();
        fleet.Add(ship);
        selected = ship;
        Debug.Log($"Bought a {def.name} for {def.cost}g. Gold: {gold}. Fleet: {fleet.Count} ships.");
        return ship;
    }

    private static Vector2Int FindFreeHexNear(Vector2Int center)
    {
        if (ShipAt(center) == null) return center;
        foreach (var n in HexGrid.NeighborOffsets)
        {
            Vector2Int c = center + n;
            if (HexGrid.Instance.IsValid(c) && HexGrid.Instance.IsTilePassable(c) && ShipAt(c) == null) return c;
        }
        return center; // caller checks occupancy again
    }

    /// <summary>
    /// End turn. The automatic-sailing system runs the end-turn gate first:
    /// the turn may only end when every ship is out of movement, set to
    /// automatic, or explicitly told to skip the turn.
    /// If the AutoShipSystem object is missing from the scene we create one
    /// rather than silently ending the turn with no automatic sailing at all.
    /// </summary>
    public static void EndTurn()
    {
        AutoShipSystem sys = AutoShipSystem.Instance;
        if (sys == null)
        {
            Debug.LogWarning("AutoShipSystem was not found in the scene — creating it now so automatic sailing works.");
            sys = AutoShipSystem.Ensure();
        }
        sys.RequestEndTurn();
    }

    /// <summary>Advance the turn: age the markets, refill movement, bump the counter.</summary>
    public static void AdvanceTurn()
    {
        turn++;
        foreach (var c in Cities)
            if (c != null) c.TickDecay();
        foreach (var s in fleet)
        {
            if (s == null) continue;
            s.remainingMovement = s.def.movement;
            // HOLD lasts only the turn it was set on. Release it now so the
            // ship must be moved, set automatic, or re-held next turn.
            s.holdTurn = false;
        }
        Ship.RefreshAllVisuals();
        Debug.Log($"--- Turn {turn}. All {fleet.Count} ships reset to full movement. ---");
    }

    /// <summary>
    /// Restart the run in place — no scene reload, no quitting the game.
    /// Resets gold, turn, markets, and the fleet back to a single Sloop.
    /// </summary>
    public static void RestartRun()
    {
        gold = GameData.STARTING_GOLD;
        turn = 1;

        foreach (var c in Cities)
            if (c != null) c.ResetLog();

        // Keep the original ship, destroy any that were bought.
        Ship keeper = null;
        for (int i = fleet.Count - 1; i >= 0; i--)
        {
            Ship s = fleet[i];
            if (s == null) { fleet.RemoveAt(i); continue; }
            if (keeper == null && s.def.name == "Sloop" && !s.boughtThisRun)
            {
                keeper = s;
                continue;
            }
            fleet.RemoveAt(i);
            Object.Destroy(s.gameObject);
        }

        if (keeper != null)
        {
            keeper.ResetToStart(GameData.Cities[0].coord);
            keeper.remainingMovement = keeper.def.movement;
            keeper.UpdateVisualMode();
            selected = keeper;
        }

        if (AutoShipSystem.Instance != null) AutoShipSystem.Instance.ResetUI();

        WinScreen.ResetStats();
        Debug.Log("--- Run restarted in place. ---");
    }
}

/// <summary>
/// The player's ship: click-to-move with BFS pathing over the hex grid,
/// movement points per turn, and cargo. Supports a fleet — click one of
/// your ships to select it, then click a hex to move it.
///
/// Automation model (playtest-driven change):
///  - The ONLY automation is a destination city. Set it from the fleet panel
///    (AUTO button) or with R, then click a city on the map.
///  - An automatic ship uses ALL of its movement every turn to sail toward
///    that destination, across as many turns as it needs, until it arrives
///    or you cancel it.
///  - On arrival the ship hands control back to the player.
///  - A ship can also be told to HOLD (skip the turn). Held ships and
///    automatic ships do not block the end of the turn.
///
/// Visual state is shown by COLOUR, never by size:
///   manual = original colour, selected = gold body + pulsing halo,
///   picking a destination = yellow, automatic = cyan, holding = amber.
/// </summary>
[RequireComponent(typeof(Collider))]
public class Ship : MonoBehaviour
{
    [Header("Ship definition (first ship = Sloop)")]
    public ShipDef def;
    public Vector2Int coord;
    public int remainingMovement;
    public List<string> cargo = new List<string>();

    [Header("Automation")]
    public bool isAuto = false;          // sailing itself toward autoTarget
    public bool hasAutoTarget = false;   // true once a destination city has been picked
    public Vector2Int autoTarget;        // the destination city hex
    public bool destMode = false;        // clicking the map picks the destination city
    public bool holdTurn = false;        // told to skip this turn (does not block ending it)
    public bool boughtThisRun = false;   // used by the in-place restart

    /// <summary>Drag your ship prefab here (or leave it — the first ship in the scene sets it).</summary>
    public static Ship prefab;

    private static int inputFrame = -1;   // frame count of the last handled input

    private bool isMoving = false;
    private bool firstShip = false;
    private float pulse = 0f;

    // --- visual state ---
    private GameObject ringObj;
    private TextMesh tagText;
    private Renderer[] shipRenderers;
    private Color[] baseColors;

    private static readonly Color ManualTint = new Color(1f, 1f, 1f);
    private static readonly Color AutoTint = new Color(0.30f, 0.85f, 1f);
    private static readonly Color HoldTint = new Color(1f, 0.72f, 0.25f);
    private static readonly Color PickTint = new Color(1f, 0.92f, 0.25f);
    // Gold, so "selected" never reads as "a mode" and never blends into the map.
    private static readonly Color SelectTint = new Color(1f, 0.82f, 0.10f);

    void Awake()
    {
        if (prefab == null) { prefab = this; firstShip = true; }
        BuildVisuals();
    }

    void Start()
    {
        GameState.Init();

        if (firstShip)
        {
            def = GameData.GetShip("Sloop");
            remainingMovement = def.movement;
            coord = GameData.Cities[0].coord;   // start docked at Caleana
            transform.position = HexGrid.Instance.AxialToWorld(coord.x, coord.y);
            GameState.fleet.Add(this);
            GameState.selected = this;
            Debug.Log($"Turn {GameState.turn}. Gold: {GameData.STARTING_GOLD}. Cargo: {cargo.Count}/{def.storage}. " +
                      $"Click your ship then a hex to move (up to {def.movement} hexes). " +
                      $"Space = end turn. H = hold this ship. R or the AUTO button = set a destination city.");
        }
        UpdateVisualMode();
    }

    void Update()
    {
        // A modal owns the screen: no map input while it is up.
        if (AutoShipSystem.Instance != null && AutoShipSystem.Instance.IsModalOpen) return;

        // While automatic ships are sailing the player still gets one key:
        // C cancels the voyage and hands the ships back.
        if (AutoShipSystem.Instance != null && AutoShipSystem.Instance.Resolving)
        {
            if (Input.GetKeyDown(KeyCode.C))
            {
                Ship cancelled = CancelAllVoyages();
                if (cancelled != null)
                {
                    GameState.selected = cancelled;
                    cancelled.UpdateVisualMode();
                }
                if (AutoShipSystem.Instance != null)
                    AutoShipSystem.Instance.ShowBanner("Automatic sailing cancelled.");
            }
            return;
        }

        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(0)) return;
        if (isMoving) return;

        // Only one ship may handle input per frame — every ship runs Update(),
        // and without this guard each keypress fires once per ship in the fleet.
        bool anyInput = Input.GetMouseButtonDown(0)
                    || Input.GetKeyDown(KeyCode.Space)
                    || Input.GetKeyDown(KeyCode.Tab)
                    || Input.GetKeyDown(KeyCode.Alpha1)
                    || Input.GetKeyDown(KeyCode.Alpha2)
                    || Input.GetKeyDown(KeyCode.Alpha3)
                    || Input.GetKeyDown(KeyCode.R)
                    || Input.GetKeyDown(KeyCode.H)
                    || Input.GetKeyDown(KeyCode.C);
        if (!anyInput) return;
        if (inputFrame == Time.frameCount) return;
        inputFrame = Time.frameCount;

        Ship sel = GameState.selected;

        // Left click: select an owned ship, or set a destination, or move.
        if (Input.GetMouseButtonDown(0))
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                Ship clicked = hit.collider.GetComponentInParent<Ship>();
                if (clicked != null && GameState.fleet.Contains(clicked))
                {
                    GameState.selected = clicked;
                    clicked.destMode = false;
                    clicked.UpdateVisualMode();
                    Debug.Log($"Selected {clicked.def.name} ({clicked.remainingMovement} movement left).");
                    RefreshAllVisuals();
                    return;
                }

                if (sel != null)
                {
                    if (sel.destMode)
                    {
                        City dc = GameState.NearestCityToWorld(hit.point, 2.6f);
                        if (dc == null) dc = GameState.CityAt(HexGrid.Instance.WorldToAxial(hit.point));

                        if (dc == null)
                        {
                            Debug.Log("Auto destinations must be a CITY — click a city.");
                            if (AutoShipSystem.Instance != null)
                                AutoShipSystem.Instance.ShowBanner("That is not a city. Click a city to set the destination.");
                        }
                        else
                        {
                            sel.SetDestination(dc.coord);
                            Debug.Log($"{sel.def.name} set to AUTO -> {dc.name}. It will sail there on its own, " +
                                      $"using all of its movement each turn, until it arrives. C cancels.");
                            if (AutoShipSystem.Instance != null)
                                AutoShipSystem.Instance.ShowBanner($"{sel.def.name} AUTO -> {dc.name}. End the turn to sail.");
                        }
                    }
                    else
                    {
                        sel.TryMove(HexGrid.Instance.WorldToAxial(hit.point));
                    }
                }
            }
        }

        // Space = end turn (blocked if a ship still has movement and isn't held/automatic)
        if (Input.GetKeyDown(KeyCode.Space))
            GameState.EndTurn();

        // R toggles destination mode — pick the city the ship will sail to on its own
        if (Input.GetKeyDown(KeyCode.R) && sel != null)
        {
            sel.destMode = !sel.destMode;
            if (sel.destMode) sel.holdTurn = false;
            sel.UpdateVisualMode();
            if (sel.destMode)
            {
                Debug.Log("Destination mode ON for " + sel.def.name + " — click a city to set where it sails by itself.");
                if (AutoShipSystem.Instance != null)
                    AutoShipSystem.Instance.ShowBanner("Click a CITY on the map to set " + sel.def.name + "'s destination.");
            }
            else
            {
                Debug.Log("Destination mode OFF for " + sel.def.name + ".");
            }
        }

        // H tells the selected ship to skip the turn (it will not block ending the turn)
        if (Input.GetKeyDown(KeyCode.H) && sel != null)
        {
            sel.SetHold(!sel.holdTurn);
            Debug.Log(sel.holdTurn
                ? $"{sel.def.name} is HOLDING — it skips this turn and will not block ending it."
                : $"{sel.def.name} is no longer holding.");
        }

        // C cancels an automatic voyage and hands the ship back to the player
        if (Input.GetKeyDown(KeyCode.C) && sel != null && sel.HasDestination)
        {
            sel.ClearDestination();
            Debug.Log($"Automatic sailing cancelled for {sel.def.name}.");
            if (AutoShipSystem.Instance != null)
                AutoShipSystem.Instance.ShowBanner("Automatic sailing cancelled for " + sel.def.name + ".");
        }

        // Debug ship purchasing (the trade panel UI has real buttons too):
        if (sel != null && GameState.CityAt(sel.coord) != null)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1)) BuyShipAt("Sloop");
            if (Input.GetKeyDown(KeyCode.Alpha2)) BuyShipAt("Caravel");
            if (Input.GetKeyDown(KeyCode.Alpha3)) BuyShipAt("Galleon");
        }

        if (Input.GetKeyDown(KeyCode.Tab) && GameState.fleet.Count > 0)
        {
            int idx = GameState.fleet.IndexOf(GameState.selected);
            GameState.selected = GameState.fleet[(idx + 1) % GameState.fleet.Count];
            Debug.Log($"Selected {GameState.selected.def.name} ({GameState.selected.remainingMovement} movement left).");
            RefreshAllVisuals();
        }
    }

    void LateUpdate()
    {
        // Keep the floating mode tag facing the camera.
        if (tagText != null && Camera.main != null)
            tagText.transform.rotation = Camera.main.transform.rotation;

        // The selected ship's halo pulses so it is unmistakable from above.
        // The ship itself never changes size.
        if (ringObj != null && ringObj.activeSelf && GameState.selected == this)
        {
            pulse += Time.deltaTime * 4f;
            float k = 1.9f + Mathf.Sin(pulse) * 0.30f;
            ringObj.transform.localScale = new Vector3(k, k, 1f);
        }
    }

    private void BuyShipAt(string name)
    {
        Ship sel = GameState.selected;
        if (sel == null) return;
        Ship bought = GameState.BuyShip(name, sel.coord, prefab);
        if (bought != null) bought.boughtThisRun = true;
    }

    public void TryMove(Vector2Int target)
    {
        if (!HexGrid.Instance.IsValid(target))
        {
            Debug.Log("That hex is outside the map.");
            return;
        }
        if (target == coord) return;
        if (GameState.ShipAt(target) != null)
        {
            Debug.Log("One of your ships is already on that hex — no two ships share a tile.");
            return;
        }
        if (!HexGrid.Instance.IsTilePassable(target))
        {
            Debug.Log("Ships can't sail over land — stay to the water, captain.");
            return;
        }

        var path = HexGrid.Instance.FindPath(coord, target);
        if (path == null) return;

        int cost = path.Count - 1;
        if (cost > remainingMovement)
        {
            Debug.Log($"Not enough movement: {cost} hexes needed, {remainingMovement} left this turn.");
            return;
        }
        StartCoroutine(MoveAlongPath(path));
    }

    private IEnumerator MoveAlongPath(List<Vector2Int> path)
    {
        isMoving = true;
        foreach (var step in path)
        {
            if (step == coord) continue;
            yield return SailStep(step);
        }
        isMoving = false;

        City city = GameState.CityAt(coord);
        if (city != null)
            Debug.Log($"Docked at {city.name}. 1/2/3 = buy a ship here; trading comes with the trade panel. ({remainingMovement} movement left)");
        else if (remainingMovement > 0)
            Debug.Log($"{remainingMovement} movement left — click another hex, or Space to end turn.");
    }

    /// <summary>
    /// Sail one hex. Shared by manual movement and the auto-sailing system so
    /// both animate identically.
    /// </summary>
    public IEnumerator SailStep(Vector2Int step)
    {
        Vector3 start = transform.position;
        Vector3 end = HexGrid.Instance.AxialToWorld(step.x, step.y);
        Vector3 dir = (end - start).normalized;
        Quaternion targetRot = dir != Vector3.zero ? Quaternion.LookRotation(dir) : transform.rotation;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime * 6f;
            transform.position = Vector3.Lerp(start, end, t);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, t);
            yield return null;
        }
        transform.rotation = targetRot;   // snap exactly at step end
        coord = step;
        remainingMovement--;
    }

    /// <summary>True if this ship has a destination city set.</summary>
    public bool HasDestination => hasAutoTarget;

    /// <summary>The city this ship is sailing to, or null.</summary>
    public City TargetCity => hasAutoTarget ? GameState.CityAt(autoTarget) : null;

    /// <summary>
    /// Set the city this ship sails to automatically. It uses all of its
    /// movement every turn until it gets there; arriving hands control back.
    /// </summary>
    public void SetDestination(Vector2Int cityHex)
    {
        autoTarget = cityHex;
        hasAutoTarget = true;
        isAuto = true;
        destMode = false;
        holdTurn = false;
        UpdateVisualMode();
    }

    /// <summary>Clear the destination and return the ship to manual control.</summary>
    public void ClearDestination()
    {
        hasAutoTarget = false;
        isAuto = false;
        destMode = false;
        UpdateVisualMode();
    }

    /// <summary>Tell the ship to skip the turn (it will not block ending it).</summary>
    public void SetHold(bool value)
    {
        holdTurn = value;
        if (value) destMode = false;
        UpdateVisualMode();
    }

    /// <summary>Clear route state and cargo, and place the ship at the given hex.</summary>
    public void ResetToStart(Vector2Int spot)
    {
        cargo.Clear();
        hasAutoTarget = false;
        autoTarget = Vector2Int.zero;
        isAuto = false;
        destMode = false;
        holdTurn = false;
        coord = spot;
        transform.position = HexGrid.Instance.AxialToWorld(spot.x, spot.y);
        remainingMovement = 0;
        boughtThisRun = false;
        UpdateVisualMode();
    }

    // ------------------------------------------------------------- visuals

    private void BuildVisuals()
    {
        if (shipRenderers == null)
        {
            shipRenderers = GetComponentsInChildren<Renderer>();
            baseColors = new Color[shipRenderers.Length];
            for (int i = 0; i < shipRenderers.Length; i++)
                if (shipRenderers[i] != null) baseColors[i] = shipRenderers[i].material.color;
        }

        if (ringObj == null)
        {
            ringObj = GameObject.CreatePrimitive(PrimitiveType.Quad);
            ringObj.name = "ModeRing";
            ringObj.transform.SetParent(transform, false);
            ringObj.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            ringObj.transform.localPosition = new Vector3(0f, 0.04f, 0f);
            ringObj.transform.localScale = new Vector3(1.9f, 1.9f, 1f);
            Collider ringCol = ringObj.GetComponent<Collider>();
            if (ringCol != null) Destroy(ringCol);   // never intercept clicks
            Shader sh = Shader.Find("Sprites/Default");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh != null) ringObj.GetComponent<Renderer>().material = new Material(sh);
        }

        if (tagText == null)
        {
            GameObject go = new GameObject("ModeTag", typeof(TextMesh));
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 1.5f, 0f);
            tagText = go.GetComponent<TextMesh>();
            tagText.fontSize = 64;
            tagText.characterSize = 0.065f;
            tagText.anchor = TextAnchor.LowerCenter;
            tagText.fontStyle = FontStyle.Bold;
        }
    }

    /// <summary>
    /// Show the ship's state on the map. Selection is its OWN channel so it can
    /// never be confused with a mode: the selected ship gets a gold body, a
    /// pulsing gold halo and a SELECTED label, while every other ship is
    /// dimmed. A ship that is both selected and automatic shows both facts.
    /// The ship is never resized.
    /// </summary>
    public void UpdateVisualMode()
    {
        if (ringObj == null || tagText == null) BuildVisuals();

        Color tint = ManualTint;
        string tag = "";
        if (hasAutoTarget && isAuto) { tint = AutoTint; tag = "AUTO"; }
        else if (destMode) { tint = PickTint; tag = "PICK"; }
        else if (holdTurn) { tint = HoldTint; tag = "HOLD"; }

        bool selected = GameState.selected == this;

        if (shipRenderers != null)
        {
            for (int i = 0; i < shipRenderers.Length; i++)
            {
                if (shipRenderers[i] == null) continue;
                Color c;
                if (selected)
                {
                    // Gold, strongly — the active ship must pop off the map.
                    c = Color.Lerp(baseColors[i], SelectTint, 0.85f);
                }
                else
                {
                    c = Color.Lerp(baseColors[i], tint, 0.65f);
                    // Push every other ship back so the active one stands out.
                    c = Color.Lerp(c, new Color(0.22f, 0.22f, 0.28f), 0.35f);
                }
                shipRenderers[i].material.color = c;
            }
        }

        if (ringObj != null)
        {
            ringObj.SetActive(selected || tag != "");
            Renderer rr = ringObj.GetComponent<Renderer>();
            if (rr != null && ringObj.activeSelf)
            {
                // Selected = gold halo; a mode keeps its own colour.
                Color rc = selected ? SelectTint : tint;
                rc.a = selected ? 0.95f : 0.8f;
                rr.material.color = rc;
            }
        }

        if (tagText != null)
        {
            string modeTag = tag;
            if (selected) tag = modeTag != "" ? modeTag + "\nSELECTED" : "SELECTED";
            tagText.text = tag;
            tagText.color = modeTag != "" ? tint : SelectTint;
            tagText.gameObject.SetActive(tag != "");
        }
    }

    /// <summary>Refresh every ship's mode visuals.</summary>
    public static void RefreshAllVisuals()
    {
        foreach (var s in GameState.fleet)
            if (s != null) s.UpdateVisualMode();
    }

    /// <summary>Cancel every automatic voyage. Returns the first ship affected.</summary>
    public static Ship CancelAllVoyages()
    {
        Ship first = null;
        foreach (var s in GameState.fleet)
        {
            if (s == null) continue;
            if (s.isAuto || s.hasAutoTarget || s.destMode)
            {
                s.ClearDestination();
                if (first == null) first = s;
            }
        }
        return first;
    }
}
