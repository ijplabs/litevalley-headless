using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using StardewHeadless.Protocol.Logging;
using StardewHeadless.Server.Security;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.Locations;
using StardewValley.Objects;
using StardewValley.Network;

namespace StardewHeadless.Server.World;

/// <summary>
/// Mengelola Kabin Karantina (Virtual Lobby) untuk pemain yang belum terautentikasi.
/// Pemain yang baru masuk ditahan di kabin rahasia (-21, -21) dengan pintu keluar diblokir
/// sampai mereka sukses melakukan autentikasi via chat (!auth OTP atau /login password).
/// </summary>
public static class LobbyCabinManager
{
    private static StructuredLogger? _logger;

    /// <summary>
    /// Koordinat tersembunyi di peta Farm untuk Kabin Lobby bersama (-21, -21).
    /// </summary>
    public static readonly Point SharedLobbyPosition = new Point(-21, -21);

    /// <summary>
    /// Titik spawn pemain saat berada di dalam Kabin Lobby (tengah ruangan).
    /// </summary>
    public static readonly Point LobbySpawnPoint = new Point(5, 7);

    /// <summary>
    /// Posisi tile pintu keluar kabin yang diblokir secara fisik.
    /// </summary>
    public static readonly Vector2 DoorBlockerTile = new Vector2(3, 11);

    private const string DoorBlockerId = "(O)JunimoServer.NoEntry";

    public static void Initialize(StructuredLogger? logger)
    {
        _logger = logger;
    }

