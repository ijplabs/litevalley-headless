using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using StardewHeadless.Protocol.Logging;

namespace StardewHeadless.Server.Services;

/// <summary>
/// Klien Pelapor Error & Crash Runtime Generik (CrashReporter).
/// Kompatibel dengan gateway webhook, Sentry-compatible endpoints, dan APM capture.
/// 
/// Privasi & Self-Host Isolation:
/// Otomatis 100% NONAKTIF dan senyap tanpa log apapun jika token/DSN tidak disetel.
/// </summary>
public static class CrashReporter
{
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(3) };
    private static string _endpoint = "https://gateway-monitor.ijplabs.dev/api/v1/capture";
    private static string _project = "litevalley-server";
    private static string? _token;
    private static string _environment = "production";
    private static string? _instanceId;
    private static string? _subdomain;
    private static bool _isInitialized;

    public static bool IsEnabled => !string.IsNullOrWhiteSpace(_token);

    public static void Initialize(StructuredLogger? logger = null)
    {
        if (_isInitialized) return;
        _isInitialized = true;

        // 1. Cek DSN terlebih dahulu jika tersedia (Format: https://<token>@<host>/<path>?project=<project>)
        var dsn = Environment.GetEnvironmentVariable("CRASH_REPORT_DSN") 
                  ?? Environment.GetEnvironmentVariable("ERROR_REPORT_DSN");

        if (!string.IsNullOrWhiteSpace(dsn) && Uri.TryCreate(dsn, UriKind.Absolute, out var uri))
        {
            if (!string.IsNullOrWhiteSpace(uri.UserInfo))
            {
                _token = Uri.UnescapeDataString(uri.UserInfo);
            }
            _endpoint = $"{uri.Scheme}://{uri.Host}{(uri.IsDefaultPort ? "" : $":{uri.Port}")}{uri.AbsolutePath.TrimEnd('/')}";

            if (!string.IsNullOrWhiteSpace(uri.Query))
            {
                var q = uri.Query.TrimStart('?');
                foreach (var pair in q.Split('&', StringSplitOptions.RemoveEmptyEntries))
                {
                    var parts = pair.Split('=', 2);
                    if (parts.Length == 2 && parts[0].Equals("project", StringComparison.OrdinalIgnoreCase))
                    {
                        _project = Uri.UnescapeDataString(parts[1]);
                    }
                }
            }
        }

        // 2. Fallback ke konfigurasi individual (Mendukung kredensial khusus Node Server)
        _token ??= Environment.GetEnvironmentVariable("CRASH_REPORT_NODE_TOKEN")
                   ?? Environment.GetEnvironmentVariable("CRASH_REPORT_SERVER_TOKEN")
                   ?? Environment.GetEnvironmentVariable("CRASH_REPORT_TOKEN")
                   ?? Environment.GetEnvironmentVariable("ERROR_TRACKER_TOKEN")
                   ?? Environment.GetEnvironmentVariable("TELEMETRY_TOKEN");

        // 2b. Fallback untuk Existing Containers: Baca file global jika ENV belum disetel
        // Semua kontainer (lama maupun baru) me-mount shared game dir ke /data/game:ro
        if (string.IsNullOrWhiteSpace(_token))
        {
            var gameDir = Environment.GetEnvironmentVariable("STARDEW_GAME_DIR") ?? "/data/game";
            var configDir = Environment.GetEnvironmentVariable("STARDEW_CONFIG_DIR") ?? "/data/config";
            string[] candidateFiles = 
            {
                System.IO.Path.Combine(configDir, "crash_reporter.json"),
                System.IO.Path.Combine(gameDir, "crash_reporter.json")
            };

            foreach (var path in candidateFiles)
            {
                if (System.IO.File.Exists(path))
                {
                    try
                    {
                        var json = System.IO.File.ReadAllText(path);
                        using var doc = JsonDocument.Parse(json);
                        var root = doc.RootElement;
                        if (root.TryGetProperty("token", out var tProp)) _token = tProp.GetString();
                        if (root.TryGetProperty("endpoint", out var eProp)) _endpoint = eProp.GetString()?.TrimEnd('/') ?? _endpoint;
                        if (root.TryGetProperty("project", out var pProp)) _project = pProp.GetString() ?? _project;
                        if (root.TryGetProperty("environment", out var envProp)) _environment = envProp.GetString() ?? _environment;
                        if (!string.IsNullOrWhiteSpace(_token)) break;
                    }
                    catch
                    {
                        // Abaikan jika berkas tidak valid
                    }
                }
            }
        }

        // Jika token kosong (seperti pada deployment Self-Host), 100% senyap tanpa log apapun
        if (!IsEnabled)
        {
            return;
        }

        var customEndpoint = Environment.GetEnvironmentVariable("CRASH_REPORT_NODE_ENDPOINT")
                             ?? Environment.GetEnvironmentVariable("CRASH_REPORT_SERVER_ENDPOINT")
                             ?? Environment.GetEnvironmentVariable("CRASH_REPORT_ENDPOINT")
                             ?? Environment.GetEnvironmentVariable("ERROR_TRACKER_ENDPOINT")
                             ?? Environment.GetEnvironmentVariable("TELEMETRY_ENDPOINT");
        if (!string.IsNullOrWhiteSpace(customEndpoint))
        {
            _endpoint = customEndpoint.TrimEnd('/');
        }

        _project = Environment.GetEnvironmentVariable("CRASH_REPORT_NODE_PROJECT")
                   ?? Environment.GetEnvironmentVariable("CRASH_REPORT_SERVER_PROJECT")
                   ?? Environment.GetEnvironmentVariable("CRASH_REPORT_PROJECT")
                   ?? Environment.GetEnvironmentVariable("ERROR_TRACKER_PROJECT")
                   ?? Environment.GetEnvironmentVariable("TELEMETRY_PROJECT")
                   ?? _project;

        _environment = Environment.GetEnvironmentVariable("CRASH_REPORT_ENVIRONMENT")
                       ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") 
                       ?? Environment.GetEnvironmentVariable("NODE_ENV") 
                       ?? "production";

        _instanceId = Environment.GetEnvironmentVariable("CRASH_REPORT_INSTANCE_ID")
                      ?? Environment.GetEnvironmentVariable("TELEMETRY_INSTANCE_ID")
                      ?? Environment.GetEnvironmentVariable("SERVER_NAME")
                      ?? Environment.MachineName;

        _subdomain = Environment.GetEnvironmentVariable("CRASH_REPORT_SUBDOMAIN")
                     ?? Environment.GetEnvironmentVariable("TELEMETRY_SUBDOMAIN")
                     ?? Environment.GetEnvironmentVariable("FARM_NAME")
                     ?? "node";

        // Log generik & profesional tanpa menyebut nama watcher atau endpoint rahasia
        logger?.Info("CrashReporter", $"Diagnostic crash reporting enabled ({_environment}).");

        // 3. Pasang hook global AppDomain Unhandled Exception
        AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                CaptureException(ex, "AppDomain", "fatal");
            }
        };

        // 4. Pasang hook unobserved task exception
        TaskScheduler.UnobservedTaskException += (sender, args) =>
        {
            if (args.Exception != null)
            {
                CaptureException(args.Exception, "TaskScheduler", "error");
            }
        };

        // 5. Sambungkan ke StructuredLogger
        StructuredLogger.OnErrorCaptured = (level, source, category, message, exception) =>
        {
            string lvl = level == LogLevel.Fatal ? "fatal" : "error";
            if (exception != null)
            {
                CaptureException(exception, category, lvl, source);
            }
            else
            {
                CaptureMessage($"[{source}::{category}] {message}", category, lvl, source);
            }
        };
    }

    /// <summary>
    /// Menangkap Exception dan mengirimkannya ke endpoint Crash Report.
    /// </summary>
    public static void CaptureException(Exception ex, string? category = null, string level = "error", string? source = null)
    {
        if (!IsEnabled) return;

        var payload = new
        {
            project = _project,
            environment = _environment,
            level = level,
            message = $"{ex.GetType().Name}: {ex.Message}",
            error_name = ex.GetType().Name,
            stacktrace = ex.ToString(),
            tags = new
            {
                runtime = "dotnet8",
                source = source ?? "StardewHeadless.Server",
                category = category ?? "Server",
                instance_id = _instanceId,
                subdomain = _subdomain,
                os = Environment.OSVersion.ToString()
            }
        };

        SendPayloadFireAndForget(payload);
    }

    /// <summary>
    /// Menangkap pesan peringatan atau audit fatal.
    /// </summary>
    public static void CaptureMessage(string message, string? category = null, string level = "info", string? source = null)
    {
        if (!IsEnabled) return;

        var payload = new
        {
            project = _project,
            environment = _environment,
            level = level,
            message = message,
            error_name = "Message",
            tags = new
            {
                runtime = "dotnet8",
                source = source ?? "StardewHeadless.Server",
                category = category ?? "Server",
                instance_id = _instanceId,
                subdomain = _subdomain,
                os = Environment.OSVersion.ToString()
            }
        };

        SendPayloadFireAndForget(payload);
    }

    private static void SendPayloadFireAndForget(object payload)
    {
        ThreadPool.QueueUserWorkItem(async _ =>
        {
            try
            {
                string json = JsonSerializer.Serialize(payload);
                string url = $"{_endpoint}?token={Uri.EscapeDataString(_token!)}";

                using var request = new HttpRequestMessage(HttpMethod.Post, url);
                request.Headers.Add("Authorization", $"Bearer {_token}");
                request.Headers.Add("x-token", _token);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");

                using var response = await HttpClient.SendAsync(request).ConfigureAwait(false);
            }
            catch
            {
                // Silent failure: jangan pernah menginterupsi game loop jika koneksi gagal
            }
        });
    }
}
