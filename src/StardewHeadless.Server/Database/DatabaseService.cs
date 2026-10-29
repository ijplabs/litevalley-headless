using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.Sqlite;
namespace StardewHeadless.Server.Database;
public class DatabaseService : IDatabaseService
{
    private readonly string _connectionString;
    private readonly string _dbPath;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    public DatabaseService(string dbPath)
    {
        _dbPath = dbPath;
        var dir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            DefaultTimeout = 30
        }.ToString();
    }
    private IDbConnection CreateConnection() => new SqliteConnection(_connectionString);
    public async Task InitializeAsync()
    {
        using var conn = CreateConnection();
        conn.Open();
        var sql = @"
            PRAGMA journal_mode = WAL;
            PRAGMA busy_timeout = 5000;
            CREATE TABLE IF NOT EXISTS users (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                username TEXT NOT NULL UNIQUE COLLATE NOCASE,
                email TEXT UNIQUE COLLATE NOCASE,
                is_email_verified INTEGER NOT NULL DEFAULT 0,
                email_verification_code TEXT,
                email_verification_expires_at TEXT,
                password_hash TEXT NOT NULL,
                salt TEXT NOT NULL,
                totp_secret TEXT,
                role TEXT NOT NULL DEFAULT 'Player',
                is_banned INTEGER NOT NULL DEFAULT 0,
                banned_at TEXT,
                ban_reason TEXT,
                last_ip TEXT,
                last_hwid TEXT,
                is_deleted INTEGER NOT NULL DEFAULT 0,
                deleted_at TEXT,
                created_at TEXT NOT NULL,
                last_login_at TEXT,
                linked_farmer_id INTEGER,
                linked_farmer_name TEXT,
                is_game_linked INTEGER NOT NULL DEFAULT 0,
                game_linked_at TEXT
            );
            CREATE TABLE IF NOT EXISTS farmhand_cabins (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                user_id INTEGER NOT NULL UNIQUE,
                cabin_index INTEGER NOT NULL UNIQUE,
                cabin_name TEXT NOT NULL,
                assigned_at TEXT NOT NULL,
                FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE
            );
            CREATE TABLE IF NOT EXISTS user_professions (
                user_id INTEGER PRIMARY KEY,
                profession_type TEXT NOT NULL,
                starter_kit_claimed INTEGER NOT NULL DEFAULT 0,
                skill_level INTEGER NOT NULL DEFAULT 0,
                last_respec_at TEXT,
                respec_count INTEGER NOT NULL DEFAULT 0,
                locked_at TEXT NOT NULL,
                FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE
            );
            CREATE TABLE IF NOT EXISTS treasury_ledger (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                user_id INTEGER,
                transaction_type TEXT NOT NULL,
                amount INTEGER NOT NULL,
                balance_after INTEGER NOT NULL,
                notes TEXT NOT NULL,
                created_at TEXT NOT NULL,
                FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE SET NULL
            );
            CREATE TABLE IF NOT EXISTS community_projects (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                project_name TEXT NOT NULL,
                target_gold INTEGER NOT NULL,
                current_gold INTEGER NOT NULL DEFAULT 0,
                status TEXT NOT NULL DEFAULT 'InProgress',
                completed_at TEXT
            );
            CREATE TABLE IF NOT EXISTS bounty_contracts (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                creator_user_id INTEGER NOT NULL,
                claimer_user_id INTEGER,
                item_name TEXT NOT NULL,
                quantity INTEGER NOT NULL,
                reward_gold INTEGER NOT NULL,
                status TEXT NOT NULL DEFAULT 'Open',
                created_at TEXT NOT NULL,
                FOREIGN KEY (creator_user_id) REFERENCES users(id) ON DELETE CASCADE,
                FOREIGN KEY (claimer_user_id) REFERENCES users(id) ON DELETE SET NULL
            );
            CREATE TABLE IF NOT EXISTS banned_ips (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                ip_address TEXT NOT NULL UNIQUE,
                reason TEXT NOT NULL,
                banned_by TEXT NOT NULL DEFAULT 'Admin',
                created_at TEXT NOT NULL,
                expires_at TEXT
            );
            CREATE TABLE IF NOT EXISTS banned_hwids (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                hwid_hash TEXT NOT NULL UNIQUE,
                last_known_username TEXT NOT NULL,
                reason TEXT NOT NULL,
                created_at TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS client_mod_manifests (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                user_id INTEGER NOT NULL,
                mod_id TEXT NOT NULL,
                mod_name TEXT NOT NULL,
                mod_version TEXT NOT NULL,
                has_binary INTEGER NOT NULL DEFAULT 0,
                scanned_at TEXT NOT NULL,
                FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE
            );
            CREATE TABLE IF NOT EXISTS mod_policies (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                mod_id TEXT NOT NULL UNIQUE COLLATE NOCASE,
                mod_name TEXT NOT NULL,
                policy_type TEXT NOT NULL,
                reason TEXT NOT NULL,
                created_at TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_users_username ON users(username);
            CREATE INDEX IF NOT EXISTS idx_users_email ON users(email);
            CREATE INDEX IF NOT EXISTS idx_treasury_created ON treasury_ledger(created_at);
            CREATE INDEX IF NOT EXISTS idx_banned_ips_ip ON banned_ips(ip_address);
            CREATE INDEX IF NOT EXISTS idx_banned_hwids_hash ON banned_hwids(hwid_hash);
            CREATE INDEX IF NOT EXISTS idx_client_mods_user ON client_mod_manifests(user_id);
            CREATE INDEX IF NOT EXISTS idx_users_linked_farmer ON users(linked_farmer_id);
            CREATE TABLE IF NOT EXISTS anticheat_incidents (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                farmer_id INTEGER NOT NULL,
                username TEXT NOT NULL,
                hwid TEXT,
                incident_type TEXT NOT NULL,
                severity_level INTEGER NOT NULL,
                action_taken TEXT NOT NULL,
                details TEXT,
                created_at TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_anticheat_farmer ON anticheat_incidents(farmer_id);
            CREATE INDEX IF NOT EXISTS idx_anticheat_created ON anticheat_incidents(created_at);
        ";
        await conn.ExecuteAsync(sql);
        // Migrasi Kolom jika database sebelumnya sudah terbentuk
        string[] migrationCols = new[]
        {
            "ALTER TABLE users ADD COLUMN email TEXT;",
            "ALTER TABLE users ADD COLUMN is_email_verified INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE users ADD COLUMN email_verification_code TEXT;",
            "ALTER TABLE users ADD COLUMN email_verification_expires_at TEXT;",
            "UPDATE users SET is_email_verified = 1 WHERE email IS NULL OR email = '';",
            "ALTER TABLE users ADD COLUMN totp_secret TEXT;",
            "ALTER TABLE users ADD COLUMN is_deleted INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE users ADD COLUMN deleted_at TEXT;",
            "ALTER TABLE users ADD COLUMN banned_at TEXT;",
            "ALTER TABLE users ADD COLUMN ban_reason TEXT;",
            "ALTER TABLE users ADD COLUMN last_ip TEXT;",
            "ALTER TABLE users ADD COLUMN last_hwid TEXT;",
            "ALTER TABLE user_professions ADD COLUMN last_respec_at TEXT;",
            "ALTER TABLE user_professions ADD COLUMN respec_count INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE users ADD COLUMN linked_farmer_id INTEGER;",
            "ALTER TABLE users ADD COLUMN linked_farmer_name TEXT;",
            "ALTER TABLE users ADD COLUMN is_game_linked INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE users ADD COLUMN game_linked_at TEXT;"
        };
        foreach (var colSql in migrationCols)
        {
            try
            {
                await conn.ExecuteAsync(colSql);
            }
            catch
            {
                // Kolom sudah ada
            }
        }
        // Seed default Blacklist Mod Policies jika kosong & Daftarkan Mod yang digantikan secara native
        var seedNow = DateTime.UtcNow.ToString("o");
        var defaultPolicies = new[]
        {
            // --- BLACKLIST (Mod Terlarang & Cheat Eksplisit) ---
            new { ModId = "CJBok.CheatsMenu", ModName = "CJB Cheats Menu", PolicyType = "Blacklist", Reason = "Manipulasi waktu, uang, stamina, dan teleportasi ilegal.", CreatedAt = seedNow },
            new { ModId = "CJBok.ItemSpawner", ModName = "CJB Item Spawner", PolicyType = "Blacklist", Reason = "Duplikasi dan spawning item gratis perusak ekonomi.", CreatedAt = seedNow },
            new { ModId = "Platonymous.AllCheats", ModName = "All Cheats", PolicyType = "Blacklist", Reason = "Cheat menu tidak diizinkan.", CreatedAt = seedNow },
            new { ModId = "Omegasis.SaveAnywhere", ModName = "Save Anywhere", PolicyType = "Blacklist", Reason = "Merusak sinkronisasi siklus tidur headless.", CreatedAt = seedNow },
            new { ModId = "spacechase0.BiggerBackpack", ModName = "Bigger Backpack", PolicyType = "Blacklist", Reason = "Digantikan oleh sistem native 58-slot inventori StardewBridge terintegrasi.", CreatedAt = seedNow },
            new { ModId = "Rakiin.AutomaticGates", ModName = "Automatic Gates", PolicyType = "Blacklist", Reason = "Digantikan oleh sistem native AutoGateController StardewBridge terintegrasi.", CreatedAt = seedNow },
            new { ModId = "NeverToxic.YetAnotherFishingMod", ModName = "Yet Another Fishing Mod", PolicyType = "Blacklist", Reason = "Cheat memancing ekstrem (0s instant catch, 5x iridium fish).", CreatedAt = seedNow },
            new { ModId = "SMAPI.ConsoleCommands", ModName = "SMAPI Console Commands", PolicyType = "Blacklist", Reason = "Eksekusi command konsol curang (player_setmoney, player_add).", CreatedAt = seedNow },
            new { ModId = "Pathoschild.ChestsAnywhere", ModName = "Chests Anywhere", PolicyType = "Blacklist", Reason = "Akses jarak jauh tanpa batas menembus peti kabin privat pemain lain.", CreatedAt = seedNow },
            new { ModId = "NCarigon.PassableCrops", ModName = "Passable Crops", PolicyType = "Blacklist", Reason = "Noclip menembus tanaman & spam paket mutasi Object.modData.", CreatedAt = seedNow },
            new { ModId = "thimadera.StackEverythingRedux", ModName = "Stack Everything Redux", PolicyType = "Blacklist", Reason = "Merusak batas stack inventori hingga 99.999 dan merusak ekonomi.", CreatedAt = seedNow },

            // --- WHITELIST (Mod Resmi Server & Yang Terverifikasi Aman) ---
            new { ModId = "IJP.StardewBridge", ModName = "StardewBridge Client", PolicyType = "Whitelist", Reason = "Mod protokol bridge resmi LiteValley / StardewHeadless.", CreatedAt = seedNow },
            new { ModId = "StardewBridge", ModName = "StardewBridge Client (Alias)", PolicyType = "Whitelist", Reason = "Mod protokol bridge resmi LiteValley.", CreatedAt = seedNow },
            new { ModId = "SMAPI.ErrorHandler", ModName = "SMAPI Error Handler", PolicyType = "Whitelist", Reason = "Mod bawaan SMAPI untuk stabilitas error handling.", CreatedAt = seedNow },
            new { ModId = "SMAPI.SaveBackup", ModName = "SMAPI Save Backup", PolicyType = "Whitelist", Reason = "Mod bawaan SMAPI untuk backup save otomatis.", CreatedAt = seedNow },
            new { ModId = "FlashShifter.SVECode", ModName = "Stardew Valley Expanded Code", PolicyType = "Whitelist", Reason = "Ekspansi resmi Stardew Valley Expanded (Logic).", CreatedAt = seedNow },
            new { ModId = "FlashShifter.StardewValleyExpandedCP", ModName = "Stardew Valley Expanded - CP", PolicyType = "Whitelist", Reason = "Ekspansi resmi Stardew Valley Expanded (Content Patcher).", CreatedAt = seedNow },
            new { ModId = "FlashShifter.StardewValleyExpandedFTM", ModName = "Stardew Valley Expanded - FTM", PolicyType = "Whitelist", Reason = "Ekspansi resmi Stardew Valley Expanded (Farm Type Manager).", CreatedAt = seedNow },
            new { ModId = "flashshifter.immersivefarm2remastered", ModName = "Immersive Farm 2 Remastered", PolicyType = "Whitelist", Reason = "Peta lahan pertanian resmi SVE (IF2R).", CreatedAt = seedNow },
            new { ModId = "flashshifter.GrandpasFarm", ModName = "Grandpa's Farm", PolicyType = "Whitelist", Reason = "Peta lahan pertanian resmi SVE (Grandpa's Farm).", CreatedAt = seedNow },
            new { ModId = "flashshifter.FrontierFarm", ModName = "Frontier Farm", PolicyType = "Whitelist", Reason = "Peta lahan pertanian resmi SVE (Frontier Farm).", CreatedAt = seedNow },
            new { ModId = "flashshifter.GrampletonFields", ModName = "Grampleton Fields", PolicyType = "Whitelist", Reason = "Lahan tambahan resmi SVE.", CreatedAt = seedNow },
            new { ModId = "Pathoschild.ContentPatcher", ModName = "Content Patcher", PolicyType = "Whitelist", Reason = "Framework inti modding pemuat konten.", CreatedAt = seedNow },
            new { ModId = "Esca.FarmTypeManager", ModName = "Farm Type Manager", PolicyType = "Whitelist", Reason = "Framework inti spawning ore, forage, dan monster.", CreatedAt = seedNow },
            new { ModId = "Pathoschild.Automate", ModName = "Automate", PolicyType = "Whitelist", Reason = "Mod otomatisasi mesin resmi (terkoneksi ChestLockGuard).", CreatedAt = seedNow },
            new { ModId = "shekurika.WaterFish", ModName = "Visible Fish", PolicyType = "Whitelist", Reason = "Mod visual ikan renang di air (client-only).", CreatedAt = seedNow },
            new { ModId = "bcmpinc.WearMoreRings", ModName = "Wear More Rings", PolicyType = "Whitelist", Reason = "Mod slot cincin resmi (dibatasi 4 cincin).", CreatedAt = seedNow },
            new { ModId = "spacechase0.StardewHack", ModName = "StardewHack", PolicyType = "Whitelist", Reason = "Pustaka pembantu WearMoreRings.", CreatedAt = seedNow },
            new { ModId = "Annosz.UiInfoSuite2", ModName = "UI Info Suite 2", PolicyType = "Whitelist", Reason = "Mod informasi HUD lokal (read-only).", CreatedAt = seedNow },
            new { ModId = "Poltergeister.SeasonalCuteCharacters", ModName = "Seasonal Cute Characters", PolicyType = "Whitelist", Reason = "Mod visual pakaian musiman NPC.", CreatedAt = seedNow },
            new { ModId = "spacechase0.GenericModConfigMenu", ModName = "Generic Mod Config Menu", PolicyType = "Whitelist", Reason = "Menu konfigurasi mod lokal klien.", CreatedAt = seedNow },
            new { ModId = "adhasangg.LemburSehatUI", ModName = "Lembur Sehat UI", PolicyType = "Whitelist", Reason = "Mod HUD jam kerja sehat & to-do.", CreatedAt = seedNow },
            new { ModId = "gaussfire.ConvenientInventory", ModName = "Convenient Inventory", PolicyType = "Whitelist", Reason = "Quick-stack inventori terproteksi.", CreatedAt = seedNow },
            new { ModId = "leclair.bettercrafting", ModName = "Better Crafting", PolicyType = "Whitelist", Reason = "Meja kerajinan terproteksi.", CreatedAt = seedNow },
            new { ModId = "Esca.DestroyableBushes", ModName = "Destroyable Bushes", PolicyType = "Whitelist", Reason = "Mod pemotong semak resmi.", CreatedAt = seedNow },
            new { ModId = "SMAPI.SaveBackup", ModName = "SMAPI Save Backup", PolicyType = "Whitelist", Reason = "Mod backup lokal SMAPI.", CreatedAt = seedNow }
        };
        foreach (var p in defaultPolicies)
        {
            await conn.ExecuteAsync(@"
                INSERT INTO mod_policies (mod_id, mod_name, policy_type, reason, created_at)
                VALUES (@ModId, @ModName, @PolicyType, @Reason, @CreatedAt)
                ON CONFLICT(mod_id) DO NOTHING;", p);
        }
        
        Console.WriteLine($"[Database] SQLite diinisialisasi sukses pada: {_dbPath}");
    }
    #region User Management
    public async Task<IEnumerable<UserAccount>> GetAllUsersAsync()
    {
        using var conn = CreateConnection();
        return await conn.QueryAsync<UserAccount>(@"
            SELECT 
                id, 
                username, 
                email AS Email,
                is_email_verified AS IsEmailVerified,
                email_verification_code AS EmailVerificationCode,
                email_verification_expires_at AS EmailVerificationExpiresAt,
                password_hash AS PasswordHash, 
                salt, 
                totp_secret AS TotpSecret, 
                role, 
                is_banned AS IsBanned,
                banned_at AS BannedAt,
                ban_reason AS BanReason,
                last_ip AS LastIp,
                last_hwid AS LastHwid,
                is_deleted AS IsDeleted,
                deleted_at AS DeletedAt,
                created_at AS CreatedAt, 
                last_login_at AS LastLoginAt,
                linked_farmer_id AS LinkedFarmerId,
                linked_farmer_name AS LinkedFarmerName,
                is_game_linked AS IsGameLinked,
                game_linked_at AS GameLinkedAt 
            FROM users 
            ORDER BY id ASC");
    }
    public async Task<UserAccount?> GetUserAsync(string username)
    {
        using var conn = CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<UserAccount>(@"
            SELECT 
                id, 
                username, 
                email AS Email,
                is_email_verified AS IsEmailVerified,
                email_verification_code AS EmailVerificationCode,
                email_verification_expires_at AS EmailVerificationExpiresAt,
                password_hash AS PasswordHash, 
                salt, 
                totp_secret AS TotpSecret, 
                role, 
                is_banned AS IsBanned,
                banned_at AS BannedAt,
                ban_reason AS BanReason,
                last_ip AS LastIp,
                last_hwid AS LastHwid,
                is_deleted AS IsDeleted,
                deleted_at AS DeletedAt,
                created_at AS CreatedAt, 
                last_login_at AS LastLoginAt,
                linked_farmer_id AS LinkedFarmerId,
                linked_farmer_name AS LinkedFarmerName,
                is_game_linked AS IsGameLinked,
                game_linked_at AS GameLinkedAt 
            FROM users 
            WHERE username = @Username",
            new { Username = username });
    }
    public async Task<UserAccount?> GetUserByIdAsync(long userId)
    {
        using var conn = CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<UserAccount>(@"
            SELECT 
                id, 
                username, 
                email AS Email,
                is_email_verified AS IsEmailVerified,
                email_verification_code AS EmailVerificationCode,
                email_verification_expires_at AS EmailVerificationExpiresAt,
                password_hash AS PasswordHash, 
                salt, 
                totp_secret AS TotpSecret, 
                role, 
                is_banned AS IsBanned,
                banned_at AS BannedAt,
                ban_reason AS BanReason,
                last_ip AS LastIp,
                last_hwid AS LastHwid,
                is_deleted AS IsDeleted,
                deleted_at AS DeletedAt,
                created_at AS CreatedAt, 
                last_login_at AS LastLoginAt,
                linked_farmer_id AS LinkedFarmerId,
                linked_farmer_name AS LinkedFarmerName,
                is_game_linked AS IsGameLinked,
                game_linked_at AS GameLinkedAt 
            FROM users 
            WHERE id = @Id",
            new { Id = userId });
    }
    public async Task<UserAccount?> GetUserByEmailAsync(string email)
    {
        using var conn = CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<UserAccount>(@"
            SELECT 
                id, 
                username, 
                email AS Email,
                is_email_verified AS IsEmailVerified,
                email_verification_code AS EmailVerificationCode,
                email_verification_expires_at AS EmailVerificationExpiresAt,
                password_hash AS PasswordHash, 
                salt, 
                totp_secret AS TotpSecret, 
                role, 
                is_banned AS IsBanned,
                banned_at AS BannedAt,
                ban_reason AS BanReason,
                last_ip AS LastIp,
                last_hwid AS LastHwid,
                is_deleted AS IsDeleted,
                deleted_at AS DeletedAt,
                created_at AS CreatedAt, 
                last_login_at AS LastLoginAt,
                linked_farmer_id AS LinkedFarmerId,
                linked_farmer_name AS LinkedFarmerName,
                is_game_linked AS IsGameLinked,
                game_linked_at AS GameLinkedAt 
            FROM users 
            WHERE email = @Email COLLATE NOCASE",
            new { Email = email.Trim() });
    }
    public async Task<UserAccount?> GetUserByLinkedFarmerAsync(long farmerId, string farmerName)
    {
        using var conn = CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<UserAccount>(@"
            SELECT 
                id, 
                username, 
                email AS Email,
                is_email_verified AS IsEmailVerified,
                email_verification_code AS EmailVerificationCode,
                email_verification_expires_at AS EmailVerificationExpiresAt,
                password_hash AS PasswordHash, 
                salt, 
                totp_secret AS TotpSecret, 
                role, 
                is_banned AS IsBanned,
                banned_at AS BannedAt,
                ban_reason AS BanReason,
                last_ip AS LastIp,
                last_hwid AS LastHwid,
                is_deleted AS IsDeleted,
                deleted_at AS DeletedAt,
                created_at AS CreatedAt, 
                last_login_at AS LastLoginAt,
                linked_farmer_id AS LinkedFarmerId,
                linked_farmer_name AS LinkedFarmerName,
                is_game_linked AS IsGameLinked,
                game_linked_at AS GameLinkedAt 
            FROM users 
            WHERE is_game_linked = 1 AND (linked_farmer_id = @FarmerId OR linked_farmer_name = @FarmerName COLLATE NOCASE)
            LIMIT 1",
            new { FarmerId = farmerId, FarmerName = farmerName.Trim() });
    }
    public async Task LinkUserToGameFarmerAsync(long userId, long farmerId, string farmerName)
    {
        await _writeLock.WaitAsync();
        try
        {
            using var conn = CreateConnection();
            await conn.ExecuteAsync(@"
                UPDATE users 
                SET linked_farmer_id = @FarmerId,
                    linked_farmer_name = @FarmerName,
                    is_game_linked = 1,
                    game_linked_at = @LinkedAt
                WHERE id = @UserId;",
                new
                {
                    UserId = userId,
                    FarmerId = farmerId,
                    FarmerName = farmerName.Trim(),
                    LinkedAt = DateTime.UtcNow.ToString("o")
                });
        }
        finally
        {
            _writeLock.Release();
        }
    }
    public async Task UnlinkUserGameFarmerAsync(long userId)
    {
        await _writeLock.WaitAsync();
        try
        {
            using var conn = CreateConnection();
            await conn.ExecuteAsync(@"
                UPDATE users 
                SET linked_farmer_id = NULL,
                    linked_farmer_name = NULL,
                    is_game_linked = 0,
                    game_linked_at = NULL
                WHERE id = @UserId;
                DELETE FROM farmhand_cabins WHERE user_id = @UserId;",
                new { UserId = userId });
        }
        finally
        {
            _writeLock.Release();
        }
    }
    public async Task ResetAllGameLinksAsync()
    {
        await _writeLock.WaitAsync();
        try
        {
            using var conn = CreateConnection();
            await conn.ExecuteAsync(@"
                UPDATE users 
                SET linked_farmer_id = NULL,
                    linked_farmer_name = NULL,
                    is_game_linked = 0,
                    game_linked_at = NULL;
                DELETE FROM farmhand_cabins;");
        }
        finally
        {
            _writeLock.Release();
        }
    }
    public async Task<UserAccount> RegisterUserAsync(string username, string password, string role = "Player")
    {
        var salt = GenerateSalt();
        var hash = HashPassword(password, salt);
        var totpSecret = Security.TotpService.GenerateSecret();
        await _writeLock.WaitAsync();
        try
        {
            using var conn = CreateConnection();
            var now = DateTime.UtcNow.ToString("o");
            var id = await conn.QuerySingleAsync<long>(@"
                INSERT INTO users (username, is_email_verified, password_hash, salt, totp_secret, role, is_banned, created_at)
                VALUES (@Username, 1, @Hash, @Salt, @TotpSecret, @Role, 0, @CreatedAt);
                SELECT last_insert_rowid();",
                new { Username = username, Hash = hash, Salt = salt, TotpSecret = totpSecret, Role = role, CreatedAt = now });
            return new UserAccount
            {
                Id = id,
                Username = username,
                IsEmailVerified = true,
                PasswordHash = hash,
                Salt = salt,
                TotpSecret = totpSecret,
                Role = role,
                IsBanned = false,
                CreatedAt = DateTime.UtcNow
            };
        }
        finally
        {
            _writeLock.Release();
        }
    }
    public async Task<UserAccount> RegisterUserWithEmailAsync(string username, string email, string password, string role = "Player")
    {
        var salt = GenerateSalt();
        var hash = HashPassword(password, salt);
        var totpSecret = Security.TotpService.GenerateSecret();
        await _writeLock.WaitAsync();
        try
        {
            using var conn = CreateConnection();
            var now = DateTime.UtcNow.ToString("o");
            var id = await conn.QuerySingleAsync<long>(@"
                INSERT INTO users (username, email, is_email_verified, password_hash, salt, totp_secret, role, is_banned, created_at)
                VALUES (@Username, @Email, 0, @Hash, @Salt, @TotpSecret, @Role, 0, @CreatedAt);
                SELECT last_insert_rowid();",
                new { Username = username, Email = email.Trim().ToLowerInvariant(), Hash = hash, Salt = salt, TotpSecret = totpSecret, Role = role, CreatedAt = now });
            return new UserAccount
            {
                Id = id,
                Username = username,
                Email = email.Trim().ToLowerInvariant(),
                IsEmailVerified = false,
                PasswordHash = hash,
                Salt = salt,
                TotpSecret = totpSecret,
                Role = role,
                IsBanned = false,
                CreatedAt = DateTime.UtcNow
            };
        }
        finally
        {
            _writeLock.Release();
        }
    }
    public async Task SetEmailVerificationCodeAsync(long userId, string code, DateTime expiresAt)
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync(@"
            UPDATE users 
            SET email_verification_code = @Code,
                email_verification_expires_at = @ExpiresAt
            WHERE id = @UserId",
            new { UserId = userId, Code = code.Trim(), ExpiresAt = expiresAt.ToString("o") });
    }
    public async Task<(bool success, string message)> VerifyEmailCodeAsync(string emailOrUsername, string code)
    {
        using var conn = CreateConnection();
        var user = await conn.QuerySingleOrDefaultAsync<UserAccount>(@"
            SELECT 
                id, 
                username, 
                email AS Email,
                is_email_verified AS IsEmailVerified,
                email_verification_code AS EmailVerificationCode,
                email_verification_expires_at AS EmailVerificationExpiresAt,
                password_hash AS PasswordHash, 
                salt, 
                totp_secret AS TotpSecret, 
                role, 
                is_banned AS IsBanned
            FROM users 
            WHERE email = @Identifier COLLATE NOCASE OR username = @Identifier COLLATE NOCASE",
            new { Identifier = emailOrUsername.Trim() });
        if (user == null)
        {
            return (false, "Akun tidak ditemukan.");
        }
        if (user.IsEmailVerified)
        {
            return (true, "Email sudah terverifikasi sebelumnya.");
        }
        if (string.IsNullOrWhiteSpace(user.EmailVerificationCode) || 
            !string.Equals(user.EmailVerificationCode.Trim(), code.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return (false, "Kode verifikasi salah atau tidak cocok.");
        }
        if (user.EmailVerificationExpiresAt.HasValue && DateTime.UtcNow > user.EmailVerificationExpiresAt.Value)
        {
            return (false, "Kode verifikasi telah kedaluwarsa. Silakan minta kode baru.");
        }
        await conn.ExecuteAsync(@"
            UPDATE users 
            SET is_email_verified = 1,
                email_verification_code = NULL,
                email_verification_expires_at = NULL
            WHERE id = @UserId",
            new { UserId = user.Id });
        return (true, "Email berhasil diverifikasi!");
    }
    public async Task<bool> VerifyPasswordAsync(string username, string password)
    {
        var user = await GetUserAsync(username);
        if (user == null || user.IsBanned) return false;
        var inputHash = HashPassword(password, user.Salt);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(inputHash),
            Encoding.UTF8.GetBytes(user.PasswordHash));
    }
    public async Task<string?> GetTotpSecretAsync(string username)
    {
        using var conn = CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<string>(
            "SELECT totp_secret FROM users WHERE username = @Username",
            new { Username = username });
    }
    public async Task<bool> VerifyTotpAsync(string username, string otpCode)
    {
        var user = await GetUserAsync(username);
        if (user == null || user.IsBanned) return false;
        var secret = await GetTotpSecretAsync(username);
        if (string.IsNullOrEmpty(secret)) return false;
        return Security.TotpService.VerifyCode(secret, otpCode);
    }
    public async Task UpdateLastLoginAsync(long userId)
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync(
            "UPDATE users SET last_login_at = @Now WHERE id = @Id",
            new { Id = userId, Now = DateTime.UtcNow.ToString("o") });
    }
    public async Task UpdateUserNetworkInfoAsync(long userId, string ip, string hwid)
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync(@"
            UPDATE users 
            SET last_ip = COALESCE(@Ip, last_ip), 
                last_hwid = COALESCE(@Hwid, last_hwid) 
            WHERE id = @Id",
            new { Id = userId, Ip = ip, Hwid = hwid });
    }
    public async Task<int> PurgePlayerAsync(long userId, string reason, int userWalletGold = 0)
    {
        await _writeLock.WaitAsync();
        try
        {
            var user = await GetUserByIdAsync(userId);
            if (user == null) return 0;
            using var conn = CreateConnection();
            int forfeitedAmount = 0;
            // 1. Sita saldo dompet pemain ke kas desa jika ada
            if (userWalletGold > 0)
            {
                await RecordTreasuryTransactionInternalAsync(
                    conn,
                    userId,
                    "forfeit_abandoned",
                    userWalletGold,
                    $"Penyitaan saldo akun dihapus '{user.Username}' ({reason}) ke Kas Pembangunan Desa.");
                forfeitedAmount = userWalletGold;
            }
            var now = DateTime.UtcNow.ToString("o");
            // 2. Lepas alokasi kabin di database
            await conn.ExecuteAsync("DELETE FROM farmhand_cabins WHERE user_id = @UserId;", new { UserId = userId });
            // 3. Lepas profesi di database
            await conn.ExecuteAsync("DELETE FROM user_professions WHERE user_id = @UserId;", new { UserId = userId });
            // 4. Tandai akun sebagai terhapus
            await conn.ExecuteAsync(@"
                UPDATE users 
                SET is_deleted = 1, 
                    deleted_at = @Now, 
                    totp_secret = NULL,
                    is_game_linked = 0,
                    linked_farmer_id = NULL,
                    linked_farmer_name = NULL,
                    game_linked_at = NULL 
                WHERE id = @UserId;", 
                new { UserId = userId, Now = now });
            return forfeitedAmount;
        }
        finally
        {
            _writeLock.Release();
        }
    }
    public async Task BanUserAsync(long userId, string reason, bool banIp = true, bool banHwid = true, string bannedBy = "Admin")
    {
        await _writeLock.WaitAsync();
        try
        {
            var user = await GetUserByIdAsync(userId);
            if (user == null) return;
            using var conn = CreateConnection();
            var now = DateTime.UtcNow.ToString("o");
            // 1. Set status banned di users
            await conn.ExecuteAsync(@"
                UPDATE users 
                SET is_banned = 1, 
                    banned_at = @Now, 
                    ban_reason = @Reason 
                WHERE id = @UserId;",
                new { UserId = userId, Now = now, Reason = reason });
            // 2. Blacklist IP jika diminta dan tersedia
            if (banIp && !string.IsNullOrWhiteSpace(user.LastIp))
            {
                await conn.ExecuteAsync(@"
                    INSERT INTO banned_ips (ip_address, reason, banned_by, created_at)
                    VALUES (@Ip, @Reason, @BannedBy, @CreatedAt)
                    ON CONFLICT(ip_address) DO UPDATE SET reason = @Reason;",
                    new { Ip = user.LastIp, Reason = $"Ban terkait user '{user.Username}': {reason}", BannedBy = bannedBy, CreatedAt = now });
            }
            // 3. Blacklist HWID jika diminta dan tersedia
            if (banHwid && !string.IsNullOrWhiteSpace(user.LastHwid))
            {
                await conn.ExecuteAsync(@"
                    INSERT INTO banned_hwids (hwid_hash, last_known_username, reason, created_at)
                    VALUES (@Hwid, @Username, @Reason, @CreatedAt)
                    ON CONFLICT(hwid_hash) DO UPDATE SET reason = @Reason, last_known_username = @Username;",
                    new { Hwid = user.LastHwid, Username = user.Username, Reason = reason, CreatedAt = now });
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }
    public async Task BanHwidAsync(string hwid, string username, string reason)
    {
        if (string.IsNullOrWhiteSpace(hwid)) return;
        await _writeLock.WaitAsync();
        try
        {
            using var conn = CreateConnection();
            var now = DateTime.UtcNow.ToString("o");
            await conn.ExecuteAsync(@"
                INSERT INTO banned_hwids (hwid_hash, last_known_username, reason, created_at)
                VALUES (@Hwid, @Username, @Reason, @CreatedAt)
                ON CONFLICT(hwid_hash) DO UPDATE SET reason = @Reason, last_known_username = @Username;",
                new { Hwid = hwid, Username = username, Reason = reason, CreatedAt = now });
        }
        finally
        {
            _writeLock.Release();
        }
    }
    public async Task UnbanUserAsync(long userId)
    {
        await _writeLock.WaitAsync();
        try
        {
            var user = await GetUserByIdAsync(userId);
            if (user == null) return;
            using var conn = CreateConnection();
            await conn.ExecuteAsync(@"
                UPDATE users 
                SET is_banned = 0, 
                    banned_at = NULL, 
                    ban_reason = NULL 
                WHERE id = @UserId;",
                new { UserId = userId });
            if (!string.IsNullOrWhiteSpace(user.LastIp))
            {
                await conn.ExecuteAsync("DELETE FROM banned_ips WHERE ip_address = @Ip;", new { Ip = user.LastIp });
            }
            if (!string.IsNullOrWhiteSpace(user.LastHwid))
            {
                await conn.ExecuteAsync("DELETE FROM banned_hwids WHERE hwid_hash = @Hwid;", new { Hwid = user.LastHwid });
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }
    public async Task<bool> IsIpBannedAsync(string ip)
    {
        if (string.IsNullOrWhiteSpace(ip)) return false;
        using var conn = CreateConnection();
        var count = await conn.QuerySingleAsync<int>(
            "SELECT COUNT(1) FROM banned_ips WHERE ip_address = @Ip",
            new { Ip = ip.Trim() });
        return count > 0;
    }
    public async Task<bool> IsHwidBannedAsync(string hwid)
    {
        if (string.IsNullOrWhiteSpace(hwid)) return false;
        using var conn = CreateConnection();
        var count = await conn.QuerySingleAsync<int>(
            "SELECT COUNT(1) FROM banned_hwids WHERE hwid_hash = @Hwid",
            new { Hwid = hwid.Trim() });
        return count > 0;
    }
    public async Task<IEnumerable<BannedIp>> GetBannedIpsAsync()
    {
        using var conn = CreateConnection();
        return await conn.QueryAsync<BannedIp>(@"
            SELECT id, ip_address AS IpAddress, reason, banned_by AS BannedBy, created_at AS CreatedAt, expires_at AS ExpiresAt 
            FROM banned_ips ORDER BY id DESC");
    }
    public async Task<IEnumerable<BannedHwid>> GetBannedHwidsAsync()
    {
        using var conn = CreateConnection();
        return await conn.QueryAsync<BannedHwid>(@"
            SELECT id, hwid_hash AS HwidHash, last_known_username AS LastKnownUsername, reason, created_at AS CreatedAt 
            FROM banned_hwids ORDER BY id DESC");
    }
    #endregion
    #region Cabin Management
    public async Task AssignCabinAsync(long userId, int cabinIndex, string cabinName)
    {
        await _writeLock.WaitAsync();
        try
        {
            using var conn = CreateConnection();
            await conn.ExecuteAsync(@"
                INSERT INTO farmhand_cabins (user_id, cabin_index, cabin_name, assigned_at)
                VALUES (@UserId, @CabinIndex, @CabinName, @AssignedAt)
                ON CONFLICT(user_id) DO UPDATE SET cabin_index = @CabinIndex, cabin_name = @CabinName;",
                new { UserId = userId, CabinIndex = cabinIndex, CabinName = cabinName, AssignedAt = DateTime.UtcNow.ToString("o") });
        }
        finally
        {
            _writeLock.Release();
        }
    }
    public async Task<FarmhandCabin?> GetCabinByUserAsync(long userId)
    {
        using var conn = CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<FarmhandCabin>(
            "SELECT id, user_id AS UserId, cabin_index AS CabinIndex, cabin_name AS CabinName, assigned_at AS AssignedAt FROM farmhand_cabins WHERE user_id = @UserId",
            new { UserId = userId });
    }
    public async Task<FarmhandCabin?> GetCabinByIndexAsync(int cabinIndex)
    {
        using var conn = CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<FarmhandCabin>(
            "SELECT id, user_id AS UserId, cabin_index AS CabinIndex, cabin_name AS CabinName, assigned_at AS AssignedAt FROM farmhand_cabins WHERE cabin_index = @CabinIndex",
            new { CabinIndex = cabinIndex });
    }
    public async Task ResetCabinsAsync()
    {
        await _writeLock.WaitAsync();
        try
        {
            using var conn = CreateConnection();
            await conn.ExecuteAsync("DELETE FROM farmhand_cabins;");
        }
        finally
        {
            _writeLock.Release();
        }
    }
    #endregion
    #region Profession Management
    public async Task<bool> LockProfessionAsync(long userId, string professionType)
    {
        await _writeLock.WaitAsync();
        try
        {
            using var conn = CreateConnection();
            var existing = await GetProfessionAsync(userId);
            if (existing != null)
            {
                // One-time lock: jika sudah ada profesi terdaftar, tolak penguncian ulang
                return false;
            }
            await conn.ExecuteAsync(@"
                INSERT INTO user_professions (user_id, profession_type, starter_kit_claimed, skill_level, locked_at)
                VALUES (@UserId, @ProfessionType, 0, 0, @LockedAt);",
                new { UserId = userId, ProfessionType = professionType.ToLowerInvariant(), LockedAt = DateTime.UtcNow.ToString("o") });
            return true;
        }
        finally
        {
            _writeLock.Release();
        }
    }
    public async Task<bool> UpdateInitialProfessionAsync(long userId, string professionType)
    {
        await _writeLock.WaitAsync();
        try
        {
            using var conn = CreateConnection();
            var existing = await GetProfessionAsync(userId);
            if (existing != null && !existing.StarterKitClaimed)
            {
                await conn.ExecuteAsync(@"
                    UPDATE user_professions
                    SET profession_type = @ProfessionType, locked_at = @LockedAt
                    WHERE user_id = @UserId;",
                    new { UserId = userId, ProfessionType = professionType.ToLowerInvariant(), LockedAt = DateTime.UtcNow.ToString("o") });
                return true;
            }
            return false;
        }
        finally
        {
            _writeLock.Release();
        }
    }
    public async Task<UserProfession?> GetProfessionAsync(long userId)
    {
        using var conn = CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<UserProfession>(@"
            SELECT 
                user_id AS UserId, 
                profession_type AS ProfessionType, 
                starter_kit_claimed AS StarterKitClaimed, 
                skill_level AS SkillLevel, 
                last_respec_at AS LastRespecAt,
                respec_count AS RespecCount,
                locked_at AS LockedAt 
            FROM user_professions 
            WHERE user_id = @UserId",
            new { UserId = userId });
    }
    public async Task ClaimStarterKitAsync(long userId)
    {
        await _writeLock.WaitAsync();
        try
        {
            using var conn = CreateConnection();
            await conn.ExecuteAsync(
                "UPDATE user_professions SET starter_kit_claimed = 1 WHERE user_id = @UserId",
                new { UserId = userId });
        }
        finally
        {
            _writeLock.Release();
        }
    }
    public async Task<(bool success, string message, int feeDeducted)> ChangeProfessionAsync(
        long userId, 
        string newProfession, 
        bool isAdminOverride, 
        int currentWalletGold = 0)
    {
        await _writeLock.WaitAsync();
        try
        {
            using var conn = CreateConnection();
            var prof = await conn.QuerySingleOrDefaultAsync<UserProfession>(@"
                SELECT 
                    user_id AS UserId, 
                    profession_type AS ProfessionType, 
                    starter_kit_claimed AS StarterKitClaimed, 
                    skill_level AS SkillLevel, 
                    last_respec_at AS LastRespecAt, 
                    respec_count AS RespecCount, 
                    locked_at AS LockedAt 
                FROM user_professions 
                WHERE user_id = @UserId",
                new { UserId = userId });
            if (prof == null)
            {
                return (false, "Pemain belum memiliki profesi terdaftar.", 0);
            }
            int fee = 0;
            if (!isAdminOverride)
            {
                // Evaluasi cooldown 28 hari in-game / musiman
                if (prof.LastRespecAt.HasValue)
                {
                    var daysSince = (DateTime.UtcNow - prof.LastRespecAt.Value).TotalDays;
                    if (daysSince < 28)
                    {
                        int remainingDays = (int)Math.Ceiling(28 - daysSince);
                        return (false, $"Cooldown pergantian profesi belum selesai. Tunggu {remainingDays} hari lagi.", 0);
                    }
                }
                // Biaya sertifikasi: max(5000g, 25% wallet)
                fee = Math.Max(5000, currentWalletGold / 4);
                if (currentWalletGold < fee)
                {
                    return (false, $"Saldo dompet ({currentWalletGold}g) tidak mencukupi untuk biaya retribusi sertifikasi profesi ({fee}g).", 0);
                }
                // Setorkan biaya ke kas desa
                await RecordTreasuryTransactionInternalAsync(conn, userId, "profession_respec_fee", fee, $"Biaya retribusi ganti profesi ke '{newProfession}'");
            }
            var now = DateTime.UtcNow.ToString("o");
            // Kunci anti-eksploitasi: starter_kit_claimed TIDAK di-reset, tetap mempertahankan nilai yang ada
            await conn.ExecuteAsync(@"
                UPDATE user_professions 
                SET profession_type = @NewProf, 
                    last_respec_at = @Now, 
                    respec_count = respec_count + 1 
                WHERE user_id = @UserId;",
                new { UserId = userId, NewProf = newProfession.ToLowerInvariant(), Now = now });
            return (true, $"Profesi berhasil diubah menjadi {newProfession.ToUpper()}!", fee);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<bool> UpdateProfessionSkillLevelAsync(long userId, int skillLevel)
    {
        await _writeLock.WaitAsync();
        try
        {
            using var conn = CreateConnection();
            int rows = await conn.ExecuteAsync(@"
                UPDATE user_professions
                SET skill_level = @SkillLevel
                WHERE user_id = @UserId;",
                new { UserId = userId, SkillLevel = skillLevel });
            return rows > 0;
        }
        finally
        {
            _writeLock.Release();
        }
    }
    #endregion
    #region Client Mod Telemetry & Policies
    public async Task RecordClientModsAsync(long userId, string hwid, string ip, IEnumerable<StardewHeadless.Protocol.Packets.ClientModInfo> mods)
    {
        await _writeLock.WaitAsync();
        try
        {
            using var conn = CreateConnection();
            var now = DateTime.UtcNow.ToString("o");
            await conn.ExecuteAsync(@"
                UPDATE users 
                SET last_ip = COALESCE(@Ip, last_ip), 
                    last_hwid = COALESCE(@Hwid, last_hwid) 
                WHERE id = @UserId;",
                new { UserId = userId, Ip = ip, Hwid = hwid });
            await conn.ExecuteAsync("DELETE FROM client_mod_manifests WHERE user_id = @UserId;", new { UserId = userId });
            if (mods != null)
            {
                foreach (var m in mods)
                {
                    await conn.ExecuteAsync(@"
                        INSERT INTO client_mod_manifests (user_id, mod_id, mod_name, mod_version, has_binary, scanned_at)
                        VALUES (@UserId, @ModId, @ModName, @ModVersion, @HasBinary, @ScannedAt);",
                        new
                        {
                            UserId = userId,
                            ModId = m.ModId,
                            ModName = m.ModName,
                            ModVersion = m.Version,
                            HasBinary = m.HasBinary ? 1 : 0,
                            ScannedAt = now
                        });
                }
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }
    public async Task<ModAuditResult> CheckModAuditAsync(IEnumerable<StardewHeadless.Protocol.Packets.ClientModInfo> mods, bool allowAllMods)
    {
        var result = new ModAuditResult();
        if (mods == null)
        {
            result.IsAllowed = true;
            return result;
        }

        using var conn = CreateConnection();
        var allPolicies = (await conn.QueryAsync<ModPolicy>(
            "SELECT id, mod_id AS ModId, mod_name AS ModName, policy_type AS PolicyType, reason, created_at AS CreatedAt FROM mod_policies"
        )).ToList();

        var blacklistDict = new Dictionary<string, ModPolicy>(StringComparer.OrdinalIgnoreCase);
        var whitelistDict = new Dictionary<string, ModPolicy>(StringComparer.OrdinalIgnoreCase);

        foreach (var p in allPolicies)
        {
            if (string.Equals(p.PolicyType, "Blacklist", StringComparison.OrdinalIgnoreCase))
            {
                blacklistDict[p.ModId] = p;
            }
            else if (string.Equals(p.PolicyType, "Whitelist", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(p.PolicyType, "Required", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(p.PolicyType, "Optional", StringComparison.OrdinalIgnoreCase))
            {
                whitelistDict[p.ModId] = p;
            }
        }

        foreach (var mod in mods)
        {
            if (blacklistDict.TryGetValue(mod.ModId, out var blacklisted))
            {
                result.BlacklistViolations.Add(blacklisted);
            }
            else if (!whitelistDict.ContainsKey(mod.ModId))
            {
                result.UnapprovedMods.Add(mod);
            }
        }

        if (result.BlacklistViolations.Count > 0)
        {
            result.IsAllowed = false;
            var list = string.Join(", ", result.BlacklistViolations.Select(v => $"'{v.ModName}' ({v.ModId})"));
            result.WarningOrRejectionMessage = $"Akses Ditolak: Mod terlarang ditemukan di komputer Anda: {list}. Hapus mod tersebut dari folder Mods/ untuk dapat bermain di server ini!";
            return result;
        }

        if (result.UnapprovedMods.Count > 0)
        {
            var unapprovedList = string.Join(", ", result.UnapprovedMods.Select(m => $"'{m.ModName}' ({m.ModId})"));

            if (!allowAllMods)
            {
                // Mode Ketat (Strict Whitelist - Default): tolak mod di luar daftar resmi
                result.IsAllowed = false;
                result.WarningOrRejectionMessage = $"Akses Ditolak (Strict Whitelist): Server ini dikunci hanya untuk mod resmi yang terdaftar. Ditemukan mod di luar daftar resmi: {unapprovedList}. Hapus mod tersebut untuk masuk ke server!";
                return result;
            }
            else
            {
                // Mode Fleksibel (Permissive / Allow All Mods): izinkan masuk namun beri peringatan risiko bug/glitch
                result.IsAllowed = true;
                result.WarningOrRejectionMessage = $"[PERINGATAN SERVER] Anda menggunakan mod di luar daftar resmi: {unapprovedList}. Mod pihak ketiga ini belum diverifikasi dan berpotensi mengakibatkan game bug, desinkronisasi multiplayer, atau glitch visual.";
                return result;
            }
        }

        result.IsAllowed = true;
        result.WarningOrRejectionMessage = string.Empty;
        return result;
    }
    public async Task<List<ModPolicy>> CheckModViolationsAsync(IEnumerable<StardewHeadless.Protocol.Packets.ClientModInfo> mods)
    {
        var violations = new List<ModPolicy>();
        if (mods == null) return violations;
        using var conn = CreateConnection();
        var blacklists = await conn.QueryAsync<ModPolicy>(
            "SELECT id, mod_id AS ModId, mod_name AS ModName, policy_type AS PolicyType, reason, created_at AS CreatedAt FROM mod_policies WHERE policy_type = 'Blacklist'");
        var blacklistDict = new Dictionary<string, ModPolicy>(StringComparer.OrdinalIgnoreCase);
        foreach (var bp in blacklists)
        {
            blacklistDict[bp.ModId] = bp;
        }
        foreach (var mod in mods)
        {
            if (blacklistDict.TryGetValue(mod.ModId, out var policy))
            {
                violations.Add(policy);
            }
        }
        return violations;
    }
    public async Task<IEnumerable<ModPolicy>> GetModPoliciesAsync()
    {
        using var conn = CreateConnection();
        return await conn.QueryAsync<ModPolicy>(
            "SELECT id, mod_id AS ModId, mod_name AS ModName, policy_type AS PolicyType, reason, created_at AS CreatedAt FROM mod_policies ORDER BY id ASC");
    }
    public async Task AddModPolicyAsync(string modId, string modName, string policyType, string reason)
    {
        await _writeLock.WaitAsync();
        try
        {
            using var conn = CreateConnection();
            var now = DateTime.UtcNow.ToString("o");
            await conn.ExecuteAsync(@"
                INSERT INTO mod_policies (mod_id, mod_name, policy_type, reason, created_at)
                VALUES (@ModId, @ModName, @PolicyType, @Reason, @CreatedAt)
                ON CONFLICT(mod_id) DO UPDATE SET policy_type = @PolicyType, reason = @Reason, mod_name = @ModName;",
                new { ModId = modId.Trim(), ModName = modName.Trim(), PolicyType = policyType.Trim(), Reason = reason.Trim(), CreatedAt = now });
        }
        finally
        {
            _writeLock.Release();
        }
    }
    public async Task RemoveModPolicyAsync(string modId)
    {
        await _writeLock.WaitAsync();
        try
        {
            using var conn = CreateConnection();
            await conn.ExecuteAsync("DELETE FROM mod_policies WHERE mod_id = @ModId COLLATE NOCASE;", new { ModId = modId.Trim() });
        }
        finally
        {
            _writeLock.Release();
        }
    }
    public async Task<IEnumerable<ClientModManifest>> GetClientModsForUserAsync(long userId)
    {
        using var conn = CreateConnection();
        return await conn.QueryAsync<ClientModManifest>(@"
            SELECT id, user_id AS UserId, mod_id AS ModId, mod_name AS ModName, mod_version AS ModVersion, has_binary AS HasBinary, scanned_at AS ScannedAt 
            FROM client_mod_manifests 
            WHERE user_id = @UserId 
            ORDER BY id ASC",
            new { UserId = userId });
    }
    #endregion
    #region Treasury Ledger
    private static async Task<int> RecordTreasuryTransactionInternalAsync(IDbConnection conn, long? userId, string transactionType, int amount, string notes)
    {
        var currentBalance = await conn.QuerySingleAsync<int>(
            "SELECT COALESCE(SUM(amount), 0) FROM treasury_ledger");
        var newBalance = currentBalance + amount;
        await conn.ExecuteAsync(@"
            INSERT INTO treasury_ledger (user_id, transaction_type, amount, balance_after, notes, created_at)
            VALUES (@UserId, @TransactionType, @Amount, @BalanceAfter, @Notes, @CreatedAt);",
            new
            {
                UserId = userId,
                TransactionType = transactionType,
                Amount = amount,
                BalanceAfter = newBalance,
                Notes = notes,
                CreatedAt = DateTime.UtcNow.ToString("o")
            });
        return newBalance;
    }
    public async Task<int> RecordTreasuryTransactionAsync(long? userId, string transactionType, int amount, string notes)
    {
        await _writeLock.WaitAsync();
        try
        {
            using var conn = CreateConnection();
            return await RecordTreasuryTransactionInternalAsync(conn, userId, transactionType, amount, notes);
        }
        finally
        {
            _writeLock.Release();
        }
    }
    public async Task<int> GetTreasuryBalanceAsync()
    {
        using var conn = CreateConnection();
        var balance = await conn.QuerySingleAsync<int>(
            "SELECT COALESCE(SUM(amount), 0) FROM treasury_ledger");
        return balance;
    }
    public async Task<IEnumerable<TreasuryRecord>> GetRecentTreasuryTransactionsAsync(int limit = 50)
    {
        using var conn = CreateConnection();
        return await conn.QueryAsync<TreasuryRecord>(
            "SELECT id, user_id AS UserId, transaction_type AS TransactionType, amount, balance_after AS BalanceAfter, notes, created_at AS CreatedAt FROM treasury_ledger ORDER BY id DESC LIMIT @Limit",
            new { Limit = limit });
    }

    public async Task<int> EnsureInitialTreasuryGrantAsync(int defaultGrant = 1500)
    {
        int current = await GetTreasuryBalanceAsync();
        if (current == 0 && defaultGrant > 0)
        {
            return await RecordTreasuryTransactionAsync(null, "initial_grant", defaultGrant, "Inisialisasi kas awal desa Stardew Headless");
        }
        return current;
    }
    #endregion
    #region Community Projects
    public async Task<IEnumerable<CommunityProject>> GetProjectsAsync()
    {
        using var conn = CreateConnection();
        return await conn.QueryAsync<CommunityProject>(
            "SELECT id, project_name AS ProjectName, target_gold AS TargetGold, current_gold AS CurrentGold, status, completed_at AS CompletedAt FROM community_projects ORDER BY id ASC");
    }
    public async Task CreateProjectAsync(string name, int targetGold)
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync(
            "INSERT INTO community_projects (project_name, target_gold, current_gold, status) VALUES (@Name, @TargetGold, 0, 'InProgress')",
            new { Name = name, TargetGold = targetGold });
    }
    #endregion
    #region Bounty Contracts (Inter-Profession Economy)
    public async Task<IEnumerable<BountyContract>> GetBountyContractsAsync(string? status = null)
    {
        using var conn = CreateConnection();
        if (string.IsNullOrWhiteSpace(status))
        {
            return await conn.QueryAsync<BountyContract>(@"
                SELECT id AS Id, creator_user_id AS CreatorUserId, claimer_user_id AS ClaimerUserId,
                       item_name AS ItemName, quantity AS Quantity, reward_gold AS RewardGold,
                       status AS Status, created_at AS CreatedAt
                FROM bounty_contracts
                ORDER BY id DESC;");
        }
        return await conn.QueryAsync<BountyContract>(@"
            SELECT id AS Id, creator_user_id AS CreatorUserId, claimer_user_id AS ClaimerUserId,
                   item_name AS ItemName, quantity AS Quantity, reward_gold AS RewardGold,
                   status AS Status, created_at AS CreatedAt
            FROM bounty_contracts
            WHERE status = @Status
            ORDER BY id DESC;", new { Status = status });
    }

    public async Task<long> CreateBountyContractAsync(long creatorUserId, string itemName, int quantity, int rewardGold)
    {
        await _writeLock.WaitAsync();
        try
        {
            using var conn = CreateConnection();
            return await conn.ExecuteScalarAsync<long>(@"
                INSERT INTO bounty_contracts (creator_user_id, item_name, quantity, reward_gold, status, created_at)
                VALUES (@CreatorUserId, @ItemName, @Quantity, @RewardGold, 'Open', @CreatedAt);
                SELECT last_insert_rowid();",
                new
                {
                    CreatorUserId = creatorUserId,
                    ItemName = itemName,
                    Quantity = quantity,
                    RewardGold = rewardGold,
                    CreatedAt = DateTime.UtcNow.ToString("o")
                });
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<bool> ClaimBountyContractAsync(long contractId, long claimerUserId)
    {
        await _writeLock.WaitAsync();
        try
        {
            using var conn = CreateConnection();
            int rows = await conn.ExecuteAsync(@"
                UPDATE bounty_contracts
                SET claimer_user_id = @ClaimerUserId, status = 'InProgress'
                WHERE id = @Id AND status = 'Open';",
                new { Id = contractId, ClaimerUserId = claimerUserId });
            return rows > 0;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<bool> CompleteBountyContractAsync(long contractId)
    {
        await _writeLock.WaitAsync();
        try
        {
            using var conn = CreateConnection();
            int rows = await conn.ExecuteAsync(@"
                UPDATE bounty_contracts
                SET status = 'Completed'
                WHERE id = @Id AND status = 'InProgress';",
                new { Id = contractId });
            return rows > 0;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<bool> CancelBountyContractAsync(long contractId, long requesterUserId)
    {
        await _writeLock.WaitAsync();
        try
        {
            using var conn = CreateConnection();
            int rows = await conn.ExecuteAsync(@"
                UPDATE bounty_contracts
                SET status = 'Cancelled'
                WHERE id = @Id AND creator_user_id = @RequesterUserId AND status = 'Open';",
                new { Id = contractId, RequesterUserId = requesterUserId });
            return rows > 0;
        }
        finally
        {
            _writeLock.Release();
        }
    }
    #endregion

    #region Helper Hash & Salt
    private static string GenerateSalt()
    {
        byte[] saltBytes = new byte[16];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(saltBytes);
        return Convert.ToBase64String(saltBytes);
    }
    private static string HashPassword(string password, string salt)
    {
        using var sha256 = SHA256.Create();
        byte[] saltedBytes = Encoding.UTF8.GetBytes(password + salt);
        byte[] hashBytes = sha256.ComputeHash(saltedBytes);
        return Convert.ToBase64String(hashBytes);
    }
    #endregion
    public async Task RecordAntiCheatIncidentAsync(long farmerId, string username, string? hwid, string incidentType, int severityLevel, string actionTaken, string? details)
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync(@"
            INSERT INTO anticheat_incidents (farmer_id, username, hwid, incident_type, severity_level, action_taken, details, created_at)
            VALUES (@FarmerId, @Username, @Hwid, @IncidentType, @SeverityLevel, @ActionTaken, @Details, @CreatedAt);",
            new
            {
                FarmerId = farmerId,
                Username = username,
                Hwid = hwid,
                IncidentType = incidentType,
                SeverityLevel = severityLevel,
                ActionTaken = actionTaken,
                Details = details,
                CreatedAt = DateTime.UtcNow.ToString("o")
            });
    }

    public async Task<IEnumerable<AntiCheatIncident>> GetAntiCheatIncidentsAsync(int limit = 100)
    {
        using var conn = CreateConnection();
        return await conn.QueryAsync<AntiCheatIncident>(@"
            SELECT id, farmer_id AS FarmerId, username AS Username, hwid AS Hwid,
                   incident_type AS IncidentType, severity_level AS SeverityLevel,
                   action_taken AS ActionTaken, details AS Details, created_at AS CreatedAt
            FROM anticheat_incidents
            ORDER BY id DESC
            LIMIT @Limit;",
            new { Limit = limit });
    }

    public void Dispose()
    {
        _writeLock.Dispose();
    }
}