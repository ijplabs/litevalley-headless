using System.Diagnostics;
using System.IO;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewHeadless.Protocol.Logging;
using StardewHeadless.Protocol.Models;
using StardewHeadless.Server.Config;
using StardewHeadless.Server.Network;
using StardewHeadless.Server.Services.Redis;
using StardewHeadless.Server.World;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.Locations;
using StardewValley.Network;
using StardewValley.SDKs;

namespace StardewHeadless.Server;

public static class ServerHost
{
    private static StructuredLogger? _logger;
    private static ServerConfig _config = new();
    private static IRedisService? _redisService;
    private static Database.IDatabaseService? _databaseService;
    private static string _saveName = string.Empty;
    private static long _targetTicks;
    private static int _targetPort = 24642;
    private static string _logsDir = string.Empty;
    private static readonly CancellationTokenSource _cts = new();
    private static readonly Action? _threadingRun = AccessTools.Method("Microsoft.Xna.Framework.Threading:Run")?.CreateDelegate<Action>();

    public static IRedisService? Redis => _redisService;
    public static Database.IDatabaseService? Database => _databaseService;
    public static ServerConfig Config => _config;
    public static string LogsDir => _logsDir;

    public static int Run(StructuredLogger logger, ServerConfig config, string[] args)
    {
        _logger = logger;
        _config = config;
        _targetPort = config.Port;
        _logsDir = config.LogDir;

        Console.CancelKeyPress += (sender, eventArgs) =>
        {
            _logger.Warn("Server", "Ctrl+C terdeteksi, memulai shutdown sequence...");
            eventArgs.Cancel = true;
            _cts.Cancel();
        };

        // 1. Inisialisasi Database SQLite
        try
        {
            var dbPath = Path.Combine(config.ConfigDir, "stardew_server.db");
            _databaseService = new Database.DatabaseService(dbPath);
            _databaseService.InitializeAsync().GetAwaiter().GetResult();
            _logger.Info("Database", $"Database SQLite aktif di '{dbPath}' (WAL Mode)");
        }
        catch (Exception ex)
        {
            _logger.Error("Database", "Gagal inisialisasi SQLite database", null, ex);
        }

        // 2. Inisialisasi Resilient Redis Service
        _redisService = new RedisService(config.RedisHost, config.RedisPort, config.RedisPassword);

        ParseCommandLine(args);

        // Setup Network & Bridge Protocol Router
        BridgeProtocolRouter.Initialize(_logger);
        HeadlessNetworkPatches.ServerPort = _targetPort;
        HeadlessNetworkPatches.Apply();

        // Setup World Simulation Service & Patches (ChestLock, CropProtection, AnimalBonus)
        WorldSimulationService.Initialize(_logger, new Harmony("com.stardewheadless.worldsim"));
        LobbyCabinManager.Initialize(_logger);

        // Inisialisasi Embedded Kestrel Web Server & REST API (Milestone 4.5)
        _ = Web.KestrelWebServer.StartAsync(_config, _databaseService, _logger, _cts.Token);

        // 2. Matikan Steam SDK, beralih ke NullSDKHelper murni
        AccessTools.Field(typeof(StardewValley.Program), "_sdk")?.SetValue(null, new NullSDKHelper());
        StardewValley.Program.GameTesterMode = true;

        // 3. Set working directory ke instalasi game agar Content/ dan maps dimuat dengan benar
        var gameInstallDir = _config.GameDir;
        if (Directory.Exists(gameInstallDir))
        {
            Directory.SetCurrentDirectory(gameInstallDir);
            _logger.Info("Boot", $"Working directory diset ke game dir: {gameInstallDir}");
        }
        else
        {
            _logger.Warn("Boot", $"Game directory '{gameInstallDir}' tidak ditemukan!");
        }

        _logger.Info("Boot", $"Save yang akan dimuat: '{_saveName}'");
        if (_targetTicks > 0)
        {
            _logger.Info("Boot", $"Mode Benchmark/Test aktif: Server akan berjalan selama {_targetTicks} ticks.");
        }

        // 4. Jalankan GameRunner
        using var gameRunner = new GameRunner();
        GameRunner.instance = gameRunner;

        _logger.Info("Boot", "Menginisialisasi GameRunner...");
        gameRunner.Run(); // Akan memanggil Platform.RunLoop yang telah di-intercept ke RunHeadlessLoop()

        _logger.Info("Shutdown", "Headless Server daemon selesai dieksekusi.");
        return 0;
    }

