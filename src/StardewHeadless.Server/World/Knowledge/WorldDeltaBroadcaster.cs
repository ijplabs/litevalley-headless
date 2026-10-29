using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using StardewHeadless.Protocol.Logging;

namespace StardewHeadless.Server.World.Knowledge;

public record WorldEntityDeltaEvent(
    string EventType, // "add", "remove", "update", "player_move"
    string LocationName,
    string EntityId,
    int TileX,
    int TileY,
    string? SpriteKey,
    object? ExtraData,
    long TimestampTicks
);

/// <summary>
/// Penyiaran pembaruan inkremental dunia (World Delta Events) melalui Server-Sent Events (SSE).
/// Menerapkan prinsip Zero-Subscriber = Zero-Overhead: jika tidak ada browser yang membuka
/// tab Radar Peta di Web Dashboard, pemindaian real-time tidak membebani tick loop server.
/// </summary>
public static class WorldDeltaBroadcaster
{
    private static readonly ConcurrentDictionary<string, Channel<WorldEntityDeltaEvent>> Subscribers = new();
    private static StructuredLogger? _logger;

    public static void Initialize(StructuredLogger? logger)
    {
        _logger = logger;
    }

    public static int SubscriberCount => Subscribers.Count;
    public static bool HasSubscribers => !Subscribers.IsEmpty;

    public static string RegisterSubscriber(Channel<WorldEntityDeltaEvent> channel)
    {
        string subId = Guid.NewGuid().ToString("N");
        Subscribers[subId] = channel;
        return subId;
    }

    public static void UnregisterSubscriber(string subId)
    {
        Subscribers.TryRemove(subId, out _);
    }

    /// <summary>
    /// Menyiarkan event perubahan visual/objek dunia ke seluruh browser yang berlangganan SSE.
    /// </summary>
    public static void Broadcast(string eventType, string location, string entityId, int tileX, int tileY, string? spriteKey = null, object? extra = null)
    {
        if (Subscribers.IsEmpty) return; // Zero-overhead guard

        var deltaEvent = new WorldEntityDeltaEvent(
            EventType: eventType,
            LocationName: location,
            EntityId: entityId,
            TileX: tileX,
            TileY: tileY,
            SpriteKey: spriteKey,
            ExtraData: extra,
            TimestampTicks: DateTime.UtcNow.Ticks
        );

        foreach (var sub in Subscribers)
        {
            sub.Value.Writer.TryWrite(deltaEvent);
        }
    }

    /// <summary>
    /// Menangani koneksi streaming HTTP Server-Sent Events (SSE) dari browser.
    /// </summary>
    public static async Task HandleSseStreamAsync(HttpContext context, string locationName, CancellationToken cancellationToken)
    {
        context.Response.Headers.Add("Content-Type", "text/event-stream");
        context.Response.Headers.Add("Cache-Control", "no-cache");
        context.Response.Headers.Add("Connection", "keep-alive");

        var channel = Channel.CreateBounded<WorldEntityDeltaEvent>(new BoundedChannelOptions(100)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });

        string subId = RegisterSubscriber(channel);
        _logger?.Info("WorldStream", $"Klien radar web tersambung ke stream SSE: {subId} (Lokasi: {locationName})");

        try
        {
            // Kirim event ping pembuka koneksi
            var initMsg = $"event: connected\ndata: {JsonSerializer.Serialize(new { subscriberId = subId, location = locationName })}\n\n";
            await context.Response.WriteAsync(initMsg, cancellationToken);
            await context.Response.Body.FlushAsync(cancellationToken);

            while (!cancellationToken.IsCancellationRequested)
            {
                var delta = await channel.Reader.ReadAsync(cancellationToken);
                if (string.Equals(delta.LocationName, locationName, StringComparison.OrdinalIgnoreCase))
                {
                    string json = JsonSerializer.Serialize(delta);
                    string ssePayload = $"event: delta\ndata: {json}\n\n";
                    await context.Response.WriteAsync(ssePayload, cancellationToken);
                    await context.Response.Body.FlushAsync(cancellationToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Klien menutup tab browser secara normal
        }
        catch (Exception ex)
        {
            _logger?.Warn("WorldStream", $"Koneksi stream SSE terputus: {ex.Message}");
        }
        finally
        {
            UnregisterSubscriber(subId);
            _logger?.Info("WorldStream", $"Klien radar web terputus: {subId}. Sisa subscriber aktif: {Subscribers.Count}");
        }
    }
}
