using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// All static game definitions for the prototype.
/// Plain structs + one static class, per the build plan.
/// Tunable after playtest — every number here is a placeholder.
/// </summary>

[System.Serializable]
public struct ResourceDef
{
    public string name;
    public int basePrice;
    public string category; // Crop, Animal, Mineral, Manufactured

    public ResourceDef(string name, int basePrice, string category)
    {
        this.name = name;
        this.basePrice = basePrice;
        this.category = category;
    }
}

[System.Serializable]
public struct ShipDef
{
    public string name;
    public int attack;
    public int defense;
    public int movement;
    public int storage;
    public int cost;

    public ShipDef(string name, int attack, int defense, int movement, int storage, int cost)
    {
        this.name = name;
        this.attack = attack;
        this.defense = defense;
        this.movement = movement;
        this.storage = storage;
        this.cost = cost;
    }
}

[System.Serializable]
public struct CityDef
{
    public string name;
    public Vector2Int coord;      // axial coords on the HexGrid
    public string[] produces;     // the 2 resources this city produces

    public CityDef(string name, Vector2Int coord, string[] produces)
    {
        this.name = name;
        this.coord = coord;
        this.produces = produces;
    }
}

public static class GameData
{
    // ---------- RESOURCES ----------
    // The 8 city-produced resources + Fish (all continents) + 2 Caleana
    // manufactured goods. 11 total — full 28 waits for the full game.
    public static readonly ResourceDef[] Resources =
    {
        new ResourceDef("Wheat",    2, "Crop"),
        new ResourceDef("Rice",     3, "Crop"),
        new ResourceDef("Tobacco",  5, "Crop"),
        new ResourceDef("Cocoa",    7, "Crop"),
        new ResourceDef("Cattle",   4, "Animal"),
        new ResourceDef("Fur",      6, "Animal"),
        new ResourceDef("Fish",     3, "Animal"),
        new ResourceDef("Spices",   6, "Crop"),
        new ResourceDef("Silver",   9, "Mineral"),
        new ResourceDef("Clothes",  8, "Manufactured"),
        new ResourceDef("Tools",    9, "Manufactured"),
    };

    public static ResourceDef GetResource(string name)
    {
        foreach (var r in Resources)
            if (r.name == name) return r;
        Debug.LogError($"Unknown resource: {name}");
        return Resources[0];
    }

    // ---------- SHIPS ----------
    // Retuned stats from design: 5/4/4 movement, larger ships can
    // (just) catch smaller ones. Health is tracked on defense per rules doc.
    public static readonly ShipDef[] Ships =
    {
        new ShipDef("Sloop",    1, 2, 5, 2, 10),
        new ShipDef("Caravel",  2, 4, 4, 4, 15),
        new ShipDef("Galleon",  4, 6, 4, 6, 20),
    };

    public static ShipDef GetShip(string name)
    {
        foreach (var s in Ships)
            if (s.name == name) return s;
        Debug.LogError($"Unknown ship: {name}");
        return Ships[0];
    }

    // ---------- CITIES ----------
    // One city per continent-equivalent, 2 produced resources each.
    // Coords assume a 10x7 grid — adjust if you change grid size.
    public static readonly CityDef[] Cities =
    {
        new CityDef("Caleana",  new Vector2Int(0, 3), new[] { "Wheat", "Cattle" }),
        new CityDef("Mandhara", new Vector2Int(9, 2), new[] { "Spices", "Rice" }),
        new CityDef("Acorbia",  new Vector2Int(4, 4), new[] { "Tobacco", "Fur" }),
        new CityDef("Huatzil",  new Vector2Int(6, 0), new[] { "Silver", "Cocoa" }),
    };

    // ---------- ECONOMY TUNING ----------
    public const float PREMIUM_MULTIPLIER = 1.5f;  // city pays +50% for goods it doesn't produce
    public const float PRODUCTION_DISCOUNT = 0.5f; // city pays half for what it produces itself
    public const float SELL_MARKUP = 1.25f;        // city charges +25% over its buy price when selling to you
    public const int SALES_LOG_SIZE = 8;           // FIFO log length per city
}

