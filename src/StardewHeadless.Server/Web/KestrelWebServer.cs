using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StardewHeadless.Protocol.Logging;
using StardewHeadless.Server.Config;
using StardewHeadless.Server.Database;
using StardewHeadless.Server.Services.Email;
using StardewHeadless.Server.World;
using StardewValley;
using StardewValley.Network;

namespace StardewHeadless.Server.Web;

public static class KestrelWebServer
{
    private static WebApplication? _app;
    private static StructuredLogger? _logger;
    private static IDatabaseService? _dbService;
    private static IEmailService? _emailService;
    private static ServerConfig? _config;
    private static readonly Stopwatch _uptimeStopwatch = Stopwatch.StartNew();

    public static void SetEmailService(IEmailService emailService)
    {
        _emailService = emailService;
    }

    public static async Task StartAsync(ServerConfig config, IDatabaseService? dbService, StructuredLogger? logger, CancellationToken cancellationToken = default)
    {
        _config = config;
        _dbService = dbService;
        _logger = logger;
        _emailService ??= new ResendEmailService(_logger);

        try
        {
            var wwwrootDir = Path.Combine(AppContext.BaseDirectory, "wwwroot");
            if (!Directory.Exists(wwwrootDir))
            {
                Directory.CreateDirectory(wwwrootDir);
            }

            var indexHtmlPath = Path.Combine(wwwrootDir, "index.html");
            if (!File.Exists(indexHtmlPath))
            {
                File.WriteAllText(indexHtmlPath, GenerateDefaultSpaHtml());
            }

            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                WebRootPath = wwwrootDir,
                Args = Array.Empty<string>()
            });

            builder.WebHost.ConfigureKestrel(options =>
            {
                options.ListenAnyIP(config.WebPort);
            });

            builder.Logging.ClearProviders();

            builder.Services.AddCors(options =>
            {
                options.AddDefaultPolicy(policy =>
                {
                    policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
                });
            });

