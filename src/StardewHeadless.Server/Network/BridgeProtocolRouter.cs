using System.IO;
using StardewHeadless.Protocol;
using StardewHeadless.Protocol.Logging;
using StardewHeadless.Protocol.Models;
using StardewHeadless.Protocol.Packets;
using StardewValley;
using StardewValley.Network;

namespace StardewHeadless.Server.Network;

public static class BridgeProtocolRouter
{
    private static StructuredLogger? _logger;
    private static readonly Dictionary<long, string> _connectedBridges = new();

    public static void Initialize(StructuredLogger logger)
    {
        _logger = logger;
        Security.LobbyGatekeeper.Initialize(logger);
    }

    public static void HandleServerMessage(GameServer server, IncomingMessage message, byte subOpCode)
    {
        try
        {
            var packet = PacketFactory.ParsePayload(subOpCode, message.Reader);
            if (packet == null)
            {
                _logger?.Warn("BridgeRouter", $"Gagal mem-parse sub-opcode 0x{subOpCode:X2} dari FarmerId={message.FarmerID}");
                return;
            }

            switch (packet)
            {
                case ClientHelloPacket hello:
                    HandleClientHello(server, message.FarmerID, hello);
                    break;

                case ClientHeartbeatPacket heartbeat:
                    HandleClientHeartbeat(server, message.FarmerID, heartbeat);
                    break;

                case ClientBugReportSignalPacket bugSignal:
                    HandleBugReportSignal(message.FarmerID, bugSignal);
                    break;

                case ProfessionSelectRequestPacket profReq:
                    HandleProfessionSelect(server, message.FarmerID, profReq);
                    break;

                case TreasuryDepositRequestPacket depositReq:
                    HandleTreasuryDeposit(server, message.FarmerID, depositReq);
                    break;

                case AuthSubmitPacket authSubmit:
                    HandleAuthSubmit(server, message.FarmerID, authSubmit);
                    break;

                case ClientModAuditPacket modAudit:
                    HandleClientModAudit(server, message.FarmerID, modAudit);
                    break;

                default:
                    _logger?.Debug("BridgeRouter", $"Menerima paket sub-opcode 0x{subOpCode:X2} ({packet.GetType().Name}) dari FarmerId={message.FarmerID}");
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger?.Error("BridgeRouter", $"Exception saat menangani paket sub-opcode 0x{subOpCode:X2}", null, ex);
        }
    }

    private static void HandleClientHello(GameServer server, long farmerId, ClientHelloPacket hello)
    {
        _connectedBridges[farmerId] = hello.BridgeVersion;
        _logger?.Info("BridgeRouter", $"[HANDSHAKE] Menerima ClientHello dari FarmerId={farmerId}, ClientName='{hello.ClientName}', BridgeVer='{hello.BridgeVersion}', ProtoVer={hello.ProtocolVersion}");

        var welcome = new ServerWelcomePacket
        {
            ProtocolVersion = ProtocolConstants.ProtocolVersion,
            AssignedFarmerId = farmerId,
            ServerName = "StardewHeadless Dedicated Server",
            ServerTick = Game1.ticks,
            Year = Game1.year,
            Season = Game1.currentSeason ?? "spring",
            DayOfMonth = Game1.dayOfMonth,
            TimeOfDay = Game1.timeOfDay
        };

        byte[] payload = welcome.Serialize();
        var reply = new OutgoingMessage(33, Game1.player.UniqueMultiplayerID, (object)payload);
        server.sendMessage(farmerId, reply);

        _logger?.Info("BridgeRouter", $"[HANDSHAKE] Mengirim ServerWelcome ke FarmerId={farmerId} (Tahun {welcome.Year} {welcome.Season} Hari {welcome.DayOfMonth}, Jam {welcome.TimeOfDay})");

        // Periksa apakah pemain ini sudah terdaftar & terautentikasi (reconnect pemain lama)
        Farmer? targetFarmer = null;
        if (Game1.otherFarmers.TryGetValue(farmerId, out var f))
        {
            targetFarmer = f;
        }

        bool isAuthenticated = Security.LobbyGatekeeper.IsClientAuthenticated(farmerId)
            || Security.LobbyGatekeeper.TryAutoAuthenticateExistingUser(farmerId, hello.ClientName, targetFarmer);

        if (isAuthenticated)
        {
            var authState = Security.LobbyGatekeeper.GetAuthState(farmerId);
            var success = new AuthSuccessPacket
            {
                SessionToken = Guid.NewGuid().ToString("N"),
                AssignedCabinIndex = authState?.AssignedCabinIndex ?? 1,
                LockedProfession = authState?.LockedProfession ?? "farmer",
                WelcomeMessage = $"Selamat datang kembali di Lembah, {hello.ClientName}!"
            };
            byte[] successPayload = success.Serialize();
            var successMsg = new OutgoingMessage(33, Game1.player.UniqueMultiplayerID, (object)successPayload);
            server.sendMessage(farmerId, successMsg);
            _logger?.Info("LobbyGatekeeper", $"[AUTH] Auto-login sukses untuk pemain terdaftar FarmerId={farmerId} ('{hello.ClientName}'). Mengirim AuthSuccessPacket.");
        }
        else
        {
            // Masukkan client baru ke Lobby Quarantine dan kirim tantangan AuthChallengePacket (0x94)
            Security.LobbyGatekeeper.OnClientConnected(farmerId, hello.ClientName, isAuthenticated: false);

            var challenge = new AuthChallengePacket
            {
                ChallengeNonce = Guid.NewGuid().ToString("N"),
                ServerName = ServerHost.Config.ServerName,
                IsRegistrationRequired = true,
                Message = "Silakan masukkan password untuk login atau registrasi akun baru beserta profesi Anda."
            };
            byte[] challengePayload = challenge.Serialize();
            var challengeMsg = new OutgoingMessage(33, Game1.player.UniqueMultiplayerID, (object)challengePayload);
            server.sendMessage(farmerId, challengeMsg);
            _logger?.Info("LobbyGatekeeper", $"[AUTH] Pemain baru FarmerId={farmerId} ('{hello.ClientName}') dimasukkan ke karantina lobi. Mengirim AuthChallengePacket.");
        }
    }

    private static void HandleAuthSubmit(GameServer server, long farmerId, AuthSubmitPacket submit)
    {
        _logger?.Info("LobbyGatekeeper", $"[AUTH] Menerima AuthSubmit dari FarmerId={farmerId}, Username='{submit.Username}'");

        Farmer? targetFarmer = null;
        if (Game1.otherFarmers.TryGetValue(farmerId, out var f))
        {
            targetFarmer = f;
        }

        Task.Run(async () =>
        {
            try
            {
                var responsePacket = await Security.LobbyGatekeeper.ProcessAuthSubmitAsync(submit, targetFarmer);
                byte[] payload = responsePacket.Serialize();
                var msg = new OutgoingMessage(33, Game1.player.UniqueMultiplayerID, (object)payload);
                server.sendMessage(farmerId, msg);

                _logger?.Info("LobbyGatekeeper", $"[AUTH] Mengirim respon {responsePacket.GetType().Name} (0x{responsePacket.SubOpCode:X2}) ke FarmerId={farmerId}");
            }
            catch (Exception ex)
            {
                _logger?.Error("LobbyGatekeeper", $"Exception saat memproses AuthSubmit dari FarmerId={farmerId}", null, ex);
            }
        });
    }

    private static void HandleClientHeartbeat(GameServer server, long farmerId, ClientHeartbeatPacket heartbeat)
    {
        // Client heartbeat ping tracking
        _logger?.Debug("BridgeRouter", $"Heartbeat dari FarmerId={farmerId}, Seq={heartbeat.Sequence}");
    }

    private static void HandleBugReportSignal(long farmerId, ClientBugReportSignalPacket bugSignal)
    {
        _logger?.Warn("BugReport", $"[BUG REPORT] Dari FarmerId={farmerId} ({bugSignal.ClientLocation} @ {bugSignal.TileX},{bugSignal.TileY}): {bugSignal.UserNote} [Menu: {bugSignal.ActiveMenu}]");

        var report = new BugReport
        {
            UserNote = bugSignal.UserNote,
            Reporter = new BugReporterInfo
            {
                FarmerId = farmerId,
                Name = Game1.GetPlayer(farmerId)?.Name ?? "Unknown"
            },
            ClientState = new ClientBugState
            {
                Location = bugSignal.ClientLocation,
                TilePosition = new TileVector { X = (int)bugSignal.TileX, Y = (int)bugSignal.TileY },
                ActiveMenu = bugSignal.ActiveMenu
            },
            ServerState = new ServerBugState
            {
                WorldTime = Game1.timeOfDay,
                TicksElapsed = Game1.ticks,
                SleepReadyCount = Game1.player?.team?.announcedSleepingFarmers?.Count ?? 0,
                SleepTotalCount = Game1.otherFarmers.Count + 1
            }
        };

        try
        {
            var logsDir = !string.IsNullOrWhiteSpace(ServerHost.LogsDir)
                ? ServerHost.LogsDir
                : Path.Combine(Directory.GetCurrentDirectory(), "logs");
            Directory.CreateDirectory(logsDir);
            var reportPath = Path.Combine(logsDir, "bug-reports.jsonl");

            var json = System.Text.Json.JsonSerializer.Serialize(report, new System.Text.Json.JsonSerializerOptions { WriteIndented = false });
            using var stream = new FileStream(reportPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            using var writer = new StreamWriter(stream, System.Text.Encoding.UTF8);
            writer.WriteLine(json);
            writer.Flush();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BridgeProtocolRouter] Gagal menyimpan bug report ke disk: {ex.Message}");
        }
    }

    private static int _villageBankBalance = 1500; // Starter fund Kas Desa

    private static void HandleProfessionSelect(GameServer server, long farmerId, ProfessionSelectRequestPacket req)
    {
        var farmer = Game1.GetPlayer(farmerId);
        string name = farmer?.Name ?? $"Farmer_{farmerId}";
        string prof = req.ProfessionName;

        if (farmer != null)
        {
            farmer.modData["svheadless/profession"] = prof;
        }

        _logger?.Info("Economy", $"[PROFESI] Farmer '{name}' ({farmerId}) memilih profesi RPG: {prof}");

        var resp = new ProfessionSyncResponsePacket
        {
            FarmerId = farmerId,
            ConfirmedProfession = prof,
            Success = true,
            Message = $"Selamat! Anda resmi memegang profesi {prof}.",
            VillageBankBalance = _villageBankBalance
        };

        byte[] payload = resp.Serialize();
        server.sendMessage(farmerId, new OutgoingMessage(33, Game1.player.UniqueMultiplayerID, (object)payload));
    }

    private static void HandleTreasuryDeposit(GameServer server, long farmerId, TreasuryDepositRequestPacket req)
    {
        var farmer = Game1.GetPlayer(farmerId);
        string name = farmer?.Name ?? $"Farmer_{farmerId}";

        if (req.Amount > 0)
        {
            _villageBankBalance += req.Amount;
            _logger?.Info("Economy", $"[KAS DESA] Farmer '{name}' mendonasikan {req.Amount}g ke Kas Desa. Total Kas Desa saat ini: {_villageBankBalance}g");

            var update = new VillageTreasuryUpdatePacket
            {
                NewBalance = _villageBankBalance,
                LastContributorName = name,
                LastContributionAmount = req.Amount
            };

            byte[] payload = update.Serialize();
            // Rebroadcast pembaruan kas desa ke seluruh client terhubung
            foreach (long peerId in Game1.otherFarmers.Keys)
            {
                server.sendMessage(peerId, new OutgoingMessage(33, Game1.player.UniqueMultiplayerID, (object)payload));
            }
        }
    }

    private static void HandleClientModAudit(GameServer server, long farmerId, ClientModAuditPacket modAudit)
    {
        _logger?.Info("ModAudit", $"[MOD TELEMETRY] Menerima manifest {modAudit.InstalledMods.Count} mod dari FarmerId={farmerId}, HWID={modAudit.HwidHash}");

        Task.Run(async () =>
        {
            try
            {
                var db = ServerHost.Database;
                if (db == null) return;

                // 1. Cek HWID Ban
                if (!string.IsNullOrWhiteSpace(modAudit.HwidHash) && await db.IsHwidBannedAsync(modAudit.HwidHash))
                {
                    _logger?.Warn("ModAudit", $"[HWID BAN] Komputer FarmerId={farmerId} (HWID={modAudit.HwidHash}) terdaftar di banned_hwids!");
                    var rejectMsg = "Akses Ditolak: Perangkat keras Anda telah diblokir secara permanen dari server ini.";
                    var response = new ClientModAuditResponsePacket
                    {
                        FarmerId = farmerId,
                        IsAllowed = false,
                        RejectionReason = rejectMsg
                    };
                    server.sendMessage(farmerId, new OutgoingMessage(33, Game1.player.UniqueMultiplayerID, (object)response.Serialize()));
                    server.kick(farmerId);
                    return;
                }

                // 2. Cek Pelanggaran Kebijakan Mod (Blacklist)
                var violations = await db.CheckModViolationsAsync(modAudit.InstalledMods);
                if (violations.Count > 0)
                {
                    var prohibitedList = string.Join(", ", violations.Select(v => $"'{v.ModName}'"));
                    _logger?.Warn("ModAudit", $"[ANTI-CHEAT] Terdeteksi mod terlarang pada FarmerId={farmerId}: {prohibitedList}");

                    var rejectMsg = $"Akses Ditolak: Mod terlarang ditemukan di komputer Anda: {prohibitedList}. Hapus mod tersebut dari folder Mods/ untuk dapat bermain di server ini!";
                    var response = new ClientModAuditResponsePacket
                    {
                        FarmerId = farmerId,
                        IsAllowed = false,
                        RejectionReason = rejectMsg
                    };
                    server.sendMessage(farmerId, new OutgoingMessage(33, Game1.player.UniqueMultiplayerID, (object)response.Serialize()));
                    server.kick(farmerId);
                    return;
                }

                // 3. Simpan Telemetri Mod jika bersih
                var farmer = Game1.GetPlayer(farmerId);
                var username = farmer?.Name ?? "Unknown";
                var user = await db.GetUserAsync(username);
                if (user != null)
                {
                    await db.RecordClientModsAsync(user.Id, modAudit.HwidHash, "", modAudit.InstalledMods);
                }

                var cleanResponse = new ClientModAuditResponsePacket
                {
                    FarmerId = farmerId,
                    IsAllowed = true,
                    RejectionReason = string.Empty
                };
                server.sendMessage(farmerId, new OutgoingMessage(33, Game1.player.UniqueMultiplayerID, (object)cleanResponse.Serialize()));
                _logger?.Info("ModAudit", $"Audit mod SUKSES untuk FarmerId={farmerId}: {modAudit.InstalledMods.Count} mod diizinkan.");
            }
            catch (Exception ex)
            {
                _logger?.Error("ModAudit", $"Exception saat memproses audit mod untuk FarmerId={farmerId}", null, ex);
            }
        });
    }

    public static bool IsBridgeClient(long farmerId) => _connectedBridges.ContainsKey(farmerId);
}
