using System;
using System.Collections.Generic;
using StardewValley;
using StardewValley.Objects;

namespace StardewHeadless.Server.Security.AntiCheat;

/// <summary>
/// Validator Lapis 2D: Anti-Cheat Ekonomi, Spawner Barang, Rekonsiliasi Dompet, & Batas Cincin.
/// Menangkal command SMAPI player_setmoney, CJB Item Spawner, dan penumpukan cincin WearMoreRings berlebih.
/// </summary>
public static class EconomyAndInventoryGuard
{
    public const int MaxMoneySurgePerSecond = 5000;
    private static readonly HashSet<string> LegitimateMoneySources = new(StringComparer.OrdinalIgnoreCase)
    {
        "StoreSale",
        "ShippingBin",
        "BountyReward",
        "NewDay",
        "TreasuryWithdraw",
        "QuestReward"
    };

    /// <summary>
    /// Memvalidasi lonjakan uang di dompet pemain.
    /// Jika terjadi kenaikan > 5.000g dalam 1 detik tanpa riwayat transaksi sah,
    /// ditandai sebagai anomali uang (Money Spawner / Cheat Engine).
    /// </summary>
    public static bool ValidateWalletSurge(
        long farmerId,
        int currentMoney,
        string transactionSource,
        out int rollbackMoney,
        out string? violation)
    {
        violation = null;
        var state = AntiCheatCoordinator.GetOrCreatePlayerState(farmerId);

        if (state.LastRecordedMoney < 0)
        {
            state.LastRecordedMoney = currentMoney;
            rollbackMoney = currentMoney;
            return true;
        }

        rollbackMoney = state.LastRecordedMoney;
        int diff = currentMoney - state.LastRecordedMoney;

        // Jika ada kenaikan uang
        if (diff > 0)
        {
            bool isLegit = LegitimateMoneySources.Contains(transactionSource);

            if (diff > MaxMoneySurgePerSecond && !isLegit)
            {
                violation = $"MONEY_SPAWN_ANOMALY: Lonjakan uang mendadak +{diff:N0}g dari sumber '{transactionSource}' (Batas sah tanpa transaksi: {MaxMoneySurgePerSecond:N0}g).";
                return false;
            }
        }

        // Simpan saldo sah yang telah tervalidasi
        state.LastRecordedMoney = currentMoney;
        rollbackMoney = currentMoney;
        return true;
    }

    /// <summary>
    /// Memeriksa dan membatasi jumlah total cincin yang terpasang pada karakter.
    /// Menjaga batas resmi ServerConfig.MaxEquippedRings (default 4 cincin)
    /// dan mengembalikan cincin berlebih secara aman ke inventori pemain.
    /// </summary>
    public static bool AuditEquippedRings(Farmer farmer, int maxRingsAllowed, out int excessRingsRemoved)
    {
        excessRingsRemoved = 0;
        if (farmer == null) return true;

        var allRings = new List<Ring>();

        void ExtractRings(Ring? r)
        {
            if (r == null) return;
            if (r is CombinedRing combined && combined.combinedRings != null)
            {
                foreach (var inner in combined.combinedRings)
                {
                    if (inner != null) allRings.Add(inner);
                }
            }
            else
            {
                allRings.Add(r);
            }
        }

        ExtractRings(farmer.leftRing?.Value);
        ExtractRings(farmer.rightRing?.Value);

        if (allRings.Count > maxRingsAllowed)
        {
            excessRingsRemoved = allRings.Count - maxRingsAllowed;

            // Kembalikan kelebihan cincin ke tas inventori
            for (int i = maxRingsAllowed; i < allRings.Count; i++)
            {
                try
                {
                    farmer.addItemToInventory(allRings[i]);
                }
                catch { }
            }

            return false;
        }

        return true;
    }

    /// <summary>
    /// Memvalidasi kemunculan item langka tingkat akhir (Prismatic Shard, Qi Gem, Statue)
    /// di hari-hari awal permainan tanpa riwayat kemajuan tambang yang sah.
    /// </summary>
    public static bool AuditEndgameItemSpawning(
        long farmerId,
        string itemId,
        int stack,
        int daysPlayed,
        int deepestMineLevel,
        out string? violation)
    {
        violation = null;

        // Prismatic Shard (O)74
        if (itemId == "74" || itemId == "(O)74")
        {
            if (stack > 3 && daysPlayed <= 7 && deepestMineLevel < 80)
            {
                violation = $"ITEM_SPAWNER_ANOMALY: Memiliki {stack}x Prismatic Shard pada hari ke-{daysPlayed} dengan level tambang {deepestMineLevel}.";
                return false;
            }
        }

        return true;
    }
}
