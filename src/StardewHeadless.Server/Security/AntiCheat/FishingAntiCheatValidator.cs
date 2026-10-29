using System;
using StardewValley;
using StardewValley.Tools;

namespace StardewHeadless.Server.Security.AntiCheat;

/// <summary>
/// Validator Lapis 2A: Anti-Cheat Memancing (Anti-Instant Catch, Anti-Skip Minigame, & Rod Enchantment Audit).
/// Menangkal mod seperti YetAnotherFishingMod, SkipFishingMinigame, AutoCatch, dan eksploitasi 4x Enchantment.
/// </summary>
public static class FishingAntiCheatValidator
{
    public const double MinimumCastToCatchDurationSeconds = 2.0; // Normal >= 3.8 detik, instant catch < 2.0s
    public const int MaxLegalFishPerCast = 3; // 1 normal, 2 Wild Bait, 3 Challenge Bait sempurna
    public const int MaxAllowedRodEnchantments = 1; // SDV 1.6 resmi: Maksimal 1 forge enchantment per alat

    public static void RecordCastStart(long farmerId)
    {
        var state = AntiCheatCoordinator.GetOrCreatePlayerState(farmerId);
        state.CastStartTimeUtc = DateTime.UtcNow;
    }

    public static bool ValidateCatch(long farmerId, int fishCount, int quality, bool hasTreasureChest, out string? violation)
    {
        violation = null;
        var state = AntiCheatCoordinator.GetOrCreatePlayerState(farmerId);

        // 1. Validasi Durasi Fisik Cast-to-Catch
        if (state.CastStartTimeUtc.HasValue)
        {
            double elapsed = (DateTime.UtcNow - state.CastStartTimeUtc.Value).TotalSeconds;
            // Reset cast timer setelah ditarik
            state.CastStartTimeUtc = null;

            if (elapsed < MinimumCastToCatchDurationSeconds)
            {
                violation = $"FISHING_INSTANT_CATCH: Tangkapan selesai dalam {elapsed:F2}s (Batas minimum sah {MinimumCastToCatchDurationSeconds}s)";
                return false;
            }
        }

        // 2. Validasi Jumlah Ikan per Lemparan Kail
        if (fishCount > MaxLegalFishPerCast)
        {
            violation = $"FISHING_ILLEGAL_FISH_COUNT: Memperoleh {fishCount}x ikan dalam 1 lemparan (Batas sah maksimal {MaxLegalFishPerCast}x)";
            return false;
        }

        // 3. Rolling Window Audit untuk Impossible RNG
        state.RollingCatchCount++;
        if (hasTreasureChest)
        {
            state.RollingGoldenChestCount++;
            if (state.RollingGoldenChestCount >= 5)
            {
                violation = $"FISHING_IMPOSSIBLE_RNG: Mendapatkan peti harta karun 100% berturut-turut ({state.RollingGoldenChestCount}x)";
                return false;
            }
        }
        else
        {
            state.RollingGoldenChestCount = 0;
        }

        return true;
    }

    /// <summary>
    /// Memeriksa status modifikasi pancingan (FishingRod) milik pemain.
    /// Membersihkan 4x enchantment ilegal YetAnotherFishingMod dan clamp tumpukan bait 999x.
    /// </summary>
    public static bool AuditFishingRod(FishingRod rod, out string? auditMessage)
    {
        auditMessage = null;
        if (rod == null) return true;

        bool hasViolation = false;

        // A. Periksa Jumlah Enchantment (Maksimal 1 di SDV 1.6)
        if (rod.enchantments != null && rod.enchantments.Count > MaxAllowedRodEnchantments)
        {
            auditMessage = $"ILLEGAL_TOOL_ENCHANTMENT: Pancingan memiliki {rod.enchantments.Count} enchantment (Batas resmi: {MaxAllowedRodEnchantments}). Menghapus enchantment curang.";
            // Hapus kelebihan enchantment hingga tersisa maksimal 1
            while (rod.enchantments.Count > MaxAllowedRodEnchantments)
            {
                rod.enchantments.RemoveAt(rod.enchantments.Count - 1);
            }
            hasViolation = true;
        }

        // B. Periksa Tumpukan Bait Ilegal (Misal 999x Challenge Bait spawned)
        if (rod.attachments != null && rod.attachments.Count > 0)
        {
            var bait = rod.attachments[0];
            if (bait != null && bait.Stack > 500)
            {
                auditMessage = (auditMessage != null ? auditMessage + " | " : "") +
                               $"ILLEGAL_BAIT_SPAWN: Umpan kail terdeteksi {bait.Stack}x (Clamping aman ke 99x)";
                bait.Stack = 99;
                hasViolation = true;
            }
        }

        return !hasViolation;
    }
}
