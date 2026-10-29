using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using StackExchange.Redis;

namespace StardewHeadless.Server.Services.Redis;

public class RedisService : IRedisService, IDisposable
{
    private ConnectionMultiplexer? _redis;
    private IDatabase? _db;
    private ISubscriber? _sub;
    private readonly bool _enabled;
    private readonly string _host;
    private readonly int _port;
    private readonly string? _password;

    // Resilient fallback storage ketika Redis tidak aktif/unreachable
    private readonly ConcurrentDictionary<string, (string Value, DateTime? Expiry)> _memoryFallback = new();
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, string>> _hashFallback = new();
    private readonly ConcurrentDictionary<string, List<Action<string, string>>> _pubSubFallback = new();

    public bool IsConnected => _redis != null && _redis.IsConnected;

    public RedisService(string? host = null, int port = 6379, string? password = null)
    {
        _host = host ?? Environment.GetEnvironmentVariable("REDIS_HOST") ?? string.Empty;
        
        if (int.TryParse(Environment.GetEnvironmentVariable("REDIS_PORT"), out int envPort))
        {
            _port = envPort;
        }
        else
        {
            _port = port;
        }

        _password = password ?? Environment.GetEnvironmentVariable("REDIS_PASSWORD");
        _enabled = !string.IsNullOrWhiteSpace(_host);

        if (!_enabled)
        {
            Console.WriteLine("[Redis] Host tidak dikonfigurasi. Berjalan dalam mode Resilient In-Memory Fallback.");
            return;
        }

        InitializeRedis();
    }

    private void InitializeRedis()
    {
        try
        {
            var config = new ConfigurationOptions
            {
                EndPoints = { $"{_host}:{_port}" },
                Password = string.IsNullOrEmpty(_password) ? null : _password,
                AbortOnConnectFail = false,
                ConnectRetry = 3,
                ConnectTimeout = 3000,
                SyncTimeout = 2000,
                KeepAlive = 60
            };

            Console.WriteLine($"[Redis] Menghubungkan ke Redis di {_host}:{_port}...");
            _redis = ConnectionMultiplexer.Connect(config);
            _redis.ConnectionFailed += (sender, args) =>
            {
                Console.WriteLine($"[Redis] Koneksi terputus: {args.FailureType}. Otomatis beralih ke memory fallback.");
            };
            _redis.ConnectionRestored += (sender, args) =>
            {
                Console.WriteLine("[Redis] Koneksi berhasil dipulihkan!");
            };

            _db = _redis.GetDatabase();
            _sub = _redis.GetSubscriber();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Redis] Gagal inisialisasi: {ex.Message}. Berjalan dalam mode memory fallback.");
        }
    }

    public async Task<bool> SetStringAsync(string key, string value, TimeSpan? expiry = null)
    {
        try
        {
            if (IsConnected && _db != null)
            {
                return await _db.StringSetAsync(key, value, expiry);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Redis] Error SetString '{key}': {ex.Message}");
        }

        // Memory Fallback
        var expireAt = expiry.HasValue ? DateTime.UtcNow.Add(expiry.Value) : (DateTime?)null;
        _memoryFallback[key] = (value, expireAt);
        return true;
    }

    public async Task<string?> GetStringAsync(string key)
    {
        try
        {
            if (IsConnected && _db != null)
            {
                var val = await _db.StringGetAsync(key);
                if (val.HasValue)
                {
                    return val.ToString();
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Redis] Error GetString '{key}': {ex.Message}");
        }

        // Memory Fallback
        if (_memoryFallback.TryGetValue(key, out var entry))
        {
            if (entry.Expiry == null || entry.Expiry.Value > DateTime.UtcNow)
            {
                return entry.Value;
            }
            _memoryFallback.TryRemove(key, out _);
        }

        return null;
    }

    public async Task<bool> RemoveAsync(string key)
    {
        try
        {
            if (IsConnected && _db != null)
            {
                return await _db.KeyDeleteAsync(key);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Redis] Error KeyDelete '{key}': {ex.Message}");
        }

        _memoryFallback.TryRemove(key, out _);
        _hashFallback.TryRemove(key, out _);
        return true;
    }

    public async Task<long> PublishAsync(string channel, string message)
    {
        try
        {
            if (IsConnected && _sub != null)
            {
                return await _sub.PublishAsync(RedisChannel.Literal(channel), message);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Redis] Error Publish '{channel}': {ex.Message}");
        }

        // In-memory pub/sub dispatch
        if (_pubSubFallback.TryGetValue(channel, out var handlers))
        {
            lock (handlers)
            {
                foreach (var handler in handlers)
                {
                    try
                    {
                        handler(channel, message);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Redis] Exception in pubsub handler: {ex.Message}");
                    }
                }
                return handlers.Count;
            }
        }

        return 0;
    }

    public async Task SubscribeAsync(string channel, Action<string, string> handler)
    {
        try
        {
            if (IsConnected && _sub != null)
            {
                await _sub.SubscribeAsync(RedisChannel.Literal(channel), (ch, msg) => handler(ch.ToString(), msg.ToString()));
                return;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Redis] Error Subscribe '{channel}': {ex.Message}");
        }

        // In-memory pub/sub registration
        var list = _pubSubFallback.GetOrAdd(channel, _ => new List<Action<string, string>>());
        lock (list)
        {
            list.Add(handler);
        }
    }

    public async Task<bool> SetHashAsync(string key, string hashField, string value)
    {
        try
        {
            if (IsConnected && _db != null)
            {
                return await _db.HashSetAsync(key, hashField, value);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Redis] Error SetHash '{key}/{hashField}': {ex.Message}");
        }

        var dict = _hashFallback.GetOrAdd(key, _ => new ConcurrentDictionary<string, string>());
        dict[hashField] = value;
        return true;
    }

    public async Task<string?> GetHashAsync(string key, string hashField)
    {
        try
        {
            if (IsConnected && _db != null)
            {
                var val = await _db.HashGetAsync(key, hashField);
                if (val.HasValue)
                {
                    return val.ToString();
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Redis] Error GetHash '{key}/{hashField}': {ex.Message}");
        }

        if (_hashFallback.TryGetValue(key, out var dict) && dict.TryGetValue(hashField, out var value))
        {
            return value;
        }

        return null;
    }

    public async Task<Dictionary<string, string>> GetHashAllAsync(string key)
    {
        try
        {
            if (IsConnected && _db != null)
            {
                var entries = await _db.HashGetAllAsync(key);
                return entries.ToDictionary(e => e.Name.ToString(), e => e.Value.ToString());
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Redis] Error HashGetAll '{key}': {ex.Message}");
        }

        if (_hashFallback.TryGetValue(key, out var dict))
        {
            return new Dictionary<string, string>(dict);
        }

        return new Dictionary<string, string>();
    }

    public void Dispose()
    {
        try
        {
            _redis?.Dispose();
        }
        catch
        {
            // Ignore
        }
    }
}
