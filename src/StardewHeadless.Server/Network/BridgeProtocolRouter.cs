using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
    private static readonly ConcurrentDictionary<long, string> _connectedBridges = new();
    private static readonly ConcurrentDictionary<long, CancellationTokenSource> _auditDeadlineCts = new();
    public static Action<long>? OnAuditDeadlineKickOverride { get; set; }

    public static void StartAuditDeadline(GameServer? server, long farmerId, TimeSpan? timeout = null)
    {
        CancelAuditDeadline(farmerId);
        var cts = new CancellationTokenSource();
        _auditDeadlineCts[farmerId] = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(timeout ?? TimeSpan.FromSeconds(12), cts.Token);
                if (!cts.Token.IsCancellationRequested)
                {
                    _logger?.Warn("ModAudit", $"[SECURITY DEADLINE] FarmerId={farmerId} tidak mengirim ClientModAudit (0x98) dalam batas waktu 12 detik. Menendang koneksi!");
                    if (OnAuditDeadlineKickOverride != null)
                    {
                        OnAuditDeadlineKickOverride(farmerId);
                    }
                    else
                    {
                        server?.kick(farmerId);
                    }
                }
            }
            catch (TaskCanceledException)
            {
                // Deadline dipenuhi
            }
            finally
            {
                _auditDeadlineCts.TryRemove(farmerId, out _);
            }
        });
    }

    public static void CancelAuditDeadline(long farmerId)
    {
        if (_auditDeadlineCts.TryRemove(farmerId, out var cts))
        {
            try
            {
                cts.Cancel();
                cts.Dispose();
            }
            catch { }
        }
    }

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

                case SkullElevatorUseRequestPacket elevatorReq:
                    HandleSkullElevatorRequest(server, message.FarmerID, elevatorReq);
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

        StartAuditDeadline(server, farmerId);

        bool isAuthenticated = Security.LobbyGatekeeper.IsClientAuthenticated(farmerId)
            || Security.LobbyGatekeeper.TryAutoAuthenticateExistingUser(farmerId, hello.ClientName, targetFarmer, hello.SessionToken);

        if (isAuthenticated)
        {
            var authState = Security.LobbyGatekeeper.GetAuthState(farmerId);
            var success = new AuthSuccessPacket
            {
                SessionToken = hello.SessionToken ?? Guid.NewGuid().ToString("N"),
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

                if (responsePacket is AuthSuccessPacket success && targetFarmer != null)
                {
                    // Kirim Starter Kit Grant 0x9A HANYA jika belum pernah diklaim sebelumnya
                    var db = ServerHost.Database;
                    var user = db != null ? await db.GetUserByLinkedFarmerAsync(farmerId, targetFarmer.Name) : null;
                    var userProf = (db != null && user != null) ? await db.GetProfessionAsync(user.Id) : null;
                    if (userProf == null || !userProf.StarterKitClaimed)
                    {
                        var grantPacket = World.StarterKitDelivery.CreateGrantPacket(farmerId, success.LockedProfession, Game1.currentSeason, success.SessionToken);
                        byte[] grantPayload = grantPacket.Serialize();
                        server.sendMessage(farmerId, new OutgoingMessage(33, Game1.player.UniqueMultiplayerID, (object)grantPayload));
                        if (db != null && user != null)
                        {
                            await db.ClaimStarterKitAsync(user.Id);
                        }
                    }

                    // Sinkronisasi Skill Level & kirim 0xA0 ProfessionBuffSyncPacket
                    _ = Professions.ProfessionLevelSyncService.SyncFarmerSkillLevelAsync(targetFarmer, server, ServerHost.Database);
                }
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

    private static void SendProfessionResponse(GameServer server, long farmerId, string prof, bool success, string message, int bankBalance = 0)
    {
        var resp = new ProfessionSyncResponsePacket
        {
            FarmerId = farmerId,
            ConfirmedProfession = prof,
            Success = success,
            Message = message,
            VillageBankBalance = bankBalance
        };
        byte[] payload = resp.Serialize();
        server.sendMessage(farmerId, new OutgoingMessage(33, Game1.player.UniqueMultiplayerID, (object)payload));
    }

    private static void HandleProfessionSelect(GameServer server, long farmerId, ProfessionSelectRequestPacket req)
    {
        Task.Run(async () =>
        {
            try
            {
                var db = ServerHost.Database;
                var farmer = Game1.GetPlayer(farmerId);
                string name = farmer?.Name ?? $"Farmer_{farmerId}";
                string prof = req.ProfessionName;

                if (db == null)
                {
                    SendProfessionResponse(server, farmerId, prof, false, "Database offline.", _villageBankBalance);
                    return;
                }

                var user = await db.GetUserByLinkedFarmerAsync(farmerId, name) ?? await db.GetUserAsync(name);
                if (user == null)
                {
                    SendProfessionResponse(server, farmerId, prof, false, "User belum terdaftar di sistem.", _villageBankBalance);
                    return;
                }

                var existingProf = await db.GetProfessionAsync(user.Id);
                if (existingProf == null)
                {
                    await db.LockProfessionAsync(user.Id, prof);
                    if (farmer != null)
                    {
                        farmer.modData["svheadless/profession"] = prof;
                    }
                    int balance = await db.GetTreasuryBalanceAsync();
                    SendProfessionResponse(server, farmerId, prof, true, $"Selamat! Anda resmi memegang profesi {prof}.", balance);
                    return;
                }

                if (string.Equals(existingProf.ProfessionType, prof, StringComparison.OrdinalIgnoreCase))
                {
                    int balance = await db.GetTreasuryBalanceAsync();
                    SendProfessionResponse(server, farmerId, prof, true, $"Profesi Anda saat ini sudah {prof}.", balance);
                    return;
                }

                int currentGold = farmer?.Money ?? 0;
                var respec = await db.ChangeProfessionAsync(user.Id, prof, isAdminOverride: false, currentWalletGold: currentGold);
                if (!respec.success)
                {
                    int balance = await db.GetTreasuryBalanceAsync();
                    SendProfessionResponse(server, farmerId, existingProf.ProfessionType, false, respec.message, balance);
                    return;
                }

                if (farmer != null)
                {
                    farmer.Money = Math.Max(0, farmer.Money - respec.feeDeducted);
                    farmer.modData["svheadless/profession"] = prof;
                }

                int newTreasuryBalance = await db.GetTreasuryBalanceAsync();
                _villageBankBalance = newTreasuryBalance;
                SendProfessionResponse(server, farmerId, prof, true, respec.message, newTreasuryBalance);

                if (farmer != null)
                {
                    _ = Professions.ProfessionLevelSyncService.SyncFarmerSkillLevelAsync(farmer, server, db);
                }

                var update = new VillageTreasuryUpdatePacket
                {
                    NewBalance = newTreasuryBalance,
                    LastContributorName = name,
                    LastContributionAmount = respec.feeDeducted
                };
                byte[] updatePayload = update.Serialize();
                foreach (long peerId in Game1.otherFarmers.Keys)
                {
                    server.sendMessage(peerId, new OutgoingMessage(33, Game1.player.UniqueMultiplayerID, (object)updatePayload));
                }
            }
            catch (Exception ex)
            {
                _logger?.Error("Economy", $"Exception saat HandleProfessionSelect farmer {farmerId}: {ex.Message}");
            }
        });
    }

    private static void HandleTreasuryDeposit(GameServer server, long farmerId, TreasuryDepositRequestPacket req)
    {
        var farmer = Game1.GetPlayer(farmerId);
        string name = farmer?.Name ?? $"Farmer_{farmerId}";

        if (farmer == null || req.Amount <= 0) return;

        // Validasi dan potong saldo dompet di main thread secara otoritatif
        if (farmer.Money < req.Amount)
        {
            _logger?.Warn("Economy", $"[KAS DESA] Donasi ditolak: Dompet '{name}' ({farmer.Money}g) tidak mencukupi untuk mendonasikan {req.Amount}g.");
            return;
        }

        farmer.Money -= req.Amount;

        Task.Run(async () =>
        {
            try
            {
                var db = ServerHost.Database;
                long? userId = null;
                if (db != null)
                {
                    var user = await db.GetUserByLinkedFarmerAsync(farmerId, name) ?? await db.GetUserAsync(name);
                    userId = user?.Id;
                    _villageBankBalance = await db.RecordTreasuryTransactionAsync(userId, "Deposit", req.Amount, $"Donasi sukarela dari farmer '{name}'");
                }
                else
                {
                    _villageBankBalance += req.Amount;
                }

                _logger?.Info("Economy", $"[KAS DESA] Farmer '{name}' mendonasikan {req.Amount}g ke Kas Desa. Total Kas Desa saat ini: {_villageBankBalance}g");

                var update = new VillageTreasuryUpdatePacket
                {
                    NewBalance = _villageBankBalance,
                    LastContributorName = name,
                    LastContributionAmount = req.Amount
                };

                byte[] payload = update.Serialize();
                if (Game1.otherFarmers != null)
                {
                    foreach (long peerId in Game1.otherFarmers.Keys)
                    {
                        server.sendMessage(peerId, new OutgoingMessage(33, Game1.player.UniqueMultiplayerID, (object)payload));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.Error("Economy", $"Gagal memproses donasi farmer '{name}': {ex.Message}");
            }
        });
    }

    private static void HandleClientModAudit(GameServer server, long farmerId, ClientModAuditPacket modAudit)
    {
        CancelAuditDeadline(farmerId);
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

                // 2. Evaluasi Kebijakan Mod (Strict Whitelist vs Permissive AllowAllMods)
                bool allowAllMods = ServerHost.Config.AllowAllMods;
                var auditResult = await db.CheckModAuditAsync(modAudit.InstalledMods, allowAllMods);

                if (!auditResult.IsAllowed)
                {
                    _logger?.Warn("ModAudit", $"[ANTI-CHEAT] Audit mod DITOLAK pada FarmerId={farmerId}: {auditResult.WarningOrRejectionMessage}");
                    var response = new ClientModAuditResponsePacket
                    {
                        FarmerId = farmerId,
                        IsAllowed = false,
                        RejectionReason = auditResult.WarningOrRejectionMessage
                    };
                    server.sendMessage(farmerId, new OutgoingMessage(33, Game1.player.UniqueMultiplayerID, (object)response.Serialize()));
                    server.kick(farmerId);
                    return;
                }

                // 3. Simpan Telemetri Mod jika lolos audit
                var farmer = Game1.GetPlayer(farmerId);
                var username = farmer?.Name ?? "Unknown";
                var user = await db.GetUserAsync(username);
                if (user != null)
                {
                    await db.RecordClientModsAsync(user.Id, modAudit.HwidHash, "", modAudit.InstalledMods);
                }

                // Jika ada mod tidak resmi dalam mode Permissive, kirim peringatan risiko bug ke chat game
                if (!string.IsNullOrWhiteSpace(auditResult.WarningOrRejectionMessage))
                {
                    _logger?.Warn("ModAudit", $"[MOD PERMISSIF] FarmerId={farmerId} ('{username}') diizinkan dengan mod tidak resmi: {auditResult.WarningOrRejectionMessage}");
                    Game1.Multiplayer?.sendChatMessage(
                        LocalizedContentManager.LanguageCode.en,
                        auditResult.WarningOrRejectionMessage,
                        farmerId
                    );
                }
                else
                {
                    _logger?.Info("ModAudit", $"Audit mod SUKSES untuk FarmerId={farmerId}: {modAudit.InstalledMods.Count} mod resmi diizinkan.");
                }

                var cleanResponse = new ClientModAuditResponsePacket
                {
                    FarmerId = farmerId,
                    IsAllowed = true,
                    RejectionReason = auditResult.WarningOrRejectionMessage
                };
                server.sendMessage(farmerId, new OutgoingMessage(33, Game1.player.UniqueMultiplayerID, (object)cleanResponse.Serialize()));
            }
            catch (Exception ex)
            {
                _logger?.Error("ModAudit", $"Exception saat memproses audit mod untuk FarmerId={farmerId}", null, ex);
            }
        });
    }

    public static void HandleSkullElevatorRequest(GameServer server, long farmerId, SkullElevatorUseRequestPacket req)
    {
        _logger?.Info("SkullElevator", $"[ELEVATOR] Menerima permintaan SkullElevator dari FarmerId={farmerId} ke Lantai={req.TargetFloor}");

        var farmer = Game1.GetPlayer(farmerId);
        if (farmer == null)
        {
            _logger?.Warn("SkullElevator", $"FarmerId={farmerId} tidak ditemukan untuk SkullElevator.");
            return;
        }

        // 1. Validasi lantai: harus kelipatan 5 dan > 0
        if (req.TargetFloor <= 0 || req.TargetFloor % 5 != 0)
        {
            SendSkullElevatorResponse(server, farmerId, req.TargetFloor, 0, false, "Lantai harus kelipatan 5 (contoh: 5, 10, 15...).");
            return;
        }

        // 2. Validasi progress pemain: tidak boleh melebihi lantai terdalam yang pernah dicapai
        int maxSkullFloor = Math.Max(0, farmer.deepestMineLevel - 120);
        if (req.TargetFloor > maxSkullFloor)
        {
            SendSkullElevatorResponse(server, farmerId, req.TargetFloor, 0, false, $"Lantai {req.TargetFloor} belum terbuka! Rekor terdalam Anda di Skull Cavern adalah Lantai {maxSkullFloor}.");
            return;
        }

        // 3. Kalkulasi Biaya (250g per 10 lantai, min 250g)
        int fee = Math.Max(250, (req.TargetFloor / 10) * 250);

        // 4. Periksa Profesi Miner & Level
        string prof = string.Empty;
        if (farmer.modData != null && farmer.modData.TryGetValue("svheadless/profession", out var pVal))
        {
            prof = pVal;
        }

        if (string.Equals(prof, "miner", StringComparison.OrdinalIgnoreCase))
        {
            int miningLevel = farmer.getEffectiveSkillLevel(Farmer.miningSkill);
            if (miningLevel >= 10)
            {
                fee = 0; // 100% gratis untuk Miner Veteran (Level 10)
            }
            else
            {
                fee /= 2; // Diskon 50% untuk Profesi Miner
            }
        }

        // 5. Cek Saldo Pemain
        if (farmer.Money < fee)
        {
            SendSkullElevatorResponse(server, farmerId, req.TargetFloor, 0, false, $"Saldo tidak mencukupi! Biaya lift: {fee}g, saldo Anda: {farmer.Money}g.");
            return;
        }

        // 6. Potong saldo & Setor ke Kas Desa
        farmer.Money -= fee;

        Task.Run(async () =>
        {
            try
            {
                if (fee > 0)
                {
                    var db = ServerHost.Database;
                    long? userId = null;
                    if (db != null)
                    {
                        var user = await db.GetUserByLinkedFarmerAsync(farmerId, farmer.Name) ?? await db.GetUserAsync(farmer.Name);
                        userId = user?.Id;
                        _villageBankBalance = await db.RecordTreasuryTransactionAsync(userId, "SkullElevatorFee", fee, $"Biaya lift Skull Cavern Lantai {req.TargetFloor} oleh '{farmer.Name}'");
                    }
                    else
                    {
                        _villageBankBalance += fee;
                    }

                    var update = new VillageTreasuryUpdatePacket
                    {
                        NewBalance = _villageBankBalance,
                        LastContributorName = farmer.Name,
                        LastContributionAmount = fee
                    };
                    byte[] updatePayload = update.Serialize();
                    foreach (long peerId in Game1.otherFarmers.Keys)
                    {
                        server.sendMessage(peerId, new OutgoingMessage(33, Game1.player.UniqueMultiplayerID, (object)updatePayload));
                    }
                }

                SendSkullElevatorResponse(server, farmerId, req.TargetFloor, fee, true, $"Lift aktif! Menuju Lantai {req.TargetFloor} (Biaya: {fee}g).");
            }
            catch (Exception ex)
            {
                _logger?.Error("SkullElevator", $"Exception saat proses fee SkullElevator farmer {farmerId}: {ex.Message}");
                SendSkullElevatorResponse(server, farmerId, req.TargetFloor, fee, true, $"Lift aktif! Menuju Lantai {req.TargetFloor}.");
            }
        });
    }

    public static void SendSkullElevatorResponse(GameServer server, long farmerId, int targetFloor, int fee, bool success, string message)
    {
        var resp = new SkullElevatorUseResponsePacket
        {
            FarmerId = farmerId,
            TargetFloor = targetFloor,
            FeeDeducted = fee,
            Success = success,
            Message = message
        };
        byte[] payload = resp.Serialize();
        server.sendMessage(farmerId, new OutgoingMessage(33, Game1.player.UniqueMultiplayerID, (object)payload));
    }

    public static bool IsBridgeClient(long farmerId) => _connectedBridges.ContainsKey(farmerId);
}

