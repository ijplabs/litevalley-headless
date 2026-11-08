using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using StardewHeadless.Protocol.Logging;

namespace StardewHeadless.Server.World.Knowledge;

/// <summary>
/// Klien IPC TCP asinkron yang mengalirkan event delta dunia ke microservice Rust litevalley-broadcaster.
/// Menjamin Game Loop C# tidak pernah terblokir (Non-Blocking Channel) dan menangani auto-reconnect otomatis
/// serta sinkronisasi subscriber count dua arah dari Rust ke C#.
/// </summary>
public static class RustBroadcasterClient
{
    private static StructuredLogger? _logger;
    private static string _host = "127.0.0.1";
    private static int _port = 4040;
    private static int _webPort = 4041;
    private static System.Diagnostics.Process? _broadcasterProcess;
    private static readonly Channel<string> Outbox = Channel.CreateBounded<string>(new BoundedChannelOptions(2000)
    {
        FullMode = BoundedChannelFullMode.DropOldest
    });

    private static volatile bool _isConnected;
    private static volatile int _activeSubscribers;
    private static CancellationTokenSource? _cts;
    private static Task? _workerTask;

    public static bool IsConnected => _isConnected;
    public static int ActiveSubscribers => _activeSubscribers;

    public static void Initialize(StructuredLogger? logger, string? host = null, int? port = null)
    {
        _logger = logger;
        if (!string.IsNullOrWhiteSpace(host)) _host = host.Trim();
        if (port.HasValue && port.Value > 0) _port = port.Value;

        // Baca dari env jika disetel
        if (int.TryParse(Environment.GetEnvironmentVariable("BROADCASTER_INGEST_PORT"), out int p))
        {
            _port = p;
        }
        if (int.TryParse(Environment.GetEnvironmentVariable("BROADCASTER_WEB_PORT"), out int wp))
        {
            _webPort = wp;
        }

        EnsureBroadcasterProcessRunning();

        if (_workerTask == null)
        {
            _cts = new CancellationTokenSource();
            _workerTask = Task.Run(() => WorkerLoopAsync(_cts.Token));
        }
    }