    public static Farm? SafeGetFarm()
    {
        try
        {
            if (Game1.locations == null || Game1.locations.Count == 0) return null;
            return Game1.getLocationFromName("Farm") as Farm;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Memeriksa apakah bangunan adalah Kabin Lobby Karantina.
    /// </summary>
    public static bool IsLobbyCabin(Building? building)
    {
        if (building == null) return false;
        return building.tileX.Value == SharedLobbyPosition.X && building.tileY.Value == SharedLobbyPosition.Y;
    }

    /// <summary>
    /// Memeriksa apakah lokasi kabin adalah interior Kabin Lobby Karantina.
    /// </summary>
    public static bool IsLobbyCabin(Cabin? cabin)
    {
        if (cabin == null) return false;
        var farm = SafeGetFarm();
        if (farm == null) return false;
        foreach (var b in farm.buildings)
        {
            if (b.isCabin && b.GetIndoors() == cabin)
            {
                return IsLobbyCabin(b);
            }
        }
        return false;
    }

    /// <summary>
    /// Memastikan bangunan Kabin Lobby bersama telah tersedia di peta Farm pada tile (-21, -21).
    /// </summary>
    public static Building? EnsureLobbyCabin(Farm farm)
    {
        if (farm == null) return null;

        Building? existing = farm.buildings.FirstOrDefault(b => b.isCabin && b.tileX.Value == SharedLobbyPosition.X && b.tileY.Value == SharedLobbyPosition.Y);
        if (existing == null)
        {
            var cabin = new Building("Cabin", new Vector2(SharedLobbyPosition.X, SharedLobbyPosition.Y));
            cabin.skinId.Value = "Log Cabin";
            cabin.daysOfConstructionLeft.Value = 0;
            cabin.load();

            if (farm.buildStructure(cabin, new Vector2(SharedLobbyPosition.X, SharedLobbyPosition.Y), Game1.player, true))
            {
                existing = cabin;
                _logger?.Info("LobbyCabin", $"Kabin karantina (Lobby) berhasil dibangun pada posisi tersembunyi ({SharedLobbyPosition.X}, {SharedLobbyPosition.Y}).");
            }
            else
            {
                _logger?.Warn("LobbyCabin", $"Gagal menempatkan kabin karantina pada ({SharedLobbyPosition.X}, {SharedLobbyPosition.Y}).");
                return null;
            }
        }

        if (existing.GetIndoors() is Cabin cabinIndoors)
        {
            SetupLobbyCabinInterior(cabinIndoors);
        }

        CleanFarmBlockerObjects(farm);

        return existing;
    }

    /// <summary>
    /// Mengambil interior Cabin dari Lobby bersama.
    /// </summary>
    public static Cabin? GetLobbyCabin(Farm? farm = null)
    {
        farm ??= SafeGetFarm();
        if (farm == null) return null;

        var building = farm.buildings.FirstOrDefault(b => b.isCabin && b.tileX.Value == SharedLobbyPosition.X && b.tileY.Value == SharedLobbyPosition.Y);
        return building?.GetIndoors() as Cabin;
    }

    /// <summary>
    /// Mengkonfigurasi interior kabin karantina agar terisolasi (tanpa warp keluar, pintu diblokir, tanpa farmhand).
    /// Menghilangkan seluruh barang bawaan kasur/TV/hadiah dan mendesain ulang kabin menjadi Lobi Ruang Tunggu Junimo.
    /// </summary>
    public static void SetupLobbyCabinInterior(Cabin cabin)
    {
        if (cabin == null) return;

        // 1. Pastikan Kabin Lobby tidak memiliki pemilik / slot farmhand aktif
        if (cabin.HasOwner)
        {
            cabin.DeleteFarmhand();
        }

        // 2. Kosongkan seluruh barang bawaan default kabin (kasur tidur, TV, meja, kursi, starter gift parsnip, perapian)
        cabin.furniture.Clear();
        cabin.objects.Clear();

        // 3. Set properti agar client dapat wake up / spawn tanpa membutuhkan kasur tidur
        try
        {
            if (cabin.map != null)
            {
                cabin.map.Properties["AllowWakeUpWithoutBed"] = "true";
            }
        }
        catch
        {
        }

        // 4. Ubah wallpaper dan lantai agar bernuansa lobi/ruang tunggu resmi (bukan kamar tidur pemain)
        try
        {
            cabin.SetWallpaper("4", "Bedroom"); // Dinding plester/batu elegan lobi
            cabin.SetFloor("39", "Bedroom");    // Lantai ubin/parquet kantor lobi
        }
        catch
        {
        }

        // 5. Konfigurasi warp pintu keluar kabin ke Farm
        cabin.warps.Clear();
        cabin.warps.Add(new Warp(3, 12, "Farm", 64, 15, false));

        // 6. Pasang blocker di tile pintu keluar (3, 11)
        try
        {
            cabin.objects[DoorBlockerTile] = new StardewValley.Object(DoorBlockerTile, DoorBlockerId);
        }
        catch
        {
        }

        // 7. Pasang furnitur dekorasi lobi kustom (waiting room, karpet, sofa, tanaman)
        DecorateLobby(cabin);

        // 8. Set pencahayaan lobby selalu terang (daylight mode)
        SetCabinDaylight(cabin);
    }

    private static void DecorateLobby(Cabin cabin)
    {
        try
        {
            // Barikade pintu keluar di tile (2, 12) dan (4, 12)
            AddFurnitureSafe(cabin, "(F)1376", 2, 12, 0);
            AddFurnitureSafe(cabin, "(F)1376", 4, 12, 0);

            // Dinding Utara: Rak arsip, spanduk, bintang Junimo
            AddFurnitureSafe(cabin, "(F)DarkBookcase", 1, 1, 0);
            AddFurnitureSafe(cabin, "(F)SmallStackedElixirShelf", 9, 1, 0);
            AddFurnitureSafe(cabin, "(F)JunimoStar", 3, 1, 0);
            AddFurnitureSafe(cabin, "(F)JunimoStar", 7, 1, 0);
            AddFurnitureSafe(cabin, "(F)1914", 4, 1, 0);
            AddFurnitureSafe(cabin, "(F)1916", 5, 1, 0);
            AddFurnitureSafe(cabin, "(F)1915", 6, 1, 0);

            // Area Tunggu Kanan (Lounge): Sofa Junimo, Meja Teh, Karpet
            AddFurnitureSafe(cabin, "(F)JunimoCouch", 9, 7, 3);
            AddFurnitureSafe(cabin, "(F)JunimoTeaTable", 9, 4, 0);
            AddFurnitureSafe(cabin, "(F)JunimoBag", 8, 4, 0);

            // Area Info Kiri: Pondok Junimo kecil, maskot tidur, hiasan
            AddFurnitureSafe(cabin, "(F)SmallJunimoHut", 1, 4, 0);
            AddFurnitureSafe(cabin, "(F)GraySleepingJunimo", 4, 4, 0);
            AddFurnitureSafe(cabin, "(F)1760", 1, 5, 0);
            AddFurnitureSafe(cabin, "(F)1762", 3, 6, 0);

            // Karpet Pusat & Samping
            AddFurnitureSafe(cabin, "(F)CircularJunimoRug", 5, 6, 0);
            AddFurnitureSafe(cabin, "(F)SquareJunimoRug", 2, 7, 0);

            // Tanaman & Pohon Hias
            AddFurnitureSafe(cabin, "(F)CurlyTree", 1, 10, 0);
            AddFurnitureSafe(cabin, "(F)CurlyTree", 9, 10, -2);
            AddFurnitureSafe(cabin, "(F)JunimoPlant", 3, 10, 0);
            AddFurnitureSafe(cabin, "(F)JunimoPlant", 5, 10, 0);
            AddFurnitureSafe(cabin, "(F)JunimoPlant", 7, 10, 0);
            AddFurnitureSafe(cabin, "(F)1733", 5, 4, 0);
        }
        catch (Exception ex)
        {
            _logger?.Debug("LobbyCabin", $"Dekorasi interior kabin dilewati: {ex.Message}");
        }
    }

    private static void AddFurnitureSafe(Cabin cabin, string itemId, int x, int y, int rot)
    {
        try
        {
            var f = ItemRegistry.Create<Furniture>(itemId);
            if (f != null)
            {
                f.TileLocation = new Vector2(x, y);
                f.currentRotation.Value = rot;
                f.updateRotation();
                cabin.furniture.Add(f);
            }
        }
        catch
        {
        }
    }

    private static void SetCabinDaylight(Cabin cabin)
    {
        try
        {
            var flags = BindingFlags.NonPublic | BindingFlags.Instance;
            typeof(GameLocation).GetField("indoorLightingColor", flags)?.SetValue(cabin, Color.White);
            typeof(GameLocation).GetField("indoorLightingNightColor", flags)?.SetValue(cabin, Color.White);
        }
        catch
        {
        }
    }

    /// <summary>
    /// Mengarahkan titik spawn dan lokasi farmhand ke Kabin Lobby Karantina.
    /// </summary>
    public static void ApplyLobbySpawnRedirect(Farmer farmer)
    {
        var lobbyCabin = GetLobbyCabin();
        if (lobbyCabin == null) return;

        var locName = lobbyCabin.NameOrUniqueName;
        farmer.Position = new Vector2(LobbySpawnPoint.X * 64f, LobbySpawnPoint.Y * 64f);
        farmer.currentLocation = lobbyCabin;
        farmer.disconnectDay.Value = (int)(Game1.stats?.DaysPlayed ?? (uint)Math.Max(1, Game1.dayOfMonth));
        farmer.disconnectLocation.Value = locName;
        farmer.disconnectPosition.Value = new Vector2(LobbySpawnPoint.X * 64f, LobbySpawnPoint.Y * 64f);
        farmer.lastSleepLocation.Value = locName;
        farmer.lastSleepPoint.Value = LobbySpawnPoint;
        farmer.sleptInTemporaryBed.Value = true;
    }

    /// <summary>
    /// Memindahkan pemain dari Kabin Lobby ke kabin asli peternakan saat autentikasi berhasil.
    /// </summary>
    public static void WarpPlayerFromLobby(long farmerId, Farmer? farmer)
    {
        try
        {
            if (farmer != null)
            {
                farmer.freezePause = 0;
            }

            var farm = SafeGetFarm();
            if (farm == null) return;

        Cabin? targetCabin = null;
        Building? targetBuilding = null;
        var authState = LobbyGatekeeper.GetAuthState(farmerId);
        int assignedIdx = authState?.AssignedCabinIndex ?? 0;

        // 1. Cari kabin berdasarkan AssignedCabinIndex
        if (assignedIdx > 0)
        {
            int idx = 0;
            foreach (var b in farm.buildings)
            {
                if (b.isCabin && !IsLobbyCabin(b))
                {
                    idx++;
                    if (idx == assignedIdx && b.GetIndoors() is Cabin c)
                    {
                        targetCabin = c;
                        targetBuilding = b;
                        break;
                    }
                }
            }
        }

        // 2. Fallback: Cocokkan kepemilikan kabin berdasarkan ID farmer
        if (targetCabin == null)
        {
            foreach (var b in farm.buildings)
            {
                if (b.isCabin && !IsLobbyCabin(b) && b.GetIndoors() is Cabin c)
                {
                    if (c.owner?.UniqueMultiplayerID == farmerId)
                    {
                        targetCabin = c;
                        targetBuilding = b;
                        break;
                    }
                }
            }
        }

        // 3. Fallback: Ambil kabin pertama yang tersedia
        if (targetCabin == null)
        {
            foreach (var b in farm.buildings)
            {
                if (b.isCabin && !IsLobbyCabin(b) && b.GetIndoors() is Cabin c)
                {
                    targetCabin = c;
                    targetBuilding = b;
                    break;
                }
            }
        }

        // Koordinat default teras depan Rumah Fisik Utama di Farm (60, 17)
        var mainHouse = farm.buildings.FirstOrDefault(b => b.isCabin && b.modData.ContainsKey("svheadless/main_estate_house"))
                        ?? farm.buildings.FirstOrDefault(b => b.isCabin && !IsLobbyCabin(b) && b.tileX.Value > 0);
        int farmX = mainHouse != null ? mainHouse.tileX.Value + 2 : 60;
        int farmY = mainHouse != null ? mainHouse.tileY.Value + 3 : 17;

        if (targetBuilding != null && targetBuilding.tileX.Value >= 0)
        {
            try
            {
                var doorPoint = targetBuilding.getPointForHumanDoor();
                farmX = doorPoint.X;
                farmY = doorPoint.Y + 1;
            }
            catch
            {
                farmX = targetBuilding.tileX.Value + 1;
                farmY = targetBuilding.tileY.Value + 3;
            }
        }
        else if (targetCabin?.ParentBuilding != null && targetCabin.ParentBuilding.tileX.Value >= 0)
        {
            var pb = targetCabin.ParentBuilding;
            try
            {
                var doorPoint = pb.getPointForHumanDoor();
                farmX = doorPoint.X;
                farmY = doorPoint.Y + 1;
            }
            catch
            {
                farmX = pb.tileX.Value + 1;
                farmY = pb.tileY.Value + 3;
            }
        }

        // Teleport langsung ke dalam kabin pribadi pemain (atau fallback ke Farm jika kabin null)
        string destLocationName;
        int tileX;
        int tileY;
        GameLocation destLocation;

        if (targetCabin != null)
        {
            destLocationName = targetCabin.NameOrUniqueName;
            var entry = targetCabin.getEntryLocation();
            tileX = entry.X;
            tileY = entry.Y;
            destLocation = targetCabin;
        }
        else
        {
            destLocationName = "Farm";
            tileX = farmX;
            tileY = farmY;
            destLocation = farm;
        }

        var lobbyCabin = GetLobbyCabin(farm);
        if (lobbyCabin != null)
        {
            // Buka blokir fisik di pintu keluar lobi
            lobbyCabin.objects.Remove(DoorBlockerTile);
            // Pasang warp pintu keluar langsung ke teras kabin asli pemain di Farm
            lobbyCabin.warps.Clear();
            lobbyCabin.warps.Add(new Warp(3, 12, "Farm", farmX, farmY, false));
        }

        // Bersihkan blocker icon stop dari peta Farm
        CleanFarmBlockerObjects(farm);

        _logger?.Info("LobbyCabin", $"Autentikasi terverifikasi untuk farmer {farmerId}. Memindahkan langsung ke kabin di {destLocationName} ({tileX}, {tileY}).");

        if (farmer != null)
        {
            farmer.freezePause = 0;
            farmer.disconnectDay.Value = (int)(Game1.stats?.DaysPlayed ?? (uint)Math.Max(1, Game1.dayOfMonth));
            farmer.disconnectLocation.Value = destLocationName;
            farmer.disconnectPosition.Value = new Vector2(tileX * 64f, tileY * 64f);
            farmer.lastSleepLocation.Value = destLocationName;
            farmer.lastSleepPoint.Value = new Point(tileX, tileY);
            farmer.currentLocation = destLocation;
            farmer.Position = new Vector2(tileX * 64f, tileY * 64f);
        }

        // Gunakan Multiplayer.passout (message type 29) untuk warp remote client secara engine-level.
        // Ini memicu Farmer.performPassoutWarp -> Game1.warpFarmer di sisi client dengan screen fade yang rapi,
        // sinkronisasi posisi yang presisi, dan menghindari desync (2 karakter) atau dump di pojok kiri farm (Grandpa's Shrine).
        if (Game1.server != null && Game1.player != null)
        {
            try
            {
                Game1.server.sendMessage(
                    farmerId,
                    Multiplayer.passout,
                    Game1.player,
                    new object[] { destLocationName, tileX, tileY, false }
                );
            }
            catch (Exception ex)
            {
                _logger?.Warn("LobbyCabin", $"Gagal memanggil sendMessage passout ke client {farmerId}: {ex.Message}");
            }
        }

        UpdateFarmerLocation(farmerId, destLocationName, tileX, tileY);

        try
        {
            Game1.Multiplayer?.sendChatMessage(
                LocalizedContentManager.LanguageCode.en,
                "[LOBBY] Autentikasi berhasil! Anda telah dipindahkan langsung ke kabin pribadi Anda. Selamat berpetualang di Lembah!",
                farmerId
            );
        }
        catch
        {
        }
        }
        catch (Exception ex)
        {
            _logger?.Debug("LobbyCabin", $"WarpPlayerFromLobby ditangguhkan: {ex.Message}");
        }
    }

    /// <summary>
    /// Memperbarui lokasi dan posisi farmer di server setelah warp dipicu.
    /// </summary>
    public static void UpdateFarmerLocation(long farmerId, string locationName, int tileX, int tileY)
    {
        try
        {
            var location = Game1.getLocationFromName(locationName, isStructure: false)
                ?? Game1.getLocationFromName(locationName, isStructure: true);
            if (location != null && Game1.otherFarmers != null && Game1.otherFarmers.TryGetValue(farmerId, out var farmer))
            {
                farmer.currentLocation = location;
                farmer.Position = new Vector2(tileX * 64f, tileY * 64f);
            }
        }
        catch (Exception ex)
        {
            _logger?.Debug("LobbyCabin", $"UpdateFarmerLocation error: {ex.Message}");
        }
    }

    /// <summary>
    /// Membersihkan seluruh objek blocker (NoEntry, DoorBlocker, Error Item) yang tertinggal di permukaan peta Farm.
    /// Menjamin tidak ada icon tanda stop liar yang mengambang di tanah peternakan.
    /// </summary>
    public static void CleanFarmBlockerObjects(Farm? farm = null)
    {
        farm ??= SafeGetFarm();
        if (farm == null) return;

        try
        {
            var toRemove = new List<Vector2>();
            foreach (var kvp in farm.objects.Pairs)
            {
                var obj = kvp.Value;
                if (obj == null) continue;
                if (obj is Chest) continue; // Lindungi peti / shared storage agar item pemain tidak terhapus!

                string id = obj.ItemId ?? string.Empty;
                string name = obj.Name ?? string.Empty;

                if (id.Contains("NoEntry", StringComparison.OrdinalIgnoreCase) ||
                    id.Contains("DoorBlocker", StringComparison.OrdinalIgnoreCase) ||
                    id == DoorBlockerId ||
                    name.Contains("NoEntry", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("DoorBlocker", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("Error Item", StringComparison.OrdinalIgnoreCase))
                {
                    toRemove.Add(kvp.Key);
                }
            }

            foreach (var tile in toRemove)
            {
                farm.objects.Remove(tile);
                _logger?.Info("LobbyCabin", $"Berhasil membersihkan blocker icon stop dari tanah Farm pada tile ({tile.X}, {tile.Y}).");
            }
        }
        catch (Exception ex)
        {
            _logger?.Debug("LobbyCabin", $"CleanFarmBlockerObjects error: {ex.Message}");
        }
    }
}
