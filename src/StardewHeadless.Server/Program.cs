using System.IO;
using System.Linq;
using System.Reflection;
using StardewHeadless.Protocol.Logging;
using StardewHeadless.Server.Config;
using StardewHeadless.Server.Harness;

namespace StardewHeadless.Server;

public static class Program
{
    private static string _gameInstallDir = @"D:\SteamLibrary\steamapps\common\Stardew Valley";

    public static int Main(string[] args)
    {
        var config = ServerConfig.Load(args);
        _gameInstallDir = config.GameDir;

        // 1. Setup Logging Telemetri terstruktur ke folder logs/
        var logsDir = config.LogDir;
        if (!Directory.Exists(logsDir))
        {
            Directory.CreateDirectory(logsDir);
        }

        var jsonlPath = Path.Combine(logsDir, "headless-server.jsonl");
        var logPath = Path.Combine(logsDir, "headless-server.log");
        using var logger = new StructuredLogger("HeadlessServer", jsonlPath, logPath, echoToConsole: true);

        logger.Info("Compliance", "=========================================================");
        logger.Info("Compliance", "LiteValley Dedicated Server - Genuine License Compliance");
        logger.Info("Compliance", "Tribute to Eric Barone (ConcernedApe) for creating Stardew Valley.");
        logger.Info("Compliance", "This server strictly runs with legally-acquired game assets.");
        logger.Info("Compliance", "=========================================================");

        // 2. Pemeriksaan Kepatuhan Hak Cipta Wajib (Zero-Piracy Compliance Guard)
        var gameBinaryPath = Path.Combine(_gameInstallDir, "Stardew Valley.dll");
        if (!File.Exists(gameBinaryPath))
        {
            logger.Fatal("Compliance", "=========================================================");
            logger.Fatal("Compliance", "[FATAL] Instalasi resmi Stardew Valley TIDAK DITEMUKAN!");
            logger.Fatal("Compliance", $"Berkas 'Stardew Valley.dll' tidak ditemukan pada direktori: '{_gameInstallDir}'");
            logger.Fatal("Compliance", "LiteValley mematuhi kebijakan Zero-Piracy dan TIDAK PERNAH membundel biner game.");
            logger.Fatal("Compliance", "Untuk menjalankan server ini, Anda WAJIB menyediakan berkas game asli berlisensi sah:");
            logger.Fatal("Compliance", "1. Mount volume direktori game resmi Anda ke '/data/game', atau");
            logger.Fatal("Compliance", "2. Masukkan STEAM_REFRESH_TOKEN resmi Anda pada container steam-downloader.");
            logger.Fatal("Compliance", "=========================================================");
            return 1;
        }

        // 3. Setup Assembly Resolver agar dapat me-load Stardew Valley.dll & MonoGame dari direktori resmi
        AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;
        System.Runtime.Loader.AssemblyLoadContext.Default.Resolving += (ctx, asmName) => OnAssemblyResolve(null, new ResolveEventArgs(asmName.FullName));
        PreloadAssemblies(_gameInstallDir);

        logger.Info("Boot", "StardewHeadless Server Daemon v1.0.0 starting...");
        logger.Info("Boot", $"Game Directory: {_gameInstallDir}");
        logger.Info("Boot", $"Save Directory: {config.SaveDir}");
        logger.Info("Boot", $"Config Directory: {config.ConfigDir}");
        logger.Info("Boot", $"Logs Directory: {logsDir}");
        logger.Info("Boot", $"Server Name: {config.ServerName}");
        logger.Info("Boot", $"UDP Port: {config.Port} | Web Port: {config.WebPort}");
        logger.Info("Boot", "=========================================================");

        try
        {
            // 4. Aplikasikan NullGraphicsHarness sebelum assembly game dijalankan
            NullGraphicsHarness.Apply(logger, config.GameDir);

            // 5. Delegasikan ke ServerHost runner
            return ServerHost.Run(logger, config, args);
        }
        catch (Exception ex)
        {
            logger.Fatal("Crash", "Fatal exception saat booting Headless Server", null, ex);
            return 1;
        }
    }

    private static Assembly? OnAssemblyResolve(object? sender, ResolveEventArgs args)
    {
        var assemblyName = new AssemblyName(args.Name).Name;
        if (string.IsNullOrEmpty(assemblyName)) return null;

        var searchDirs = new[]
        {
            _gameInstallDir,
            Path.Combine(_gameInstallDir, "smapi-internal"),
            AppContext.BaseDirectory
        };

        foreach (var dir in searchDirs)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;

            var path = Path.Combine(dir, assemblyName + ".dll");
            if (File.Exists(path))
            {
                return Assembly.LoadFrom(path);
            }
        }

        return null;
    }

    private static readonly string[] _essentialAssemblies = new[]
    {
        "MonoGame.Framework",
        "Stardew Valley",
        "StardewValley.GameData",
        "Lidgren.Network",
        "xTile",
        "Steamworks.NET",
        "GalaxyCSharp"
    };

    private static void PreloadAssemblies(string gameDir)
    {
        var searchDirs = new[]
        {
            gameDir,
            Path.Combine(gameDir, "smapi-internal"),
            AppContext.BaseDirectory
        };

        foreach (var name in _essentialAssemblies)
        {
            foreach (var dir in searchDirs)
            {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;

                var path = Path.Combine(dir, name + ".dll");
                if (File.Exists(path))
                {
                    try
                    {
                        if (!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == name))
                        {
                            Assembly.LoadFrom(path);
                        }
                    }
                    catch
                    {
                        // Abaikan unmanaged DLL atau native libraries
                    }
                    break;
                }
            }
        }
    }
}
