using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using StardewHeadless.Protocol.Logging;
using StardewHeadless.Server.Database;
using StardewHeadless.Server.World.Knowledge;
using StardewValley;

namespace StardewHeadless.Server.Security.AntiCheat;

public enum AntiCheatPolicyMode
{
    MonitorOnly = 0,
    EnforceSoft = 1,
    EnforceStrict = 2
}

public class PlayerAntiCheatState
{
    public long FarmerId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string? Hwid { get; set; }
    public int StrikeCount { get; set; } = 0;
    public Vector2 LastValidPosition { get; set; }
    public string LastValidLocation { get; set; } = "Farm";
    public DateTime LastPositionTimeUtc { get; set; } = DateTime.UtcNow;
    public int ConsecutiveSpeedViolations { get; set; } = 0;

    // Fishing tracking
    public DateTime? CastStartTimeUtc { get; set; }
    public int RollingCatchCount { get; set; } = 0;
    public int RollingGoldenChestCount { get; set; } = 0;

    // Combat & Vital tracking
    public int ConsecutiveZeroDamageHits { get; set; } = 0;
    public DateTime LastHitTimeUtc { get; set; } = DateTime.MinValue;
    public int ConsecutiveToolSwings { get; set; } = 0;
    public float LastStamina { get; set; } = -1f;

    // Economy tracking
    public int LastRecordedMoney { get; set; } = -1;
    public DateTime LastMoneyCheckUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Koordinator Sentral Sistem Pertahanan Anti-Cheat Berlapis (Lapis 2 - Server Authoritative).
/// Menangani pelaporan insiden, evaluasi metrik pelanggaran, matriks eskalasi hukuman
/// (Rubberband -> Rollback/Kick -> Auto-Ban HWID), serta integrasi database forensik.
/// </summary>
public static class AntiCheatCoordinator
{
    private static StructuredLogger? _logger;
    private static IDatabaseService? _dbService;
    private static readonly ConcurrentDictionary<long, PlayerAntiCheatState> PlayerStates = new();
    private static readonly ConcurrentQueue<AntiCheatIncident> RecentIncidents = new();

    public static AntiCheatPolicyMode PolicyMode { get; set; } = AntiCheatPolicyMode.EnforceSoft;
    public static int MaxEquippedRings { get; set; } = 4;
    public static bool IsShadowAuditEnabled { get; set; } = false;

    public static void Initialize(StructuredLogger? logger, IDatabaseService? dbService)
    {
        _logger = logger;
        _dbService = dbService;
    }

    public static PlayerAntiCheatState GetOrCreatePlayerState(long farmerId, string username = "Unknown", string? hwid = null)
    {
        return PlayerStates.GetOrAdd(farmerId, id => new PlayerAntiCheatState
        {
            FarmerId = id,
            Username = username,
            Hwid = hwid,
            LastPositionTimeUtc = DateTime.UtcNow
        });
    }

    public static void RemovePlayerState(long farmerId)
    {
        PlayerStates.TryRemove(farmerId, out _);
    }

    /// <summary>
    /// Mencatat pelanggaran anti-cheat dan menerapkan matriks eskalasi penindakan otomatis.
    /// Level 1: Anomali Ringan / Jitter -> Koreksi State / Rubberband (Tanpa Kick)
    /// Level 2: Eksploitasi Aktif -> Rollback / Pembatalan + Kick dari Sesi
    /// Level 3: Cheat Berat / Berulang -> Auto-Ban Akun + HWID Ban + Sita Harta Curang
    /// </summary>
    public static async Task<string> TriggerViolationAsync(
        long farmerId,
        string username,
        string? hwid,
        string incidentType,
        int severityLevel,
        string details)
    {
        var state = GetOrCreatePlayerState(farmerId, username, hwid);
        state.StrikeCount++;

        string actionTaken = "Logged";

        if (PolicyMode == AntiCheatPolicyMode.MonitorOnly)
        {
            actionTaken = "MonitorOnly_Logged";
        }
        else if (severityLevel == 1)
        {
            actionTaken = "StateCorrected_Rubberband";
        }
        else if (severityLevel == 2)
        {
            actionTaken = state.StrikeCount >= 3 ? "AutoBan_RepeatedViolations" : "KickSession";
        }
        else if (severityLevel >= 3)
        {
            actionTaken = "AutoBan_SevereExploit";
        }

        var incident = new AntiCheatIncident
        {
            FarmerId = farmerId,
            Username = username,
            Hwid = hwid,
            IncidentType = incidentType,
            SeverityLevel = severityLevel,
            ActionTaken = actionTaken,
            Details = details,
            CreatedAt = DateTime.UtcNow.ToString("o")
        };

        RecentIncidents.Enqueue(incident);
        while (RecentIncidents.Count > 200)
        {
            RecentIncidents.TryDequeue(out _);
        }

        _logger?.Warn("AntiCheat", $"[STRIKE #{state.StrikeCount}] {username} (FarmerId: {farmerId}) terdeteksi {incidentType} [Level {severityLevel}]: {details}. Tindakan: {actionTaken}");

        // Simpan catatan forensik ke SQLite
        if (_dbService != null)
        {
            try
            {
                await _dbService.RecordAntiCheatIncidentAsync(farmerId, username, hwid, incidentType, severityLevel, actionTaken, details);

                if (actionTaken.StartsWith("AutoBan"))
                {
                    await _dbService.BanUserAsync(farmerId, $"Anti-Cheat Violation: {incidentType} ({details})", banIp: true, banHwid: !string.IsNullOrWhiteSpace(hwid), bannedBy: "ServerAntiCheatWatchdog");
                    if (!string.IsNullOrWhiteSpace(hwid))
                    {
                        await _dbService.BanHwidAsync(hwid, username, $"Anti-Cheat Violation: {incidentType} ({details})");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.Warn("AntiCheat", $"Gagal menyimpan insiden ke SQLite: {ex.Message}");
            }
        }

        // Siarkan event delta agar Web Dashboard & Sysadmin menerima alarm seketika
        WorldDeltaBroadcaster.Broadcast("anticheat:alert", "Farm", $"farmer_{farmerId}", (int)state.LastValidPosition.X / 64, (int)state.LastValidPosition.Y / 64, "alert:cheat", new
        {
            incidentType,
            severityLevel,
            actionTaken,
            username,
            details
        });

        // Eksekusi penindakan game
        if (actionTaken.Contains("Kick") || actionTaken.Contains("AutoBan"))
        {
            try
            {
                Game1.server?.kick(farmerId);
            }
            catch
            {
                // Mengabaikan pengecualian jika dalam lingkungan mock testing
            }
        }

        return actionTaken;
    }

    public static IEnumerable<AntiCheatIncident> GetRecentIncidents(int limit = 50)
    {
        return RecentIncidents.TakeLast(limit).Reverse();
    }
}
