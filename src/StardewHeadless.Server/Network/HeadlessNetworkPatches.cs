using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using Lidgren.Network;
using StardewHeadless.Server.Security;
using StardewHeadless.Server.World;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Network;

namespace StardewHeadless.Server.Network;

public static class HeadlessNetworkPatches
{
    private static Harmony? _harmony;
    public static int ServerPort { get; set; } = 24642;

    public static void Apply(Harmony? harmony = null)
    {
        _harmony = harmony ?? new Harmony("com.stardewheadless.network");

        var processMsgMethod = AccessTools.Method(typeof(GameServer), nameof(GameServer.processIncomingMessage));
        if (processMsgMethod != null)
        {
            var prefix = AccessTools.Method(typeof(HeadlessNetworkPatches), nameof(ProcessIncomingMessage_Prefix));
            var finalizer = AccessTools.Method(typeof(HeadlessNetworkPatches), nameof(ProcessIncomingMessage_Finalizer));
            _harmony.Patch(processMsgMethod, prefix: new HarmonyMethod(prefix), finalizer: new HarmonyMethod(finalizer));
        }

        var rebroadcastMethod = AccessTools.Method(typeof(GameServer), "rebroadcastClientMessage");
        if (rebroadcastMethod != null)
        {
            _harmony.Patch(rebroadcastMethod, prefix: new HarmonyMethod(typeof(HeadlessNetworkPatches), nameof(RebroadcastClientMessage_Prefix)));
        }

        var lidgrenInitMethod = AccessTools.Method(typeof(LidgrenServer), nameof(LidgrenServer.initialize));
        if (lidgrenInitMethod != null)
        {
            _harmony.Patch(lidgrenInitMethod, prefix: new HarmonyMethod(typeof(HeadlessNetworkPatches), nameof(LidgrenServer_Initialize_Prefix)));
        }

        var isGameAvailableMethod = AccessTools.Method(typeof(GameServer), nameof(GameServer.isGameAvailable));
        if (isGameAvailableMethod != null)
        {
            _harmony.Patch(isGameAvailableMethod, prefix: new HarmonyMethod(typeof(HeadlessNetworkPatches), nameof(IsGameAvailable_Prefix)));
        }

        var checkFarmhandMethod = AccessTools.Method(typeof(GameServer), nameof(GameServer.checkFarmhandRequest));
        if (checkFarmhandMethod != null)
        {
            var prefix = AccessTools.Method(typeof(HeadlessNetworkPatches), nameof(CheckFarmhandRequest_Prefix));
            var postfix = AccessTools.Method(typeof(HeadlessNetworkPatches), nameof(CheckFarmhandRequest_Postfix));
            _harmony.Patch(checkFarmhandMethod, prefix: new HarmonyMethod(prefix), postfix: new HarmonyMethod(postfix));
        }

        var sendFarmhandsMethod = AccessTools.Method(typeof(GameServer), nameof(GameServer.sendAvailableFarmhands));
        if (sendFarmhandsMethod != null)
        {
            _harmony.Patch(
                sendFarmhandsMethod,
                prefix: new HarmonyMethod(typeof(HeadlessNetworkPatches), nameof(SendAvailableFarmhands_Prefix))
            );
        }

        var sendServerIntroMethod = AccessTools.Method(typeof(GameServer), nameof(GameServer.sendServerIntroduction));
        if (sendServerIntroMethod != null)
        {
            _harmony.Patch(
                sendServerIntroMethod,
                prefix: new HarmonyMethod(typeof(HeadlessNetworkPatches), nameof(SendServerIntroduction_Prefix))
            );
        }

        var canAssignMethod = AccessTools.Method(typeof(Cabin), "CanAssignTo");
        if (canAssignMethod != null)
        {
            _harmony.Patch(
                canAssignMethod,
                postfix: new HarmonyMethod(typeof(HeadlessNetworkPatches), nameof(Cabin_CanAssignTo_Postfix))
            );
        }

        var warpFarmerMethod = AccessTools.Method(
            typeof(GameServer),
            "warpFarmer",
            new[] { typeof(Farmer), typeof(short), typeof(short), typeof(string), typeof(bool) }
        );
        if (warpFarmerMethod != null)
        {
            _harmony.Patch(
                warpFarmerMethod,
                prefix: new HarmonyMethod(typeof(HeadlessNetworkPatches), nameof(WarpFarmer_Prefix))
            );
        }

        var playerDisconnectedMethod = AccessTools.Method(
            typeof(GameServer),
            nameof(GameServer.playerDisconnected),
            new[] { typeof(long) }
        );
        if (playerDisconnectedMethod != null)
        {
            _harmony.Patch(
                playerDisconnectedMethod,
                postfix: new HarmonyMethod(typeof(HeadlessNetworkPatches), nameof(PlayerDisconnected_Postfix))
            );
        }
    }

