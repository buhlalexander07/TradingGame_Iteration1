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
    public static int gold = 12;               // starting gold (locked in design)
    public static int turn = 1;
    public static List<City> Cities = new List<City>();
    public static List<Ship> fleet = new List<Ship>();
    public static Ship selected;               // which ship receives move orders



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
            if (c.coord == coord) return c;
        return null;
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
        ship.remainingMovement = 0; //Ship has no movemwnt upon purchase
        ship.coord = spot;
        ship.transform.position = HexGrid.Instance.AxialToWorld(spot.x, spot.y);
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

    /// <summary>End turn: age every city's saturation log, reset every ship.</summary>
    public static void EndTurn()
    {
        turn++;
        foreach (var c in Cities)
            c.TickDecay();
        foreach (var s in fleet)
            if (s != null) s.remainingMovement = s.def.movement;
        Debug.Log($"--- Turn {turn}. All {fleet.Count} ships reset to full movement. ---");
    }
}

/// <summary>
/// The player's ship: click-to-move with BFS pathing over the hex grid,
/// movement points per turn, and cargo. Supports a fleet — click one of
/// your ships to select it, then click a hex to move it.
/// Board-game turn structure: move your full movement, then trade at your docked city.
/// </summary>
[RequireComponent(typeof(Collider))]
public class Ship : MonoBehaviour
{
    [Header("Ship definition (first ship = Sloop)")]
    public ShipDef def;
    public Vector2Int coord;
    public int remainingMovement;
    public List<string> cargo = new List<string>();

    private static int inputFrame = -1;   // frame count of the last handled input

    /// <summary>Drag your ship prefab here (or leave it — the first ship in the scene sets it).</summary>
    public static Ship prefab;

    private bool isMoving = false;
    private Vector3 baseScale;
    private bool firstShip = false;

    void Awake()
    {
        baseScale = transform.localScale;
        if (prefab == null) { prefab = this; firstShip = true; }
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
            Debug.Log($"Turn {GameState.turn}. Gold: {GameState.gold}. Cargo: {cargo.Count}/{def.storage}. " +
                      $"Click your ship then a hex to move (up to {def.movement} hexes). " +
                      $"Space = end turn. Docked at a city: 1/2/3 = buy Sloop/Caravel/Galleon.");
        }
        Highlight(GameState.selected == this);
    }

    void Update()
    {
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(0)) return;
        if (isMoving) return;
        // Only one ship may handle input per frame — every ship runs Update(),
        // and without this guard each keypress fires once per ship in the fleet.
        bool anyInput = Input.GetMouseButtonDown(0)
                    || Input.GetKeyDown(KeyCode.Space)
                    || Input.GetKeyDown(KeyCode.Tab)
                    || Input.GetKeyDown(KeyCode.Alpha1)
                    || Input.GetKeyDown(KeyCode.Alpha2)
                    || Input.GetKeyDown(KeyCode.Alpha3);
        if (!anyInput) return;
        if (inputFrame == Time.frameCount) return;
        inputFrame = Time.frameCount;


        // Left click: select an owned ship, or move the selected ship to the clicked hex
        if (Input.GetMouseButtonDown(0))
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                Ship clicked = hit.collider.GetComponentInParent<Ship>();
                if (clicked != null && GameState.fleet.Contains(clicked))
                {
                    GameState.selected = clicked;
                    Debug.Log($"Selected {clicked.def.name} ({clicked.remainingMovement} movement left).");
                    RefreshHighlights();
                    return;
                }
                if (GameState.selected != null)
                    GameState.selected.TryMove(HexGrid.Instance.WorldToAxial(hit.point));
            }
        }

        // Space = end turn (proper UI button comes with the trade panel step)
        if (Input.GetKeyDown(KeyCode.Space))
            GameState.EndTurn();

        // Debug ship purchasing (the trade panel UI will replace these keys):
        if (GameState.selected != null && GameState.CityAt(GameState.selected.coord) != null)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1)) GameState.BuyShip("Sloop", GameState.selected.coord, prefab);
            if (Input.GetKeyDown(KeyCode.Alpha2)) GameState.BuyShip("Caravel", GameState.selected.coord, prefab);
            if (Input.GetKeyDown(KeyCode.Alpha3)) GameState.BuyShip("Galleon", GameState.selected.coord, prefab);
        }

        if (Input.GetKeyDown(KeyCode.Tab) && GameState.fleet.Count > 0)
        {
            int idx = GameState.fleet.IndexOf(GameState.selected);
            GameState.selected = GameState.fleet[(idx + 1) % GameState.fleet.Count];
            Debug.Log($"Selected {GameState.selected.def.name} ({GameState.selected.remainingMovement} movement left).");
            RefreshHighlights();
        }
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
            Vector3 start = transform.position;
            Vector3 end = HexGrid.Instance.AxialToWorld(step.x, step.y);
            float t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime * 6f;
                transform.position = Vector3.Lerp(start, end, t);
                yield return null;
            }
            coord = step;
            remainingMovement--;
        }
        isMoving = false;

        City city = GameState.CityAt(coord);
        if (city != null)
            Debug.Log($"Docked at {city.name}. 1/2/3 = buy a ship here; trading comes with the trade panel. ({remainingMovement} movement left)");
        else if (remainingMovement > 0)
            Debug.Log($"{remainingMovement} movement left — click another hex, or Space to end turn.");
    }

    // --- selection highlight: selected ship is scaled up slightly ---
    void RefreshHighlights()
    {
        foreach (var s in GameState.fleet)
            if (s != null) s.Highlight(s == GameState.selected);
    }

    void Highlight(bool on)
    {
        transform.localScale = on ? baseScale * 1.35f : baseScale;
    }
}

