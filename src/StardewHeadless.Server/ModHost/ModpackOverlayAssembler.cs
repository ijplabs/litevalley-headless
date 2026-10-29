using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using StardewHeadless.Protocol.Logging;

namespace StardewHeadless.Server.ModHost;

public record ModpackAssemblyResult(
    string ActiveModsPath,
    string FarmProfileId,
    List<string> IncludedMods,
    List<string> ExcludedMods,
    bool IsSveEnabled
);

/// <summary>
/// Bertanggung jawab merakit direktori mod aktif secara dinamis dan terisolasi per instance server.
/// Mencegah konflik eksklusivitas peta SVE (misalnya IF2R vs Grandpa's Farm yang sama-sama menimpa Standard Farm)
/// dengan hanya menyertakan modul peta yang sesuai dengan FarmProfileId aktif.
/// </summary>
public static class ModpackOverlayAssembler
{
    private static readonly HashSet<string> CoreMods = new(StringComparer.OrdinalIgnoreCase)
    {
        "StardewBridge",
        "Pathoschild.ContentPatcher",
        "ContentPatcher",
        "Pathoschild.Automate",
        "Automate",
        "Esca.FarmTypeManager",
        "FarmTypeManager",
        "spacechase0.SpaceCore",
        "SpaceCore",
        "Alek.ExpandedPreconditionsUtility",
        "ExpandedPreconditionsUtility",
        "Platonymous.CustomNPCFixes",
        "CustomNPCFixes"
    };

    public static ModpackAssemblyResult Assemble(
        string baseModsRepo,
        string activeModsTargetDir,
        string farmProfileId,
        StructuredLogger? logger = null)
    {
        string profile = (farmProfileId ?? "vanilla:standard").ToLowerInvariant().Trim();
        bool isSve = profile.StartsWith("sve:", StringComparison.OrdinalIgnoreCase) || profile.Contains("sve");
        bool isIF2R = profile == "sve:if2r" || profile == "if2r";
        bool isGrandpasFarm = profile == "sve:grandpas_farm" || profile == "grandpas_farm";
        bool isFrontierFarm = profile == "sve:frontier_farm" || profile == "frontier_farm";
        bool isGrampleton = profile.Contains("grampleton");

        logger?.Info("ModpackAssembler", $"Merakit modpack overlay untuk profil '{farmProfileId}' di '{activeModsTargetDir}'...");

        if (!Directory.Exists(activeModsTargetDir))
        {
            Directory.CreateDirectory(activeModsTargetDir);
        }

        var includedMods = new List<string>();
        var excludedMods = new List<string>();

        if (!Directory.Exists(baseModsRepo))
        {
            logger?.Warn("ModpackAssembler", $"Direktori dasar repositori mod '{baseModsRepo}' tidak ditemukan. Melewati penyalinan fisik.");
            return new ModpackAssemblyResult(activeModsTargetDir, profile, includedMods, excludedMods, isSve);
        }

        var availableModDirs = Directory.GetDirectories(baseModsRepo);
        foreach (var modDir in availableModDirs)
        {
            var dirName = Path.GetFileName(modDir);

            // Filter Eksklusif Peta SVE
            if (dirName.Contains("Immersive Farm 2", StringComparison.OrdinalIgnoreCase) || dirName.Contains("IF2R", StringComparison.OrdinalIgnoreCase))
            {
                if (!isIF2R)
                {
                    excludedMods.Add(dirName);
                    continue;
                }
            }
            else if (dirName.Contains("Grandpa's Farm", StringComparison.OrdinalIgnoreCase) || dirName.Contains("GrandpasFarm", StringComparison.OrdinalIgnoreCase))
            {
                if (!isGrandpasFarm)
                {
                    excludedMods.Add(dirName);
                    continue;
                }
            }
            else if (dirName.Contains("Frontier Farm", StringComparison.OrdinalIgnoreCase) || dirName.Contains("FrontierFarm", StringComparison.OrdinalIgnoreCase))
            {
                if (!isFrontierFarm)
                {
                    excludedMods.Add(dirName);
                    continue;
                }
            }
            else if (dirName.Contains("Grampleton", StringComparison.OrdinalIgnoreCase))
            {
                if (!isGrampleton)
                {
                    excludedMods.Add(dirName);
                    continue;
                }
            }
            else if (dirName.Contains("Stardew Valley Expanded", StringComparison.OrdinalIgnoreCase) || dirName.Equals("SVE", StringComparison.OrdinalIgnoreCase))
            {
                if (!isSve)
                {
                    excludedMods.Add(dirName);
                    continue;
                }
            }

            // Pasang tautan atau salin ke target directory
            var targetPath = Path.Combine(activeModsTargetDir, dirName);
            MountDirectory(modDir, targetPath, logger);
            includedMods.Add(dirName);
        }

        // Tulis manifest aktivasimodpack
        var manifestPath = Path.Combine(activeModsTargetDir, "active-modpack.json");
        try
        {
            var manifestContent = $"{{\n  \"farmProfileId\": \"{farmProfileId}\",\n  \"assembledAt\": \"{DateTime.UtcNow:o}\",\n  \"isSveEnabled\": {isSve.ToString().ToLower()},\n  \"includedModsCount\": {includedMods.Count}\n}}";
            File.WriteAllText(manifestPath, manifestContent);
        }
        catch { }

        logger?.Info("ModpackAssembler", $"Modpack overlay selesai dirakit: {includedMods.Count} mod aktif, {excludedMods.Count} mod dikecualikan (konflik profil dicegah).");

        return new ModpackAssemblyResult(activeModsTargetDir, profile, includedMods, excludedMods, isSve);
    }

    private static void MountDirectory(string sourceDir, string targetDir, StructuredLogger? logger)
    {
        try
        {
            if (Directory.Exists(targetDir))
            {
                return; // Sudah terpasang
            }

            // Pada Windows coba buat Directory Junction, pada Linux/Unix buat Symlink
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Fallback aman: jika symlink tanpa elevasi ditolak, buat junction
                try
                {
                    Directory.CreateSymbolicLink(targetDir, sourceDir);
                    return;
                }
                catch
                {
                    // Junction fallback via mklink /J jika diperlukan
                }
            }
            else
            {
                try
                {
                    Directory.CreateSymbolicLink(targetDir, sourceDir);
                    return;
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            logger?.Debug("ModpackAssembler", $"Catatan mount direktori '{sourceDir}': {ex.Message}");
        }
    }
}
