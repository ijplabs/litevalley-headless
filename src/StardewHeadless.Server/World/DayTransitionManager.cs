using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewHeadless.Protocol.Logging;
using StardewHeadless.Server.Database;
using StardewValley;
using StardewValley.Network;
using StardewValley.Network.Dedicated;

namespace StardewHeadless.Server.World;

public static class DayTransitionManager
{
    private static StructuredLogger? _logger;
    private static IDatabaseService? _databaseService;

    /// <summary>
    /// Ambang batas persentase kuorum tidur (default: 50% atau 0.5).
    /// Jika 50% pemain aktif telah berada di kasur, server otomatis memulai transisi NewDay.
    /// </summary>
    public static double SleepQuorumPercentage { get; set; } = 0.5;

    /// <summary>
    /// Tarif pajak ekspor komoditas hasil bumi ke Kas Desa (default: 5% atau 0.05).
    /// </summary>
    public static double VillageExportTaxRate { get; set; } = 0.05;

    public static void Initialize(StructuredLogger? logger, IDatabaseService? dbService)
    {
        _logger = logger;
        _databaseService = dbService;
        _logger?.Info("DayTransition", $"DayTransitionManager diinisialisasi (Kuorum Tidur: {SleepQuorumPercentage * 100}%, Pajak Ekspor: {VillageExportTaxRate * 100}%).");
    }

    #region Sleep Quorum & Synchronization

    /// <summary>
    /// Menghitung apakah kuorum pemain yang siap tidur telah terpenuhi.
    /// </summary>
    public static bool IsSleepQuorumMet(int readyCount, int totalRequired, double quorumPercentage = 0.5)
    {
        if (totalRequired <= 0) return false;
        if (readyCount <= 0) return false;

        double ratio = (double)readyCount / totalRequired;
        return ratio >= quorumPercentage;
    }

    /// <summary>
    /// Menentukan apakah dunia harus dijeda (pause) berdasarkan jumlah pemain manusia yang aktif dan terautentikasi.
    /// Jika tidak ada pemain terautentikasi (0 orang atau seluruh pemain masih di lobby/karantina), server otomatis menjeda waktu agar tanaman tidak mati sia-sia.
    /// </summary>
    public static bool DetermineShouldPause(int activePlayerCount, int authenticatedPlayerCount = -1)
    {
        if (authenticatedPlayerCount >= 0)
        {
            return authenticatedPlayerCount <= 0;
        }
        return activePlayerCount <= 0;
    }

    /// <summary>
    /// Mengaplikasikan status jeda waktu pada NetWorldState dengan dukungan filter autentikasi Lobby.
    /// IsPaused (pause mesin game) hanya aktif jika 0 pemain online (mencegah black screen client).
    /// IsTimePaused (pause jam in-game) aktif jika belum ada pemain terautentikasi (mencegah tanaman layu saat di karantina).
    /// </summary>
    public static void UpdateServerTimeProgression(int activePlayerCount, int authenticatedPlayerCount, NetWorldState? netWorldState)
    {
        if (netWorldState == null) return;

        bool shouldPauseGame = activePlayerCount <= 0;
        bool shouldPauseTime = DetermineShouldPause(activePlayerCount, authenticatedPlayerCount);

        if (netWorldState.IsPaused != shouldPauseGame || netWorldState.IsTimePaused != shouldPauseTime)
        {
            netWorldState.IsPaused = shouldPauseGame;
            netWorldState.IsTimePaused = shouldPauseTime;
            Game1.paused = shouldPauseGame;
            _logger?.Info("TimeProgression", $"Status waktu server diperbarui: IsPaused = {shouldPauseGame}, IsTimePaused = {shouldPauseTime} (Pemain aktif: {activePlayerCount}, Terautentikasi: {authenticatedPlayerCount})");
        }
    }

