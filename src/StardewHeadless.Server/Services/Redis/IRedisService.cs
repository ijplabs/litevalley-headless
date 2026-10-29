using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace StardewHeadless.Server.Services.Redis;

public interface IRedisService
{
    bool IsConnected { get; }
    Task<bool> SetStringAsync(string key, string value, TimeSpan? expiry = null);
    Task<string?> GetStringAsync(string key);
    Task<bool> RemoveAsync(string key);
    Task<long> PublishAsync(string channel, string message);
    Task SubscribeAsync(string channel, Action<string, string> handler);
    Task<bool> SetHashAsync(string key, string hashField, string value);
    Task<string?> GetHashAsync(string key, string hashField);
    Task<Dictionary<string, string>> GetHashAllAsync(string key);
}
