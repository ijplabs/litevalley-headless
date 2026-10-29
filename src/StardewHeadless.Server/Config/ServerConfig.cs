using System;
using System.IO;

namespace StardewHeadless.Server.Config;

public class ServerConfig
{
    public string GameDir { get; set; } = string.Empty;
    public string SaveDir { get; set; } = string.Empty;
    public string ConfigDir { get; set; } = string.Empty;
    public string LogDir { get; set; } = string.Empty;
    public int Port { get; set; } = 24642;
    public int WebPort { get; set; } = 8080;
    public string ServerName { get; set; } = "Stardew Headless Server";
    public string FarmName { get; set; } = "LembahMakmur";
    public int FarmType { get; set; } = 0; // 0=Standard, 1=Riverland, 2=Forest, 3=Hilltop, 4=Wilderness, 7=Custom (IF2R)
    public int StartingCabins { get; set; } = 4;
    public bool UseSeparateWallets { get; set; } = true;
    public bool AutoCreateFreshWorld { get; set; } = false;
    public string? RedisHost { get; set; }
    public int RedisPort { get; set; } = 6379;
    public string? RedisPassword { get; set; }
    public string? AdminPin { get; set; }

    /// <summary>
    /// Menentukan kebijakan penegakan mod klien:
    /// false (Default / Strict Whitelist): Hanya mod resmi terdaftar yang diizinkan masuk ke server; mod tak dikenal ditolak.
    /// true (Permissive / Allow All Mods): Mod tak dikenal diizinkan masuk, namun diberi peringatan risiko bug/glitch.
    /// Mod cheat terlarang (Blacklist) akan SELALU ditolak di kedua mode.
    /// </summary>
    public bool AllowAllMods { get; set; } = false;

    /// <summary>
    /// Slug profil peternakan terstandarisasi (misal: "vanilla:standard", "vanilla:meadowlands", "sve:if2r", "sve:grandpas_farm", "sve:frontier_farm").
    /// </summary>
    public string FarmProfileId { get; set; } = "vanilla:standard";

    /// <summary>
    /// Path direktori mod terisolasi untuk instance server saat ini (digunakan oleh Headless SMAPI).
    /// </summary>
    public string? ModsPath { get; set; }

    /// <summary>
    /// Menentukan apakah SMAPI di-boot untuk server ini.
    /// </summary>
    public bool EnableSmapi { get; set; } = false;