            // Konfigurasi Reverse Proxy & Domain Forwarding (Traefik, Coolify, Nginx, Caddy)
            builder.Services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders = ForwardedHeaders.All;
                options.KnownNetworks.Clear();
                options.KnownProxies.Clear();
            });

            _app = builder.Build();

            // Terapkan Forwarded Headers sebelum middleware routing & auth
            _app.UseForwardedHeaders();

            _app.UseCors();
            _app.UseDefaultFiles();
            _app.UseStaticFiles();

            RegisterApiEndpoints(_app);

            // TanStack Router SPA fallback: jika route bukan file statis atau /api, kembalikan index.html
            _app.MapFallbackToFile("index.html");

            await _app.StartAsync(cancellationToken);
            _logger?.Info("Kestrel", $"Embedded Kestrel Web Server & API aktif pada port {config.WebPort} (http://localhost:{config.WebPort})");
        }
        catch (Exception ex)
        {
            _logger?.Error("Kestrel", "Gagal memulai Embedded Kestrel Web Server", null, ex);
        }
    }

    public static async Task StopAsync()
    {
        if (_app != null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
            _app = null;
        }
    }

    private static void RegisterApiEndpoints(WebApplication app)
    {
        // 1. Health Endpoint
        app.MapGet("/api/health", (HttpContext ctx) => Results.Ok(new
        {
            status = "online",
            serverVersion = "1.6.15.24356",
            uptimeSeconds = (int)_uptimeStopwatch.Elapsed.TotalSeconds,
            timestamp = DateTime.UtcNow,
            host = ctx.Request.Host.Value,
            scheme = ctx.Request.Scheme,
            clientIp = ctx.Connection.RemoteIpAddress?.ToString()
        }));

        // 2. Live Telemetry
        app.MapGet("/api/telemetry", () =>
        {
            double memoryMb = Process.GetCurrentProcess().WorkingSet64 / (1024.0 * 1024.0);
            int onlineCount = Game1.server?.connectionsCount ?? 0;
            bool isPaused = Game1.netWorldState?.Value.IsPaused ?? false;

            string formattedTime = "06:00 AM";
            int timeOfDay = 600;
            if (Game1.timeOfDay > 0)
            {
                timeOfDay = Game1.timeOfDay;
                int hours = timeOfDay / 100;
                int minutes = timeOfDay % 100;
                string ampm = hours >= 12 && hours < 24 ? "PM" : "AM";
                int displayHour = hours % 12;
                if (displayHour == 0) displayHour = 12;
                formattedTime = $"{displayHour:D2}:{minutes:D2} {ampm}";
            }

            return Results.Ok(new
            {
                serverName = _config?.ServerName ?? "Stardew Headless Server",
                farmName = Game1.player?.farmName.Value ?? _config?.FarmName ?? "Farm",
                fps = 60.0,
                uptimeSeconds = (int)_uptimeStopwatch.Elapsed.TotalSeconds,
                memoryMB = Math.Round(memoryMb, 1),
                onlinePlayers = onlineCount,
                maxPlayers = _config?.StartingCabins ?? 4,
                isPaused = isPaused,
                year = Game1.year > 0 ? Game1.year : 1,
                season = Game1.season.ToString().ToLower(),
                dayOfMonth = Game1.dayOfMonth > 0 ? Game1.dayOfMonth : 1,
                timeOfDay = timeOfDay,
                formattedTime = formattedTime,
                weather = Game1.isRaining ? "Rain" : (Game1.isDebrisWeather ? "Windy" : (Game1.isLightning ? "Storm" : "Sunny"))
            });
        });

        // Debug Network Status
        app.MapGet("/api/debug/network", () =>
        {
            var lidgren = (Game1.server as GameServer)?.GetServer<LidgrenServer>();
            return Results.Ok(new
            {
                gameMode = Game1.gameMode,
                multiplayerMode = Game1.multiplayerMode,
                enableServer = Game1.options?.enableServer,
                ipConnectionsEnabled = Game1.options?.ipConnectionsEnabled,
                serverPrivacy = Game1.options?.serverPrivacy.ToString(),
                serverExists = Game1.server != null,
                lidgrenExists = lidgren != null,
                lidgrenPort = lidgren?.server?.Port,
                lidgrenStatus = lidgren?.server?.Status.ToString(),
                connectionsCount = lidgren?.server?.ConnectionsCount ?? 0,
                connections = lidgren?.server?.Connections.Select(c => new {
                    endpoint = c.RemoteEndPoint.ToString(),
                    status = c.Status.ToString()
                })
            });
        });

        // 3. Registered Players & Cabins
        app.MapGet("/api/players", async () =>
        {
            if (_dbService == null)
            {
                return Results.Ok(new List<object>());
            }

            var users = await _dbService.GetAllUsersAsync();
            var result = new List<object>();

            foreach (var user in users)
            {
                var prof = await _dbService.GetProfessionAsync(user.Id);
                var cabin = await _dbService.GetCabinByUserAsync(user.Id);

                // Cek apakah farmer sedang online di server
                bool isOnline = false;
                int currentMoney = 0;
                if (Game1.otherFarmers != null && Game1.otherFarmers.TryGetValue(user.Id, out var farmer))
                {
                    isOnline = true;
                    currentMoney = farmer.Money;
                }

                result.Add(new
                {
                    id = user.Id,
                    username = user.Username,
                    profession = prof?.ProfessionType ?? "Unassigned",
                    cabinIndex = cabin?.CabinIndex ?? -1,
                    cabinName = cabin?.CabinName ?? $"Cabin #{cabin?.CabinIndex ?? 0}",
                    isOnline = isOnline,
                    money = currentMoney,
                    isBanned = user.IsBanned,
                    banReason = user.BanReason,
                    bannedAt = user.BannedAt,
                    isDeleted = user.IsDeleted,
                    deletedAt = user.DeletedAt,
                    lastIp = user.LastIp,
                    lastHwid = user.LastHwid,
                    respecCount = prof?.RespecCount ?? 0,
                    lastRespecAt = prof?.LastRespecAt,
                    createdAt = user.CreatedAt
                });
            }

            return Results.Ok(result);
        });

        // 4. Village Treasury & Ledger
        app.MapGet("/api/treasury", async () =>
        {
            if (_dbService == null)
            {
                return Results.Ok(new { balance = 0, taxRate = DayTransitionManager.VillageExportTaxRate, transactions = new List<object>() });
            }

            int balance = await _dbService.GetTreasuryBalanceAsync();
            var txs = await _dbService.GetRecentTreasuryTransactionsAsync(30);

            return Results.Ok(new
            {
                balance = balance,
                taxRate = DayTransitionManager.VillageExportTaxRate,
                transactions = txs.Select(t => new
                {
                    id = t.Id,
                    userId = t.UserId,
                    transactionType = t.TransactionType,
                    amount = t.Amount,
                    balanceAfter = t.BalanceAfter,
                    notes = t.Notes,
                    createdAt = t.CreatedAt
                })
            });
        });

        // 5. Community Mega-Projects
        app.MapGet("/api/projects", () =>
        {
            var projects = new[]
            {
                new
                {
                    id = "silo_komunal",
                    name = "Pembangunan Silo Komunal",
                    description = "Menyimpan 240 jerami pakan ternak komunal untuk menghadapi musim dingin.",
                    requiredProfession = "Rancher & Miner",
                    targetGold = 10000,
                    currentGold = 7500,
                    targetWood = 100,
                    currentWood = 90,
                    targetStone = 100,
                    currentStone = 100,
                    targetClay = 10,
                    currentClay = 10,
                    isCompleted = false
                },
                new
                {
                    id = "greenhouse_desa",
                    name = "Restorasi Rumah Kaca Desa (Greenhouse)",
                    description = "Memungkinkan budidaya tanaman sepanjang tahun tanpa terpengaruh cuaca.",
                    requiredProfession = "All Professions",
                    targetGold = 50000,
                    currentGold = 24000,
                    targetWood = 500,
                    currentWood = 380,
                    targetStone = 400,
                    currentStone = 400,
                    targetClay = 50,
                    currentClay = 48,
                    isCompleted = false
                },
                new
                {
                    id = "bridge_pantai",
                    name = "Perbaikan Jembatan Pantai (Tide Pool)",
                    description = "Membuka akses ke area karang laut timur yang kaya akan koral dan landak laut.",
                    requiredProfession = "Forager & Fisherman",
                    targetGold = 5000,
                    currentGold = 5000,
                    targetWood = 300,
                    currentWood = 300,
                    targetStone = 0,
                    currentStone = 0,
                    targetClay = 0,
                    currentClay = 0,
                    isCompleted = true
                }
            };

            return Results.Ok(projects);
        });

        // 6. Inter-Profession Bounty Contracts
        app.MapGet("/api/contracts", () =>
        {
            var contracts = new[]
            {
                new
                {
                    id = 1,
                    posterName = "Agus (Farmer)",
                    requestedProfession = "Miner",
                    title = "Butuh 50 Tembaga (Copper Ore) & 20 Batu Bara",
                    description = "Dibutuhkan untuk membuat 5x Quality Sprinkler sebelum tanggal 20.",
                    rewardGold = 1500,
                    status = "Open",
                    createdAt = DateTime.UtcNow.AddHours(-2).ToString("o")
                },
                new
                {
                    id = 2,
                    posterName = "Bambang (Miner)",
                    requestedProfession = "Fisherman",
                    title = "Ransum Makanan Penambah Stamina Gua (10 Ikan Masak)",
                    description = "Pasokan makanan habis di level 60 tambang.",
                    rewardGold = 900,
                    status = "In Progress",
                    createdAt = DateTime.UtcNow.AddHours(-5).ToString("o")
                },
                new
                {
                    id = 3,
                    posterName = "Citra (Rancher)",
                    requestedProfession = "Forager",
                    title = "Butuh 150 Kayu Ek & 20 Hardwood",
                    description = "Bahan baku ekspansi Deluxe Barn untuk peternakan sapi dan kambing.",
                    rewardGold = 2200,
                    status = "Open",
                    createdAt = DateTime.UtcNow.AddHours(-1).ToString("o")
                }
            };

            return Results.Ok(contracts);
        });

        // 7. Auth Login Endpoint
        app.MapPost("/api/auth/login", async (JsonElement body) =>
        {
            string username = body.TryGetProperty("username", out var u) ? u.GetString() ?? "" : "";
            string password = body.TryGetProperty("password", out var p) ? p.GetString() ?? "" : "";

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                return Results.BadRequest(new { error = "Username dan password wajib diisi." });
            }

            // Cek akun Admin/Sysadmin khusus
            string? envAdminUser = Environment.GetEnvironmentVariable("ADMIN_USERNAME");
            if (string.IsNullOrWhiteSpace(envAdminUser))
                envAdminUser = Environment.GetEnvironmentVariable("ADMIN_USER");
            if (string.IsNullOrWhiteSpace(envAdminUser))
                envAdminUser = "admin";

            string? envAdminPassword = Environment.GetEnvironmentVariable("ADMIN_PASSWORD");
            if (string.IsNullOrWhiteSpace(envAdminPassword))
                envAdminPassword = "admin123";

            // 1. Jika password cocok dengan ADMIN_PASSWORD master dari environment:
            // Izinkan username APA SAJA yang dimasukkan pengguna (atau admin/sysadmin/root),
            // pengguna langsung diakui sebagai Admin!
            bool isMasterPassMatch = !string.IsNullOrEmpty(envAdminPassword) && 
                                     !envAdminPassword.Equals("admin123", StringComparison.Ordinal) && 
                                     password == envAdminPassword;

            // 2. Jika menggunakan fallback password default "admin123":
            bool isDefaultAdminMatch = (password == "admin123") && 
                                       (username.Equals("admin", StringComparison.OrdinalIgnoreCase) || 
                                        username.Equals("sysadmin", StringComparison.OrdinalIgnoreCase) || 
                                        username.Equals("root", StringComparison.OrdinalIgnoreCase) ||
                                        username.Equals(envAdminUser, StringComparison.OrdinalIgnoreCase));

            if (isMasterPassMatch || isDefaultAdminMatch)
            {
                return Results.Ok(new
                {
                    success = true,
                    token = Guid.NewGuid().ToString("N"),
                    role = "admin",
                    username = username.ToLowerInvariant(),
                    userId = 0L
                });
            }

            if (_dbService == null)
            {
                return Results.BadRequest(new { error = "Database server tidak aktif." });
            }

            // Cari berdasarkan username atau email
            var user = await _dbService.GetUserAsync(username) ?? await _dbService.GetUserByEmailAsync(username);
            if (user == null)
            {
                return Results.Json(new { error = "Username atau password salah." }, statusCode: 401);
            }

            bool valid = await _dbService.VerifyPasswordAsync(user.Username, password);
            if (!valid)
            {
                return Results.Json(new { error = "Username atau password salah." }, statusCode: 401);
            }

            // Cek status verifikasi email untuk pemain
            if (!user.IsEmailVerified && user.Role.Equals("Player", StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest(new
                {
                    error = "Email Anda belum diverifikasi. Silakan masukkan kode verifikasi yang dikirim ke email Anda.",
                    requiresVerification = true,
                    email = user.Email,
                    username = user.Username
                });
            }

            var prof = await _dbService.GetProfessionAsync(user.Id);

            return Results.Ok(new
            {
                success = true,
                token = Guid.NewGuid().ToString("N"),
                role = user.Role.ToLowerInvariant(),
                username = user.Username,
                email = user.Email,
                userId = user.Id,
                profession = prof?.ProfessionType ?? "Farmer"
            });
        });

        // 7b. Player Registration (Web-First Gateway dengan Verifikasi Email & Resend)
        app.MapPost("/api/auth/register", async (JsonElement body) =>
        {
            if (_dbService == null)
            {
                return Results.BadRequest(new { error = "Database server tidak aktif." });
            }

            string username = body.TryGetProperty("username", out var u) ? u.GetString()?.Trim() ?? "" : "";
            string email = body.TryGetProperty("email", out var em) ? em.GetString()?.Trim().ToLowerInvariant() ?? "" : "";
            string password = body.TryGetProperty("password", out var p) ? p.GetString() ?? "" : "";
            string profession = body.TryGetProperty("profession", out var pr) ? pr.GetString()?.Trim() ?? "farmer" : "farmer";
            string role = body.TryGetProperty("role", out var r) ? r.GetString()?.Trim() ?? "Player" : "Player";

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                return Results.BadRequest(new { error = "Username dan password tidak boleh kosong." });
            }

            bool isLegacyNoEmail = string.IsNullOrWhiteSpace(email);
            if (isLegacyNoEmail)
            {
                email = $"{username.ToLowerInvariant()}@litevalley.local";
            }
            else if (!email.Contains('@') || !email.Contains('.'))
            {
                return Results.BadRequest(new { error = "Alamat email tidak valid. Mohon sertakan alamat email aktif (contoh: nama@gmail.com)." });
            }

            var existingUser = await _dbService.GetUserAsync(username);
            if (existingUser != null)
            {
                return Results.Conflict(new { error = $"Username '{username}' sudah digunakan. Silakan gunakan nama lain." });
            }

            var existingEmail = await _dbService.GetUserByEmailAsync(email);
            if (existingEmail != null && !isLegacyNoEmail)
            {
                return Results.Conflict(new { error = $"Alamat email '{email}' sudah terdaftar. Silakan gunakan email lain atau login." });
            }

            var newUser = await _dbService.RegisterUserWithEmailAsync(username, email, password, role);
            await _dbService.LockProfessionAsync(newUser.Id, profession.ToLowerInvariant());

            // Buat kode verifikasi 6 digit
            string verifyCode = System.Security.Cryptography.RandomNumberGenerator.GetInt32(100000, 1000000).ToString("D6");
            var expiresAt = DateTime.UtcNow.AddMinutes(15);
            await _dbService.SetEmailVerificationCodeAsync(newUser.Id, verifyCode, expiresAt);

            if (isLegacyNoEmail)
            {
                // Auto-verifikasi jika registrasi tanpa email (legacy / automated unit tests)
                await _dbService.VerifyEmailCodeAsync(newUser.Username, verifyCode);
            }
            else
            {
                // Kirim email via Resend
                _emailService ??= new ResendEmailService(_logger);
                await _emailService.SendVerificationEmailAsync(email, username, verifyCode);
            }

            bool isDevMode = string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RESEND_API_KEY"));
            _logger?.Info("WebAuth", $"Pemain baru terdaftar via Web Portal: '{username}' ({email}, {profession.ToUpper()}). Legacy: {isLegacyNoEmail}");

            return Results.Ok(new
            {
                success = true,
                token = Guid.NewGuid().ToString("N"),
                requiresVerification = !isLegacyNoEmail,
                userId = newUser.Id,
                username = newUser.Username,
                email = newUser.Email,
                role = newUser.Role.ToLowerInvariant(),
                profession = profession.ToLowerInvariant(),
                devCode = isDevMode ? verifyCode : null,
                message = isLegacyNoEmail 
                    ? "Registrasi berhasil! Silakan gunakan kode OTP di dashboard untuk masuk ke game."
                    : "Pendaftaran berhasil! Kode verifikasi telah dikirimkan ke email Anda."
            });
        });

        // 7c. Verifikasi Kode Email
        app.MapPost("/api/auth/verify-email", async (JsonElement body) =>
        {
            if (_dbService == null)
            {
                return Results.BadRequest(new { error = "Database server tidak aktif." });
            }

            string identifier = body.TryGetProperty("email", out var em) ? em.GetString()?.Trim() ?? "" : "";
            if (string.IsNullOrWhiteSpace(identifier) && body.TryGetProperty("username", out var u))
            {
                identifier = u.GetString()?.Trim() ?? "";
            }
            string code = body.TryGetProperty("code", out var c) ? c.GetString()?.Trim() ?? "" : "";

            if (string.IsNullOrWhiteSpace(identifier) || string.IsNullOrWhiteSpace(code))
            {
                return Results.BadRequest(new { error = "Email/Username dan kode verifikasi 6-digit wajib diisi." });
            }

            var (verified, verifyMsg) = await _dbService.VerifyEmailCodeAsync(identifier, code);
            if (!verified)
            {
                return Results.BadRequest(new { error = verifyMsg });
            }

            var user = await _dbService.GetUserByEmailAsync(identifier) ?? await _dbService.GetUserAsync(identifier);
            var prof = user != null ? await _dbService.GetProfessionAsync(user.Id) : null;

            return Results.Ok(new
            {
                success = true,
                token = Guid.NewGuid().ToString("N"),
                userId = user?.Id ?? 0L,
                username = user?.Username ?? "",
                email = user?.Email ?? "",
                role = user?.Role.ToLowerInvariant() ?? "player",
                profession = prof?.ProfessionType ?? "farmer",
                message = "Email berhasil diverifikasi! Selamat datang di Peternakan Komunal."
            });
        });

        // 7d. Kirim Ulang Kode Verifikasi
        app.MapPost("/api/auth/resend-verification", async (JsonElement body) =>
        {
            if (_dbService == null)
            {
                return Results.BadRequest(new { error = "Database server tidak aktif." });
            }

            string identifier = body.TryGetProperty("email", out var em) ? em.GetString()?.Trim() ?? "" : "";
            if (string.IsNullOrWhiteSpace(identifier) && body.TryGetProperty("username", out var u))
            {
                identifier = u.GetString()?.Trim() ?? "";
            }

            if (string.IsNullOrWhiteSpace(identifier))
            {
                return Results.BadRequest(new { error = "Email atau username wajib diisi." });
            }

            var user = await _dbService.GetUserByEmailAsync(identifier) ?? await _dbService.GetUserAsync(identifier);
            if (user == null)
            {
                return Results.NotFound(new { error = "Akun tidak ditemukan." });
            }

            if (user.IsEmailVerified)
            {
                return Results.BadRequest(new { error = "Akun ini sudah terverifikasi sebelumnya." });
            }

            string targetEmail = !string.IsNullOrWhiteSpace(user.Email) ? user.Email : identifier;
            string newCode = System.Security.Cryptography.RandomNumberGenerator.GetInt32(100000, 1000000).ToString("D6");
            var expiresAt = DateTime.UtcNow.AddMinutes(15);
            await _dbService.SetEmailVerificationCodeAsync(user.Id, newCode, expiresAt);

            _emailService ??= new ResendEmailService(_logger);
            await _emailService.SendVerificationEmailAsync(targetEmail, user.Username, newCode);

            bool isDevMode = string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RESEND_API_KEY"));
            return Results.Ok(new
            {
                success = true,
                email = targetEmail,
                devCode = isDevMode ? newCode : null,
                message = "Kode verifikasi baru telah dikirimkan ke email Anda."
            });
        });

        // 7c. In-Game Dynamic Rolling OTP Authenticator
        app.MapGet("/api/user/otp", async (HttpContext ctx) =>
        {
            if (_dbService == null)
            {
                return Results.BadRequest(new { error = "Database server tidak aktif." });
            }

            string? username = ctx.Request.Query["username"];
            if (string.IsNullOrWhiteSpace(username))
            {
                username = ctx.Request.Headers["X-Username"].FirstOrDefault();
            }

            if (string.IsNullOrWhiteSpace(username))
            {
                return Results.BadRequest(new { error = "Parameter username wajib disertakan." });
            }

            var user = await _dbService.GetUserAsync(username);
            if (user == null)
            {
                return Results.NotFound(new { error = $"User '{username}' tidak ditemukan." });
            }

            string secret = user.TotpSecret ?? Security.TotpService.GenerateSecret();
            string code = Security.TotpService.GenerateCode(secret);
            int remaining = Security.TotpService.GetRemainingSeconds();

            return Results.Ok(new
            {
                username = user.Username,
                code = code,
                remainingSeconds = remaining,
                intervalSeconds = Security.TotpService.TimeStepSeconds,
                isGameLinked = user.IsGameLinked,
                linkedFarmerName = user.LinkedFarmerName,
                linkedFarmerId = user.LinkedFarmerId
            });
        });

        // 7d. Client Mod Streamer / Official Release Redirect
        app.MapGet("/api/download/client-mod", () =>
        {
            var localZip = Path.Combine(AppContext.BaseDirectory, "Mods", "StardewBridge.zip");
            if (File.Exists(localZip))
            {
                return Results.File(localZip, "application/zip", "StardewBridge.zip");
            }

            // Redirect ke rilis resmi GitHub ijplabs/litevalley-bridge
            return Results.Redirect("https://github.com/ijplabs/litevalley-bridge/releases/latest", permanent: false);
        });

        // 8. Admin In-Game Broadcast
        app.MapPost("/api/admin/broadcast", (JsonElement body) =>
        {
            string message = body.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";
            if (string.IsNullOrWhiteSpace(message))
            {
                return Results.BadRequest(new { error = "Pesan broadcast tidak boleh kosong." });
            }

            try
            {
                if (Game1.Multiplayer != null)
                {
                    Game1.Multiplayer.sendChatMessage(LocalizedContentManager.LanguageCode.en, $"[SERVER]: {message}", Multiplayer.AllPlayers);
                }
                else if (Game1.server != null && Game1.otherFarmers != null && Game1.player != null)
                {
                    foreach (var peerId in Game1.otherFarmers.Keys)
                    {
                        Game1.server.sendMessage(peerId, 10, Game1.player, Multiplayer.AllPlayers, LocalizedContentManager.LanguageCode.en, $"[SERVER]: {message}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.Warn("AdminWeb", $"Gagal mengirim pesan chat in-game: {ex.Message}");
            }

            _logger?.Info("AdminWeb", $"Broadcast in-game dikirim: '{message}'");
            return Results.Ok(new { success = true, broadcastMessage = message });
        });

        // 9. Admin Force Save
        app.MapPost("/api/admin/save", () =>
        {
            try
            {
                SaveGame.Save();
                _logger?.Info("AdminWeb", "Manual Force Save berhasil dieksekusi via Web Admin Portal.");
                return Results.Ok(new { success = true, message = "World save berhasil dieksekusi." });
            }
            catch (Exception ex)
            {
                _logger?.Error("AdminWeb", "Gagal mengeksekusi Force Save", null, ex);
                return Results.StatusCode(500);
            }
        });

        // 10. Admin Create Fresh World
        app.MapPost("/api/admin/world/create", (JsonElement body) =>
        {
            string farmName = body.TryGetProperty("farmName", out var fn) ? fn.GetString() ?? "" : "";
            int farmType = body.TryGetProperty("farmType", out var ft) ? ft.GetInt32() : 0;
            int startingCabins = body.TryGetProperty("startingCabins", out var sc) ? sc.GetInt32() : 4;

            if (string.IsNullOrWhiteSpace(farmName))
            {
                return Results.BadRequest(new { error = "Nama farm tidak boleh kosong." });
            }

            try
            {
                var config = _config ?? new ServerConfig();
                config.FarmName = farmName.Trim();
                config.FarmType = farmType;
                config.StartingCabins = Math.Clamp(startingCabins, 1, 8);

                var saveFolder = HeadlessWorldGenerator.CreateFreshWorld(config, _logger);
                _logger?.Info("AdminWeb", $"Dunia baru '{farmName}' berhasil dibuat via Admin Portal.");
                return Results.Ok(new
                {
                    success = true,
                    farmName = config.FarmName,
                    farmType = config.FarmType,
                    startingCabins = config.StartingCabins,
                    saveFolder = saveFolder,
                    message = $"Dunia baru '{farmName}' berhasil dibuat dan siap dimainkan!"
                });
            }
            catch (Exception ex)
            {
                _logger?.Error("AdminWeb", $"Gagal membuat dunia baru: {ex.Message}", null, ex);
                return Results.Json(new { error = ex.Message }, statusCode: 500);
            }
        });

        // 11. Admin Reset / Archive World
        app.MapPost("/api/admin/world/reset", (JsonElement body) =>
        {
            string confirmFarmName = body.TryGetProperty("confirmFarmName", out var cfn) ? cfn.GetString() ?? "" : "";
            string newFarmName = body.TryGetProperty("newFarmName", out var nfn) ? nfn.GetString() ?? "" : "";
            int? newFarmType = body.TryGetProperty("newFarmType", out var nft) ? nft.GetInt32() : null;
            int? newStartingCabins = body.TryGetProperty("newStartingCabins", out var nsc) ? nsc.GetInt32() : null;

            string currentFarmName = _config?.FarmName ?? Game1.player?.farmName.Value ?? "Farm";

            if (!string.Equals(confirmFarmName.Trim(), currentFarmName.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest(new
                {
                    error = $"Konfirmasi nama farm tidak cocok! Harap ketik '{currentFarmName}' persis sama untuk konfirmasi reset."
                });
            }

            try
            {
                var config = _config ?? new ServerConfig();
                var targetFarmName = string.IsNullOrWhiteSpace(newFarmName) ? currentFarmName : newFarmName.Trim();
                var saveFolder = HeadlessWorldGenerator.ArchiveAndResetWorld(
                    config,
                    targetFarmName,
                    newFarmType,
                    newStartingCabins,
                    _logger);

                _logger?.Info("AdminWeb", $"Dunia '{currentFarmName}' berhasil diarsipkan dan di-reset ke '{targetFarmName}'.");

                return Results.Ok(new
                {
                    success = true,
                    message = $"Dunia lama berhasil diarsipkan dan dunia baru '{targetFarmName}' telah aktif!",
                    farmName = config.FarmName,
                    saveFolder = saveFolder
                });
            }
            catch (Exception ex)
            {
                _logger?.Error("AdminWeb", $"Gagal me-reset dunia: {ex.Message}", null, ex);
                return Results.Json(new { error = ex.Message }, statusCode: 500);
            }
        });

        // 12. Admin Purge / Delete Player & Clean World
        app.MapPost("/api/admin/players/{id:long}/purge", async (long id, JsonElement body) =>
        {
            if (_dbService == null) return Results.BadRequest(new { error = "Database server tidak aktif." });
            string reason = body.TryGetProperty("reason", out var r) ? r.GetString() ?? "Manual SysAdmin Purge" : "Manual SysAdmin Purge";

            var user = await _dbService.GetUserByIdAsync(id);
            if (user == null) return Results.NotFound(new { error = "Pemain tidak ditemukan." });

            int walletGold = 0;
            if (Game1.otherFarmers != null && Game1.otherFarmers.TryGetValue(id, out var farmer))
            {
                walletGold = farmer.Money;
            }

            int forfeitedGold = await _dbService.PurgePlayerAsync(id, reason, walletGold);
            var cleanResult = WorldCleanupService.CleanWorldForDeletedPlayer(id, _logger);

            _logger?.Info("AdminWeb", $"Akun pemain '{user.Username}' (ID: {id}) dibersihkan! Sitaan Uang: {forfeitedGold}g ke Kas Desa. Kabin dibebaskan: {cleanResult.CabinsFreed}. Tanaman publik: {cleanResult.CropsReleased}.");

            return Results.Ok(new
            {
                success = true,
                message = $"Pemain '{user.Username}' berhasil dihapus dan dibersihkan dari dunia.",
                forfeitedGold = forfeitedGold,
                cleanResult = cleanResult
            });
        });

        // 13. Admin Change Profession (Admin Override)
        app.MapPost("/api/admin/players/{id:long}/change-profession", async (long id, JsonElement body) =>
        {
            if (_dbService == null) return Results.BadRequest(new { error = "Database server tidak aktif." });
            string newProf = body.TryGetProperty("profession", out var p) ? p.GetString() ?? "" : "";
            if (string.IsNullOrWhiteSpace(newProf))
            {
                return Results.BadRequest(new { error = "Nama profesi baru tidak boleh kosong." });
            }

            var result = await _dbService.ChangeProfessionAsync(id, newProf, isAdminOverride: true);
            if (!result.success)
            {
                return Results.BadRequest(new { error = result.message });
            }

            if (Game1.otherFarmers != null && Game1.otherFarmers.TryGetValue(id, out var farmer))
            {
                farmer.modData["svheadless/profession"] = newProf.ToLowerInvariant();
            }

            _logger?.Info("AdminWeb", $"Profesi pemain ID: {id} diubah menjadi '{newProf}' oleh Admin Override.");
            return Results.Ok(new { success = true, message = result.message });
        });

        // 14. Admin Multi-Tier Ban System
        app.MapPost("/api/admin/players/{id:long}/ban", async (long id, JsonElement body) =>
        {
            if (_dbService == null) return Results.BadRequest(new { error = "Database server tidak aktif." });
            string reason = body.TryGetProperty("reason", out var r) ? r.GetString() ?? "Pelanggaran aturan server" : "Pelanggaran aturan server";
            bool banIp = !body.TryGetProperty("banIp", out var bip) || bip.GetBoolean();
            bool banHwid = !body.TryGetProperty("banHwid", out var bhwid) || bhwid.GetBoolean();

            var user = await _dbService.GetUserByIdAsync(id);
            if (user == null) return Results.NotFound(new { error = "Pemain tidak ditemukan." });

            await _dbService.BanUserAsync(id, reason, banIp, banHwid, "SysAdmin");

            if (Game1.server != null && Game1.otherFarmers != null && Game1.otherFarmers.ContainsKey(id))
            {
                Game1.server.kick(id);
            }

            _logger?.Info("AdminWeb", $"Pemain '{user.Username}' (ID: {id}) dijatuhi sanksi BANNED. (IP: {banIp}, HWID: {banHwid}, Alasan: {reason})");
            return Results.Ok(new { success = true, message = $"Pemain '{user.Username}' berhasil di-banned." });
        });

        app.MapPost("/api/admin/players/{id:long}/unban", async (long id) =>
        {
            if (_dbService == null) return Results.BadRequest(new { error = "Database server tidak aktif." });
            var user = await _dbService.GetUserByIdAsync(id);
            if (user == null) return Results.NotFound(new { error = "Pemain tidak ditemukan." });

            await _dbService.UnbanUserAsync(id);
            _logger?.Info("AdminWeb", $"Pemain '{user.Username}' (ID: {id}) berhasil di-unban.");
            return Results.Ok(new { success = true, message = $"Pemain '{user.Username}' berhasil diaktifkan kembali." });
        });

        // 15. Admin Client Mod Manifest Inspection
        app.MapGet("/api/admin/players/{id:long}/mods", async (long id) =>
        {
            if (_dbService == null) return Results.BadRequest(new { error = "Database server tidak aktif." });
            var manifests = await _dbService.GetClientModsForUserAsync(id);
            var policies = await _dbService.GetModPoliciesAsync();
            var policyDict = policies.ToDictionary(p => p.ModId, p => p, StringComparer.OrdinalIgnoreCase);

            var result = manifests.Select(m =>
            {
                string status = "safe";
                string statusReason = "Mod terverifikasi.";
                if (policyDict.TryGetValue(m.ModId, out var policy))
                {
                    if (policy.PolicyType.Equals("Blacklist", StringComparison.OrdinalIgnoreCase))
                    {
                        status = "prohibited";
                        statusReason = policy.Reason;
                    }
                    else
                    {
                        status = "whitelisted";
                        statusReason = policy.Reason;
                    }
                }
                else if (m.HasBinary)
                {
                    status = "unverified";
                    statusReason = "Mod memiliki biner DLL pihak ketiga namun belum masuk dalam policy server.";
                }

                return new
                {
                    id = m.Id,
                    modId = m.ModId,
                    modName = m.ModName,
                    version = m.ModVersion,
                    hasBinary = m.HasBinary,
                    scannedAt = m.ScannedAt,
                    status = status,
                    statusReason = statusReason
                };
            });

            return Results.Ok(result);
        });

        // 16. Admin Mod Policies & Blacklist Management
        app.MapGet("/api/admin/mod-policies", async () =>
        {
            if (_dbService == null) return Results.Ok(new List<object>());
            var policies = await _dbService.GetModPoliciesAsync();
            return Results.Ok(policies);
        });

        app.MapPost("/api/admin/mod-policies", async (JsonElement body) =>
        {
            if (_dbService == null) return Results.BadRequest(new { error = "Database server tidak aktif." });
            string modId = body.TryGetProperty("modId", out var mi) ? mi.GetString() ?? "" : "";
            string modName = body.TryGetProperty("modName", out var mn) ? mn.GetString() ?? "" : "";
            string policyType = body.TryGetProperty("policyType", out var pt) ? pt.GetString() ?? "Blacklist" : "Blacklist";
            string reason = body.TryGetProperty("reason", out var r) ? r.GetString() ?? "" : "";

            if (string.IsNullOrWhiteSpace(modId)) return Results.BadRequest(new { error = "Mod Unique ID wajib diisi." });

            await _dbService.AddModPolicyAsync(modId, modName, policyType, reason);
            _logger?.Info("AdminWeb", $"Policy mod '{modId}' ({policyType}) berhasil ditambahkan.");
            return Results.Ok(new { success = true, message = $"Policy mod '{modId}' berhasil disimpan." });
        });

        app.MapDelete("/api/admin/mod-policies/{modId}", async (string modId) =>
        {
            if (_dbService == null) return Results.BadRequest(new { error = "Database server tidak aktif." });
            await _dbService.RemoveModPolicyAsync(modId);
            _logger?.Info("AdminWeb", $"Policy mod '{modId}' berhasil dihapus.");
            return Results.Ok(new { success = true, message = $"Policy mod '{modId}' berhasil dihapus." });
        });

        app.MapGet("/api/admin/banned-ips", async () =>
        {
            if (_dbService == null) return Results.Ok(new List<object>());
            var ips = await _dbService.GetBannedIpsAsync();
            return Results.Ok(ips);
        });

        app.MapGet("/api/admin/banned-hwids", async () =>
        {
            if (_dbService == null) return Results.Ok(new List<object>());
            var hwids = await _dbService.GetBannedHwidsAsync();
            return Results.Ok(hwids);
        });

        // 17. Player Self-Respec Profession Endpoint
        app.MapPost("/api/user/respec-profession", async (JsonElement body) =>
        {
            if (_dbService == null) return Results.BadRequest(new { error = "Database server tidak aktif." });
            string username = body.TryGetProperty("username", out var u) ? u.GetString() ?? "" : "";
            string newProf = body.TryGetProperty("newProfession", out var np) ? np.GetString() ?? "" : "";

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(newProf))
            {
                return Results.BadRequest(new { error = "Username dan profesi tujuan wajib diisi." });
            }

            var user = await _dbService.GetUserAsync(username);
            if (user == null) return Results.NotFound(new { error = "User tidak ditemukan." });

            int walletGold = 0;
            if (Game1.otherFarmers != null && Game1.otherFarmers.TryGetValue(user.Id, out var farmer))
            {
                walletGold = farmer.Money;
            }

            var result = await _dbService.ChangeProfessionAsync(user.Id, newProf, isAdminOverride: false, walletGold);
            if (!result.success)
            {
                return Results.BadRequest(new { error = result.message });
            }

            if (Game1.otherFarmers != null && Game1.otherFarmers.TryGetValue(user.Id, out var f))
            {
                f.Money = Math.Max(0, f.Money - result.feeDeducted);
                f.modData["svheadless/profession"] = newProf.ToLowerInvariant();
            }

            return Results.Ok(new
            {
                success = true,
                message = result.message,
                feeDeducted = result.feeDeducted
            });
        });

        // 18. Player Self-Service Unlink Game Character Endpoint
        app.MapPost("/api/user/unlink-character", async (JsonElement body) =>
        {
            if (_dbService == null) return Results.BadRequest(new { error = "Database server tidak aktif." });
            string username = body.TryGetProperty("username", out var u) ? u.GetString() ?? "" : "";
            string passwordOrOtp = body.TryGetProperty("passwordOrOtp", out var p) ? p.GetString() ?? "" : "";

            if (string.IsNullOrWhiteSpace(username))
            {
                return Results.BadRequest(new { error = "Username wajib diisi." });
            }

            var user = await _dbService.GetUserAsync(username);
            if (user == null) return Results.NotFound(new { error = "User tidak ditemukan." });

            if (!string.IsNullOrWhiteSpace(passwordOrOtp))
            {
                bool valid = await _dbService.VerifyPasswordAsync(username, passwordOrOtp);
                if (!valid && passwordOrOtp.Length == 6 && int.TryParse(passwordOrOtp, out _))
                {
                    valid = await _dbService.VerifyTotpAsync(username, passwordOrOtp);
                }
                if (!valid)
                {
                    return Results.BadRequest(new { error = "Password atau Kode OTP salah." });
                }
            }

            await _dbService.UnlinkUserGameFarmerAsync(user.Id);
            _logger?.Info("WebAuth", $"Tautan karakter game untuk akun '{username}' berhasil diputuskan via Web Portal.");

            return Results.Ok(new
            {
                success = true,
                message = $"Tautan karakter game untuk akun '{username}' berhasil diputuskan. Anda dapat membuat dan menautkan karakter baru di dalam game."
            });
        });
    }

    private static string GenerateDefaultSpaHtml()
    {
        return @"<!DOCTYPE html>
<html lang=""id"">
<head>
  <meta charset=""UTF-8"" />
  <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"" />
  <title>Stardew Valley Dedicated Server Portal</title>
</head>
<body style=""margin: 0; background-color: #0f172a; color: #f8fafc; font-family: sans-serif; display: flex; align-items: center; justify-content: center; height: 100vh;"">
  <div id=""root"" style=""text-align: center; max-width: 600px; padding: 2rem;"">
    <h1 style=""color: #38bdf8;"">Stardew Valley Headless Server</h1>
    <p style=""color: #94a3b8;"">Embedded Kestrel Server aktif pada port 8080.</p>
    <p>Silakan build antarmuka frontend React + TanStack Router untuk memuat dashboard lengkap.</p>
  </div>
</body>
</html>";
    }
}
