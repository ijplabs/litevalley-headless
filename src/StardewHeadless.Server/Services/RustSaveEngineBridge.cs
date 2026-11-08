using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using StardewHeadless.Protocol.Logging;

namespace StardewHeadless.Server.Services;

public enum RustBridgeResult
{
    Success,
    Error,
    NotAvailable
}

public class RustInGameDate
{
    [JsonPropertyName("season")]
    public string Season { get; set; } = "spring";

    [JsonPropertyName("day")]
    public int Day { get; set; } = 1;

    [JsonPropertyName("year")]
    public int Year { get; set; } = 1;

    [JsonPropertyName("display")]
    public string Display { get; set; } = "Spring 1, Year 1";
}

public class RustFarmerSummary
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("money")]
    public int Money { get; set; }

    [JsonPropertyName("total_money_earned")]
    public long TotalMoneyEarned { get; set; }

    [JsonPropertyName("daily_luck")]
    public double DailyLuck { get; set; }
}

public class RustSaveMetadata
{
    [JsonPropertyName("valid")]
    public bool Valid { get; set; }

    [JsonPropertyName("game_version")]
    public string? GameVersion { get; set; }

    [JsonPropertyName("farm_name")]
    public string FarmName { get; set; } = string.Empty;

    [JsonPropertyName("farm_type")]
    public int FarmType { get; set; }

    [JsonPropertyName("farm_type_name")]
    public string FarmTypeName { get; set; } = "Standard";

    [JsonPropertyName("unique_id")]
    public string UniqueId { get; set; } = string.Empty;

    [JsonPropertyName("in_game_date")]
    public RustInGameDate InGameDate { get; set; } = new();

    [JsonPropertyName("host")]
    public RustFarmerSummary Host { get; set; } = new();

    [JsonPropertyName("cabins_count")]
    public int CabinsCount { get; set; }

    [JsonPropertyName("file_size_bytes")]
    public long FileSizeBytes { get; set; }

    [JsonPropertyName("parse_duration_ms")]
    public double ParseDurationMs { get; set; }

    [JsonPropertyName("source_type")]
    public string SourceType { get; set; } = "unknown";
}

/// <summary>
/// Jembatan interop antara Server Kestrel C# dengan biner Rust native litevalley-save-engine.
/// Menjamin operasi backup, inspeksi, impor savegame, dan auto-provisioning kabin berjalan tanpa beban LOH.
/// </summary>
public static class RustSaveEngineBridge
{
    private static string? _resolvedBinaryPath;
    private static bool _initialized;

    public static string? ResolveBinaryPath(StructuredLogger? logger = null)
    {
        if (_initialized) return _resolvedBinaryPath;
        _initialized = true;

        // 1. Cek Environment Variable
        string? envPath = Environment.GetEnvironmentVariable("LITEVALLEY_SAVE_ENGINE_BIN");
        if (!string.IsNullOrWhiteSpace(envPath) && File.Exists(envPath))
        {
            _resolvedBinaryPath = envPath;
            logger?.Info("RustBridge", $"Biner Rust terdeteksi via ENV: {_resolvedBinaryPath}");
            return _resolvedBinaryPath;
        }

        // 2. Cek path standar Linux container
        if (File.Exists("/usr/local/bin/litevalley-save-engine"))
        {
            _resolvedBinaryPath = "/usr/local/bin/litevalley-save-engine";
            logger?.Info("RustBridge", "Biner Rust terdeteksi di /usr/local/bin/litevalley-save-engine");
            return _resolvedBinaryPath;
        }

        string exeName = OperatingSystem.IsWindows() ? "litevalley-save-engine.exe" : "litevalley-save-engine";

        // 3. Cek direktori aplikasi lokal (Windows / Linux)
        string baseDir = AppContext.BaseDirectory;
        string localPath = Path.Combine(baseDir, exeName);
        if (File.Exists(localPath))
        {
            _resolvedBinaryPath = localPath;
            logger?.Info("RustBridge", $"Biner Rust terdeteksi di direktori runtime: {_resolvedBinaryPath}");
            return _resolvedBinaryPath;
        }

        // 4. Cari repo root secara dinamis dengan menelusuri folder induk
        var searchDir = new DirectoryInfo(baseDir);
        while (searchDir != null && searchDir.Exists)
        {
            string candidate = Path.Combine(searchDir.FullName, "src", "litevalley-save-engine", "target", "release", exeName);
            if (File.Exists(candidate))
            {
                _resolvedBinaryPath = Path.GetFullPath(candidate);
                logger?.Info("RustBridge", $"Biner Rust terdeteksi di workspace dev: {_resolvedBinaryPath}");
                return _resolvedBinaryPath;
            }
            searchDir = searchDir.Parent;
        }

        // 5. Cek CurrentDirectory fallback
        string cwdCandidate = Path.Combine(Directory.GetCurrentDirectory(), "src", "litevalley-save-engine", "target", "release", exeName);
        if (File.Exists(cwdCandidate))
        {
            _resolvedBinaryPath = Path.GetFullPath(cwdCandidate);
            logger?.Info("RustBridge", $"Biner Rust terdeteksi di current directory: {_resolvedBinaryPath}");
            return _resolvedBinaryPath;
        }

        logger?.Warn("RustBridge", "Biner native litevalley-save-engine tidak ditemukan; menggunakan fallback engine C#.");
        return null;
    }

