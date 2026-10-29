using System;
using System.IO;
using StardewHeadless.Protocol.Logging;
using StardewHeadless.Server.Config;

namespace StardewHeadless.Server.ModHost;

public record SmapiHostStatus(
    bool IsAvailable,
    string? SmapiDllPath,
    string? Version,
    string? ActiveModsPath
);

/// <summary>
/// Bertanggung jawab mendeteksi, mengonfigurasi, dan memvalidasi lingkungan Headless SMAPI (Stardew Modding API)
/// untuk memuat mod pihak ketiga berlisensi tanpa GPU hardware.
/// </summary>
public static class HeadlessSmapiBootstrapper
{
    public static SmapiHostStatus CheckStatus(string gameDir, string? customModsPath = null)
    {
        if (string.IsNullOrWhiteSpace(gameDir) || !Directory.Exists(gameDir))
        {
            return new SmapiHostStatus(false, null, null, customModsPath);
        }

        var candidateDlls = new[]
        {
            Path.Combine(gameDir, "StardewModdingAPI.dll"),
            Path.Combine(gameDir, "StardewValleyModdingAPI.dll"),
            Path.Combine(gameDir, "SMAPI.dll")
        };

        foreach (var dll in candidateDlls)
        {
            if (File.Exists(dll))
            {
                string version = "Unknown";
                try
                {
                    var fvi = System.Diagnostics.FileVersionInfo.GetVersionInfo(dll);
                    version = fvi.ProductVersion ?? fvi.FileVersion ?? "Unknown";
                }
                catch { }

                return new SmapiHostStatus(true, dll, version, customModsPath);
            }
        }

        return new SmapiHostStatus(false, null, null, customModsPath);
    }

    public static string[] BuildSmapiArguments(ServerConfig config, string activeModsPath)
    {
        var args = new List<string>
        {
            "--mods-path", activeModsPath
        };

        if (config.AutoCreateFreshWorld)
        {
            args.Add("--create-new");
        }

        return args.ToArray();
    }
}