    private static void EnsureBroadcasterProcessRunning()
    {
        if (_broadcasterProcess != null && !_broadcasterProcess.HasExited)
        {
            return;
        }

        try
        {
            string? binPath = ResolveBinaryPath();
            if (string.IsNullOrEmpty(binPath) || !File.Exists(binPath))
            {
                return;
            }

            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = binPath,
                Arguments = $"--ingest-port {_port} --web-port {_webPort}",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = false,
                RedirectStandardError = false
            };

            _broadcasterProcess = System.Diagnostics.Process.Start(startInfo);
            if (_broadcasterProcess != null)
            {
                _logger?.Info("BroadcasterIPC", $"Berhasil meluncurkan sidecar Rust litevalley-broadcaster (PID {_broadcasterProcess.Id}) di Ingest Port {_port} & Web Port {_webPort}");
                AppDomain.CurrentDomain.ProcessExit += (_, _) =>
                {
                    try
                    {
                        if (_broadcasterProcess != null && !_broadcasterProcess.HasExited)
                        {
                            _broadcasterProcess.Kill(true);
                        }
                    }
                    catch { }
                };
            }
        }
        catch (Exception ex)
        {
            _logger?.Debug("BroadcasterIPC", $"Tidak dapat meluncurkan binary litevalley-broadcaster secara otomatis: {ex.Message}");
        }
    }

    private static string? ResolveBinaryPath()
    {
        // 1. Cek ENV
        var envBin = Environment.GetEnvironmentVariable("LITEVALLEY_BROADCASTER_BIN");
        if (!string.IsNullOrEmpty(envBin) && File.Exists(envBin)) return envBin;

        // 2. Cek /usr/local/bin di Linux container
        if (File.Exists("/usr/local/bin/litevalley-broadcaster")) return "/usr/local/bin/litevalley-broadcaster";

        // 3. Cek AppDomain BaseDirectory
        string exeName = OperatingSystem.IsWindows() ? "litevalley-broadcaster.exe" : "litevalley-broadcaster";
        string localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, exeName);
        if (File.Exists(localPath)) return localPath;

        // 4. Cek folder rilis repo dev
        string devRelPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "../../../../src/litevalley-broadcaster/target/release", exeName));
        if (File.Exists(devRelPath)) return devRelPath;

        string devDebugPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "../../../../src/litevalley-broadcaster/target/debug", exeName));
        if (File.Exists(devDebugPath)) return devDebugPath;

        return null;
    }

    public static bool TryEnqueue(WorldEntityDeltaEvent delta)
    {
        if (!_isConnected) return false;

        try
        {
            var compact = new
            {
                t = delta.EventType,
                loc = delta.LocationName,
                id = delta.EntityId,
                x = delta.TileX,
                y = delta.TileY,
                sp = delta.SpriteKey,
                d = delta.ExtraData,
                ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            string jsonLine = JsonSerializer.Serialize(compact) + "\n";
            return Outbox.Writer.TryWrite(jsonLine);
        }
        catch
        {
            return false;
        }
    }

    private static async Task WorkerLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient? client = null;
            NetworkStream? stream = null;
            StreamWriter? writer = null;

            try
            {
                EnsureBroadcasterProcessRunning();
                client = new TcpClient();
                client.NoDelay = true;

                // Coba sambungkan dengan timeout 2 detik
                var connectTask = client.ConnectAsync(_host, _port);
                var completed = await Task.WhenAny(connectTask, Task.Delay(2000, ct));

                if (completed != connectTask || !client.Connected)
                {
                    _isConnected = false;
                    _activeSubscribers = 0;
                    client.Dispose();
                    await Task.Delay(3000, ct);
                    continue;
                }

                stream = client.GetStream();
                writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
                _isConnected = true;
                _logger?.Info("BroadcasterIPC", $"Tersambung ke Rust litevalley-broadcaster di {_host}:{_port}");

                // Baca respons stream dari Rust untuk melacak active subscriber count
                _ = Task.Run(async () =>
                {
                    try
                    {
                        using var reader = new StreamReader(stream, Encoding.UTF8, false, 1024, leaveOpen: true);
                        while (!ct.IsCancellationRequested && client.Connected)
                        {
                            string? line = await reader.ReadLineAsync();
                            if (line == null) break;
                            if (line.StartsWith("SUBS:", StringComparison.OrdinalIgnoreCase))
                            {
                                if (int.TryParse(line.Substring(5).Trim(), out int count))
                                {
                                    _activeSubscribers = count;
                                }
                            }
                        }
                    }
                    catch { }
                }, ct);

                while (!ct.IsCancellationRequested && client.Connected)
                {
                    // Tunggu ketersediaan pesan di Outbox atau timeout heartbeat 3 detik (tanpa mengorbankan ReadAsync)
                    using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    waitCts.CancelAfter(3000);

                    bool hasData = false;
                    try
                    {
                        hasData = await Outbox.Reader.WaitToReadAsync(waitCts.Token);
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    {
                        hasData = false;
                    }

                    if (hasData)
                    {
                        while (Outbox.Reader.TryRead(out string? line))
                        {
                            await writer.WriteAsync(line);
                        }
                    }
                    else
                    {
                        // Heartbeat ping agar koneksi TCP tetap hidup dan sinkronisasi subscriber count
                        await writer.WriteAsync("PING\n");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _isConnected = false;
                _activeSubscribers = 0;
                _logger?.Debug("BroadcasterIPC", $"Koneksi ke Rust broadcaster terputus: {ex.Message}. Mencoba reconnect...");
            }
            finally
            {
                _isConnected = false;
                _activeSubscribers = 0;
                writer?.Dispose();
                stream?.Dispose();
                client?.Dispose();
            }

            if (!ct.IsCancellationRequested)
            {
                await Task.Delay(3000, ct);
            }
        }
    }

    public static void Shutdown()
    {
        _cts?.Cancel();
    }
}
