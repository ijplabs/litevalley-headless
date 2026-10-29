using System;

namespace StardewHeadless.Server.Database;

public class UserAccount
{
    public long Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string? Email { get; set; }
    public bool IsEmailVerified { get; set; } = false;
    public string? EmailVerificationCode { get; set; }
    public DateTime? EmailVerificationExpiresAt { get; set; }
    public string PasswordHash { get; set; } = string.Empty;
    public string Salt { get; set; } = string.Empty;
    public string? TotpSecret { get; set; }
    public string Role { get; set; } = "Player"; // Admin | Player
    public bool IsBanned { get; set; } = false;
    public DateTime? BannedAt { get; set; }
    public string? BanReason { get; set; }
    public string? LastIp { get; set; }
    public string? LastHwid { get; set; }
    public bool IsDeleted { get; set; } = false;
    public DateTime? DeletedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
    public long? LinkedFarmerId { get; set; }
    public string? LinkedFarmerName { get; set; }
    public bool IsGameLinked { get; set; } = false;
    public DateTime? GameLinkedAt { get; set; }
}

public class FarmhandCabin
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public int CabinIndex { get; set; }
    public string CabinName { get; set; } = string.Empty;
    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
}

public class UserProfession
{
    public long UserId { get; set; }
    public string ProfessionType { get; set; } = string.Empty;
    public bool StarterKitClaimed { get; set; } = false;
    public int SkillLevel { get; set; } = 0;
    public DateTime? LastRespecAt { get; set; }
    public int RespecCount { get; set; } = 0;
    public DateTime LockedAt { get; set; } = DateTime.UtcNow;
}

public class BannedIp
{
    public long Id { get; set; }
    public string IpAddress { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string BannedBy { get; set; } = "Admin";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAt { get; set; }
}

public class BannedHwid
{
    public long Id { get; set; }
    public string HwidHash { get; set; } = string.Empty;
    public string LastKnownUsername { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class ClientModManifest
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string ModId { get; set; } = string.Empty;
    public string ModName { get; set; } = string.Empty;
    public string ModVersion { get; set; } = string.Empty;
    public bool HasBinary { get; set; } = false;
    public DateTime ScannedAt { get; set; } = DateTime.UtcNow;
}

public class ModPolicy
{
    public long Id { get; set; }
    public string ModId { get; set; } = string.Empty;
    public string ModName { get; set; } = string.Empty;
    public string PolicyType { get; set; } = "Blacklist"; // Blacklist | Whitelist
    public string Reason { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class TreasuryRecord
{
    public long Id { get; set; }
    public long? UserId { get; set; }
    public string TransactionType { get; set; } = "Deposit"; // Deposit | Tax | Expense
    public int Amount { get; set; }
    public int BalanceAfter { get; set; }
    public string Notes { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class CommunityProject
{
    public long Id { get; set; }
    public string ProjectName { get; set; } = string.Empty;
    public int TargetGold { get; set; }
    public int CurrentGold { get; set; }
    public string Status { get; set; } = "InProgress"; // InProgress | Completed
    public DateTime? CompletedAt { get; set; }
}

public class BountyContract
{
    public long Id { get; set; }
    public long CreatorUserId { get; set; }
    public long? ClaimerUserId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public int RewardGold { get; set; }
    public string Status { get; set; } = "Open"; // Open | InProgress | Completed | Cancelled
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
