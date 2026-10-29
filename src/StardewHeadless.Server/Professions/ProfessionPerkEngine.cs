using System;
using StardewValley;

namespace StardewHeadless.Server.Professions;

/// <summary>
/// Mesin kalkulasi perk dan bonus 3-tier untuk 5 Profesi RPG (Farmer, Miner, Fisherman, Rancher, Forager).
/// Berdasarkan spesifikasi matriks docs/30 bagian 4.2.
/// </summary>
public static class ProfessionPerkEngine
{
    public const int Tier1MaxLevel = 4;
    public const int Tier2MaxLevel = 9;

    public static int GetTier(int skillLevel)
    {
        if (skillLevel >= 10) return 3;
        if (skillLevel >= 5) return 2;
        return 1;
    }

    public static int ResolveSkillLevel(Farmer farmer, string profession)
    {
        if (farmer == null) return 0;
        var prof = profession?.ToLowerInvariant() ?? string.Empty;

        return prof switch
        {
            "farmer" => farmer.FarmingLevel,
            "miner" => farmer.MiningLevel,
            "fisherman" => farmer.FishingLevel,
            "rancher" => farmer.FarmingLevel,
            "forager" => farmer.ForagingLevel,
            _ => 0
        };
    }

    public static (int skillLevel, int tier) GetFarmerProfessionStatus(Farmer farmer)
    {
        if (farmer == null) return (0, 1);
        string prof = string.Empty;
        if (farmer.modData != null && farmer.modData.TryGetValue("svheadless/profession", out var pVal))
        {
            prof = pVal;
        }

        int level = ResolveSkillLevel(farmer, prof);
        int tier = GetTier(level);
        return (level, tier);
    }

    #region Farmer Perks
    public static int GetFarmerToolStaminaDiscountPercent(int tier) => tier >= 1 ? 15 : 0;
    public static double GetExtraCropYieldChance(int tier) => tier >= 2 ? 0.15 : 0.0;
    public static double GetFarmerCropPriceMultiplier(int tier) => tier >= 2 ? 1.10 : 1.0;
    public static double GetFarmerMachineSpeedMultiplier(int tier) => tier >= 3 ? 1.20 : 1.0; // 20% faster Keg & Preserves Jar
    #endregion

    #region Miner Perks
    public static int GetMinerPickaxeStaminaDiscountPercent(int tier) => tier >= 1 ? 20 : 0;
    public static int GetExtraOreYield(int tier) => tier >= 1 ? 1 : 0;
    public static double GetFurnaceSpeedMultiplier(int tier) => tier >= 2 ? 2.0 : 1.0; // 2x faster Smelting
    public static int GetSkullElevatorDiscountPercent(int tier) => tier switch
    {
        >= 3 => 100, // 100% Free at Tier 3 (Lv 10)
        >= 2 => 50,  // 50% Discount at Tier 2 (Lv 5-9)
        _ => 0
    };
    public static int GetMinesDefenseBonus(int tier) => tier >= 3 ? 3 : 0;
    #endregion

    #region Fisherman Perks
    public static int GetBobberBarExtraPixels(int tier) => tier >= 1 ? 16 : 0; // +16px BobberBar
    public static double GetBiteTimeMultiplier(int tier) => tier >= 1 ? 0.85 : 1.0; // -15% wait time
    public static double GetFishPriceMultiplier(int tier) => tier >= 2 ? 1.15 : 1.0; // +15% fish/roe price
    public static bool IsCrabPotTrashless(int tier) => tier >= 2;
    public static double GetDoubleCatchChance(int tier) => tier >= 3 ? 0.12 : 0.0; // +12% double catch
    #endregion

    #region Rancher Perks
    public static int GetPettingFriendshipBonus(int tier) => tier >= 1 ? 40 : 0; // +40 friendship
    public static double GetGrassConsumptionMultiplier(int tier) => tier >= 1 ? 0.75 : 1.0; // -25% grass consumption
    public static double GetIncubationSpeedMultiplier(int tier) => tier >= 2 ? 1.30 : 1.0; // 30% faster incubation
    public static double GetAnimalProductPriceMultiplier(int tier) => tier >= 2 ? 1.15 : 1.0; // +15% animal goods price
    public static double GetIridiumProductBonusChance(int tier) => tier >= 3 ? 0.35 : 0.0;
    #endregion

    #region Forager Perks
    public static double GetWoodMultiplier(int tier) => tier >= 1 ? 1.20 : 1.0; // +20% wood
    public static int GetForagerAxeStaminaDiscountPercent(int tier) => tier >= 1 ? 20 : 0;
    public static double GetTapperSpeedMultiplier(int tier) => tier >= 2 ? 1.25 : 1.0; // +25% tapper speed
    public static float GetForestSpeedBonus(int tier) => tier >= 2 ? 0.5f : 0.0f; // +0.5 speed in woods
    public static bool IsIridiumForageGuaranteed(int tier) => tier >= 3;
    public static double GetTreeSeedDropChance(int tier) => tier >= 3 ? 1.0 : 0.2;
    #endregion
}
