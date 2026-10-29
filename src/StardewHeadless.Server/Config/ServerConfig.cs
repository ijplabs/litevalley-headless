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
        if (bool.TryParse(Environment.GetEnvironmentVariable("SEPARATE_WALLETS"), out bool envWallets))
            config.UseSeparateWallets = envWallets;

        config.RedisHost = Environment.GetEnvironmentVariable("REDIS_HOST");
        if (int.TryParse(Environment.GetEnvironmentVariable("REDIS_PORT"), out int envRedisPort))
            config.RedisPort = envRedisPort;
        config.RedisPassword = Environment.GetEnvironmentVariable("REDIS_PASSWORD");

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
