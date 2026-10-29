using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using StardewHeadless.Protocol.Logging;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.Locations;
using StardewValley.Objects;

namespace StardewHeadless.Server.World;

public static class ChestLockManager
{
    private static StructuredLogger? _logger;
    private static readonly ConcurrentDictionary<string, HashSet<long>> _sharedChestAccess = new();

    public static void Initialize(StructuredLogger? logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Mendapatkan atau membuat ID unik berbasis modData untuk Chest.
    /// </summary>
    public static string GetChestId(Chest chest)
    {
        if (chest.modData.TryGetValue("svheadless/chest_id", out var id) && !string.IsNullOrWhiteSpace(id))
        {
            return id;
        }

        var newId = Guid.NewGuid().ToString("N");
        chest.modData["svheadless/chest_id"] = newId;
        return newId;
    }

    /// <summary>
    /// Memastikan 2 Peti Bersama (Community Chests) tersedia di Peternakan untuk barter antar-profesi.
    /// Diletakkan di depan kabin paling tengah dan menggunakan sprite peti kayu asli Stardew Valley.
    /// </summary>
    public static void EnsureCommunityStorage(Farm farm)
    {
        if (farm == null) return;

        bool isIF2R = farm.NameOrUniqueName?.Contains("IF2R", StringComparison.OrdinalIgnoreCase) == true;

        Vector2 tileChest1;
        Vector2 tileChest2;

        var layout = MapLayoutResolver.Resolve(farm, ServerHost.Config?.FarmProfileId);
        tileChest1 = new Vector2(layout.CommunalChest1.X, layout.CommunalChest1.Y);
        tileChest2 = new Vector2(layout.CommunalChest2.X, layout.CommunalChest2.Y);

        PlaceOrVerifyCommunityChest(farm, tileChest1, "Peti Komunal Desa #1 (Hasil Bumi & Makanan)");
        PlaceOrVerifyCommunityChest(farm, tileChest2, "Peti Komunal Desa #2 (Mineral, Kayu & Alat)");

        // Bersihkan peti komunal lama di posisi lain dan migrasikan itemnya ke peti baru
        MigrateOldCommunityChests(farm, tileChest1, tileChest2);
    }

    private static void PlaceOrVerifyCommunityChest(Farm farm, Vector2 tile, string label)
    {
        // Bersihkan objek liar/rumput di tile target jika bukan Chest
        farm.terrainFeatures?.Remove(tile);
        if (farm.Objects.TryGetValue(tile, out var existingObj) && !(existingObj is Chest))
        {
            farm.Objects.Remove(tile);
            existingObj = null;
        }

        if (existingObj == null)
        {
            // Buat peti kayu vanilla Stardew Valley asli (ItemId "130", Qualified "(BC)130")
            var chest = new Chest(playerChest: true, tileLocation: tile, itemId: "130");
            chest.modData["svheadless/community_storage"] = "true";
            chest.modData["svheadless/chest_label"] = label;
            chest.playerChoiceColor.Value = Color.White; // Tampilan standar kayu stardew tanpa glitch
            
            farm.Objects[tile] = chest;
            _logger?.Info("CommunityStorage", $"Peti Komunal '{label}' berhasil ditempatkan pada tile ({tile.X}, {tile.Y}).");
        }
        else if (existingObj is Chest chest)
        {
            // Pulihkan aset jika sebelumnya berstatus error item / missing ItemId
            if (string.IsNullOrEmpty(chest.ItemId) || chest.ItemId == "0")
            {
                chest.ItemId = "130";
                chest.ParentSheetIndex = 130;
                chest.bigCraftable.Value = true;
            }
            chest.playerChest.Value = true;
            chest.modData["svheadless/community_storage"] = "true";
            chest.modData["svheadless/chest_label"] = label;
            chest.playerChoiceColor.Value = Color.White;
        }
    }

    private static void MigrateOldCommunityChests(Farm farm, Vector2 newTile1, Vector2 newTile2)
    {
        var chestsToMigrate = new List<(Vector2 Tile, Chest Chest)>();

        foreach (var pair in farm.Objects.Pairs)
        {
            if (pair.Value is Chest chest && pair.Key != newTile1 && pair.Key != newTile2)
            {
                // Cek apakah ini peti komunal lama atau peti dengan ItemId kosong/error di sekitar kabin
                if (chest.modData.ContainsKey("svheadless/community_storage") ||
                    string.IsNullOrEmpty(chest.ItemId) || chest.ItemId == "0" ||
                    (pair.Key.X >= 40 && pair.Key.X <= 75 && pair.Key.Y >= 12 && pair.Key.Y <= 20 && chest.Items.Count == 0))
                {
                    chestsToMigrate.Add((pair.Key, chest));
                }
            }
        }

        if (chestsToMigrate.Count > 0 && farm.Objects.TryGetValue(newTile1, out var c1Obj) && c1Obj is Chest targetChest1)
        {
            foreach (var (oldTile, oldChest) in chestsToMigrate)
            {
                foreach (var item in oldChest.Items)
                {
                    if (item != null)
                    {
                        targetChest1.addItem(item);
                    }
                }
                farm.Objects.Remove(oldTile);
                _logger?.Info("CommunityStorage", $"Peti komunal lama di ({oldTile.X}, {oldTile.Y}) berhasil dipindahkan ke ({newTile1.X}, {newTile1.Y}) dan dihapus.");
            }
        }
    }

    /// <summary>
    /// Memvalidasi apakah seorang pemain diizinkan membuka peti tertentu.
    /// Mencegah pencurian barang dari kabin pemain lain (anti-griefing).
    /// </summary>
    public static bool CanPlayerAccessChest(Farmer player, Chest chest)
    {
        if (player == null || chest == null) return false;

        // 1. Peti komunal desa selalu dapat diakses oleh semua warga
        if (chest.modData.ContainsKey("svheadless/community_storage"))
        {
            return true;
        }

        // 2. Jika peti berada di dalam Kabin orang lain (periksa lokasi peti dan lokasi pemain)
        GameLocation? chestLoc = chest.Location;
        if (chestLoc is Cabin targetCabin)
        {
            long cabinOwnerId = targetCabin.farmhandReference?.UID ?? 0;
            if (cabinOwnerId == 0)
            {
                try { cabinOwnerId = targetCabin.owner?.UniqueMultiplayerID ?? 0; } catch { }
            }

            if (cabinOwnerId != 0 && cabinOwnerId != player.UniqueMultiplayerID)
            {
                return false;
            }
        }
        else if (player.currentLocation is Cabin playerCabin)
        {
            long playerCabinOwnerId = playerCabin.farmhandReference?.UID ?? 0;
            if (playerCabinOwnerId == 0)
            {
                try { playerCabinOwnerId = playerCabin.owner?.UniqueMultiplayerID ?? 0; } catch { }
            }

            if (playerCabinOwnerId != 0 && playerCabinOwnerId != player.UniqueMultiplayerID)
            {
                return false;
            }
        }

        // 2b. Cek modData kepemilikan peti langsung
        if (chest.modData.TryGetValue("svheadless/chest_owner", out var ownerStr) && long.TryParse(ownerStr, out long lockedOwnerId))
        {
            if (lockedOwnerId != 0 && lockedOwnerId != player.UniqueMultiplayerID)
            {
                string cId = GetChestId(chest);
                if (_sharedChestAccess.TryGetValue(cId, out var allowed) && allowed.Contains(player.UniqueMultiplayerID))
                {
                    return true;
                }
                return false;
            }
        }

        // 3. Jika peti milik pemain itu sendiri atau peti bebas tanpa pemilik (owner == 0)
        if (chest.owner.Value == 0 || chest.owner.Value == player.UniqueMultiplayerID)
        {
            return true;
        }

        // 4. Periksa apakah akses telah dibagikan
        string chestId = GetChestId(chest);
        if (_sharedChestAccess.TryGetValue(chestId, out var allowedUsers))
        {
            return allowedUsers.Contains(player.UniqueMultiplayerID);
        }

        return false;
    }

    public static void ShareChestAccess(string chestId, long friendFarmerId)
    {
        var set = _sharedChestAccess.GetOrAdd(chestId, _ => new HashSet<long>());
        lock (set)
        {
            set.Add(friendFarmerId);
        }
    }

    public static void ShareChestAccess(Chest chest, long friendFarmerId)
    {
        if (chest == null) return;
        ShareChestAccess(GetChestId(chest), friendFarmerId);
    }
}