    public static bool IsAvailable(StructuredLogger? logger = null)
    {
        return !string.IsNullOrEmpty(ResolveBinaryPath(logger));
    }

    /// <summary>
    /// Menginspeksi file savegame atau zip secara instan tanpa alokasi DOM XML.
    /// </summary>
    public static async Task<RustSaveMetadata?> InspectSaveAsync(string path, StructuredLogger? logger = null)
    {
        string? binPath = ResolveBinaryPath(logger);
        if (string.IsNullOrEmpty(binPath))
        {
            return null; // Fallback ke C#
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = binPath,
                Arguments = $"inspect \"{path}\" --json",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc == null) return null;

            string jsonOutput = await proc.StandardOutput.ReadToEndAsync();
            await proc.WaitForExitAsync();

            if (proc.ExitCode == 0 && !string.IsNullOrWhiteSpace(jsonOutput))
            {
                return JsonSerializer.Deserialize<RustSaveMetadata>(jsonOutput);
            }
        }
        catch (Exception ex)
        {
            logger?.Warn("RustBridge", $"Gagal memanggil inspect Rust: {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// Mengekspor folder savegame ke file .zip streaming via Rust (Zero-LOH).
    /// </summary>
    public static async Task<bool> ExportZipAsync(string sourceDir, string outputZipPath, StructuredLogger? logger = null)
    {
        string? binPath = ResolveBinaryPath(logger);
        if (string.IsNullOrEmpty(binPath))
        {
            return false; // Fallback ke ZipFile .NET
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = binPath,
                Arguments = $"export \"{sourceDir}\" -o \"{outputZipPath}\" --json",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc == null) return false;

            await proc.WaitForExitAsync();
            return proc.ExitCode == 0;
        }
        catch (Exception ex)
        {
            logger?.Warn("RustBridge", $"Gagal mengekspor via Rust: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Mengimpor arsip savegame .zip secara aman dengan proteksi ZipSlip via Rust.
    /// Membedakan antara: Success, Error (ditolak oleh Rust), dan NotAvailable (biner belum terpasang).
    /// </summary>
    public static async Task<RustBridgeResult> ImportZipAsync(string zipPath, string targetDir, StructuredLogger? logger = null)
    {
        string? binPath = ResolveBinaryPath(logger);
        if (string.IsNullOrEmpty(binPath))
        {
            return RustBridgeResult.NotAvailable;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = binPath,
                Arguments = $"import \"{zipPath}\" -t \"{targetDir}\" --json",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc == null) return RustBridgeResult.NotAvailable;

            await proc.WaitForExitAsync();
            return proc.ExitCode == 0 ? RustBridgeResult.Success : RustBridgeResult.Error;
        }
        catch (Exception ex)
        {
            logger?.Warn("RustBridge", $"Gagal mengimpor via Rust: {ex.Message}");
            return RustBridgeResult.Error;
        }
    }

    /// <summary>
    /// Menjamin bahwa savegame memiliki minimal targetCabins kabin (Auto-Provisioning Kabin Coop).
    /// </summary>
    public static async Task<bool> EnsureCabinsAsync(string path, int targetCabins, StructuredLogger? logger = null)
    {
        string? binPath = ResolveBinaryPath(logger);
        if (string.IsNullOrEmpty(binPath))
        {
            return false;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = binPath,
                Arguments = $"add-cabins \"{path}\" --count {targetCabins} --json",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc == null) return false;

            await proc.WaitForExitAsync();
            return proc.ExitCode == 0;
        }
        catch (Exception ex)
        {
            logger?.Warn("RustBridge", $"Gagal menambahkan kabin via Rust: {ex.Message}");
            return false;
        }
    }
}