    public static void CheckFarmhandRequest_Prefix(NetFarmerRoot farmer)
    {
        if (farmer?.Value == null) return;
        long farmerId = farmer.Value.UniqueMultiplayerID;
        string clientName = string.IsNullOrWhiteSpace(farmer.Value.Name) ? "Farmer" : farmer.Value.Name;

        bool autoAuthed = LobbyGatekeeper.TryAutoAuthenticateExistingUser(farmerId, clientName, farmer.Value);
        if (!autoAuthed)
        {
            // Karakter baru: pastikan redirect ke lobi diatur sebelum sendLocation di checkFarmhandRequest
            LobbyGatekeeper.OnClientConnected(farmerId, clientName, isAuthenticated: false);
            LobbyCabinManager.ApplyLobbySpawnRedirect(farmer.Value);
        }
    }

    public static void CheckFarmhandRequest_Postfix(NetFarmerRoot farmer)
    {
        if (farmer?.Value != null)
        {
            long farmerId = farmer.Value.UniqueMultiplayerID;
            string clientName = string.IsNullOrWhiteSpace(farmer.Value.Name) ? "Farmer" : farmer.Value.Name;

            Task.Run(async () =>
            {
                try
                {
                    var authState = LobbyGatekeeper.GetAuthState(farmerId);
                    if (authState != null && authState.IsAuthenticated)
                    {
                        // Pemain lama yang sudah terdaftar & terautentikasi (reconnect)
                        await Task.Delay(1000);
                        Game1.Multiplayer?.sendChatMessage(
                            LocalizedContentManager.LanguageCode.en,
                            $"[SERVER] Selamat datang kembali di Lembah, {clientName}! (Profesi: {authState.LockedProfession.ToUpper()})",
                            farmerId
                        );
                        return;
                    }

                    // Pemain baru dalam karantina lobby
                    await Task.Delay(1500);
                    LobbyGatekeeper.SendQuarantineWelcomeNotice(farmerId, clientName);
                }
                catch
                {
                }
            });
        }
    }

    public static void SendAvailableFarmhands_Prefix()
    {
        if (Game1.netWorldState?.Value?.farmhandData == null) return;
        var lobbyCabin = LobbyCabinManager.GetLobbyCabin();
        if (lobbyCabin == null) return;

        foreach (var farmhandRef in Game1.netWorldState.Value.farmhandData.FieldDict.Values)
        {
            var f = farmhandRef.Value;
            if (f != null)
            {
                // Jangan override lokasi farmhand lama yang sudah dibuat / memiliki nama terdaftar
                bool isNewOrUnclaimed = !f.isCustomized.Value || string.IsNullOrWhiteSpace(f.Name);
                if (isNewOrUnclaimed && !LobbyGatekeeper.IsClientAuthenticated(f.UniqueMultiplayerID))
                {
                    LobbyCabinManager.ApplyLobbySpawnRedirect(f);
                }
            }
        }
    }

    public static bool SendServerIntroduction_Prefix(GameServer __instance, long peer)
    {
        if (!LobbyGatekeeper.IsClientAuthenticated(peer))
        {
            var lobbyCabin = LobbyCabinManager.GetLobbyCabin();
            if (lobbyCabin != null)
            {
                try
                {
                    var sendLocationMethod = AccessTools.Method(typeof(GameServer), "sendLocation", new[] { typeof(long), typeof(GameLocation), typeof(bool) });
                    sendLocationMethod?.Invoke(__instance, new object[] { peer, lobbyCabin, true });
                }
                catch
                {
                }
            }
        }
        return true;
    }

    public static void Cabin_CanAssignTo_Postfix(Cabin __instance, Farmer farmhand, ref bool __result)
    {
        if (LobbyCabinManager.IsLobbyCabin(__instance))
        {
            __result = false;
        }
    }

    /// <summary>
    /// PREFIX on GameServer.warpFarmer:
    /// Memperbaiki issue client mengirim warp request dengan isStructure=false untuk kabin interior
    /// (seperti saat menerima Multiplayer.passout warp dari Lobby ke kabin).
    /// Jika lookup normal mengembalikan null, coba cari sebagai struktur (isStructure: true).
    /// </summary>
    public static bool WarpFarmer_Prefix(
        GameServer __instance,
        Farmer farmer,
        short x,
        short y,
        string name,
        bool isStructure
    )
    {
        if (isStructure)
        {
            return true;
        }

        var location = Game1.getLocationFromName(name, isStructure: false);
        if (location != null)
        {
            return true;
        }

        location = Game1.getLocationFromName(name, isStructure: true);
        if (location == null)
        {
            return true;
        }

        if (Game1.IsMasterGame)
        {
            location.hostSetup();
        }

        farmer.currentLocation = location;
        int spriteH = farmer.Sprite != null ? farmer.Sprite.getHeight() : 32;
        farmer.Position = new Microsoft.Xna.Framework.Vector2(
            x * 64f,
            y * 64f - (spriteH - 32) + 16f
        );

        try
        {
            var sendLocationMethod = AccessTools.Method(
                typeof(GameServer),
                "sendLocation",
                new[] { typeof(long), typeof(GameLocation), typeof(bool) }
            );
            sendLocationMethod?.Invoke(__instance, new object[] { farmer.UniqueMultiplayerID, location, false });
        }
        catch
        {
        }

        return false;
    }

