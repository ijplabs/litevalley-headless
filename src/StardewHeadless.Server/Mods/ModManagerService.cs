using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using StardewHeadless.Protocol.Logging;
using StardewHeadless.Server.Database;

namespace StardewHeadless.Server.Mods;

public record InstalledModInfo(
    string UniqueId,
    string Name,
    string Author,
    string Version,
    string? Description,
    string FolderName,
    string FolderPath,
    bool IsEnabled,
    string Category,
    long SizeBytes,
    string? ContentPackFor,
    List<string> Dependencies,
    DateTime InstalledAt
);

public record ModInstallResult(
    bool Success,
    string Message,
    List<InstalledModInfo> InstalledMods,
    List<string> Warnings
);

public class SmapiManifestDependency
{
    public string? UniqueID { get; set; }
    public string? MinimumVersion { get; set; }
    public bool IsRequired { get; set; } = true;
}

public class SmapiContentPackFor
{
    public string? UniqueID { get; set; }
    public string? MinimumVersion { get; set; }
}

public class SmapiManifest
{
    public string? Name { get; set; }
    public string? Author { get; set; }
    public string? Version { get; set; }
    public string? Description { get; set; }
    public string? UniqueID { get; set; }
    public string? MinimumApiVersion { get; set; }
    public string? EntryDll { get; set; }
    public SmapiContentPackFor? ContentPackFor { get; set; }
    public List<SmapiManifestDependency>? Dependencies { get; set; }
}

/// <summary>
/// Layanan manajemen mod fisik pada storage server Headless.
/// Menyediakan operasi scan manifest SMAPI, inspeksi dependency, instalasi aman zip (Anti-ZipSlip),
/// toggle enable/disable mod, serta sinkronisasi otomatis ke tabel mod_policies.
/// </summary>
public class ModManagerService
{
    private readonly string _modsDir;
    private readonly IDatabaseService? _dbService;
    private readonly StructuredLogger? _logger;

