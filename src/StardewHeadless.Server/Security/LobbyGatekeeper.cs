using StardewValley.Network;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using StardewHeadless.Protocol;
using StardewHeadless.Protocol.Logging;
using StardewHeadless.Protocol.Packets;
using StardewHeadless.Server.Database;
using StardewHeadless.Server.World;
using StardewValley;

namespace StardewHeadless.Server.Security;

public class ClientAuthState
{
    public long FarmerId { get; set; }
    public bool IsAuthenticated { get; set; }
    public string Username { get; set; } = string.Empty;
    public int AssignedCabinIndex { get; set; }
    public string LockedProfession { get; set; } = string.Empty;
    public string? SessionToken { get; set; }
    public DateTime ConnectedAt { get; set; } = DateTime.UtcNow;
}

public static class LobbyGatekeeper
{
    private static StructuredLogger? _logger;
    private static IDatabaseService? _dbOverride;
    private static readonly ConcurrentDictionary<long, ClientAuthState> _clients = new();
    private static readonly ConcurrentDictionary<string, (long UserId, long FarmerId, DateTime ExpiresAt)> _activeSessionTokens = new();

    public static bool AllowLegacyAutoAuthWithoutTokenForTesting { get; set; } = false;

    public static string IssueSessionToken(long userId, long farmerId, TimeSpan? lifetime = null)
    {
        var token = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var expiry = DateTime.UtcNow.Add(lifetime ?? TimeSpan.FromDays(7));
        _activeSessionTokens[token] = (userId, farmerId, expiry);
        return token;
    }

