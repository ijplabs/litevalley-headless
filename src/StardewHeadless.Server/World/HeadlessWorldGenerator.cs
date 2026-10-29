using System;
using System.IO;
using Microsoft.Xna.Framework;
using StardewHeadless.Protocol.Logging;
using StardewHeadless.Server.Config;
using StardewValley;

namespace StardewHeadless.Server.World;

public static class HeadlessWorldGenerator
{
    public static string CreateFreshWorld(ServerConfig config, StructuredLogger? logger = null)
    {
        logger?.Info("WorldGen", $"Memulai pembuatan dunia multiplayer baru (Fresh World): '{config.FarmName}' (Tipe Farm: {config.FarmType})...");

        // 1. Konfigurasi Mode Multiplayer & Dompet Terpisah
        Game1.multiplayerMode = 2; // Dedicated Server Mode (Game1.IsServer = true)
        Game1.whichFarm = config.FarmType;

        // 2. Identitas Host Headless
        if (Game1.player != null)
        {
            Game1.player.Name = "Server";
            Game1.player.displayName = "Server";
            Game1.player.farmName.Value = config.FarmName;
            Game1.player.favoriteThing.Value = "Peace";
            Game1.player.isCustomized.Value = true;
            if (Game1.player.team != null)
            {
                Game1.player.team.useSeparateWallets.Value = config.UseSeparateWallets;
            }
        }

        Game1.startingCabins = config.StartingCabins;
        Game1.cabinsSeparate = true;
        Game1.bundleType = Game1.BundleType.Default;

        // 3. Generate ID unik untuk game baru
        var rng = new Random();
        Game1.uniqueIDForThisGame = (ulong)rng.NextInt64(100000000, 999999999);

        logger?.Info("WorldGen", $"UniqueID generated: {Game1.uniqueIDForThisGame}. Memanggil loadForNewGame()...");

        // 4. Bangun dunia awal tanpa antarmuka grafis
        if (Game1.game1 != null)
        {
            Game1.game1.loadForNewGame();

            // 5. Setup siklus hari pertama (Spring 1 Year 1)
            Game1.saveOnNewDay = true;
            Game1.dayOfMonth = 1;
            Game1.season = Season.Spring;
            Game1.year = 1;
            Game1.timeOfDay = 600;
            if (Game1.player != null)
            {
                Game1.player.currentLocation = Game1.RequireLocation("FarmHouse") ?? Utility.getHomeOfFarmer(Game1.player);
                Game1.player.Position = new Vector2(9f, 9f) * 64f;
                Game1.player.isInBed.Value = false;
                Game1.player.CanMove = true;
            }
            Game1.currentLocation = Game1.player?.currentLocation ?? Game1.RequireLocation("FarmHouse");
            Game1.netWorldState?.Value.UpdateFromGame1();

            logger?.Info("WorldGen", "Menjalankan Game1.NewDay(0f) untuk menyelesaikan generasi Spring 1 Year 1...");
            Game1.NewDay(0f);
            Game1.dayOfMonth = 1;
            Game1.season = Season.Spring;
            Game1.year = 1;
            Game1.timeOfDay = 600;
            Game1.exitActiveMenu();
            Game1.setGameMode(3); // In-Game Playing Mode
            Game1.currentLocation = Game1.player?.currentLocation ?? Game1.RequireLocation("FarmHouse");
            Game1.netWorldState?.Value.UpdateFromGame1();

            // 6. Tempatkan kabin awal yang terstruktur dan map-aware
            var farm = Game1.getFarm();
            if (farm != null)
            {
                ServerHost.EnsureMultiplayerCabins(farm, config.StartingCabins);
            }

            // 7. Simpan save game baru ke disk
            logger?.Info("WorldGen", "Menyimpan dunia baru ke disk via SaveGame.Save()...");
            Game1.SetSaveName(config.FarmName);
            SaveGame.Save();
        }
        else
        {
            logger?.Warn("WorldGen", "Game1.game1 instance tidak aktif (mode pengujian harness). Melewati inisialisasi grafis MonoGame.");
        }

        string saveBaseName = config.FarmName;
        try
        {
            if (Game1.player != null)
            {
                saveBaseName = Game1.GetSaveGameName();
            }
        }
        catch
        {
            saveBaseName = config.FarmName;
        }

        var saveFolder = $"{SaveGame.FilterFileName(saveBaseName)}_{Game1.uniqueIDForThisGame}";
        logger?.Info("WorldGen", $"Dunia multiplayer baru berhasil dibuat di '{saveFolder}'! Siap dibuka untuk pemain.");

        return saveFolder;
    }

    public static string ArchiveAndResetWorld(
        ServerConfig config,
        string? newFarmName = null,
        int? newFarmType = null,
        int? newStartingCabins = null,
        StructuredLogger? logger = null)
    {
        logger?.Info("WorldGen", $"Memulai proses reset & pengarsipan dunia saat ini: '{config.FarmName}'...");

        // 1. Arsipkan save folder lama jika ada
        var saveRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StardewValley", "Saves");
        if (Directory.Exists(saveRoot))
        {
            var archiveDir = Path.Combine(saveRoot, "archive");
            if (!Directory.Exists(archiveDir))
            {
                Directory.CreateDirectory(archiveDir);
            }

            foreach (var folder in Directory.GetDirectories(saveRoot))
            {
                var folderName = Path.GetFileName(folder);
                if (folderName.StartsWith(config.FarmName, StringComparison.OrdinalIgnoreCase))
                {
                    var targetArchive = Path.Combine(archiveDir, $"{folderName}_{DateTime.UtcNow:yyyyMMdd_HHmmss}");
                    try
                    {
                        Directory.Move(folder, targetArchive);
                        logger?.Info("WorldGen", $"Save folder '{folderName}' berhasil diarsipkan ke '{targetArchive}'.");
                    }
                    catch (Exception ex)
                    {
                        logger?.Warn("WorldGen", $"Gagal memindahkan save folder ke arsip: {ex.Message}");
                    }
                }
            }
        }

        // 2. Terapkan konfigurasi dunia baru jika disediakan
        if (!string.IsNullOrWhiteSpace(newFarmName))
        {
            config.FarmName = newFarmName.Trim();
        }
        if (newFarmType.HasValue && newFarmType.Value >= 0 && newFarmType.Value <= 6)
        {
            config.FarmType = newFarmType.Value;
        }
        if (newStartingCabins.HasValue && newStartingCabins.Value >= 1 && newStartingCabins.Value <= 8)
        {
            config.StartingCabins = newStartingCabins.Value;
        }

        // 3. Reset kepemilikan kabin dan tautan karakter game di SQLite jika database aktif
        if (ServerHost.Database != null)
        {
            try
            {
                ServerHost.Database.ResetCabinsAsync().GetAwaiter().GetResult();
                ServerHost.Database.ResetAllGameLinksAsync().GetAwaiter().GetResult();
                logger?.Info("WorldGen", "Data kepemilikan kabin dan tautan karakter game di SQLite berhasil di-reset.");
            }
            catch (Exception ex)
            {
                logger?.Warn("WorldGen", $"Gagal me-reset data game di database: {ex.Message}");
            }
        }

        // 4. Generate dunia baru
        return CreateFreshWorld(config, logger);
    }
}
