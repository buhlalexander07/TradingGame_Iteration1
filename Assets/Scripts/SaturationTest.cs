using UnityEngine;

/// <summary>
/// Throwaway verification script for the saturation mechanic (Phase 2 gate).
/// Attach to an empty GameObject and press Play. Read the Console.
/// Delete this script once the behavior is verified.
///
/// Expected behavior being verified:
/// 1. Dumping 4 Wheat on Mandhara (which does NOT produce Wheat) crashes
///    the price: 3g, then 1g, 1g, 1g as the penalty stacks.
/// 2. Ticking turns recovers the price step by step back to 3g by turn ~7.
/// 3. Goods not in the log are unaffected (Spices stays at its produce price).
/// </summary>
public class SaturationTest : MonoBehaviour
{
    void Start()
    {
        City mandhara = new City(GameData.Cities[1]); // produces Spices, Rice

        Debug.Log("=== SATURATION MECHANIC VERIFICATION ===");
        Debug.Log($"Mandhara produces: {string.Join(", ", mandhara.produces)}");

        // --- Baseline prices before any sales ---
        Debug.Log($"[Turn 0] Wheat buy price (city pays you): {mandhara.GetBuyPrice("Wheat")}g  (expected 3 = 2 base x 1.5 premium)");
        Debug.Log($"[Turn 0] Spices buy price (city produces it): {mandhara.GetBuyPrice("Spices")}g  (expected 3 = 6 base x 0.5 discount)");
        Debug.Log($"[Turn 0] Wheat price TO BUY from city: {mandhara.GetSellPriceToPlayer("Wheat")}g  (expected 4 = 3 x 1.25 markup)");

        // --- Dump 4 Wheat ---
        int earned = mandhara.PlayerSells("Wheat", 4);
        Debug.Log($"Sold 4 Wheat for {earned}g total (expected 3 + 1 + 1 + 1 = 6)");
        Debug.Log($"After dump: Wheat buy price = {mandhara.GetBuyPrice("Wheat")}g  (expected 1, crashed by -80% penalty)");
        Debug.Log(mandhara.DebugLog());

        // --- Tick turns, watch recovery ---
        for (int turn = 1; turn <= 8; turn++)
        {
            mandhara.TickDecay();
            int wheatPrice = mandhara.GetBuyPrice("Wheat");
            int spicesPrice = mandhara.GetBuyPrice("Spices");
            Debug.Log($"[Turn {turn}] Wheat: {wheatPrice}g (recovering) | Spices: {spicesPrice}g (unaffected)");
        }

        Debug.Log("=== VERIFICATION COMPLETE ===");
        Debug.Log("PASS if: Wheat crashed to 1g, recovered to 3g by turn ~7, Spices never changed.");
    }
}

