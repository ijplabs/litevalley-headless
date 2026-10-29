using System;
using StardewValley;

namespace StardewHeadless.Server.Security.AntiCheat;

/// <summary>
/// Validator Lapis 2C: Anti-Cheat Pertempuran, Kebal (GodMode), & Stamina Tak Terbatas.
/// Mendeteksi anomali pemain yang tidak mengalami penurunan HP saat menerima serangan monster
/// atau melakukan ayunan alat terus-menerus tanpa konsumsi stamina.
/// </summary>
public static class CombatAndVitalValidator
{
    public const int MaxZeroDamageHitsBeforeGodModeFlag = 5;
    public const int MaxSwingsWithoutStaminaLossBeforeFlag = 30;

    /// <summary>
    /// Memvalidasi kontak tabrakan monster terhadap HP pemain.
    /// Jika pemain terkena hit monster sah sebanyak 5x berturut-turut tanpa penurunan HP,
    /// ditandai sebagai GodMode / Invincibility cheat.
    /// </summary>
    public static bool ValidateMonsterCombatHit(
        long farmerId,
        int currentHealth,
        int previousHealth,
        int incomingMonsterDamage,
        out string? violation)
    {
        violation = null;
        var state = AntiCheatCoordinator.GetOrCreatePlayerState(farmerId);

        // Jika ada serangan monster yang menimbulkan damage > 0
        if (incomingMonsterDamage > 0)
        {
            if (currentHealth >= previousHealth)
            {
                state.ConsecutiveZeroDamageHits++;
                if (state.ConsecutiveZeroDamageHits >= MaxZeroDamageHitsBeforeGodModeFlag)
                {
                    violation = $"GODMODE_INVINCIBILITY: Menerima {incomingMonsterDamage} damage monster sebanyak {state.ConsecutiveZeroDamageHits}x tanpa penurunan HP.";
                    return false;
                }
            }
            else
            {
                // Pemain menerima damage secara wajar
                state.ConsecutiveZeroDamageHits = 0;
            }
        }

        return true;
    }

    /// <summary>
    /// Memvalidasi penggunaan stamina saat mengayunkan alat (Cangkul, Beliung, Kapak, Pancing).
    /// Jika pemain mengayunkan alat >= 30 kali tanpa kehilangan energi sedikit pun,
    /// ditandai sebagai Infinite Stamina cheat.
    /// </summary>
    public static bool ValidateToolEnergyConsumption(
        long farmerId,
        float currentStamina,
        out string? violation)
    {
        violation = null;
        var state = AntiCheatCoordinator.GetOrCreatePlayerState(farmerId);

        if (state.LastStamina < 0)
        {
            state.LastStamina = currentStamina;
            return true;
        }

        // Jika stamina tidak berkurang saat mengayunkan alat
        if (currentStamina >= state.LastStamina)
        {
            state.ConsecutiveToolSwings++;
            if (state.ConsecutiveToolSwings >= MaxSwingsWithoutStaminaLossBeforeFlag)
            {
                violation = $"INFINITE_STAMINA_DETECTED: Mengayunkan alat sebanyak {state.ConsecutiveToolSwings}x berturut-turut tanpa konsumsi stamina.";
                return false;
            }
        }
        else
        {
            // Stamina berkurang wajar
            state.ConsecutiveToolSwings = 0;
            state.LastStamina = currentStamina;
        }

        return true;
    }

    /// <summary>
    /// Dipanggil saat pemain makan makanan, minum, atau tidur di kasur agar counter ayunan alat di-reset.
    /// </summary>
    public static void ResetStaminaTracking(long farmerId, float newStamina)
    {
        var state = AntiCheatCoordinator.GetOrCreatePlayerState(farmerId);
        state.ConsecutiveToolSwings = 0;
        state.LastStamina = newStamina;
    }
}
