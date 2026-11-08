using StardewHeadless.Protocol.Logging;

namespace StardewHeadless.Server.Services;

/// <summary>
/// Alias kompatibilitas mundur untuk CrashReporter.
/// </summary>
public static class IjpTelemetryClient
{
    public static bool IsEnabled => CrashReporter.IsEnabled;

    public static void Initialize(StructuredLogger? logger = null)
    {
        CrashReporter.Initialize(logger);
    }
}
