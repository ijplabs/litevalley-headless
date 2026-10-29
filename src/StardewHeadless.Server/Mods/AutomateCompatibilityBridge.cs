using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Objects;

namespace StardewHeadless.Server.Mods;

public record AutomateGroupInfo(
    string LocationName,
    Point ChestLocation,
    List<Point> ConnectedMachines,
    List<string> MachineTypes,
    int TotalStoredItems,
    bool IsPrivateLocked
);

/// <summary>
/// Jembatan kompatibilitas dengan mod Pathoschild.Automate di lingkungan Headless Server.
/// Mengimplementasikan ChestLockAutomationGuard untuk mencegah mesin otomatis menyedot (siphon)
/// isi peti privat milik pemain lain, serta mengekspos topologi grup mesin untuk endpoint web /api/v2/world/automation.
/// </summary>
public static class AutomateCompatibilityBridge
{
    /// <summary>
    /// Memvalidasi apakah mesin Automate diizinkan mengakses peti tertentu.
    /// Mengunci peti privat pemain lain dari akses sedot otomatis.
    /// </summary>
    public static bool CanAutomateAccessChest(Chest? chest, long? accessingFarmerId = null)
    {
        if (chest == null) return false;

        // 1. Peti komunal desa selalu dapat diotomasi untuk kemakmuran bersama
        if (chest.modData.ContainsKey("svheadless/community_storage"))
        {
            return true;
        }

        // 2. Peti terkunci sementara (misal sedang dibuka atau dikunci PIN) diblokir dari automasi
        if (chest.modData.ContainsKey("svheadless/locked_by"))
        {
            return false;
        }

        // 3. Peti privat milik pemain
        if (chest.modData.TryGetValue("svheadless/private_owner", out var ownerStr))
        {
            if (long.TryParse(ownerStr, out var ownerId))
            {
                // Jika automasi dipicu atas nama pemilik sah, izinkan
                if (accessingFarmerId.HasValue && accessingFarmerId.Value == ownerId)
                {
                    return true;
                }

                // Ditolak: Automate milik pemain lain tidak boleh menyedot peti privat ini!
                return false;
            }
        }

        // Peti liar atau tanpa pemilik di lahan publik
        return true;
    }

    /// <summary>
    /// Memindai lokasi untuk mendeteksi klaster automasi (peti dan mesin di sekitarnya)
    /// untuk kebutuhan pelaporan status API server.
    /// </summary>
    public static List<AutomateGroupInfo> ScanAutomateGroups(GameLocation location)
    {
        var result = new List<AutomateGroupInfo>();
        if (location == null) return result;

        foreach (var pair in location.Objects.Pairs)
        {
            if (pair.Value is Chest chest)
            {
                var chestTile = new Point((int)pair.Key.X, (int)pair.Key.Y);
                var connectedMachines = new List<Point>();
                var machineTypes = new HashSet<string>();

                // Periksa 4 tetangga ortogonal (atas, bawah, kiri, kanan)
                var neighbors = new[]
                {
                    new Point(chestTile.X, chestTile.Y - 1),
                    new Point(chestTile.X, chestTile.Y + 1),
                    new Point(chestTile.X - 1, chestTile.Y),
                    new Point(chestTile.X + 1, chestTile.Y)
                };

                foreach (var n in neighbors)
                {
                    var nVec = new Vector2(n.X, n.Y);
                    if (location.Objects.TryGetValue(nVec, out var neighborObj) && !(neighborObj is Chest))
                    {
                        if (neighborObj.bigCraftable.Value || neighborObj.heldObject.Value != null)
                        {
                            connectedMachines.Add(n);
                            machineTypes.Add(neighborObj.Name ?? "Machine");
                        }
                    }
                }

                if (connectedMachines.Count > 0)
                {
                    bool isPrivate = chest.modData.ContainsKey("svheadless/private_owner") || chest.modData.ContainsKey("svheadless/locked_by");
                    int itemsCount = chest.Items?.Count(i => i != null) ?? 0;

                    result.Add(new AutomateGroupInfo(
                        LocationName: location.NameOrUniqueName ?? "Unknown",
                        ChestLocation: chestTile,
                        ConnectedMachines: connectedMachines,
                        MachineTypes: machineTypes.ToList(),
                        TotalStoredItems: itemsCount,
                        IsPrivateLocked: isPrivate
                    ));
                }
            }
        }

        return result;
    }
}
