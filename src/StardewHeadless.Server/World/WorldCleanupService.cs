using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using StardewHeadless.Protocol.Logging;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.Locations;
using StardewValley.Objects;
using StardewValley.TerrainFeatures;

namespace StardewHeadless.Server.World;

public class WorldCleanResult
{
    public int CropsReleased { get; set; }
    public int ChestsUnlocked { get; set; }
    public int CabinsFreed { get; set; }
}

public static class WorldCleanupService
{
    public static WorldCleanResult CleanWorldForDeletedPlayer(long farmerId, StructuredLogger? logger = null)
    {
        var result = new WorldCleanResult();
        string farmerIdStr = farmerId.ToString();

        try
        {
            // 1. Kick/Putuskan sesi jika farmer masih online di Game1
            if (Game1.otherFarmers != null && Game1.otherFarmers.TryGetValue(farmerId, out var onlineFarmer))
            {
                Game1.server?.kick(farmerId);
                logger?.Info("WorldCleanup", $"Memutuskan koneksi aktif in-game untuk FarmerId={farmerId} ({onlineFarmer.Name}).");
            }

            if (Game1.locations == null)
            {
                return result;
            }

            // 2. Iterasi seluruh lokasi dunia
            foreach (var loc in Game1.locations)
            {
                if (loc == null) continue;

                // A. Pembebasan Tanaman (HoeDirt Crops)
                if (loc.terrainFeatures != null)
                {
                    foreach (var pair in loc.terrainFeatures.Pairs)
                    {
                        if (pair.Value is HoeDirt dirt)
                        {
                            if (dirt.modData.TryGetValue("svheadless/planter_id", out var pid) && pid == farmerIdStr)
                            {
                                dirt.modData["svheadless/planter_id"] = "0";
                                dirt.modData["svheadless/public_crop"] = "true";
                                dirt.modData.Remove("svheadless/planter_name");
                                result.CropsReleased++;
                            }
                        }
                    }
                }

                // B. Pembukaan Kunci Peti (Private Chests)
                if (loc.Objects != null)
                {
                    foreach (var pair in loc.Objects.Pairs)
                    {
                        if (pair.Value is Chest chest)
                        {
                            if (chest.owner.Value == farmerId)
                            {
                                chest.owner.Value = 0L;
                                result.ChestsUnlocked++;
                            }
                        }
                    }
                }
            }

            // C. Pembebasan Kabin di Peternakan
            var farm = Game1.getFarm();
            if (farm != null && farm.buildings != null)
            {
                foreach (var building in farm.buildings)
                {
                    if (building != null && building.GetIndoors() is Cabin cabin)
                    {
                        if (cabin.owner != null && cabin.owner.UniqueMultiplayerID == farmerId)
                        {
                            cabin.CreateFarmhand();
                            result.CabinsFreed++;
                            logger?.Info("WorldCleanup", $"Kabin #{building.tileX.Value},{building.tileY.Value} di-reset ke Farmhand baru.");
                        }
                    }
                }
            }

            logger?.Info("WorldCleanup", $"Pembersihan dunia untuk FarmerId={farmerId} selesai: {result.CropsReleased} tanaman publik, {result.ChestsUnlocked} peti dibuka, {result.CabinsFreed} kabin dilepas.");
        }
        catch (Exception ex)
        {
            logger?.Error("WorldCleanup", $"Exception saat melakukan pembersihan dunia untuk FarmerId={farmerId}", null, ex);
        }

        return result;
    }
}