    public static bool IsAuthChatCommand(string chatText)
    {
        if (string.IsNullOrWhiteSpace(chatText)) return false;
        var t = chatText.TrimStart();
        return t.StartsWith("!auth", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("/auth", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("!login", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("/login", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("!register", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("/register", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("!profesi", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("/profesi", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("!profession", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("/profession", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("!help", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("/help", StringComparison.OrdinalIgnoreCase);
    }

    public static void LidgrenServer_ReceiveMessages_Prefix(LidgrenServer __instance)
    {
        if (Game1.options != null)
        {
            Game1.options.ipConnectionsEnabled = true;
            Game1.options.enableServer = true;
            Game1.options.serverPrivacy = ServerPrivacy.Public;
        }
        Game1.multiplayerMode = 2;
    }

    public static void LidgrenServer_SendVersionInfo_Prefix(NetIncomingMessage message)
    {
        Console.WriteLine($"[LidgrenServer] Menerima DiscoveryRequest dan mengirim VersionInfo (v{Multiplayer.protocolVersion}) ke {message.SenderEndPoint}");
    }

    public static void LidgrenServer_StatusChanged_Prefix(NetIncomingMessage message)
    {
        var status = (NetConnectionStatus)message.PeekByte();
        Console.WriteLine($"[LidgrenServer] StatusChanged dari {message.SenderConnection?.RemoteEndPoint}: {status}");
    }

    public static bool IsGameAvailable_Prefix(ref bool __result)
    {
        // Dedicated headless server tidak memiliki pemain lokal/layar GUI (0-GPU),
        // selalu siap menerima koneksi pemain tanpa terhalang cutscene/intro host.
        __result = true;
        return false;
    }

    public static bool LidgrenServer_Initialize_Prefix(LidgrenServer __instance)
    {
        var netPeerConfiguration = new NetPeerConfiguration("StardewValley")
        {
            Port = ServerPort,
            ConnectionTimeout = 30f,
            PingInterval = 5f,
            MaximumConnections = (Game1.Multiplayer?.playerLimit ?? 8) * 2,
            MaximumTransmissionUnit = 1200
        };
        netPeerConfiguration.EnableMessageType(NetIncomingMessageType.DiscoveryRequest);
        netPeerConfiguration.EnableMessageType(NetIncomingMessageType.ConnectionApproval);

        __instance.server = new NetServer(netPeerConfiguration);
        __instance.server.Start();
        return false; // Skip original hardcoded 24642 init
    }

    public static bool ProcessIncomingMessage_Prefix(GameServer __instance, IncomingMessage message)
    {
        // 1. Intersepsi Packet 33 (dedicatedServerSync) untuk StardewBridge sub-opcodes (0x80..0xFF)
        if (message.MessageType == 33 && message.Reader != null)
        {
            long origPos = message.Reader.BaseStream.Position;
            if (message.Reader.BaseStream.Length > origPos)
            {
                byte subOpCode = message.Reader.ReadByte();

                if (subOpCode >= 0x80)
                {
                    // subOpCode telah dibaca (consumed). Posisi reader sekarang tepat di awal data payload.
                    BridgeProtocolRouter.HandleServerMessage(__instance, message, subOpCode);
                    // Kembalikan false agar tidak diteruskan ke DedicatedServer vanilla dan tidak di-rebroadcast
                    return false;
                }

                // Kembalikan posisi reader untuk penanganan vanilla jika ini adalah paket vanilla (< 0x80)
                message.Reader.BaseStream.Position = origPos;
            }
        }

        // 2. Intersepsi Packet 10 (chatMessage) untuk perintah Auth & Lobby in-game
        if (message.MessageType == 10 && message.Reader != null)
        {
            long origPos = message.Reader.BaseStream.Position;
            try
            {
                long recipientID = message.Reader.ReadInt64();
                message.Reader.ReadInt16(); // language code
                string chatText = message.Reader.ReadString()?.Trim() ?? string.Empty;
                message.Reader.BaseStream.Position = origPos;

                if (IsAuthChatCommand(chatText))
                {
                    // Amankan: JANGAN rebroadcast perintah auth ke pemain lain (lindungi password/OTP)
                    Farmer? senderFarmer = message.SourceFarmer;
                    long farmerId = message.FarmerID;
                    Task.Run(async () =>
                    {
                        await LobbyGatekeeper.ProcessChatAuthAsync(farmerId, chatText, senderFarmer);
                    });
                    return false;
                }
            }
            catch
            {
                message.Reader.BaseStream.Position = origPos;
            }
        }

        // 3. ENFORCE QUARANTINE: Jika pemain belum terautentikasi (berada dalam karantina kabin)
        if (message.FarmerID != Game1.player.UniqueMultiplayerID && !LobbyGatekeeper.IsClientAuthenticated(message.FarmerID))
        {
            // Intersepsi upaya keluar dari kabin menuju peternakan (WarpFarmer = 5)
            if (message.MessageType == 5)
            {
                LobbyGatekeeper.SendQuarantineReminder(message.FarmerID);
                return false; // Batalkan perpindahan peta keluar Farm, tahan di kabin
            }

            // Izinkan paket esensial untuk handshake, kustomisasi karakter, orientasi, pergerakan dalam kabin, sync, dan chat:
            // 0: farmerDelta (orientasi dan pergerakan dalam kabin)
            // 2: playerIntroduction
            // 10: chatMessage (chat reguler)
            // 17: readyCheck
            // 19: disconnecting
            // 22: userName
            // 28: requestPlayerUpdate
            // 33: dedicatedServerSync
            if (message.MessageType == 0 ||
                message.MessageType == 2 ||
                message.MessageType == 10 ||
                message.MessageType == 17 ||
                message.MessageType == 19 ||
                message.MessageType == 22 ||
                message.MessageType == 28 ||
                message.MessageType == 33)
            {
                return true;
            }

            // Untuk aksi di luar paket esensial, tolak tanpa membekukan tombol kontrol (tanpa freezePause)
            return false;
        }

        return true;
    }

    public static bool RebroadcastClientMessage_Prefix(IncomingMessage message)
    {
        // 1. Pastikan tidak ada paket bridge internal (>= 0x80) yang bocor ke klien lain
        if (message.MessageType == 33 && message.Reader != null)
        {
            long origPos = message.Reader.BaseStream.Position;
            if (message.Reader.BaseStream.Length > origPos)
            {
                byte subOpCode = message.Reader.ReadByte();
                message.Reader.BaseStream.Position = origPos;

                if (subOpCode >= 0x80)
                {
                    return false;
                }
            }
        }

        // 2. Cegah kebocoran perintah auth in-game (/login, !auth, dll) ke layar pemain lain
        if (message.MessageType == 10 && message.Reader != null)
        {
            long origPos = message.Reader.BaseStream.Position;
            try
            {
                long recipientID = message.Reader.ReadInt64();
                message.Reader.ReadInt16(); // language code
                string chatText = message.Reader.ReadString()?.Trim() ?? string.Empty;
                message.Reader.BaseStream.Position = origPos;

                if (IsAuthChatCommand(chatText))
                {
                    return false;
                }
            }
            catch
            {
                message.Reader.BaseStream.Position = origPos;
            }
        }

        // 3. Jangan rebroadcast paket dari client unauthenticated (kecuali handshake/appearance)
        if (message.FarmerID != Game1.player.UniqueMultiplayerID && !LobbyGatekeeper.IsClientAuthenticated(message.FarmerID))
        {
            if (message.MessageType != 0 && message.MessageType != 2 && message.MessageType != 10)
            {
                return false;
            }
        }

        return true;
    }

    public static void PlayerDisconnected_Postfix(long disconnectee)
    {
        Console.WriteLine($"[GameServer] Pemain FarmerId={disconnectee} terputus dari game server.");
        LobbyGatekeeper.OnClientDisconnected(disconnectee);

        // Langsung perbarui progres waktu server saat pemain keluar
        int onlineCount = Game1.server?.connectionsCount ?? 0;
        int authCount = onlineCount == 0 ? 0 : LobbyGatekeeper.GetAuthenticatedPlayerCount();
        if (onlineCount == 0)
        {
            LobbyGatekeeper.ClearAll();
        }
        World.DayTransitionManager.UpdateServerTimeProgression(onlineCount, authCount, Game1.netWorldState?.Value);
    }

    public static Exception? ProcessIncomingMessage_Finalizer(Exception? __exception)
    {
        if (__exception != null)
        {
            if (__exception is InvalidOperationException)
            {
                // NetFields bit array mismatch during polymorphic delta packet (e.g. initial starter tool sync),
                // suppress to avoid crashing or throwing unhandled exception into 60 Hz tick loop
                return null;
            }
            return __exception;
        }
        return null;
    }
}
