using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runtime city state: inventory log (FIFO saturation), price calculation.
/// The star mechanic of the prototype.
///
/// Saturation model:
/// - FIFO log of the last 8 sales, newest-first.
/// - Penalty by position: pos 0 = -50%, decaying stepwise toward 0% at pos 7.
/// - Each turn every entry shifts one position deeper (price recovers).
/// - Selling the same good again moves that entry back to the front.
/// </summary>
public class City
{
    public string name;
    public Vector2Int coord;
    public string[] produces;

    // FIFO sales log — index 0 is the newest sale.
    private List<string> salesLog = new List<string>();

    public City(CityDef def)
    {
        name = def.name;
        coord = def.coord;
        produces = def.produces;
    }

    // ---------- PRICE CALCULATION ----------

    /// <summary>
    /// The price the city pays the player for one unit of a resource
    /// (what YOU get when you SELL to the city).
    /// </summary>
    public int GetBuyPrice(string resourceName)
    {
        float price = GetUnsaturatedPrice(resourceName);
        price *= (1f - GetSaturationPenalty(resourceName));
        return Mathf.Max(GameData.PRICE_FLOOR, Mathf.RoundToInt(price));
    }

    /// <summary>
    /// The reference price for a resource here, with NO saturation penalty
    /// applied. Used for the price preview so the player can see what the
    /// market "should" be paying and what their own dumping has done to it.
    /// </summary>
    public int GetUnsaturatedPrice(string resourceName)
    {
        ResourceDef def = GameData.GetResource(resourceName);
        float price = def.basePrice;

        // Production logic: city pays less for what it makes, more for what it lacks.
        if (System.Array.IndexOf(produces, resourceName) >= 0)
            price *= GameData.PRODUCTION_DISCOUNT;
        else
            price *= GameData.PREMIUM_MULTIPLIER;

        return Mathf.Max(GameData.PRICE_FLOOR, Mathf.RoundToInt(price));
    }

    /// <summary>
    /// What the city would pay for the NEXT unit you sell here — i.e. the
    /// price AFTER this sale lands at the front of the log. This is the
    /// "sell preview" shown in the trade panel.
    /// </summary>
    public int GetBuyPriceAfterSale(string resourceName)
    {
        float price = GetUnsaturatedPrice(resourceName);
        price *= (1f - GameData.SaturationPenalties[0]);
        return Mathf.Max(GameData.PRICE_FLOOR, Mathf.RoundToInt(price));
    }

    /// <summary>
    /// The price the city charges the player for one unit of a resource
    /// (what YOU pay when you BUY from the city). Buying never changes the
    /// saturation log — only selling does.
    /// </summary>
    public int GetSellPriceToPlayer(string resourceName)
    {
        return Mathf.Max(GameData.PRICE_FLOOR, Mathf.RoundToInt(GetBuyPrice(resourceName) * GameData.SELL_MARKUP));
    }

    /// <summary>Penalty (0..0.5) from the newest matching log entry, or 0 if none.</summary>
    public float GetSaturationPenalty(string resourceName)
    {
        for (int i = 0; i < salesLog.Count; i++)
        {
            if (salesLog[i] == resourceName)
                return PenaltyForPosition(i);
        }
        return 0f;
    }

    // Position-weighted penalties, newest (index 0) = -50%.
    private float PenaltyForPosition(int index)
    {
        if (index < 0 || index >= GameData.SaturationPenalties.Length) return 0f;
        return GameData.SaturationPenalties[index];
    }

    // ---------- PRODUCTION RULES ----------

    /// <summary>Does this city produce the given resource?</summary>
    public bool Produces(string resourceName)
    {
        return System.Array.IndexOf(produces, resourceName) >= 0;
    }

    /// <summary>
    /// A city only buys what it does NOT produce. Selling a resource the
    /// city grows is refused outright: the city has no use for it, and
    /// allowing it would let a player dump surplus goods on the very city
    /// that grows them, skipping the cross-city trade the game is about.
    /// </summary>
    public bool CanSellTo(string resourceName)
    {
        return !Produces(resourceName);
    }

    // ---------- SELLING / LOG MAINTENANCE ----------

    /// <summary>
    /// Player sells N units to this city. Returns total gold earned.
    /// Each unit's price is computed AFTER the previous unit's log push,
    /// so dumping multiples stacks the penalty correctly.
    /// </summary>
    public int PlayerSells(string resourceName, int quantity)
    {
        // Refuse goods the city produces: no sale, no gold, no log entry.
        if (!CanSellTo(resourceName) || quantity <= 0) return 0;

        int total = 0;
        for (int i = 0; i < quantity; i++)
        {
            total += GetBuyPrice(resourceName);
            PushSale(resourceName);
        }
        return total;
    }

    /// <summary>Push a sale to the front of the FIFO log (or move an existing entry to the front).</summary>
    private void PushSale(string resourceName)
    {
        salesLog.Remove(resourceName);          // move existing entry to front if present
        salesLog.Insert(0, resourceName);        // newest first
        while (salesLog.Count > GameData.SALES_LOG_SIZE)
            salesLog.RemoveAt(salesLog.Count - 1); // oldest falls off = fully recovered
    }

    /// <summary>Called on end turn — every entry shifts one position deeper (decays toward 0%).</summary>
    public void TickDecay()
    {
        // The penalty is position-based; shifting everything one deeper is
        // equivalent to aging the log. We model aging by inserting a spacer
        // that pushes all entries one position down.
        salesLog.Insert(0, null); // null = aged placeholder
        while (salesLog.Count > GameData.SALES_LOG_SIZE)
            salesLog.RemoveAt(salesLog.Count - 1);
    }

    /// <summary>Clear the log (used by the in-place restart).</summary>
    public void ResetLog()
    {
        salesLog.Clear();
    }

    /// <summary>Read-only view of the log for UI (index 0 = newest). Nulls are aged slots.</summary>
    public List<string> GetSalesLog() => new List<string>(salesLog);

    /// <summary>Debug: formatted log with positions and penalties.</summary>
    public string DebugLog()
    {
        var sb = new System.Text.StringBuilder($"{name} log (newest first): ");
        for (int i = 0; i < salesLog.Count; i++)
            sb.Append($"[{i}] {(salesLog[i] ?? "(aged)")} ({PenaltyForPosition(i):P0}) ");
        return sb.ToString();
    }
}