    private static void ParseCommandLine(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].Equals("--save", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                _saveName = args[++i];
            }
            else if (args[i].Equals("--ticks", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                long.TryParse(args[++i], out _targetTicks);
            }
            else if (args[i].Equals("--port", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                int.TryParse(args[++i], out _targetPort);
            }
        }

        if (string.IsNullOrWhiteSpace(_saveName))
        {
            _saveName = AutoDetectLatestSave();
        }
    }

    private static string AutoDetectLatestSave()
    {
        var candidateDirs = new List<string>();
        if (!string.IsNullOrWhiteSpace(_config.SaveDir) && Directory.Exists(_config.SaveDir))
        {
            candidateDirs.Add(_config.SaveDir);
        }
        if (Directory.Exists("/data/config/Saves"))
        {
            candidateDirs.Add("/data/config/Saves");
        }
        var appDataFallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StardewValley", "Saves");
        if (Directory.Exists(appDataFallback))
        {
            candidateDirs.Add(appDataFallback);
        }

        foreach (var dir in candidateDirs)
        {
            var dirInfo = new DirectoryInfo(dir);
            var latestDir = dirInfo.GetDirectories()
                .Where(d => !d.Name.Equals("archive", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(d => d.LastWriteTimeUtc)
                .FirstOrDefault();

            if (latestDir != null)
            {
                _logger?.Info("SaveLoader", $"Auto-detected latest save folder: '{latestDir.Name}' di '{dir}'");
                return latestDir.Name;
            }
        }

        return string.Empty;
    }

    public static void RunHeadlessLoop()
    {
        _logger?.Info("ServerLoop", "Memasuki pure headless tick loop (60 TPS)...");

        // 1. Muat save file atau generate dunia baru (Fresh World)
        if (_config.AutoCreateFreshWorld || string.IsNullOrWhiteSpace(_saveName))
        {
            _logger?.Info("WorldGen", "Membuat dunia baru (Fresh World) karena opsi create-new aktif atau tidak ditemukan save yang valid...");
            try
            {
                _saveName = HeadlessWorldGenerator.CreateFreshWorld(_config, _logger);
            }
            catch (Exception ex)
            {
                _logger?.Error("WorldGen", "Gagal men-generate fresh world secara otomatis", null, ex);
            }
        }

        else if (!string.IsNullOrWhiteSpace(_saveName))
        {
            LoadSaveGame(_saveName);
        }
        else
        {
            _logger?.Warn("SaveLoader", "Tidak ada file save yang dimuat. Menjalankan server dalam mode empty title state.");
        }

        // 2. Setup server multiplayer flags & listener
        if (Game1.game1 != null)
        {
            if (Game1.currentLocation == null)
            {
                Game1.currentLocation = Game1.RequireLocation("FarmHouse") ?? Game1.getFarm() ?? Game1.locations?.FirstOrDefault();
            }
            if (Game1.player != null && Game1.player.currentLocation == null)
            {
                Game1.player.currentLocation = Game1.currentLocation;
            }
            if (Game1.dayOfMonth == 0)
            {
                Game1.dayOfMonth = 1;
            }
            if (Game1.timeOfDay == 0)
            {
                Game1.timeOfDay = 600;
            }
            Game1.netWorldState?.Value.UpdateFromGame1();

            Game1.setGameMode(3); // In-Game Playing Mode (WAJIB agar GameServer memproses paket jaringan!)
            Game1.multiplayerMode = 2; // Server mode
            Game1.options.enableServer = true;
            Game1.options.ipConnectionsEnabled = true;
            Game1.options.serverPrivacy = ServerPrivacy.Public;
            Game1.options.enableFarmhandCreation = true;
            Game1.Multiplayer.playerLimit = 8;

            if (Game1.player?.team != null)
            {
                var dedHostField = AccessTools.Field(typeof(FarmerTeam), "hasDedicatedHost")?.GetValue(Game1.player.team) as Netcode.NetBool;
                if (dedHostField != null)
                {
                    dedHostField.Value = true;
                }
            }

            var currentFarm = Game1.getFarm();
            if (currentFarm != null)
            {
                LobbyCabinManager.EnsureLobbyCabin(currentFarm);
            }
            EnsureMultiplayerCabins(8);
            WorldSimulationService.UpdateWorldObjects(Game1.getFarm());

            if (Game1.server == null)
            {
                _logger?.Info("NetworkServer", $"Memulai GameServer & Lidgren UDP listener pada port {_targetPort}...");
                Game1.Multiplayer.StartServer();
            }

            _logger?.Info("NetworkServer", $"GameServer aktif pada port {_targetPort} UDP! Status koneksi: {Game1.server?.connectionsCount ?? 0}");
        }

        // 3. Main 60 Hz tick loop
        var targetInterval = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 60); // 16.666 ms
        var stopwatch = Stopwatch.StartNew();
        var lastSnapshotTime = stopwatch.Elapsed;
        var lastTickTime = stopwatch.Elapsed;
        long totalTicks = 0;
        long windowTicks = 0;

        _logger?.Info("ServerLoop", "Headless Game Loop aktif pada 60 Hz.");

        while (!_cts.IsCancellationRequested && (_targetTicks == 0 || totalTicks < _targetTicks))
        {
            var nextTickTarget = lastTickTime + targetInterval;

            try
            {
                if (Game1.gameMode != 3)
                {
                    Game1.setGameMode(3);
                }

                if (Game1.currentLocation == null && Game1.locations != null && Game1.locations.Count > 0)
                {
                    Game1.currentLocation = Game1.getLocationFromName("FarmHouse") ?? Game1.getFarm();
                }

                if (Game1.player != null && Game1.player.currentLocation == null)
                {
                    Game1.player.currentLocation = Game1.currentLocation;
                }

                GameRunner.instance.Tick();
                _threadingRun?.Invoke();

                if (Game1.server != null)
                {
                    Game1.server.receiveMessages();
                }

                if (Game1.Multiplayer != null)
                {
                    Game1.Multiplayer.UpdateLate();
                }
            }
            catch (Exception ex)
            {
                _logger?.Error("ServerLoop", "Exception di dalam GameRunner.Tick()", null, ex);
            }

            totalTicks++;
            windowTicks++;
            lastTickTime = stopwatch.Elapsed;

            // Setiap 60 ticks (~1 detik), hitung TPS & tulis snapshot telemetri
            if ((stopwatch.Elapsed - lastSnapshotTime).TotalSeconds >= 1.0)
            {
                var elapsedSec = (stopwatch.Elapsed - lastSnapshotTime).TotalSeconds;
                var currentTps = windowTicks / elapsedSec;
                PublishLiveSnapshot(totalTicks, currentTps, stopwatch.Elapsed.TotalSeconds);

                // Update otomatis status jeda waktu server berdasarkan pemain aktif & terautentikasi (Milestone 4.4)
                int onlinePlayerCount = Game1.server?.connectionsCount ?? 0;
                if (onlinePlayerCount == 0) Security.LobbyGatekeeper.ClearAll();
                int authPlayerCount = onlinePlayerCount == 0 ? 0 : Security.LobbyGatekeeper.GetAuthenticatedPlayerCount();
                DayTransitionManager.UpdateServerTimeProgression(onlinePlayerCount, authPlayerCount, Game1.netWorldState?.Value);

                windowTicks = 0;
                lastSnapshotTime = stopwatch.Elapsed;
            }

            // Presisi timing tanpa spinning 100% CPU
            var sleepTime = nextTickTarget - stopwatch.Elapsed;
            if (sleepTime.TotalMilliseconds >= 1.0)
            {
                Thread.Sleep((int)sleepTime.TotalMilliseconds);
            }
            else if (sleepTime > TimeSpan.Zero)
            {
                Thread.Yield();
            }
        }

        _logger?.Info("ServerLoop", $"Tick loop selesai. Total ticks dijalankan: {totalTicks}.");
        if (_targetTicks > 0)
        {
            _logger?.Info("ServerHost", "Benchmark/Test run selesai dengan sukses (Exit code 0).");
            Environment.Exit(0);
        }
    }

    private static void LoadSaveGame(string saveName)
    {
        _logger?.Info("SaveLoader", $"Memulai pemuatan save '{saveName}'...");

        try
        {
            SaveGame.Load(saveName);

            int lastProgress = -1;
            var loadSw = Stopwatch.StartNew();

            while (Game1.currentLoader != null && Game1.currentLoader.MoveNext())
            {
                int progress = Game1.currentLoader.Current;
                if (progress != lastProgress && progress % 20 == 0)
                {
                    _logger?.Info("SaveLoader", $"Pemuatan save '{saveName}'... {progress}%");
                    lastProgress = progress;
                }
                Thread.Sleep(5);
            }

            loadSw.Stop();
            _logger?.Info("SaveLoader", $"File save '{saveName}' berhasil dimuat dalam {loadSw.ElapsedMilliseconds} ms!");

            if (Game1.currentLocation == null)
            {
                Game1.currentLocation = Game1.RequireLocation("FarmHouse") ?? Game1.getFarm() ?? Game1.locations?.FirstOrDefault();
            }
            if (Game1.player != null && Game1.player.currentLocation == null)
            {
                Game1.player.currentLocation = Game1.currentLocation;
            }
            if (Game1.dayOfMonth == 0)
            {
                Game1.dayOfMonth = 1;
            }
            if (Game1.timeOfDay == 0)
            {
                Game1.timeOfDay = 600;
            }
            Game1.netWorldState?.Value.UpdateFromGame1();
            Game1.setGameMode(3);

            if (Game1.player != null)
            {
                var player = Game1.player;
                var details = new Dictionary<string, object?>
                {
                    ["farmerName"] = player.Name,
                    ["farmName"] = player.farmName.Value,
                    ["money"] = player.Money,
                    ["year"] = Game1.year,
                    ["season"] = Game1.season.ToString(),
                    ["dayOfMonth"] = Game1.dayOfMonth,
                    ["timeOfDay"] = Game1.timeOfDay,
                    ["daysPlayed"] = Game1.stats?.DaysPlayed ?? 0
                };
                _logger?.Info("SaveLoader", $"World State Aktif: Farmer '{player.Name}', {player.farmName.Value} Farm, Tahun {Game1.year} {Game1.season} tgl {Game1.dayOfMonth}, Jam {Game1.timeOfDay}", details);
            }
        }
        catch (Exception ex)
        {
            _logger?.Error("SaveLoader", $"Gagal memuat save file '{saveName}'", null, ex);
        }
    }

    public static void EnsureMultiplayerCabins(int minCabins = 8)
    {
        var farm = Game1.getFarm();
        if (farm != null)
        {
            EnsureMultiplayerCabins(farm, minCabins);
        }
    }

    public static void EnsureMultiplayerCabins(Farm farm, int minCabins = 8)
    {
        if (farm == null)
        {
            _logger?.Warn("CabinManager", "Game1.getFarm() mengembalikan null. Melewati pemeriksaan kabin.");
            return;
        }

        // Relokasi dan virtualisasi: 1 Rumah Fisik di Farm (The Grand Estate) + kabin virtual off-screen
        EnsureVirtualGrandEstate(farm, minCabins);

        // Pastikan setiap kabin yang belum berpenghuni memiliki instance farmhand di netWorldState
        foreach (var b in farm.buildings)
        {
            if (LobbyCabinManager.IsLobbyCabin(b)) continue;
            if (b.GetIndoors() is Cabin cabinIndoors && !cabinIndoors.HasOwner)
            {
                cabinIndoors.CreateFarmhand();
            }
        }

        // Bersihkan objek blocker / icon stop liar yang mungkin tertinggal di permukaan Farm
        LobbyCabinManager.CleanFarmBlockerObjects(farm);
    }

    /// <summary>
    /// Menerapkan arsitektur 1 Rumah Fisik di Farm (The Grand Estate) + kabin virtual off-screen untuk seluruh pemain.
    /// Menghemat >85% lahan peternakan sambil memberikan 8 rumah pribadi penuh yang dapat di-upgrade.
    /// </summary>
    public static void EnsureVirtualGrandEstate(Farm farm, int minCabins = 8)
    {
        if (farm == null) return;

        bool isIF2R = farm.NameOrUniqueName?.Contains("IF2R", StringComparison.OrdinalIgnoreCase) == true
                      || farm.map?.Properties?.ContainsKey("FarmCaveEntry") == true;
        if (isIF2R) return;

        // 1. Amankan Farmhouse host agar tidak memicu tabrakan/collision tak kasat mata
        var farmhouse = farm.buildings.FirstOrDefault(b => b.buildingType.Value == "Farmhouse");
        if (farmhouse != null && (farmhouse.tileX.Value >= 0 || farmhouse.tileY.Value >= 0))
        {
            farmhouse.tileX.Value = -100;
            farmhouse.tileY.Value = -100;
        }

        // 2. Kumpulkan seluruh kabin pemain non-lobby
        var nonLobbyCabins = farm.buildings.Where(b => b.isCabin && !LobbyCabinManager.IsLobbyCabin(b))
                                           .OrderBy(b => b.tileX.Value)
                                           .ToList();

        const int mainHouseX = 58;
        const int mainHouseY = 14;
        const int mainDoorX = 60;
        const int mainEntranceY = 17;

        // 3. Pastikan Kabin #1 adalah Rumah Fisik Utama di Farm (58, 14)
        if (nonLobbyCabins.Count > 0)
        {
            var mainCabin = nonLobbyCabins[0];
            mainCabin.modData["svheadless/main_estate_house"] = "true";
            mainCabin.skinId.Value = "Stone Cabin";

            if (mainCabin.tileX.Value != mainHouseX || mainCabin.tileY.Value != mainHouseY)
            {
                _logger?.Info("CabinManager", $"Menempatkan Rumah Fisik Utama di ({mainHouseX}, {mainHouseY})...");

                // Bersihkan debris pada jejak kaki rumah fisik
                for (int x = mainHouseX; x < mainHouseX + mainCabin.tilesWide.Value; x++)
                {
                    for (int y = mainHouseY; y < mainHouseY + mainCabin.tilesHigh.Value; y++)
                    {
                        var t = new Vector2(x, y);
                        farm.removeObject(t, false);
                        farm.terrainFeatures?.Remove(t);
                    }
                }

                // Bersihkan pintu masuk
                var entranceTile = new Vector2(mainDoorX, mainEntranceY);
                farm.removeObject(entranceTile, false);
                farm.terrainFeatures?.Remove(entranceTile);

                mainCabin.OnStartMove();
                mainCabin.tileX.Value = mainHouseX;
                mainCabin.tileY.Value = mainHouseY;
                mainCabin.updateInteriorWarps();
                mainCabin.OnEndMove();
            }

            // 4. Pindahkan sisa kabin fisik lama yang berada di atas tanah Farm ke off-screen
            for (int i = 1; i < nonLobbyCabins.Count; i++)
            {
                var vCabin = nonLobbyCabins[i];
                int targetOffX = -30 - ((i - 1) * 8);
                const int targetOffY = -30;

                vCabin.modData["svheadless/virtual_house"] = "true";

                // Jika kabin ini sebelumnya berdiri di atas tanah Farm (tileX >= 0), bersihkan bekasnya
                if (vCabin.tileX.Value >= 0 && vCabin.tileY.Value >= 0)
                {
                    _logger?.Info("CabinManager", $"Memindahkan kabin #{i + 1} dari tanah Farm ({vCabin.tileX.Value}, {vCabin.tileY.Value}) ke area virtual off-screen ({targetOffX}, {targetOffY}).");

                    for (int x = vCabin.tileX.Value; x < vCabin.tileX.Value + vCabin.tilesWide.Value; x++)
                    {
                        for (int y = vCabin.tileY.Value; y < vCabin.tileY.Value + vCabin.tilesHigh.Value; y++)
                        {
                            var t = new Vector2(x, y);
                            farm.removeObject(t, false);
                            farm.terrainFeatures?.Remove(t);
                        }
                    }
                }

                if (vCabin.tileX.Value != targetOffX || vCabin.tileY.Value != targetOffY)
                {
                    vCabin.OnStartMove();
                    vCabin.tileX.Value = targetOffX;
                    vCabin.tileY.Value = targetOffY;
                    vCabin.OnEndMove();
                }
            }
        }

        // 5. Bangun kabin baru langsung di off-screen hingga mencapai target minCabins (8)
        int currentCount = farm.buildings.Count(b => b.isCabin && !LobbyCabinManager.IsLobbyCabin(b));
        if (currentCount < minCabins)
        {
            for (int i = currentCount; i < minCabins; i++)
            {
                Vector2 cabinTile = (i == 0)
                    ? new Vector2(mainHouseX, mainHouseY)
                    : new Vector2(-30 - ((i - 1) * 8), -30);

                _logger?.Info("CabinManager", $"Membangun auto-cabin #{i + 1} pada tile ({cabinTile.X}, {cabinTile.Y})...");

                if (farm.buildStructure("Cabin", cabinTile, Game1.player, out Building? newCabin, magicalConstruction: true, skipSafetyChecks: true) && newCabin != null)
                {
                    newCabin.daysOfConstructionLeft.Value = 0;
                    newCabin.skinId.Value = "Stone Cabin";
                    newCabin.load();
                    if (i == 0)
                    {
                        newCabin.modData["svheadless/main_estate_house"] = "true";
                    }
                    else
                    {
                        newCabin.modData["svheadless/virtual_house"] = "true";
                    }

                    if (newCabin.GetIndoors() is Cabin cabinIndoors)
                    {
                        cabinIndoors.CreateFarmhand();
                        _logger?.Info("CabinManager", $"Auto-cabin #{i + 1} berhasil dibangun dan slot Farmhand diinisialisasi.");
                    }
                }
            }
        }

        // 6. Sinkronisasi Seluruh Titik Warp Keluar Pintu Interior
        SyncAllCabinExitWarps(farm, mainDoorX, mainEntranceY);
    }

    /// <summary>
    /// Menyelaraskan seluruh pintu keluar dari setiap interior kabin pribadi pemain agar selalu bermuara
    /// tepat di depan teras Rumah Fisik Utama di Farm.
    /// </summary>
    public static void SyncAllCabinExitWarps(Farm farm, int mainDoorX = 60, int mainEntranceY = 17)
    {
        if (farm == null) return;

        foreach (var b in farm.buildings)
        {
            if (b.isCabin && !LobbyCabinManager.IsLobbyCabin(b) && b.GetIndoors() is Cabin cabin)
            {
                var entry = cabin.getEntryLocation();
                bool found = false;
                foreach (var w in cabin.warps)
                {
                    if (w.TargetName == "Farm" || (w.X == entry.X && w.Y == entry.Y))
                    {
                        w.TargetName = "Farm";
                        w.TargetX = mainDoorX;
                        w.TargetY = mainEntranceY;
                        found = true;
                    }
                }

                if (!found)
                {
                    cabin.warps.Add(new Warp(entry.X, entry.Y, "Farm", mainDoorX, mainEntranceY, false));
                }
            }
        }
    }

    private static void PublishLiveSnapshot(long ticksElapsed, double currentTps, double uptimeSeconds)
    {
        try
        {
            var memoryMb = GC.GetTotalMemory(false) / (1024.0 * 1024.0);

            var snapshot = new LiveStateSnapshot
            {
                Timestamp = DateTime.UtcNow.ToString("o"),
                Server = new ServerPerformanceSnapshot
                {
                    UptimeSeconds = Math.Round(uptimeSeconds, 1),
                    Tps = Math.Round(currentTps, 1),
                    TargetTps = 60.0,
                    MemoryMb = Math.Round(memoryMb, 2),
                    TicksElapsed = ticksElapsed,
                    IsPaused = Game1.paused
                },
                World = new WorldStateSnapshot
                {
                    Year = Game1.year,
                    Season = Game1.season.ToString(),
                    DayOfMonth = Game1.dayOfMonth,
                    TimeOfDay = Game1.timeOfDay,
                    DaysPlayed = (int)(Game1.stats?.DaysPlayed ?? 0),
                    Weather = Game1.isRaining ? "Rain" : (Game1.isDebrisWeather ? "Wind" : "Sun")
                },
                Quorum = new QuorumStateSnapshot
                {
                    SleepReadyCount = Game1.player?.team?.announcedSleepingFarmers.Count ?? 0,
                    SleepTotalCount = Game1.otherFarmers.Count + 1,
                    AllReady = false
                }
            };

            // Tambahkan data pemain online
            if (Game1.player != null)
            {
                snapshot.Players.Add(new PlayerStateSnapshot
                {
                    FarmerId = Game1.player.UniqueMultiplayerID,
                    Name = Game1.player.Name,
                    Location = Game1.currentLocation?.NameOrUniqueName ?? "None",
                    TilePosition = new TileVector
                    {
                        X = Game1.player.TilePoint.X,
                        Y = Game1.player.TilePoint.Y
                    },
                    IsSleeping = Game1.player.isInBed?.Value ?? false
                });
            }

            foreach (var other in Game1.otherFarmers.Values)
            {
                snapshot.Players.Add(new PlayerStateSnapshot
                {
                    FarmerId = other.UniqueMultiplayerID,
                    Name = other.Name,
                    Location = other.currentLocation?.NameOrUniqueName ?? "None",
                    TilePosition = new TileVector
                    {
                        X = other.TilePoint.X,
                        Y = other.TilePoint.Y
                    },
                    IsSleeping = other.isInBed?.Value ?? false
                });
            }

            var snapshotPath = Path.Combine(_logsDir, "live-state-snapshot.json");
            var json = snapshot.ToJson(indented: true);
            File.WriteAllText(snapshotPath, json);

            // Publish ke Redis channel agar Web Apps / Monitor menerima update real-time
            if (_redisService != null)
            {
                _ = _redisService.PublishAsync("server:telemetry", json);
                _ = _redisService.SetHashAsync("server:status", "tps", snapshot.Server.Tps.ToString("F1"));
                _ = _redisService.SetHashAsync("server:status", "memoryMb", snapshot.Server.MemoryMb.ToString("F1"));
                _ = _redisService.SetHashAsync("server:status", "timeOfDay", snapshot.World.TimeOfDay.ToString());
                _ = _redisService.SetHashAsync("server:status", "playerCount", (snapshot.Players.Count).ToString());
            }

            _logger?.Debug("Telemetry", $"TPS: {snapshot.Server.Tps:F1} | RAM: {snapshot.Server.MemoryMb:F1} MB | Time: {snapshot.World.TimeOfDay} | Year {snapshot.World.Year} {snapshot.World.Season} {snapshot.World.DayOfMonth}");
        }
        catch (Exception ex)
        {
            _logger?.Warn("Telemetry", $"Gagal menulis live state snapshot: {ex.Message}");
        }
    }
}
