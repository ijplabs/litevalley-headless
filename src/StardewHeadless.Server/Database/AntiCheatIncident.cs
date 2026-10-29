namespace StardewHeadless.Server.Database;

public class AntiCheatIncident
{
    public long Id { get; set; }
    public long FarmerId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string? Hwid { get; set; }
    public string IncidentType { get; set; } = string.Empty;
    public int SeverityLevel { get; set; }
    public string ActionTaken { get; set; } = string.Empty;
    public string? Details { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
}
