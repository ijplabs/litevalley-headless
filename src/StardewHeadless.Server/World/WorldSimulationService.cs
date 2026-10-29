using System;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewHeadless.Protocol.Logging;
using StardewValley;
using StardewValley.BellsAndWhistles;
using StardewValley.Objects;
using StardewValley.TerrainFeatures;
using StardewValley.Tools;

namespace StardewHeadless.Server.World;

public static class WorldSimulationService
{
    private static StructuredLogger? _logger;
    private static bool _patchesApplied = false;

    private class DummyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    public static void Initialize(StructuredLogger? logger, Harmony harmony)
    {
        _logger = logger;
        ChestLockManager.Initialize(logger);
        DayTransitionManager.Initialize(logger, ServerHost.Database);

        EnsureHeadlessContentManager();

        if (!_patchesApplied)
        {
            ApplyPatches(harmony);
            _patchesApplied = true;
        }

        _logger?.Info("WorldSim", "WorldSimulationService berhasil diinisialisasi.");
    }

    public static void EnsureHeadlessContentManager()
    {
        if (Game1.content == null)
        {
            Game1.content = new LocalizedContentManager(new DummyServiceProvider(), "Content");
        }
    }

    private static void ApplyPatches(Harmony harmony)
    {
        try
        {
            // 0. Patch PlayerStatusList AddSpriteDefinition (Mencegah null reference saat Game1.content null/headless)
            harmony.Patch(
                original: AccessTools.Method(typeof(PlayerStatusList), nameof(PlayerStatusList.AddSpriteDefinition)),
                prefix: new HarmonyMethod(typeof(WorldSimulationService), nameof(PlayerStatusList_AddSpriteDefinition_Prefix))
            );

            // 0b. Patch Game1.getLocationFromName (Mencegah null reference saat Game1.locations belum diinisialisasi)
            harmony.Patch(
                original: AccessTools.Method(typeof(Game1), nameof(Game1.getLocationFromName), new[] { typeof(string), typeof(bool) }),
                prefix: new HarmonyMethod(typeof(WorldSimulationService), nameof(Game1_GetLocationFromName_Prefix))
            );

            // 0c. Patch Game1.currentLocation getter (Mencegah null reference saat Game1.game1 belum diinisialisasi)
            harmony.Patch(
                original: AccessTools.PropertyGetter(typeof(Game1), nameof(Game1.currentLocation)),
                prefix: new HarmonyMethod(typeof(WorldSimulationService), nameof(Game1_GetCurrentLocation_Prefix))
            );

            // 0d. Patch Farmer.LearnDefaultRecipes (Mencegah null reference saat DataLoader.CraftingRecipes belum tersedia)
            harmony.Patch(
                original: AccessTools.Method(typeof(Farmer), nameof(Farmer.LearnDefaultRecipes)),
                prefix: new HarmonyMethod(typeof(WorldSimulationService), nameof(Farmer_LearnDefaultRecipes_Prefix))
            );

            // 0e. Patch ItemRegistry.GetErrorItemName & Description (Mencegah ContentLoadException StringsFromCSFiles dalam mode headless)
            try
            {
                var getErrNameStr = AccessTools.Method(typeof(ItemRegistry), nameof(ItemRegistry.GetErrorItemName), new[] { typeof(string) });
                if (getErrNameStr != null)
                {
                    harmony.Patch(getErrNameStr, prefix: new HarmonyMethod(typeof(WorldSimulationService), nameof(ItemRegistry_GetErrorItemName_Prefix)));
                }

                var getErrNameNoArg = AccessTools.Method(typeof(ItemRegistry), nameof(ItemRegistry.GetErrorItemName), Type.EmptyTypes);
                if (getErrNameNoArg != null)
                {
                    harmony.Patch(getErrNameNoArg, prefix: new HarmonyMethod(typeof(WorldSimulationService), nameof(ItemRegistry_GetErrorItemName_NoArg_Prefix)));
                }
            }
            catch { }

            // 0f. Patch DataLoader.Machines (Mencegah ContentLoadException Data\Machines saat ItemRegistry.Create dalam mode headless)
            try
            {
                var machinesMethod = AccessTools.Method(typeof(DataLoader), nameof(DataLoader.Machines), new[] { typeof(LocalizedContentManager) });
                if (machinesMethod != null)
                {
                    harmony.Patch(
                        original: machinesMethod,
                        finalizer: new HarmonyMethod(typeof(WorldSimulationService), nameof(DataLoader_Machines_Finalizer))
                    );
                }
            }
            catch { }

            // 1. Patch Chest Access
            harmony.Patch(
                original: AccessTools.Method(typeof(Chest), nameof(Chest.checkForAction)),
                prefix: new HarmonyMethod(typeof(WorldSimulationService), nameof(Chest_CheckForAction_Prefix))
            );

            // 2. Patch HoeDirt Planting
            harmony.Patch(
                original: AccessTools.Method(typeof(HoeDirt), nameof(HoeDirt.plant)),
                postfix: new HarmonyMethod(typeof(WorldSimulationService), nameof(HoeDirt_Plant_Postfix))
            );

            // 3. Patch HoeDirt Tool Action (Crop Protection)
            harmony.Patch(
                original: AccessTools.Method(typeof(HoeDirt), nameof(HoeDirt.performToolAction)),
                prefix: new HarmonyMethod(typeof(WorldSimulationService), nameof(HoeDirt_PerformToolAction_Prefix))
            );

            // 4. Patch FarmAnimal Petting
            harmony.Patch(
                original: AccessTools.Method(typeof(FarmAnimal), nameof(FarmAnimal.pet)),
                postfix: new HarmonyMethod(typeof(WorldSimulationService), nameof(FarmAnimal_Pet_Postfix))
            );

            // 5. Patch DedicatedServer CheckOthersReady (Sleep Quorum)
            harmony.Patch(
                original: AccessTools.Method(typeof(StardewValley.Network.Dedicated.DedicatedServer), "CheckOthersReady"),
                prefix: new HarmonyMethod(typeof(DayTransitionManager), nameof(DayTransitionManager.CheckOthersReady_Prefix))
            );

            // 6. Patch Game1.newDayAfterFade (Pajak Ekspor Kas Desa & Sinkronisasi Perk Profesi)
            var newDayMethod = AccessTools.Method(typeof(Game1), "newDayAfterFade");
            if (newDayMethod != null)
            {
                harmony.Patch(
                    original: newDayMethod,
                    prefix: new HarmonyMethod(typeof(DayTransitionManager), nameof(DayTransitionManager.NewDay_Prefix)),
                    postfix: new HarmonyMethod(typeof(DayTransitionManager), nameof(DayTransitionManager.NewDay_Postfix))
                );
            }

            _logger?.Info("WorldSim", "Harmony patches untuk ChestLock, CropProtection, FarmAnimal, SleepQuorum, dan NewDayTax berhasil diaplikasikan.");
        }
        catch (Exception ex)
        {
            _logger?.Error("WorldSim", "Gagal mengaplikasikan sebagian patch WorldSimulationService", null, ex);
        }
    }