    public static bool ValidateSessionToken(string? token, long userId, long farmerId)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;
        if (_activeSessionTokens.TryGetValue(token, out var entry))
        {
            if (DateTime.UtcNow > entry.ExpiresAt)
            {
                _activeSessionTokens.TryRemove(token, out _);
                return false;
            }
            return entry.UserId == userId && (entry.FarmerId == farmerId || entry.FarmerId == 0);
        }
        return false;
    }

    public static void ClearSessionTokens()
    {
        _activeSessionTokens.Clear();
    }

    public static Func<long?, string?, bool>? OldFarmerExistsOverride { get; set; }

    public static IDatabaseService? Database => _dbOverride ?? ServerHost.Database;

    public static void Initialize(StructuredLogger? logger, IDatabaseService? db = null)
    {
        _logger = logger;
        _dbOverride = db;
    }

    public static bool IsClientAuthenticated(long farmerId)
    {
        if (_clients.TryGetValue(farmerId, out var state))
        {
            return state.IsAuthenticated;
        }
        return false;
    }

    public static ClientAuthState OnClientConnected(long farmerId, string clientName, bool isAuthenticated = false)
    {
        if (_clients.TryGetValue(farmerId, out var existing) && existing.IsAuthenticated)
        {
            return existing;
        }

        var state = new ClientAuthState
        {
            FarmerId = farmerId,
            Username = clientName,
            IsAuthenticated = isAuthenticated,
            ConnectedAt = DateTime.UtcNow
        };
        _clients[farmerId] = state;
        if (isAuthenticated)
        {
            _logger?.Info("LobbyGatekeeper", $"Client '{clientName}' (ID: {farmerId}) terverifikasi dari database, bypass karantina.");
        }
        else
        {
            _logger?.Info("LobbyGatekeeper", $"Client '{clientName}' (ID: {farmerId}) dimasukkan ke virtual Lobby QUARANTINE menunggu autentikasi.");
        }
        return state;
    }

    /// <summary>
    /// Memeriksa apakah karakter/user sudah terdaftar dan terverifikasi di database.
    /// Jika ya, otomatis tandai terautentikasi dan bypass karantina lobi (lobi hanya 1x saat buat karakter baru).
    /// </summary>
    public static bool TryAutoAuthenticateExistingUser(long farmerId, string clientName, Farmer? farmer, string? sessionToken = null)
    {
        var db = Database;
        if (db == null) return false;

        try
        {
            if (string.IsNullOrEmpty(sessionToken) && _clients.TryGetValue(farmerId, out var existingState) && !string.IsNullOrEmpty(existingState.SessionToken))
            {
                sessionToken = existingState.SessionToken;
            }

            var user = db.GetUserByLinkedFarmerAsync(farmerId, clientName).GetAwaiter().GetResult()
                       ?? db.GetUserAsync(clientName).GetAwaiter().GetResult();

            if (user != null && !user.IsBanned && !user.IsDeleted)
            {
                bool isValid = false;
                if (!string.IsNullOrEmpty(sessionToken) && ValidateSessionToken(sessionToken, user.Id, farmerId))
                {
                    isValid = true;
                }
                else if (AllowLegacyAutoAuthWithoutTokenForTesting)
                {
                    isValid = true;
                }

                if (!isValid)
                {
                    _logger?.Warn("LobbyGatekeeper", $"Auto-auth ditolak untuk '{clientName}' (ID: {farmerId}): SessionToken tidak valid atau tidak disediakan.");
                    return false;
                }

                var state = OnClientConnected(farmerId, clientName, isAuthenticated: true);
                state.SessionToken = sessionToken;
                var prof = db.GetProfessionAsync(user.Id).GetAwaiter().GetResult();
                var cabin = db.GetCabinByUserAsync(user.Id).GetAwaiter().GetResult();
                state.LockedProfession = prof?.ProfessionType ?? "farmer";
                state.AssignedCabinIndex = cabin?.CabinIndex ?? 1;

                if (farmer != null)
                {
                    farmer.freezePause = 0;
                    farmer.modData["svheadless/profession"] = state.LockedProfession;
                }
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger?.Warn("LobbyGatekeeper", $"Gagal auto-authenticate user '{clientName}': {ex.Message}");
        }

        return false;
    }

    public static ClientAuthState? GetAuthState(long farmerId)
    {
        if (_clients.TryGetValue(farmerId, out var state))
        {
            return state;
        }
        return null;
    }

    public static HashSet<long> GetUnauthenticatedFarmerIds()
    {
        var set = new HashSet<long>();
        foreach (var kvp in _clients)
        {
            if (!kvp.Value.IsAuthenticated)
            {
                set.Add(kvp.Key);
            }
        }
        return set;
    }

    public static int GetAuthenticatedPlayerCount()
    {
        int count = 0;
        foreach (var client in _clients.Values)
        {
            if (client.IsAuthenticated) count++;
        }
        return count;
    }

    private static readonly ConcurrentDictionary<long, DateTime> _lastQuarantineReminder = new();

    public static void SendQuarantineReminder(long farmerId)
    {
        var now = DateTime.UtcNow;
        if (_lastQuarantineReminder.TryGetValue(farmerId, out var last) && (now - last).TotalSeconds < 4)
        {
            return;
        }
        _lastQuarantineReminder[farmerId] = now;

        try
        {
            Game1.Multiplayer?.sendChatMessage(
                LocalizedContentManager.LanguageCode.en,
                "[LOBBY] Akses dibatasi karantina! Ketik di chat: !auth <kode-otp> atau /login <password>",
                farmerId
            );
        }
        catch
        {
        }
    }

    public static void SendQuarantineWelcomeNotice(long farmerId, string clientName)
    {
        try
        {
            if (Game1.Multiplayer == null) return;

            Game1.Multiplayer.sendChatMessage(
                LocalizedContentManager.LanguageCode.en,
                $"--- [LOBBY KARANTINA LITEVALLEY] ---",
                farmerId
            );
            Game1.Multiplayer.sendChatMessage(
                LocalizedContentManager.LanguageCode.en,
                $"Halo {clientName}! Akun Anda berada dalam status Karantina.",
                farmerId
            );
            Game1.Multiplayer.sendChatMessage(
                LocalizedContentManager.LanguageCode.en,
                $"Pilihan 1 (OTP): Buka https://litevalley.ijplabs.dev tab Authenticator, lalu ketik: !auth <kode-otp>",
                farmerId
            );
            Game1.Multiplayer.sendChatMessage(
                LocalizedContentManager.LanguageCode.en,
                $"Pilihan 2 (Password): Ketik /login <password> (lama) atau /register <password> <profesi> (baru)",
                farmerId
            );
            Game1.Multiplayer.sendChatMessage(
                LocalizedContentManager.LanguageCode.en,
                $"Pilihan Profesi: farmer, miner, fisherman, rancher, forager",
                farmerId
            );
        }
        catch (Exception ex)
        {
            _logger?.Warn("LobbyGatekeeper", $"Gagal mengirim pesan selamat datang karantina ke {farmerId}: {ex.Message}");
        }
    }

    /// <summary>
    /// Membersihkan seluruh sesi klien saat tidak ada koneksi pemain yang aktif di server.
    /// </summary>
    public static void ClearAll()
    {
        if (!_clients.IsEmpty)
        {
            _clients.Clear();
            _logger?.Info("LobbyGatekeeper", "Semua sesi klien online dibersihkan (Server kosong).");
        }
    }

    public static void OnClientDisconnected(long farmerId)
    {
        if (_clients.TryRemove(farmerId, out var state))
        {
            _logger?.Info("LobbyGatekeeper", $"Client '{state.Username}' (ID: {farmerId}) terputus dari Lobby.");
        }
    }

    public static bool DoesFarmerExistInWorld(long? farmerId, string? farmerName)
    {
        if (OldFarmerExistsOverride != null)
        {
            return OldFarmerExistsOverride(farmerId, farmerName);
        }

        // 1. Cek apakah farmer lama sedang online / aktif di server saat ini
        if (farmerId.HasValue && _clients.TryGetValue(farmerId.Value, out var state) && state.IsAuthenticated)
        {
            return true;
        }

        // 2. Cek ke world save farmhand data jika game engine aktif
        if (Game1.netWorldState?.Value?.farmhandData != null)
        {
            if (farmerId.HasValue && Game1.netWorldState.Value.farmhandData.ContainsKey(farmerId.Value))
            {
                return true;
            }
            if (!string.IsNullOrWhiteSpace(farmerName))
            {
                foreach (var fRef in Game1.netWorldState.Value.farmhandData.FieldDict.Values)
                {
                    if (fRef?.Value != null && string.Equals(fRef.Value.Name, farmerName, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        // 3. Fallback jika netWorldState belum aktif (mode test harness tanpa world):
        // Jika ada farmerId terdaftar di sesi _clients, anggap ada. Jika tidak, anggap tidak ada.
        return farmerId.HasValue && _clients.ContainsKey(farmerId.Value);
    }

    public static async Task<BridgePacket> ProcessAuthSubmitAsync(AuthSubmitPacket submit, Farmer? farmer)
    {
        var db = _dbOverride ?? ServerHost.Database;
        if (db == null)
        {
            return new AuthRejectPacket
            {
                FarmerId = submit.FarmerId,
                ReasonCode = 5,
                ErrorMessage = "Database server sedang offline."
            };
        }

        string gameName = !string.IsNullOrWhiteSpace(farmer?.Name)
            ? farmer.Name.Trim()
            : (!string.IsNullOrWhiteSpace(submit.Username) ? submit.Username.Trim() : "Farmer");

        string username = !string.IsNullOrWhiteSpace(submit.Username)
            ? submit.Username.Trim()
            : gameName;

        var existingUser = await db.GetUserAsync(username);
        if (existingUser == null)
        {
            existingUser = await db.GetUserByLinkedFarmerAsync(submit.FarmerId, gameName);
            if (existingUser != null)
            {
                username = existingUser.Username;
            }
        }

        // Jika user tidak ditemukan berdasarkan nama farmer, namun input berupa kode OTP 6 digit,
        // cari user terdaftar yang memiliki TOTP aktif sesuai kode tersebut (auto-pairing).
        string inputPassOrOtp = submit.PasswordHash.Trim();
        if (existingUser == null && inputPassOrOtp.Length == 6 && int.TryParse(inputPassOrOtp, out _))
        {
            var allUsers = await db.GetAllUsersAsync();
            foreach (var u in allUsers)
            {
                if (!string.IsNullOrEmpty(u.TotpSecret) && TotpService.VerifyCode(u.TotpSecret, inputPassOrOtp))
                {
                    existingUser = u;
                    username = u.Username;
                    _logger?.Info("LobbyGatekeeper", $"Auto-pairing berhasil! Farmer '{gameName}' di-link ke akun web '{username}' via OTP.");
                    break;
                }
            }
        }

        // ====================================================================
        // KASUS 1: Registrasi Akun Baru (User Belum Terdaftar)
        // ====================================================================
        if (existingUser == null)
        {
            _logger?.Info("LobbyGatekeeper", $"Mendaftarkan akun baru untuk '{username}'...");

            var requestedProf = submit.ChosenProfession.Trim().ToLowerInvariant();
            var validProfessions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "farmer", "miner", "fisherman", "rancher", "forager"
            };

            if (string.IsNullOrEmpty(requestedProf) || !validProfessions.Contains(requestedProf))
            {
                return new AuthRejectPacket
                {
                    FarmerId = submit.FarmerId,
                    ReasonCode = 2,
                    ErrorMessage = "Pendaftaran pertama kali WAJIB memilih 1 profesi: farmer, miner, fisherman, rancher, atau forager."
                };
            }

            // Daftarkan ke database
            var newUser = await db.RegisterUserAsync(username, submit.PasswordHash);
            await db.LinkUserToGameFarmerAsync(newUser.Id, submit.FarmerId, gameName);

            // Kunci profesi 1x permanen
            await db.LockProfessionAsync(newUser.Id, requestedProf);

            // Alokasikan kabin kosong (cari index kabin 1..4 yang belum dimiliki)
            int assignedCabinIndex = 1;
            for (int i = 1; i <= 8; i++)
            {
                var owner = await db.GetCabinByIndexAsync(i);
                if (owner == null)
                {
                    assignedCabinIndex = i;
                    break;
                }
            }
            await db.AssignCabinAsync(newUser.Id, assignedCabinIndex, $"Cabin {assignedCabinIndex}");

            // Berikan paket peralatan awal (Starter Kit)
            if (farmer != null)
            {
                farmer.freezePause = 0;
                farmer.modData["svheadless/profession"] = requestedProf;
                var delivered = StarterKitDelivery.DeliverKit(farmer, requestedProf);
                await db.ClaimStarterKitAsync(newUser.Id);
                _logger?.Info("StarterKit", $"Starter kit '{requestedProf}' diberikan ke '{username}': {string.Join(", ", delivered)}");
            }

            var newSessionToken = IssueSessionToken(newUser.Id, submit.FarmerId);
            var authState = _clients.GetOrAdd(submit.FarmerId, id => new ClientAuthState { FarmerId = id });
            authState.IsAuthenticated = true;
            authState.Username = username;
            authState.AssignedCabinIndex = assignedCabinIndex;
            authState.LockedProfession = requestedProf;
            authState.SessionToken = newSessionToken;

            LobbyCabinManager.WarpPlayerFromLobby(submit.FarmerId, farmer);
            _logger?.Info("LobbyGatekeeper", $"Registrasi SUKSES! '{username}' resmi menjadi '{requestedProf.ToUpper()}' di Kabin #{assignedCabinIndex}. Pintu world dibuka!");
            return new AuthSuccessPacket
            {
                FarmerId = submit.FarmerId,
                SessionToken = newSessionToken,
                AssignedCabinIndex = assignedCabinIndex,
                LockedProfession = requestedProf,
                WelcomeMessage = $"Farmer '{gameName}' berhasil bergabung ke game dan ditautkan dengan akun '{newUser.Username}'! (Profesi: {requestedProf.ToUpper()})"
            };
        }

        // ====================================================================
        // KASUS 2: Login Akun Lama (User Sudah Terdaftar)
        // ====================================================================
        _logger?.Info("LobbyGatekeeper", $"Memverifikasi login untuk pengguna terdaftar: '{username}' (Farmer: '{gameName}')...");

        // Anti-Reuse & 1:1 Binding Check:
        // Jika akun web sudah tertaut ke suatu karakter, verifikasi keabsahan karakter.
        if (existingUser.IsGameLinked)
        {
            bool isSameFarmer = (submit.FarmerId == existingUser.LinkedFarmerId)
                || string.Equals(gameName, existingUser.LinkedFarmerName, StringComparison.OrdinalIgnoreCase);

            if (!isSameFarmer)
            {
                // Cek apakah karakter lama masih eksis di save game aktif (farmhandData atau online)
                bool oldFarmerStillExists = DoesFarmerExistInWorld(existingUser.LinkedFarmerId, existingUser.LinkedFarmerName);

                if (oldFarmerStillExists)
                {
                    _logger?.Warn("LobbyGatekeeper", $"Akun web '{existingUser.Username}' sudah ditautkan ke karakter '{existingUser.LinkedFarmerName}' (ID: {existingUser.LinkedFarmerId}). Percobaan oleh karakter '{gameName}' (ID: {submit.FarmerId}) ditolak.");
                    return new AuthRejectPacket
                    {
                        FarmerId = submit.FarmerId,
                        ReasonCode = 2,
                        ErrorMessage = $"Akun web '{existingUser.Username}' sudah ditautkan secara permanen dengan karakter '{existingUser.LinkedFarmerName}'. Pilih karakter tersebut di menu atau reset tautan via Web Dashboard!"
                    };
                }
                else
                {
                    // Karakter lama sudah tidak ada di save dunia aktif (misal: server di-reset, world save baru, atau slot dihapus).
                    // Smart Re-link: otomatis perbarui tautan akun ke karakter baru
                    _logger?.Info("LobbyGatekeeper", $"Karakter lama '{existingUser.LinkedFarmerName}' (ID: {existingUser.LinkedFarmerId}) tidak ditemukan di save dunia aktif. Memperbarui tautan akun '{existingUser.Username}' ke karakter baru '{gameName}' (ID: {submit.FarmerId}).");
                    await db.LinkUserToGameFarmerAsync(existingUser.Id, submit.FarmerId, gameName);
                    existingUser.LinkedFarmerId = submit.FarmerId;
                    existingUser.LinkedFarmerName = gameName;
                }
            }
        }

        // Cek apakah karakter game saat ini sudah ditautkan ke akun web lain
        var alreadyLinkedUser = await db.GetUserByLinkedFarmerAsync(submit.FarmerId, gameName);
        if (alreadyLinkedUser != null && alreadyLinkedUser.Id != existingUser.Id)
        {
            bool isRealConflict = (alreadyLinkedUser.LinkedFarmerId == submit.FarmerId);
            if (!isRealConflict)
            {
                // Jika hanya cocok nama tapi ID beda, cek apakah pemilik lama masih ada di dunia
                isRealConflict = DoesFarmerExistInWorld(alreadyLinkedUser.LinkedFarmerId, alreadyLinkedUser.LinkedFarmerName);
            }

            if (isRealConflict)
            {
                _logger?.Warn("LobbyGatekeeper", $"Karakter '{gameName}' (ID: {submit.FarmerId}) sudah ditautkan ke akun web '{alreadyLinkedUser.Username}'. Percobaan menautkan ke '{existingUser.Username}' ditolak.");
                return new AuthRejectPacket
                {
                    FarmerId = submit.FarmerId,
                    ReasonCode = 2,
                    ErrorMessage = $"Karakter '{gameName}' sudah tertaut secara permanen dengan akun web '{alreadyLinkedUser.Username}'. Tidak dapat ditautkan ke akun web lain!"
                };
            }
        }

        if (existingUser.IsBanned)
        {
            _logger?.Warn("LobbyGatekeeper", $"Percobaan login oleh akun yang dibanned: '{username}'");
            return new AuthRejectPacket
            {
                FarmerId = submit.FarmerId,
                ReasonCode = 4,
                ErrorMessage = $"Akses Ditolak: Akun Anda telah di-banned ({existingUser.BanReason ?? "Pelanggaran aturan server"})."
            };
        }

        if (existingUser.IsDeleted)
        {
            _logger?.Warn("LobbyGatekeeper", $"Percobaan login oleh akun terhapus: '{username}'");
            return new AuthRejectPacket
            {
                FarmerId = submit.FarmerId,
                ReasonCode = 3,
                ErrorMessage = "Akses Ditolak: Akun Anda telah dihapus karena inaktivitas. Silakan lakukan registrasi ulang."
            };
        }

        if (!existingUser.IsEmailVerified && existingUser.Role.Equals("Player", StringComparison.OrdinalIgnoreCase))
        {
            _logger?.Warn("LobbyGatekeeper", $"Percobaan login oleh akun belum verifikasi email: '{username}'");
            return new AuthRejectPacket
            {
                FarmerId = submit.FarmerId,
                ReasonCode = 2,
                ErrorMessage = "Akses Ditolak: Email akun Anda belum diverifikasi. Silakan buka Web Portal untuk memasukkan kode verifikasi email."
            };
        }

        bool authValid = await db.VerifyPasswordAsync(username, inputPassOrOtp);
        if (!authValid && inputPassOrOtp.Length == 6 && int.TryParse(inputPassOrOtp, out _))
        {
            authValid = await db.VerifyTotpAsync(username, inputPassOrOtp);
        }

        if (!authValid)
        {
            _logger?.Warn("LobbyGatekeeper", $"Autentikasi GAGAL untuk '{username}': Password atau Kode OTP tidak cocok!");
            return new AuthRejectPacket
            {
                FarmerId = submit.FarmerId,
                ReasonCode = 1,
                ErrorMessage = "Password akun atau Kode OTP Authenticator salah/kedaluwarsa!"
            };
        }

        // Verifikasi kepemilikan kabin (alokasikan jika pendaftar web belum punya kabin)
        var cabin = await db.GetCabinByUserAsync(existingUser.Id);
        int cabinIdx = cabin?.CabinIndex ?? 0;
        if (cabinIdx == 0)
        {
            for (int i = 1; i <= 8; i++)
            {
                var owner = await db.GetCabinByIndexAsync(i);
                if (owner == null)
                {
                    cabinIdx = i;
                    break;
                }
            }
            if (cabinIdx == 0) cabinIdx = 1;
            await db.AssignCabinAsync(existingUser.Id, cabinIdx, $"Cabin {cabinIdx}");
        }

        // Ambil profesi permanen & serahkan starter kit jika belum pernah diambil
        var userProf = await db.GetProfessionAsync(existingUser.Id);
        var lockedProf = userProf?.ProfessionType ?? "farmer";

        if (farmer != null)
        {
            farmer.freezePause = 0;
            farmer.modData["svheadless/profession"] = lockedProf;
            if (userProf != null && !userProf.StarterKitClaimed)
            {
                var delivered = StarterKitDelivery.DeliverKit(farmer, lockedProf);
                await db.ClaimStarterKitAsync(existingUser.Id);
                _logger?.Info("StarterKit", $"Starter kit '{lockedProf}' diserahkan ke '{username}': {string.Join(", ", delivered)}");
            }
        }

        if (!existingUser.IsGameLinked)
        {
            await db.LinkUserToGameFarmerAsync(existingUser.Id, submit.FarmerId, gameName);
            existingUser.IsGameLinked = true;
            existingUser.LinkedFarmerId = submit.FarmerId;
            existingUser.LinkedFarmerName = gameName;
            _logger?.Info("LobbyGatekeeper", $"Akun web '{existingUser.Username}' resmi ditautkan secara permanen dengan karakter '{gameName}' (ID: {submit.FarmerId}).");
        }

        await db.UpdateLastLoginAsync(existingUser.Id);

        var sessionToken = IssueSessionToken(existingUser.Id, submit.FarmerId);
        var existingAuthState = _clients.GetOrAdd(submit.FarmerId, id => new ClientAuthState { FarmerId = id });
        existingAuthState.IsAuthenticated = true;
        existingAuthState.Username = username;
        existingAuthState.AssignedCabinIndex = cabinIdx;
        existingAuthState.LockedProfession = lockedProf;
        existingAuthState.SessionToken = sessionToken;

        LobbyCabinManager.WarpPlayerFromLobby(submit.FarmerId, farmer);
        _logger?.Info("LobbyGatekeeper", $"Login SUKSES! Selamat datang kembali '{username}' ({lockedProf.ToUpper()}) di Kabin #{cabinIdx}.");
        return new AuthSuccessPacket
        {
            FarmerId = submit.FarmerId,
            SessionToken = sessionToken,
            AssignedCabinIndex = cabinIdx,
            LockedProfession = lockedProf,
            WelcomeMessage = $"Farmer '{gameName}' berhasil bergabung ke game dan ditautkan dengan akun '{existingUser.Username}'! (Profesi: {lockedProf.ToUpper()})"
        };
    }

    public static async Task<(bool success, string message)> ProcessChatAuthAsync(long farmerId, string chatCommand, Farmer? farmer)
    {
        if (string.IsNullOrWhiteSpace(chatCommand))
        {
            return (false, "Perintah kosong.");
        }

        string cmd = chatCommand.Trim();
        string[] parts = cmd.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return (false, "Perintah tidak valid.");

        string action = parts[0].ToLowerInvariant();

        if (action == "!help" || action == "/help")
        {
            string helpMsg = "[LOBBY] Cara Autentikasi: \n1. !auth <kode-otp>\n2. /login <password>\n3. /register <password> <profesi>\nProfesi: farmer, miner, fisherman, rancher, forager.";
            Game1.Multiplayer?.sendChatMessage(LocalizedContentManager.LanguageCode.en, helpMsg, farmerId);
            return (true, helpMsg);
        }

        if (action == "!profesi" || action == "/profesi" || action == "!profession" || action == "/profession")
        {
            string profMsg = "[LOBBY] Pilihan 5 Profesi Lembah: farmer, miner, fisherman, rancher, forager.";
            if (farmer != null && farmer.modData.TryGetValue("svheadless/profession", out var currentProf) && !string.IsNullOrEmpty(currentProf))
            {
                profMsg = $"[LOBBY] Profesi aktif Anda: {currentProf.ToUpper()}.";
            }
            Game1.Multiplayer?.sendChatMessage(LocalizedContentManager.LanguageCode.en, profMsg, farmerId);
            return (true, profMsg);
        }

        if (farmer == null && Game1.otherFarmers != null && Game1.otherFarmers.TryGetValue(farmerId, out var resolvedFarmer))
        {
            farmer = resolvedFarmer;
        }

        string username = farmer?.Name?.Trim() ?? "Player";
        string passwordOrOtp = string.Empty;
        string chosenProfession = string.Empty;

        if (action == "!auth" || action == "/auth")
        {
            if (parts.Length < 2)
            {
                string err = "[LOBBY GAGAL] Format salah! Gunakan: !auth <kode-otp> (contoh: !auth 742819)";
                Game1.Multiplayer?.sendChatMessage(LocalizedContentManager.LanguageCode.en, err, farmerId);
                return (false, err);
            }
            passwordOrOtp = parts[1];
            if (parts.Length > 2)
            {
                chosenProfession = parts[2];
            }
        }
        else if (action == "!login" || action == "/login")
        {
            if (parts.Length < 2)
            {
                string err = "[LOBBY GAGAL] Format salah! Gunakan: /login <password> atau /login <username> <password>";
                Game1.Multiplayer?.sendChatMessage(LocalizedContentManager.LanguageCode.en, err, farmerId);
                return (false, err);
            }
            if (parts.Length >= 3)
            {
                username = parts[1];
                passwordOrOtp = parts[2];
            }
            else
            {
                passwordOrOtp = parts[1];
            }
        }
        else if (action == "!register" || action == "/register")
        {
            if (parts.Length < 3)
            {
                string err = "[LOBBY GAGAL] Format salah! Gunakan: /register <password> <profesi> (contoh: /register Rahasia123 miner)";
                Game1.Multiplayer?.sendChatMessage(LocalizedContentManager.LanguageCode.en, err, farmerId);
                return (false, err);
            }
            if (parts.Length >= 4)
            {
                username = parts[1];
                passwordOrOtp = parts[2];
                chosenProfession = parts[3];
            }
            else
            {
                passwordOrOtp = parts[1];
                chosenProfession = parts[2];
            }
        }
        else
        {
            return (false, "Perintah tidak dikenali.");
        }

        var submit = new AuthSubmitPacket
        {
            FarmerId = farmerId,
            Username = username,
            PasswordHash = passwordOrOtp,
            ChosenProfession = chosenProfession
        };

        var response = await ProcessAuthSubmitAsync(submit, farmer);

        if (response is AuthSuccessPacket success)
        {
            if (farmer != null)
            {
                farmer.freezePause = 0;
            }

            // Pesan privat ke farmer
            Game1.Multiplayer?.sendChatMessage(
                LocalizedContentManager.LanguageCode.en,
                $"[LOBBY] {success.WelcomeMessage}",
                farmerId
            );

            // Pengumuman publik ke seluruh server
            Game1.Multiplayer?.sendChatMessage(
                LocalizedContentManager.LanguageCode.en,
                $"[LOBBY] {success.WelcomeMessage}",
                Multiplayer.AllPlayers
            );

            // Kirim paket bridge jika ada server aktif
            if (Game1.server != null)
            {
                try
                {
                    byte[] payload = success.Serialize();
                    var msg = new OutgoingMessage(33, Game1.player.UniqueMultiplayerID, (object)payload);
                    Game1.server.sendMessage(farmerId, msg);
                }
                catch
                {
                }
            }

            return (true, success.WelcomeMessage);
        }
        else if (response is AuthRejectPacket reject)
        {
            string err = $"[LOBBY GAGAL] {reject.ErrorMessage}";
            Game1.Multiplayer?.sendChatMessage(LocalizedContentManager.LanguageCode.en, err, farmerId);
            return (false, reject.ErrorMessage);
        }

        return (false, "Respon tidak dikenal dari gatekeeper.");
    }
}