    /// <summary>
    /// Overload kompatibilitas untuk status jeda waktu server berdasarkan jumlah pemain aktif saja.
    /// </summary>
    public static void UpdateServerTimeProgression(int activePlayerCount, NetWorldState? netWorldState)
    {
        UpdateServerTimeProgression(activePlayerCount, -1, netWorldState);
    }

    #endregion

    #region Shipping Bin & 5% Village Tax

    /// <summary>
    /// Menghitung pembagian bruto, pajak ekspor (5%), dan penerimaan bersih petani.
    /// </summary>
    public static (int gross, int tax, int net) CalculateTax(int grossRevenue, double taxRate = 0.05)
    {
        if (grossRevenue <= 0)
        {
            return (0, 0, 0);
        }

        int tax = (int)Math.Floor(grossRevenue * taxRate);
        int net = grossRevenue - tax;
        return (grossRevenue, tax, net);
    }

    /// <summary>
    /// Menghitung total nilai barang di dalam Shipping Bin seorang pemain dan potongan pajaknya.
    /// </summary>
    public static (int gross, int tax, int net) CalculateShippingBinTax(IEnumerable<Item> items, long farmerId, double taxRate = 0.05)
    {
        if (items == null)
        {
            return (0, 0, 0);
        }

        int gross = 0;
        foreach (var item in items)
        {
            if (item == null) continue;

            int unitPrice = 0;
            if (item is StardewValley.Object obj)
            {
                if (Game1.player != null)
                {
                    try
                    {
                        unitPrice = obj.sellToStorePrice(farmerId);
                    }
                    catch
                    {
                        unitPrice = obj.Price;
                    }
                }
                else
                {
                    unitPrice = obj.Price;
                }
            }
            else
            {
                unitPrice = item.salePrice();
            }

            int stack = Math.Max(1, item.Stack);
            gross += Math.Max(0, unitPrice * stack);
        }

        return CalculateTax(gross, taxRate);
    }

    /// <summary>
    /// Memproses pemotongan pajak ekspor dari hasil penjualan Shipping Bin dan menyetorkannya ke Kas Desa (SQLite).
    /// </summary>
    public static async Task<(int gross, int tax, int net)> ProcessShippingTaxAsync(
        Farmer farmer, 
        IEnumerable<Item> shippedItems, 
        IDatabaseService? dbService = null, 
        StructuredLogger? logger = null)
    {
        if (farmer == null || shippedItems == null)
        {
            return (0, 0, 0);
        }

        var db = dbService ?? _databaseService;
        var log = logger ?? _logger;

        var (gross, tax, net) = CalculateShippingBinTax(shippedItems, farmer.UniqueMultiplayerID, VillageExportTaxRate);

        if (tax > 0 && db != null)
        {
            try
            {
                int newBalance = await db.RecordTreasuryTransactionAsync(
                    userId: farmer.UniqueMultiplayerID,
                    transactionType: "tax_export",
                    amount: tax,
                    notes: $"Pajak Ekspor {VillageExportTaxRate * 100:0.#}% dari hasil penjualan harian ({gross}g) oleh Farmer '{farmer.Name}'"
                );

                log?.Info("Treasury", $"[PAJAK DESA] Farmer '{farmer.Name}' menjual {gross}g komoditas. Pajak {tax}g disetorkan ke Kas Desa. Saldo Kas Desa sekarang: {newBalance}g.");
            }
            catch (Exception ex)
            {
                log?.Error("Treasury", $"Gagal mencatat transaksi pajak ekspor untuk Farmer '{farmer.Name}'", null, ex);
            }
        }

        return (gross, tax, net);
    }

    #endregion