    #region Harmony Patch Callbacks

    private static Exception? DataLoader_Machines_Finalizer(Exception? __exception, ref Dictionary<string, StardewValley.GameData.Machines.MachineData>? __result)
    {
        if (__exception != null)
        {
            __result = new Dictionary<string, StardewValley.GameData.Machines.MachineData>();
            return null;
        }
        return null;
    }

    private static bool PlayerStatusList_AddSpriteDefinition_Prefix()
    {
        if (Game1.graphics?.GraphicsDevice == null) return false;
        return true;
    }

    private static bool Game1_GetLocationFromName_Prefix(ref GameLocation? __result, string name, bool isStructure)
    {
        if (Game1.game1 == null)
        {
            __result = null;
            return false;
        }
        return true;
    }

    private static bool Game1_GetCurrentLocation_Prefix(ref GameLocation? __result)
    {
        if (Game1.game1 == null)
        {
            __result = null;
            return false;
        }

        if (Game1.game1.instanceGameLocation == null && Game1.locations != null && Game1.locations.Count > 0)
        {
            var fallback = Game1.getLocationFromName("FarmHouse") ?? Game1.getLocationFromName("Farm") ?? Game1.locations[0];
            Game1.game1.instanceGameLocation = fallback;
            __result = fallback;
            return false;
        }

        return true;
    }

    private static bool Farmer_LearnDefaultRecipes_Prefix()
    {
        if (Game1.game1 == null)
        {
            return false;
        }
        return true;
    }

    private static bool ItemRegistry_GetErrorItemName_Prefix(string itemId, ref string __result)
    {
        __result = $"Item {itemId ?? "Unknown"}";
        return false;
    }

    private static bool ItemRegistry_GetErrorItemName_NoArg_Prefix(ref string __result)
    {
        __result = "Item Unknown";
        return false;
    }

    private static bool Chest_CheckForAction_Prefix(Chest __instance, Farmer who, bool justCheckingForActivity)
    {
        if (justCheckingForActivity) return true;
        return ChestLockManager.CanPlayerAccessChest(who, __instance);
    }

    private static void HoeDirt_Plant_Postfix(HoeDirt __instance, Farmer who)
    {
        CropProtectionService.RecordPlanting(__instance, who);
    }

    private static bool HoeDirt_PerformToolAction_Prefix(HoeDirt __instance, Tool t, int damage, Vector2 tileLocation)
    {
        if (t?.getLastFarmerToUse() is Farmer who)
        {
            return CropProtectionService.CanPlayerModifyCrop(who, __instance);
        }
        return true;
    }

    private static void FarmAnimal_Pet_Postfix(FarmAnimal __instance, Farmer who, bool is_auto_pet = false)
    {
        if (!is_auto_pet)
        {
            FarmAnimalManager.ApplyPetBonus(__instance, who);
        }
    }

    #endregion

    /// <summary>
    /// Dipanggil setiap beberapa detik di server loop untuk memastikan objek dunia aktif terus berproduksi.
    /// </summary>
    public static void UpdateWorldObjects(Farm farm)
    {
        if (farm == null) return;

        // Pastikan Peti Bersama Komunal selalu ada
        ChestLockManager.EnsureCommunityStorage(farm);
    }
}
