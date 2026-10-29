using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using StardewValley;
using StardewValley.Objects;

namespace StardewHeadless.Server.Mods;

public record RingsSanitizationResult(
    int TotalInitialRings,
    int TotalFinalRings,
    List<Ring> DemotedExcessRings,
    bool WasCapped
);

/// <summary>
/// Bertanggung jawab membatasi dan mengamankan penggunaan cincin berlebih (WearMoreRings & CombinedRing).
/// Menegakkan batas maksimal 4 cincin aktif di server multiplayer kompetitif/seimbang,
/// serta mencegah eksploitasi save yang membawa CombinedRing bertumpuk 20 cincin ilegal (God-Mode).
/// </summary>
public static class WearMoreRingsGuard
{
    public const int DefaultMaxEquippedRings = 4;

    /// <summary>
    /// Memeriksa dan membatasi cincin yang terpasang pada petani ke batas maksimal (default: 4 cincin).
    /// Cincin yang melebihi batas dipisahkan dan dikembalikan secara aman ke inventori pemain tanpa hilang.
    /// </summary>
    public static RingsSanitizationResult EnforceMaxRings(Farmer? farmer, int maxAllowedRings = DefaultMaxEquippedRings)
    {
        var demoted = new List<Ring>();
        if (farmer == null)
        {
            return new RingsSanitizationResult(0, 0, demoted, false);
        }

        var allEquippedRings = new List<Ring>();

        void CollectFromSlot(Ring? r)
        {
            if (r == null) return;
            if (r is CombinedRing combined)
            {
                foreach (var inner in combined.combinedRings)
                {
                    if (inner != null) allEquippedRings.Add(inner);
                }
            }
            else
            {
                allEquippedRings.Add(r);
            }
        }

        CollectFromSlot(farmer.leftRing.Value);
        CollectFromSlot(farmer.rightRing.Value);

        int totalInitial = allEquippedRings.Count;
        if (totalInitial <= maxAllowedRings)
        {
            return new RingsSanitizationResult(totalInitial, totalInitial, demoted, false);
        }

        // Ambil maksimal N cincin pertama yang sah, sisanya didemotasi
        var keptRings = allEquippedRings.Take(maxAllowedRings).ToList();
        var excessRings = allEquippedRings.Skip(maxAllowedRings).ToList();

        // Kembalikan cincin berlebih ke inventori pemain dengan aman
        foreach (var excess in excessRings)
        {
            demoted.Add(excess);
            try
            {
                if (!farmer.addItemToInventoryBool(excess))
                {
                    // Jika tas penuh, taruh di mailbox agar tidak musnah
                    farmer.mailbox.Add($"ItemTransfer:{excess.ItemId}");
                }
            }
            catch
            {
                // Fallback aman untuk lingkungan headless / test di mana Game1.player belum terinisialisasi
                farmer.Items.Add(excess);
            }
        }

        // Susun ulang slot cincin pemain menjadi sah (maks 2 per slot)
        RebuildPlayerRings(farmer, keptRings);

        return new RingsSanitizationResult(totalInitial, keptRings.Count, demoted, true);
    }

    private static CombinedRing CreateSafeCombinedRing(Ring r1, Ring r2)
    {
        CombinedRing comb;
        try
        {
            comb = new CombinedRing();
        }
        catch
        {
            comb = (CombinedRing)RuntimeHelpers.GetUninitializedObject(typeof(CombinedRing));
            comb.ItemId = "880";
            comb.Category = -96;
            comb.Name = "Combined Ring";
        }
        comb.combinedRings.Add(r1);
        comb.combinedRings.Add(r2);
        return comb;
    }

    private static void RebuildPlayerRings(Farmer farmer, List<Ring> kept)
    {
        farmer.leftRing.Value = null;
        farmer.rightRing.Value = null;

        if (kept.Count == 0) return;

        // Slot Kiri
        if (kept.Count == 1)
        {
            farmer.leftRing.Value = kept[0];
            return;
        }

        if (kept.Count == 2)
        {
            farmer.leftRing.Value = kept[0];
            farmer.rightRing.Value = kept[1];
            return;
        }

        if (kept.Count == 3)
        {
            farmer.leftRing.Value = CreateSafeCombinedRing(kept[0], kept[1]);
            farmer.rightRing.Value = kept[2];
            return;
        }

        if (kept.Count >= 4)
        {
            farmer.leftRing.Value = CreateSafeCombinedRing(kept[0], kept[1]);
            farmer.rightRing.Value = CreateSafeCombinedRing(kept[2], kept[3]);
        }
    }
}