    public static async Task ProcessAllShippingTaxesAndBroadcastAsync(StardewValley.Network.IGameServer? server)
    {
        if (_databaseService == null) return;

        int totalTaxCollected = 0;
        string lastFarmer = string.Empty;

        var allFarmers = new List<Farmer>();
        if (Game1.otherFarmers != null)
        {
            allFarmers.AddRange(Game1.otherFarmers.Values);
        }

        foreach (var farmer in allFarmers)
        {
            var shippedItems = new List<Item>();
            try
            {
                if (farmer.personalShippingBin?.Value != null && farmer.personalShippingBin.Value.Count > 0)
                {
                    shippedItems.AddRange(farmer.personalShippingBin.Value);
                }
                var farm = Game1.getFarm();
                if (farm != null)
                {
                    var bin = farm.getShippingBin(farmer);
                    if (bin != null && bin.Count > 0)
                    {
                        foreach (var item in bin)
                        {
                            if (!shippedItems.Contains(item)) shippedItems.Add(item);
                        }
                    }
                }
            }
            catch
            {
            }

            if (shippedItems.Count > 0)
            {
                var (gross, tax, net) = await ProcessShippingTaxAsync(farmer, shippedItems, _databaseService, _logger);
                if (tax > 0)
                {
                    totalTaxCollected += tax;
                    lastFarmer = farmer.Name;
                }
            }
        }

        if (totalTaxCollected > 0 && server != null && Game1.otherFarmers != null)
        {
            int newTreasuryBalance = await _databaseService.GetTreasuryBalanceAsync();
            var update = new StardewHeadless.Protocol.Packets.VillageTreasuryUpdatePacket
            {
                NewBalance = newTreasuryBalance,
                LastContributorName = lastFarmer,
                LastContributionAmount = totalTaxCollected
            };
            byte[] payload = update.Serialize();
            foreach (long peerId in Game1.otherFarmers.Keys)
            {
                server.sendMessage(peerId, new StardewValley.Network.OutgoingMessage(33, Game1.player?.UniqueMultiplayerID ?? 0, (object)payload));
            }
        }

        // Sinkronisasi SkillLevel seluruh petani yang terhubung dan bagikan buff perk 0xA0
        await StardewHeadless.Server.Professions.ProfessionLevelSyncService.SyncAllConnectedFarmersAsync(server, _databaseService);
    }

    private static readonly Dictionary<long, (int gross, int tax, string name)> _pendingShippingTaxes = new();

