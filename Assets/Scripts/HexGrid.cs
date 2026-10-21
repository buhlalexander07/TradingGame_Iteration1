using UnityEngine;

/// <summary>
/// Generates a flat-top hex grid using axial coordinates (q, r).
/// World position: x = size * 1.5 * q
///                 z = size * sqrt(3) * (r + q/2)
/// Neighbors: E(+1,0) W(-1,0) N(0,-1) S(0,+1) NE(+1,-1) SW(-1,+1)
/// Rows are offset per column (-floor(q/2)) so the map reads as a
/// rectangle with staggered top/bottom rows instead of a parallelogram.
/// </summary>
public class HexGrid : MonoBehaviour
{
    [Header("Grid Settings")]
    public int width = 20;
    public int height = 12;
    public float hexSize = 1f;

    [Header("Prefabs")]
    public GameObject hexTilePrefab;

    [Header("Water")]
    public GameObject waterTilePrefab;   // optional: separate mesh for water tiles
    [TextArea(3, 6)]
    public string mapMask = "";          // 7 rows of 10 chars, ';' separated. W = water, anything else = land

    public static HexGrid Instance { get; private set; }

    // Store tiles in a dictionary keyed by axial coords for O(1) lookup
    private System.Collections.Generic.Dictionary<Vector2Int, GameObject> tiles =
        new System.Collections.Generic.Dictionary<Vector2Int, GameObject>();

    public const float SQRT3 = 1.7320508f;

    bool IsWater(int col, int row)
    {
        if (string.IsNullOrEmpty(mapMask)) return false;
        string[] rows = mapMask.Split(';');
        if (row >= rows.Length || col >= rows[row].Length) return false;
        return char.ToUpper(rows[row][col]) == 'W';
    }

