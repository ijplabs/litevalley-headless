using System.Collections.Generic;
using StardewHeadless.Protocol.Packets;

namespace StardewHeadless.Server.Database;

/// <summary>
/// Hasil evaluasi audit integritas mod klien terhadap kebijakan server (Strict Whitelist vs Permissive).
/// </summary>
public class ModAuditResult
{
    public bool IsAllowed { get; set; }
    public List<ModPolicy> BlacklistViolations { get; set; } = new();
    public List<ClientModInfo> UnapprovedMods { get; set; } = new();
    public string WarningOrRejectionMessage { get; set; } = string.Empty;
}