    /// <summary>
    /// Prefix patch sebelum Game1.newDayAfterFade membersihkan shipping bin.
    /// Mengumpulkan seluruh komoditas yang dikirim dan menghitung potongan pajak 5% Kas Desa.
    /// </summary>
    public static void NewDay_Prefix()
    {
        _pendingShippingTaxes.Clear();
        try
        {
            var allFarmers = new List<Farmer>();
            if (Game1.player != null) allFarmers.Add(Game1.player);
            if (Game1.otherFarmers != null) allFarmers.AddRange(Game1.otherFarmers.Values);

            var farm = Game1.getFarm();
            foreach (var farmer in allFarmers)
            {
                var shippedItems = new List<Item>();
                try
                {
                    if (farmer.personalShippingBin?.Value != null && farmer.personalShippingBin.Value.Count > 0)
                    {
                        shippedItems.AddRange(farmer.personalShippingBin.Value);
                    }
                    if (farm != null)
                    {
                        var bin = farm.getShippingBin(farmer);
                        if (bin != null && bin.Count > 0)
                        {
                            foreach (var item in bin)
                            {
                                if (!shippedItems.Contains(item)) shippedItems.Add(item);
                            }
                        }
                    }
                }
                catch { }

                if (shippedItems.Count > 0)
                {
                    var (gross, tax, _) = CalculateShippingBinTax(shippedItems, farmer.UniqueMultiplayerID, VillageExportTaxRate);
                    if (tax > 0)
                    {
                        _pendingShippingTaxes[farmer.UniqueMultiplayerID] = (gross, tax, farmer.Name);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.Error("DayTransition", "Exception saat kalkulasi NewDay_Prefix pajak ekspor", null, ex);
        }
    }

    public static void NewDay_Postfix()
    {
        // 1. Potong pajak dari dompet masing-masing petani di main thread
        int totalTaxCollected = 0;
        string lastFarmer = "Petani Desa";

        foreach (var (farmerId, taxInfo) in _pendingShippingTaxes)
        {
            Farmer? f = (Game1.player?.UniqueMultiplayerID == farmerId)
                ? Game1.player
                : (Game1.otherFarmers != null && Game1.otherFarmers.TryGetValue(farmerId, out var of) ? of : null);

            if (f != null && taxInfo.tax > 0)
            {
                f.Money = Math.Max(0, f.Money - taxInfo.tax);
                totalTaxCollected += taxInfo.tax;
                lastFarmer = taxInfo.name;
            }
        }

        var taxSnapshot = _pendingShippingTaxes.ToDictionary(k => k.Key, v => v.Value);
        _pendingShippingTaxes.Clear();

        _ = Task.Run(async () =>
        {
            try
            {
                if (_databaseService != null && taxSnapshot.Count > 0)
                {
                    foreach (var (farmerId, (gross, tax, name)) in taxSnapshot)
                    {
                        if (tax > 0)
                        {
                            var user = await _databaseService.GetUserByLinkedFarmerAsync(farmerId, name);
                            await _databaseService.RecordTreasuryTransactionAsync(
                                user?.Id,
                                transactionType: "tax_export",
                                amount: tax,
                                notes: $"Pajak Ekspor {VillageExportTaxRate * 100:0.#}% dari penjualan ({gross}g) oleh '{name}'");
                        }
                    }
                }

                if (totalTaxCollected > 0 && Game1.server != null && Game1.otherFarmers != null && _databaseService != null)
                {
                    int newTreasuryBalance = await _databaseService.GetTreasuryBalanceAsync();
                    var update = new StardewHeadless.Protocol.Packets.VillageTreasuryUpdatePacket
                    {
                        NewBalance = newTreasuryBalance,
                        LastContributorName = lastFarmer,
                        LastContributionAmount = totalTaxCollected
                    };
                    byte[] payload = update.Serialize();
                    foreach (long peerId in Game1.otherFarmers.Keys)
                    {
                        Game1.server.sendMessage(peerId, new StardewValley.Network.OutgoingMessage(33, Game1.player?.UniqueMultiplayerID ?? 0, (object)payload));
                    }
                }

                // Sinkronisasi SkillLevel seluruh petani yang terhubung dan bagikan buff perk 0xA0
                await StardewHeadless.Server.Professions.ProfessionLevelSyncService.SyncAllConnectedFarmersAsync(Game1.server, _databaseService);
            }
            catch (Exception ex)
            {
                _logger?.Error("DayTransition", "Gagal memproses pajak ekspor NewDay ke database", null, ex);
            }
        });
    }

        #region Harmony Patch Interceptors

    /// <summary>
    /// Prefix patch untuk DedicatedServer.CheckOthersReady guna mendukung Sleep Quorum.
    /// </summary>
    public static bool CheckOthersReady_Prefix(ref bool __result, string readyCheck)
    {
        if (readyCheck == "sleep")
        {
            int numberReady = Game1.netReady.GetNumberReady(readyCheck);
            if (numberReady <= 0)
            {
                __result = false;
                return false;
            }

            int requiredOthers = Game1.netReady.GetNumberRequired(readyCheck) - 1;
            if (requiredOthers <= 0)
            {
                __result = false;
                return false;
            }

            if (!Game1.netReady.IsReady(readyCheck))
            {
                if (IsSleepQuorumMet(numberReady, requiredOthers, SleepQuorumPercentage))
                {
                    _logger?.Info("DayTransition", $"[KUORUM TIDUR TERCAPAI] {numberReady}/{requiredOthers} pemain telah tidur (Kuorum: {SleepQuorumPercentage * 100}%). Menidurkan Host...");
                    __result = true;
                    return false;
                }
            }
        }

        return true; // Lanjutkan logika vanilla untuk festival atau ready checks lainnya
    }

    #endregion
}