    /// <summary>True if a ship can occupy this hex: water tiles and city port tiles.</summary>
    public bool IsTilePassable(Vector2Int coord)
    {
        if (!tiles.ContainsKey(coord)) return false;
        // City hexes are ports — always passable
        if (System.Array.Exists(GameData.Cities, c => c.coord == coord)) return true;
        // Convert the axial trueR back to the on-screen mask row for this column
        int maskRow = coord.y + Mathf.FloorToInt(coord.x / 2f);
        return IsWater(coord.x, maskRow);
    }

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        GenerateGrid();
        CenterCamera();
    }

    public void GenerateGrid()
    {
        Debug.Log($"Mask length: {mapMask.Length}, rows: {mapMask.Split(';').Length}");
        // Clear any existing tiles
        foreach (var tile in tiles.Values)
            if (tile != null) Destroy(tile);
        tiles.Clear();

        for (int q = 0; q < width; q++)
        {
            // Shift each column down by whole hexes so the overall
            // shape is a rectangle (with staggered top/bottom rows)
            int rOffset = -Mathf.FloorToInt(q / 2f);

            for (int r = 0; r < height; r++)
            {
                int trueR = r + rOffset;
                Vector3 pos = AxialToWorld(q, trueR);

                bool isWater = IsWater(q, r);   // NOTE: loop indices, not axial coords — the mask matches the on-screen rectangle
                GameObject prefabToUse = (isWater && waterTilePrefab != null) ? waterTilePrefab : hexTilePrefab;
                GameObject tile = Instantiate(prefabToUse, pos, Quaternion.identity, transform);
                tile.name = isWater ? $"Water_{q}_{trueR}" : $"Hex_{q}_{trueR}";

                if (isWater && waterTilePrefab == null)
                {
                    Renderer rend = tile.GetComponent<Renderer>();
                    if (rend != null)
                    {
                        bool isCity = System.Array.Exists(GameData.Cities, c => c.coord == new Vector2Int(q, trueR));
                        rend.material.color = isCity
                            ? new Color(0.85f, 0.75f, 0.4f)   // sandy harbor gold — city ports read at a glance
                            : new Color(0.25f, 0.45f, 0.7f);  // ocean blue
                    }
                }
                else
                {
                    Renderer rend = tile.GetComponent<Renderer>();
                    if (rend != null) rend.material.color = new Color(0.35f, 0.6f, 0.3f);   // land green
                }

                Vector3 scale = tile.transform.localScale;
                tile.transform.localScale = new Vector3(SQRT3 * hexSize, scale.y, SQRT3 * hexSize);
                tiles[new Vector2Int(q, trueR)] = tile;
            }
        }
    }

    /// <summary>Convert axial coordinates to world position (flat-top).</summary>
    public Vector3 AxialToWorld(int q, int r)
    {
        float x = hexSize * 1.5f * q;
        float z = hexSize * SQRT3 * (r + q / 2f);
        return new Vector3(x, 0, z);
    }

    /// <summary>Convert a world position back to the nearest axial coordinate.</summary>
    public Vector2Int WorldToAxial(Vector3 worldPos)
    {
        float q = (2f / 3f * worldPos.x) / hexSize;
        float r = (-1f / 3f * worldPos.x + SQRT3 / 3f * worldPos.z) / hexSize;
        return AxialRound(q, r);
    }

    /// <summary>Cube rounding — snaps fractional axial coords to the nearest valid hex.</summary>
    private Vector2Int AxialRound(float qF, float rF)
    {
        float x = qF;
        float z = rF;
        float y = -x - z;

        int rx = Mathf.RoundToInt(x);
        int ry = Mathf.RoundToInt(y);
        int rz = Mathf.RoundToInt(z);

        float xDiff = Mathf.Abs(rx - x);
        float yDiff = Mathf.Abs(ry - y);
        float zDiff = Mathf.Abs(rz - z);

        if (xDiff > yDiff && xDiff > zDiff) rx = -ry - rz;
        else if (yDiff > zDiff) ry = -rx - rz;
        else rz = -rx - ry;

        return new Vector2Int(rx, rz);
    }


    /// <summary>The 6 axial neighbor offsets (flat-top).</summary>
    public static readonly Vector2Int[] NeighborOffsets =
    {
        new Vector2Int(+1, 0),  // E
        new Vector2Int(-1, 0),  // W
        new Vector2Int(0, -1),  // N
        new Vector2Int(0, +1),  // S
        new Vector2Int(+1, -1), // NE
        new Vector2Int(-1, +1)  // SW
    };

    /// <summary>Returns true if the coords are inside the grid.</summary>
    public bool IsValid(Vector2Int coord)
    {
        return tiles.ContainsKey(coord);
    }

    public GameObject GetTile(Vector2Int coord)
    {
        tiles.TryGetValue(coord, out GameObject tile);
        return tile;
    }

    /// <summary>
    /// BFS pathfinding — returns the list of coords from start to goal
    /// (inclusive), or null if unreachable. Ignores movement cost (all
    /// tiles cost 1) per the prototype design.
    /// </summary>
    public System.Collections.Generic.List<Vector2Int> FindPath(Vector2Int start, Vector2Int goal)
    {
        if (!IsValid(start) || !IsValid(goal)) return null;

        var cameFrom = new System.Collections.Generic.Dictionary<Vector2Int, Vector2Int>();
        var queue = new System.Collections.Generic.Queue<Vector2Int>();
        queue.Enqueue(start);
        cameFrom[start] = start;

        while (queue.Count > 0)
        {
            Vector2Int current = queue.Dequeue();
            if (current == goal)
            {
                // Reconstruct path
                var path = new System.Collections.Generic.List<Vector2Int>();
                Vector2Int node = goal;
                while (node != start)
                {
                    path.Add(node);
                    node = cameFrom[node];
                }
                path.Add(start);
                path.Reverse();
                return path;
            }

            foreach (var offset in NeighborOffsets)
            {
                Vector2Int next = current + offset;
                if (IsValid(next) && IsTilePassable(next) && !cameFrom.ContainsKey(next))
                {
                    cameFrom[next] = current;
                    queue.Enqueue(next);
                }
            }
        }
        return null;
    }

    /// <summary>Distance in hexes between two coords (for movement range checks).</summary>
    public int HexDistance(Vector2Int a, Vector2Int b)
    {
        Vector2Int d = b - a;
        // Convert axial to cube coords for distance
        int x = d.x;
        int z = d.y;
        int y = -x - z;
        return Mathf.Max(Mathf.Abs(x), Mathf.Max(Mathf.Abs(y), Mathf.Abs(z)));
    }

    void CenterCamera()
    {
        var cam = Camera.main;
        if (cam == null) return;
        Vector3 center = AxialToWorld(width / 2, height / 2 - Mathf.FloorToInt(width / 2f / 2f));
        cam.transform.position = center + Vector3.up * 25f;
    }

    /// <summary>
    /// Draws the grid bounds in the Scene view so you can verify layout
    /// and prefab spacing before pressing Play.
    /// </summary>
    void OnDrawGizmos()
    {
        if (hexTilePrefab == null) return;
        Gizmos.color = Color.cyan;
        for (int q = 0; q < width; q++)
        {
            int rOffset = -Mathf.FloorToInt(q / 2f);
            for (int r = 0; r < height; r++)
                Gizmos.DrawWireSphere(AxialToWorld(q, r + rOffset), 0.2f);
        }
    }
}