    private static readonly HashSet<string> FrameworkUniqueIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "Pathoschild.ContentPatcher",
        "spacechase0.SpaceCore",
        "Esca.FarmTypeManager",
        "Alek.ExpandedPreconditionsUtility",
        "Platonymous.CustomNPCFixes",
        "Cherry.ExpandedStorage",
        "DaLion.ShopTileFramework",
        "StardewBridge"
    };

    private static readonly string[] BlacklistedCheatKeywords = new[]
    {
        "CJB",
        "CheatsMenu",
        "ItemSpawner",
        "FishingMadeEasy",
        "EasyFishing",
        "InstantBite",
        "InfiniteHealth",
        "MovementSpeed",
        "SkipFishing",
        "AllCheats"
    };

    public ModManagerService(string modsDir, IDatabaseService? dbService = null, StructuredLogger? logger = null)
    {
        _modsDir = modsDir;
        _dbService = dbService;
        _logger = logger;

        if (!Directory.Exists(_modsDir))
        {
            try
            {
                Directory.CreateDirectory(_modsDir);
            }
            catch (Exception ex)
            {
                _logger?.Warn("ModManager", $"Tidak dapat membuat direktori mods di '{_modsDir}': {ex.Message}");
            }
        }
    }

    public string ModsDirectory => _modsDir;

    /// <summary>
    /// Memindai seluruh folder mod di storage server dan membaca manifest.json masing-masing.
    /// </summary>
    public List<InstalledModInfo> GetInstalledMods()
    {
        var result = new List<InstalledModInfo>();
        if (!Directory.Exists(_modsDir)) return result;

        var subDirs = Directory.GetDirectories(_modsDir);
        foreach (var dir in subDirs)
        {
            var dirName = Path.GetFileName(dir);
            bool isEnabled = !dirName.StartsWith(".") && !dirName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);

            var manifestPath = Path.Combine(dir, "manifest.json");
            if (File.Exists(manifestPath))
            {
                var modInfo = ParseModDirectory(dir, manifestPath, isEnabled);
                if (modInfo != null)
                {
                    result.Add(modInfo);
                }
            }
            else
            {
                // Cek kemungkinan subfolder bertingkat (misal SVE yang berisi multiple mod folders di dalamnya)
                var nestedManifests = Directory.GetFiles(dir, "manifest.json", SearchOption.AllDirectories);
                foreach (var nestedManifest in nestedManifests)
                {
                    var nestedDir = Path.GetDirectoryName(nestedManifest) ?? dir;
                    var nestedName = Path.GetFileName(nestedDir);
                    bool nestedEnabled = isEnabled && !nestedName.StartsWith(".") && !nestedName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
                    var modInfo = ParseModDirectory(nestedDir, nestedManifest, nestedEnabled);
                    if (modInfo != null && !result.Any(m => m.UniqueId.Equals(modInfo.UniqueId, StringComparison.OrdinalIgnoreCase)))
                    {
                        result.Add(modInfo);
                    }
                }
            }
        }

        return result.OrderBy(m => m.Name).ToList();
    }

    private InstalledModInfo? ParseModDirectory(string dirPath, string manifestPath, bool isEnabled)
    {
        try
        {
            var json = File.ReadAllText(manifestPath);
            var manifest = JsonSerializer.Deserialize<SmapiManifest>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (manifest == null || string.IsNullOrWhiteSpace(manifest.UniqueID))
            {
                return null;
            }

            string category = ClassifyMod(manifest);
            long totalSize = CalculateDirectorySize(dirPath);
            var deps = manifest.Dependencies?
                .Where(d => !string.IsNullOrEmpty(d.UniqueID))
                .Select(d => d.UniqueID!)
                .ToList() ?? new List<string>();

            var dirInfo = new DirectoryInfo(dirPath);
            DateTime installedAt = dirInfo.CreationTimeUtc > dirInfo.LastWriteTimeUtc 
                ? dirInfo.LastWriteTimeUtc 
                : dirInfo.CreationTimeUtc;

            return new InstalledModInfo(
                UniqueId: manifest.UniqueID.Trim(),
                Name: manifest.Name ?? Path.GetFileName(dirPath),
                Author: manifest.Author ?? "Unknown",
                Version: manifest.Version ?? "1.0.0",
                Description: manifest.Description,
                FolderName: Path.GetFileName(dirPath),
                FolderPath: dirPath,
                IsEnabled: isEnabled,
                Category: category,
                SizeBytes: totalSize,
                ContentPackFor: manifest.ContentPackFor?.UniqueID,
                Dependencies: deps,
                InstalledAt: installedAt
            );
        }
        catch (Exception ex)
        {
            _logger?.Warn("ModManager", $"Gagal mem-parsing manifest di '{manifestPath}': {ex.Message}");
            return null;
        }
    }

    private static string ClassifyMod(SmapiManifest manifest)
    {
        string uid = manifest.UniqueID ?? "";
        string name = manifest.Name ?? "";
        string clean = (uid + " " + name).Replace(" ", "").Replace(".", "").Replace("_", "").ToLowerInvariant();

        if (BlacklistedCheatKeywords.Any(k => clean.Contains(k.ToLowerInvariant())))
            return "ProhibitedCheat";

        if (FrameworkUniqueIds.Contains(uid))
            return "Framework";

        if (manifest.ContentPackFor != null && !string.IsNullOrEmpty(manifest.ContentPackFor.UniqueID))
            return "ContentPack";

        if (uid.Contains("Expanded", StringComparison.OrdinalIgnoreCase) || 
            name.Contains("Expanded", StringComparison.OrdinalIgnoreCase) ||
            uid.Contains("Farm", StringComparison.OrdinalIgnoreCase))
            return "ExpansionMap";

        if (name.Contains("Automate", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Elevator", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Backpack", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Rings", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Gates", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("UI Info", StringComparison.OrdinalIgnoreCase))
            return "QoL";

        return "General";
    }

    private static long CalculateDirectorySize(string directoryPath)
    {
        try
        {
            var d = new DirectoryInfo(directoryPath);
            return d.EnumerateFiles("*", SearchOption.AllDirectories).Sum(fi => fi.Length);
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Mengekstrak file zip mod dengan perlindungan Zip-Slip, memvalidasi manifest,
    /// menolak cheat terlarang, memindahkan ke folder mod, dan menyinkronkan mod policy.
    /// </summary>
    public async Task<ModInstallResult> InstallModZipAsync(Stream zipStream, string fileName)
    {
        var tempExtractDir = Path.Combine(Path.GetTempPath(), "svheadless_mod_extract_" + Guid.NewGuid().ToString("N"));
        var warnings = new List<string>();

        try
        {
            Directory.CreateDirectory(tempExtractDir);

            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Read))
            {
                foreach (var entry in archive.Entries)
                {
                    // Proteksi Keamanan 1: Zip-Slip Vulnerability Guard (dengan Trailing Separator)
                    string safeBaseDir = Path.GetFullPath(tempExtractDir);
                    if (!safeBaseDir.EndsWith(Path.DirectorySeparatorChar))
                    {
                        safeBaseDir += Path.DirectorySeparatorChar;
                    }

                    string destinationPath = Path.GetFullPath(Path.Combine(tempExtractDir, entry.FullName));
                    if (!destinationPath.StartsWith(safeBaseDir, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException($"Deteksi serangan keamanan Zip-Slip: entri '{entry.FullName}' mencoba menulis di luar direktori aman.");
                    }

                    // Proteksi Keamanan 2: Blokir eksekusi script asing berbahaya (.exe, .bat, .cmd, .ps1, .vbs)
                    string ext = Path.GetExtension(entry.FullName).ToLowerInvariant();
                    if (ext is ".exe" or ".bat" or ".cmd" or ".ps1" or ".vbs" or ".sh")
                    {
                        warnings.Add($"File eksekusi non-SMAPI '{entry.FullName}' diabaikan demi keamanan server.");
                        continue;
                    }

                    if (string.IsNullOrEmpty(entry.Name))
                    {
                        // Directory entry
                        Directory.CreateDirectory(destinationPath);
                    }
                    else
                    {
                        var parentDir = Path.GetDirectoryName(destinationPath);
                        if (!string.IsNullOrEmpty(parentDir))
                        {
                            Directory.CreateDirectory(parentDir);
                        }
                        entry.ExtractToFile(destinationPath, overwrite: true);
                    }
                }
            }

            // Temukan semua manifest.json dalam hasil ekstraksi
            var manifests = Directory.GetFiles(tempExtractDir, "manifest.json", SearchOption.AllDirectories);
            if (manifests.Length == 0)
            {
                return new ModInstallResult(
                    Success: false,
                    Message: "File zip tidak memiliki 'manifest.json' SMAPI yang valid. Pastikan arsip berisi mod Stardew Valley resmi.",
                    InstalledMods: new List<InstalledModInfo>(),
                    Warnings: warnings
                );
            }

            var installedList = new List<InstalledModInfo>();

            // Validasi & instal setiap modul mod yang ditemukan dalam zip
            foreach (var manifestPath in manifests)
            {
                var modFolder = Path.GetDirectoryName(manifestPath)!;
                var json = await File.ReadAllTextAsync(manifestPath);
                var manifest = JsonSerializer.Deserialize<SmapiManifest>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (manifest == null || string.IsNullOrWhiteSpace(manifest.UniqueID))
                {
                    warnings.Add($"Manifest di '{Path.GetFileName(modFolder)}' tidak memiliki UniqueID yang valid.");
                    continue;
                }

                // Proteksi Keamanan: Sanitasi regex ketat untuk UniqueID
                if (!System.Text.RegularExpressions.Regex.IsMatch(manifest.UniqueID, @"^[A-Za-z0-9_.-]+$"))
                {
                    throw new InvalidOperationException($"UniqueID mod '{manifest.UniqueID}' mengandung karakter tidak aman. Hanya alfanumerik, titik, strip, dan garis bawah yang diizinkan.");
                }

                // Proteksi Anti-Cheat: Cek Blacklist
                string category = ClassifyMod(manifest);
                if (category == "ProhibitedCheat")
                {
                    throw new InvalidOperationException($"Mod '{manifest.Name}' ({manifest.UniqueID}) terdeteksi sebagai CHEAT TERLARANG dan ditolak oleh anticheat server!");
                }

                if (_dbService != null)
                {
                    var policies = await _dbService.GetModPoliciesAsync();
                    var blacklisted = policies.FirstOrDefault(p => p.ModId.Equals(manifest.UniqueID, StringComparison.OrdinalIgnoreCase) && p.PolicyType.Equals("Blacklist", StringComparison.OrdinalIgnoreCase));
                    if (blacklisted != null)
                    {
                        throw new InvalidOperationException($"Mod '{manifest.Name}' ({manifest.UniqueID}) tercantum dalam Blacklist server: {blacklisted.Reason}");
                    }
                }

                // Tentukan nama folder tujuan di direktori mods
                string targetFolderName = Path.GetFileName(modFolder);
                if (targetFolderName.Equals(Path.GetFileName(tempExtractDir), StringComparison.OrdinalIgnoreCase) ||
                    targetFolderName.Contains('/') || targetFolderName.Contains('\\') || targetFolderName.Contains(".."))
                {
                    targetFolderName = manifest.UniqueID;
                }

                string safeModsBase = Path.GetFullPath(_modsDir);
                if (!safeModsBase.EndsWith(Path.DirectorySeparatorChar))
                {
                    safeModsBase += Path.DirectorySeparatorChar;
                }

                string destinationDir = Path.GetFullPath(Path.Combine(_modsDir, targetFolderName));
                if (!destinationDir.StartsWith(safeModsBase, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Target direktori mod '{targetFolderName}' berada di luar folder Mods yang diizinkan.");
                }

                if (Directory.Exists(destinationDir))
                {
                    Directory.Delete(destinationDir, recursive: true);
                }

                // Pindahkan folder mod ke folder mods server
                CopyDirectoryRecursively(modFolder, destinationDir);

                var installedInfo = ParseModDirectory(destinationDir, Path.Combine(destinationDir, "manifest.json"), isEnabled: true);
                if (installedInfo != null)
                {
                    installedList.Add(installedInfo);

                    // Auto-register ke mod_policies jika belum ada
                    if (_dbService != null)
                    {
                        try
                        {
                            var existingPolicies = await _dbService.GetModPoliciesAsync();
                            bool exists = existingPolicies.Any(p => p.ModId.Equals(installedInfo.UniqueId, StringComparison.OrdinalIgnoreCase));
                            if (!exists)
                            {
                                string policyType = category is "ExpansionMap" or "Framework" ? "Required" : "Optional";
                                await _dbService.AddModPolicyAsync(
                                    installedInfo.UniqueId,
                                    installedInfo.Name,
                                    policyType,
                                    $"Auto-registered via mod zip upload ({installedInfo.Version})"
                                );
                            }
                        }
                        catch (Exception ex)
                        {
                            warnings.Add($"Gagal auto-register policy untuk '{installedInfo.UniqueId}': {ex.Message}");
                        }
                    }
                }
            }

            _logger?.Info("ModManager", $"Berhasil menginstal {installedList.Count} mod dari file '{fileName}'.");

            return new ModInstallResult(
                Success: true,
                Message: $"Berhasil menginstal {installedList.Count} mod ke server.",
                InstalledMods: installedList,
                Warnings: warnings
            );
        }
        catch (Exception ex)
        {
            _logger?.Error("ModManager", $"Gagal menginstal zip mod '{fileName}'", null, ex);
            return new ModInstallResult(
                Success: false,
                Message: $"Gagal memproses arsip zip mod: {ex.Message}",
                InstalledMods: new List<InstalledModInfo>(),
                Warnings: warnings
            );
        }
        finally
        {
            // Bersihkan direktori temporary ekstraksi
            try
            {
                if (Directory.Exists(tempExtractDir))
                {
                    Directory.Delete(tempExtractDir, recursive: true);
                }
            }
            catch { }
        }
    }

    /// <summary>
    /// Mengaktifkan atau menonaktifkan mod dengan me-rename folder (SMAPI mengabaikan folder dengan awalan titik '.')
    /// </summary>
    public bool ToggleMod(string uniqueId, bool enable)
    {
        var mods = GetInstalledMods();
        var targetMod = mods.FirstOrDefault(m => m.UniqueId.Equals(uniqueId, StringComparison.OrdinalIgnoreCase));
        if (targetMod == null) return false;

        if (targetMod.IsEnabled == enable) return true; // Sudah sesuai

        string currentDir = targetMod.FolderPath;
        string parentDir = Path.GetDirectoryName(currentDir) ?? _modsDir;
        string currentFolderName = Path.GetFileName(currentDir);

        string newFolderName;
        if (enable)
        {
            // Buang prefix '.' atau suffix '.disabled'
            newFolderName = currentFolderName.TrimStart('.');
            if (newFolderName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
            {
                newFolderName = newFolderName[..^9];
            }
        }
        else
        {
            // Tambahkan prefix '.' agar SMAPI mengabaikannya
            newFolderName = "." + currentFolderName;
        }

        string newDir = Path.Combine(parentDir, newFolderName);
        if (Directory.Exists(newDir))
        {
            Directory.Delete(newDir, recursive: true);
        }

        Directory.Move(currentDir, newDir);
        _logger?.Info("ModManager", $"Status mod '{uniqueId}' diubah menjadi: {(enable ? "Enabled" : "Disabled")}");
        return true;
    }

    /// <summary>
    /// Menghapus folder mod dari storage server.
    /// </summary>
    public bool DeleteMod(string uniqueId)
    {
        var mods = GetInstalledMods();
        var targetMod = mods.FirstOrDefault(m => m.UniqueId.Equals(uniqueId, StringComparison.OrdinalIgnoreCase));
        if (targetMod == null) return false;

        try
        {
            if (Directory.Exists(targetMod.FolderPath))
            {
                Directory.Delete(targetMod.FolderPath, recursive: true);
                _logger?.Info("ModManager", $"Mod '{uniqueId}' ({targetMod.Name}) berhasil dihapus dari direktori mods.");
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger?.Error("ModManager", $"Gagal menghapus folder mod '{targetMod.FolderPath}'", null, ex);
        }

        return false;
    }

    private static void CopyDirectoryRecursively(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            string dest = Path.Combine(targetDir, Path.GetFileName(file));
            File.Copy(file, dest, overwrite: true);
        }

        foreach (var subDir in Directory.GetDirectories(sourceDir))
        {
            string dest = Path.Combine(targetDir, Path.GetFileName(subDir));
            CopyDirectoryRecursively(subDir, dest);
        }
    }
}
