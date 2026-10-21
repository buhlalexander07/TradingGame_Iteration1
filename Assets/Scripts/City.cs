using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runtime city state: inventory log (FIFO saturation), price calculation.
/// The star mechanic of the prototype.
///
/// Saturation model (locked in design):
/// - FIFO log of the last 8 sales, newest-first.
/// - Penalty by position: pos 1 = -80%, decaying stepwise toward 0% at pos 8.
/// - Each turn, every entry shifts one position deeper.
/// - Selling more of the same good: the newest matching entry moves to the
///   front; additional units stack behind it.
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
        ResourceDef def = GameData.GetResource(resourceName);
        float price = def.basePrice;

        // Production logic: city pays less for what it makes,
        // more for what it lacks.
        if (System.Array.IndexOf(produces, resourceName) >= 0)
            price *= GameData.PRODUCTION_DISCOUNT;
        else
            price *= GameData.PREMIUM_MULTIPLIER;

        // Saturation penalty from the newest matching log entry.
        float penalty = GetSaturationPenalty(resourceName);
        price *= (1f - penalty);

        return Mathf.Max(1, Mathf.RoundToInt(price));
    }

    /// <summary>
    /// The price the city charges the player for one unit of a resource
    /// (what YOU pay when you BUY from the city).
    /// </summary>
    public int GetSellPriceToPlayer(string resourceName)
    {
        // City charges a markup over what it would pay.
        return Mathf.Max(1, Mathf.RoundToInt(GetBuyPrice(resourceName) * GameData.SELL_MARKUP));
    }

    /// <summary>Penalty (0..0.8) from the newest matching entry, or 0 if none.</summary>
    public float GetSaturationPenalty(string resourceName)
    {
        for (int i = 0; i < salesLog.Count; i++)
        {
            if (salesLog[i] == resourceName)
                return PenaltyForPosition(i);
        }
        return 0f;
    }

    // Position-weighted penalties, newest (index 0) = -80%.
    // Falls to 0 once the entry is pushed past the log end.
    private float PenaltyForPosition(int index)
    {
        switch (index)
        {
            case 0: return 0.80f;
            case 1: return 0.60f;
            case 2: return 0.45f;
            case 3: return 0.30f;
            case 4: return 0.20f;
            case 5: return 0.10f;
            case 6: return 0.05f;
            default: return 0f;
        }
    }

    // ---------- SELLING / LOG MAINTENANCE ----------

    /// <summary>
    /// Player sells N units to this city. Returns total gold earned.
    /// Each unit's price is computed AFTER the previous unit's log push,
    /// so dumping multiples stacks the penalty correctly.
    /// </summary>
    public int PlayerSells(string resourceName, int quantity)
    {
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
        salesLog.Insert(0, resourceName);      // newest first
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

