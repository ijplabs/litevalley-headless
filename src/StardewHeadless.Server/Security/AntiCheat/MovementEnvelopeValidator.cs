using System;
using Microsoft.Xna.Framework;
using StardewValley;

namespace StardewHeadless.Server.Security.AntiCheat;

/// <summary>
/// Validator Lapis 2B: Dynamic Legal Speed Envelope & Rubberband Guard.
/// Menghitung amplop kecepatan maksimum sah pemain per detik secara dinamis
/// (Base Run + Buff Kopi + Makanan + Kuda + Perk Profesi + Toleransi Jitter 25%)
/// dan melakukan koreksi Rubberband jika speedhack terdeteksi lebih dari 3 sampel berturut-turut.
/// </summary>
public static class MovementEnvelopeValidator
{
    public const float BaseRunSpeedTilesPerSec = 5.0f;
    public const float HorseBonusSpeedTilesPerSec = 1.6f;
    public const float FoodBuffSpeedTilesPerSec = 1.0f;
    public const float DrinkBuffSpeedTilesPerSec = 1.0f;
    public const float ProfessionPerkSpeedTilesPerSec = 1.0f;
    public const float JitterToleranceFactor = 1.25f; // 25% kelonggaran untuk lag spike / packet loss

    /// <summary>
    /// Menghitung batas kecepatan lari sah maksimum per detik untuk petani tertentu.
    /// Memperhitungkan seluruh bonus sah resmi agar 0% False Positive!
    /// </summary>
    public static float CalculateMaxLegalSpeedTilesPerSec(Farmer? farmer = null, bool hasForagerSpeedPerk = false)
    {
        float speed = BaseRunSpeedTilesPerSec;

        if (farmer != null)
        {
            // Cek apakah pemain sedang menunggang kuda
            try
            {
                if (farmer.isRidingHorse())
                {
                    speed += HorseBonusSpeedTilesPerSec;
                }
            }
            catch { }

            // Cek buffs makanan/minuman (misal Spicy Eel + Triple Shot Espresso)
            try
            {
                if (farmer.buffs != null && farmer.buffs.AppliedBuffs.Count > 0)
                {
                    speed += (FoodBuffSpeedTilesPerSec + DrinkBuffSpeedTilesPerSec);
                }
            }
            catch { }
        }

        if (hasForagerSpeedPerk)
        {
            speed += ProfessionPerkSpeedTilesPerSec;
        }

        return speed * JitterToleranceFactor;
    }

    /// <summary>
    /// Memvalidasi perpindahan spasial pemain.
    /// Jika kecepatan melebihi amplop sah selama >= 3 sampel beruntun,
    /// mengembalikan target Rubberband ke posisi sah terakhir.
    /// </summary>
    public static bool ValidateMovement(
        long farmerId,
        Vector2 currentPosition,
        double deltaTimeSeconds,
        Farmer? farmer,
        bool hasForagerPerk,
        out Vector2 rubberbandPosition,
        out string? violation)
    {
        violation = null;
        var state = AntiCheatCoordinator.GetOrCreatePlayerState(farmerId);
        rubberbandPosition = state.LastValidPosition;

        if (deltaTimeSeconds <= 0.001)
        {
            return true;
        }

        // Jika posisi awal belum diinisialisasi
        if (state.LastValidPosition == Vector2.Zero)
        {
            state.LastValidPosition = currentPosition;
            rubberbandPosition = currentPosition;
            return true;
        }

        float distancePixels = Vector2.Distance(currentPosition, state.LastValidPosition);
        float distanceTiles = distancePixels / 64f;
        float tilesPerSec = (float)(distanceTiles / deltaTimeSeconds);

        float maxSpeed = CalculateMaxLegalSpeedTilesPerSec(farmer, hasForagerPerk);

        if (tilesPerSec > maxSpeed)
        {
            state.ConsecutiveSpeedViolations++;
            if (state.ConsecutiveSpeedViolations >= 3)
            {
                violation = $"SPEEDHACK_DETECTED: Kecepatan {tilesPerSec:F1} tiles/s melampaui batas maksimum sah ({maxSpeed:F1} tiles/s) selama {state.ConsecutiveSpeedViolations} sampel berturut-turut.";
                rubberbandPosition = state.LastValidPosition;
                return false;
            }
            return true; // Beri toleransi untuk 1-2 sampel jika terjadi lag spike
        }

        // Kecepatan normal: reset violation dan simpan posisi sah
        state.ConsecutiveSpeedViolations = 0;
        state.LastValidPosition = currentPosition;
        rubberbandPosition = currentPosition;
        return true;
    }
}