    /// <summary>
    /// Menghitung tipe farm numerik (0..7) yang sesuai dengan FarmProfileId atau FarmType.
    /// </summary>
    public int ResolvedFarmType
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(FarmProfileId))
            {
                var lower = FarmProfileId.ToLowerInvariant().Trim();
                if (lower == "vanilla:standard" || lower == "standard") return 0;
                if (lower == "vanilla:riverland" || lower == "riverland") return 1;
                if (lower == "vanilla:forest" || lower == "forest") return 2;
                if (lower == "vanilla:hilltop" || lower == "hilltop") return 3;
                if (lower == "vanilla:wilderness" || lower == "wilderness") return 4;
                if (lower == "vanilla:fourcorners" || lower == "fourcorners") return 5;
                if (lower == "vanilla:beach" || lower == "beach") return 6;
                if (lower == "vanilla:meadowlands" || lower == "meadowlands") return 7;
                if (lower == "sve:if2r" || lower == "if2r") return 0; // IF2R overrides Standard (0)
                if (lower == "sve:grandpas_farm" || lower == "grandpas_farm") return 0; // Grandpa's Farm overrides Standard (0)
                if (lower == "sve:frontier_farm" || lower == "frontier_farm") return 7; // Frontier Farm uses 1.6 AdditionalFarms (7)
                if (lower == "sve:grampleton" || lower == "grampleton") return 0; // Grampleton Fields companion map (0)
            }
            return FarmType;
        }
    }

    /// <summary>
    /// Menghitung Id ModFarmType untuk farm tipe 7 (Meadowlands atau Frontier Farm).
    /// </summary>
    public string? ResolvedModFarmId
    {
        get
        {
            var lower = (FarmProfileId ?? string.Empty).ToLowerInvariant().Trim();
            if (lower == "sve:frontier_farm" || lower == "frontier_farm") return "FrontierFarm";
            if (lower == "vanilla:meadowlands" || lower == "meadowlands") return "MeadowlandsFarm";
            if (ResolvedFarmType == 7) return "MeadowlandsFarm";
            return null;
        }
    }

    public bool IsSveProfile => !string.IsNullOrWhiteSpace(FarmProfileId) && FarmProfileId.StartsWith("sve:", StringComparison.OrdinalIgnoreCase);

    public static ServerConfig Load(string[] args)
    {
        var config = new ServerConfig();

        // 1. Baca dari Environment Variables
        config.GameDir = Environment.GetEnvironmentVariable("STARDEW_GAME_DIR") ?? string.Empty;
        config.SaveDir = Environment.GetEnvironmentVariable("STARDEW_SAVE_DIR") ?? string.Empty;
        config.ConfigDir = Environment.GetEnvironmentVariable("STARDEW_CONFIG_DIR") ?? string.Empty;
        config.LogDir = Environment.GetEnvironmentVariable("STARDEW_LOG_DIR") ?? string.Empty;

        if (int.TryParse(Environment.GetEnvironmentVariable("PORT"), out int envPort))
            config.Port = envPort;
        if (int.TryParse(Environment.GetEnvironmentVariable("WEB_PORT"), out int envWebPort))
            config.WebPort = envWebPort;

        config.ServerName = Environment.GetEnvironmentVariable("SERVER_NAME") ?? config.ServerName;
        config.FarmName = Environment.GetEnvironmentVariable("FARM_NAME") ?? config.FarmName;

        if (int.TryParse(Environment.GetEnvironmentVariable("FARM_TYPE"), out int envFarmType))
            config.FarmType = envFarmType;
        if (int.TryParse(Environment.GetEnvironmentVariable("STARTING_CABINS"), out int envCabins))
            config.StartingCabins = envCabins;
        else if (int.TryParse(Environment.GetEnvironmentVariable("MAX_PLAYERS"), out int envMaxPlayers))
            config.StartingCabins = envMaxPlayers;

        config.StartingCabins = Math.Clamp(config.StartingCabins, 1, 16);
        if (bool.TryParse(Environment.GetEnvironmentVariable("SEPARATE_WALLETS"), out bool envWallets))
            config.UseSeparateWallets = envWallets;

        config.RedisHost = Environment.GetEnvironmentVariable("REDIS_HOST");
        if (int.TryParse(Environment.GetEnvironmentVariable("REDIS_PORT"), out int envRedisPort))
            config.RedisPort = envRedisPort;
        config.RedisPassword = Environment.GetEnvironmentVariable("REDIS_PASSWORD");
        config.AdminPin = Environment.GetEnvironmentVariable("ADMIN_PASSWORD") ?? Environment.GetEnvironmentVariable("ADMIN_PIN");

        if (bool.TryParse(Environment.GetEnvironmentVariable("ALLOW_ALL_MODS"), out bool envAllowAllMods))
            config.AllowAllMods = envAllowAllMods;

        var envProfile = Environment.GetEnvironmentVariable("FARM_PROFILE") ?? Environment.GetEnvironmentVariable("FARM_PROFILE_ID");
        if (!string.IsNullOrWhiteSpace(envProfile))
            config.FarmProfileId = envProfile.Trim();

        config.ModsPath = Environment.GetEnvironmentVariable("MODS_PATH");
        if (bool.TryParse(Environment.GetEnvironmentVariable("ENABLE_SMAPI"), out bool envSmapi))
            config.EnableSmapi = envSmapi;

        // 2. Override dari Command-Line Arguments
        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.Equals("--game-dir", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                config.GameDir = args[++i];
            else if (arg.Equals("--save-dir", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                config.SaveDir = args[++i];
            else if (arg.Equals("--config-dir", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                config.ConfigDir = args[++i];
            else if (arg.Equals("--log-dir", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                config.LogDir = args[++i];
            else if (arg.Equals("--port", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                if (int.TryParse(args[++i], out int p)) config.Port = p;
            }
            else if (arg.Equals("--web-port", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                if (int.TryParse(args[++i], out int wp)) config.WebPort = wp;
            }
            else if (arg.Equals("--server-name", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                config.ServerName = args[++i];
            else if (arg.Equals("--farm-name", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                config.FarmName = args[++i];
            else if (arg.Equals("--farm-type", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                if (int.TryParse(args[++i], out int ft)) config.FarmType = ft;
            }
            else if (arg.Equals("--cabins", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                if (int.TryParse(args[++i], out int c)) config.StartingCabins = c;
            }
            else if (arg.Equals("--create-new", StringComparison.OrdinalIgnoreCase))
                config.AutoCreateFreshWorld = true;
            else if (arg.Equals("--redis-host", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                config.RedisHost = args[++i];
            else if (arg.Equals("--admin-pin", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                config.AdminPin = args[++i];
            else if (arg.Equals("--allow-all-mods", StringComparison.OrdinalIgnoreCase))
                config.AllowAllMods = true;
            else if (arg.Equals("--strict-mods", StringComparison.OrdinalIgnoreCase))
                config.AllowAllMods = false;
            else if ((arg.Equals("--farm-profile", StringComparison.OrdinalIgnoreCase) || arg.Equals("--profile", StringComparison.OrdinalIgnoreCase)) && i + 1 < args.Length)
                config.FarmProfileId = args[++i];
            else if (arg.Equals("--mods-path", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                config.ModsPath = args[++i];
            else if (arg.Equals("--enable-smapi", StringComparison.OrdinalIgnoreCase))
                config.EnableSmapi = true;
        }

        // 3. Defaults jika belum ditentukan
        if (string.IsNullOrWhiteSpace(config.GameDir))
        {
            if (Directory.Exists(@"D:\SteamLibrary\steamapps\common\Stardew Valley"))
                config.GameDir = @"D:\SteamLibrary\steamapps\common\Stardew Valley";
            else if (Directory.Exists("/data/game"))
                config.GameDir = "/data/game";
            else
                config.GameDir = AppContext.BaseDirectory;
        }

        if (string.IsNullOrWhiteSpace(config.SaveDir))
        {
            if (Directory.Exists("/data/saves"))
            {
                config.SaveDir = "/data/saves";
            }
            else
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                config.SaveDir = Path.Combine(appData, "StardewValley", "Saves");
            }
        }

        if (string.IsNullOrWhiteSpace(config.LogDir))
        {
            config.LogDir = Path.Combine(config.GameDir, "logs");
        }

        if (string.IsNullOrWhiteSpace(config.ConfigDir))
        {
            config.ConfigDir = Path.Combine(AppContext.BaseDirectory, "config");
        }

        return config;
    }
}
