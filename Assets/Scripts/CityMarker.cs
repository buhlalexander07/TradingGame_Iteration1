using UnityEngine;

/// <summary>
/// Phase 1 closer: renders a visual marker for each city defined in
/// GameData.Cities on its hex. Cities are data objects (see City.cs);
/// this just gives them a body on the map.
///
/// Setup: create an empty GameObject named "CityMarkers" and add this
/// component. Assign a marker prefab (a flattened cylinder or capsule
/// works from the top-down camera). Optional: assign a TextMesh prefab
/// for the name label, or leave it null.
/// </summary>
public class CityMarkers : MonoBehaviour
{
    [Header("Prefabs")]
    public GameObject cityMarkerPrefab;   // visual body for a city
    public GameObject labelPrefab;        // optional TextMesh label

    void Start()
    {
        // Wait one frame so HexGrid.Start() has generated the tiles
        // (script execution order alternative: this is the cheap fix)
        Invoke(nameof(SpawnCities), 0.1f);
    }

    void SpawnCities()
    {
        if (HexGrid.Instance == null)
        {
            Debug.LogError("CityMarkers: no HexGrid found in scene.");
            return;
        }

        foreach (var cityDef in GameData.Cities)
        {
            if (!HexGrid.Instance.IsValid(cityDef.coord))
            {
                Debug.LogError($"City {cityDef.name} coord {cityDef.coord} is outside the grid!");
                continue;
            }

            Vector3 pos = HexGrid.Instance.AxialToWorld(cityDef.coord.x, cityDef.coord.y);

            // Marker sits slightly above the tile surface
            Vector3 markerPos = pos + Vector3.up * 0.25f;
            GameObject marker = Instantiate(cityMarkerPrefab, markerPos, Quaternion.identity, transform);
            marker.name = $"City_{cityDef.name}";

            // Color the marker so cities are distinguishable from tiles
            var renderer = marker.GetComponentInChildren<Renderer>();
            if (renderer != null)
                renderer.material.color = new Color(0.9f, 0.75f, 0.2f); // gold-ish

            // Optional floating name label
            if (labelPrefab != null)
            {
                GameObject label = Instantiate(labelPrefab,
                    markerPos + Vector3.up * 1f, Quaternion.identity, transform);
                var tm = label.GetComponentInChildren<TextMesh>();
                if (tm != null) tm.text = cityDef.name;
            }

            Debug.Log($"City spawned: {cityDef.name} at {cityDef.coord} " +
                      $"(produces {string.Join(", ", cityDef.produces)})");
        }
    }
}

