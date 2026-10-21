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

    public static HexGrid Instance { get; private set; }

    // Store tiles in a dictionary keyed by axial coords for O(1) lookup
    private System.Collections.Generic.Dictionary<Vector2Int, GameObject> tiles =
        new System.Collections.Generic.Dictionary<Vector2Int, GameObject>();

    public const float SQRT3 = 1.7320508f;

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
                Vector3 pos = AxialToWorld(q, r + rOffset);
                GameObject tile = Instantiate(hexTilePrefab, pos, Quaternion.identity, transform);
                tile.name = $"Hex_{q}_{r}";

                // Scale the tile so adjacent tiles exactly touch.
                // Assumes a default Unity cylinder (diameter 1) flattened
                // by the prefab's own Y scale.
                Vector3 scale = tile.transform.localScale;
                tile.transform.localScale = new Vector3(SQRT3 * hexSize, scale.y, SQRT3 * hexSize);

                tiles[new Vector2Int(q, r)] = tile;
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
                if (IsValid(next) && !cameFrom.ContainsKey(next))
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

