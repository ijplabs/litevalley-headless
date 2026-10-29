using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace StardewHeadless.Server.Database;

public interface IDatabaseService : IDisposable
{
    Task InitializeAsync();

    // User Management
    Task<IEnumerable<UserAccount>> GetAllUsersAsync();
    Task<UserAccount?> GetUserAsync(string username);
    Task<UserAccount?> GetUserByIdAsync(long userId);
    Task<UserAccount?> GetUserByEmailAsync(string email);
    Task<UserAccount?> GetUserByLinkedFarmerAsync(long farmerId, string farmerName);
    Task LinkUserToGameFarmerAsync(long userId, long farmerId, string farmerName);
    Task UnlinkUserGameFarmerAsync(long userId);
    Task ResetAllGameLinksAsync();
    Task<UserAccount> RegisterUserAsync(string username, string password, string role = "Player");
    Task<UserAccount> RegisterUserWithEmailAsync(string username, string email, string password, string role = "Player");
    Task SetEmailVerificationCodeAsync(long userId, string code, DateTime expiresAt);
    Task<(bool success, string message)> VerifyEmailCodeAsync(string emailOrUsername, string code);
    Task<bool> VerifyPasswordAsync(string username, string password);
    Task<string?> GetTotpSecretAsync(string username);
    Task<bool> VerifyTotpAsync(string username, string otpCode);
    Task UpdateLastLoginAsync(long userId);
    Task UpdateUserNetworkInfoAsync(long userId, string ip, string hwid);

    // Lifecycle: Purge / Delete
    Task<int> PurgePlayerAsync(long userId, string reason, int userWalletGold = 0);

    // Multi-tier Ban System
    Task BanUserAsync(long userId, string reason, bool banIp = true, bool banHwid = true, string bannedBy = "Admin");
    Task BanHwidAsync(string hwid, string username, string reason);
    Task UnbanUserAsync(long userId);
    Task<bool> IsIpBannedAsync(string ip);
    Task<bool> IsHwidBannedAsync(string hwid);
    Task<IEnumerable<BannedIp>> GetBannedIpsAsync();
    Task<IEnumerable<BannedHwid>> GetBannedHwidsAsync();

    // Cabin Binding
    Task AssignCabinAsync(long userId, int cabinIndex, string cabinName);
    Task<FarmhandCabin?> GetCabinByUserAsync(long userId);
    Task<FarmhandCabin?> GetCabinByIndexAsync(int cabinIndex);
    Task ResetCabinsAsync();

    // Profession 1-Time Lock & Anti-Exploit Respec
    Task<bool> LockProfessionAsync(long userId, string professionType);
    Task<UserProfession?> GetProfessionAsync(long userId);
    Task ClaimStarterKitAsync(long userId);
    Task<bool> UpdateProfessionSkillLevelAsync(long userId, int skillLevel);
    Task<(bool success, string message, int feeDeducted)> ChangeProfessionAsync(long userId, string newProfession, bool isAdminOverride, int currentWalletGold = 0);

    // Client Mod Telemetry & Mod Policies
    Task RecordClientModsAsync(long userId, string hwid, string ip, IEnumerable<StardewHeadless.Protocol.Packets.ClientModInfo> mods);
    Task<List<ModPolicy>> CheckModViolationsAsync(IEnumerable<StardewHeadless.Protocol.Packets.ClientModInfo> mods);
    Task<ModAuditResult> CheckModAuditAsync(IEnumerable<StardewHeadless.Protocol.Packets.ClientModInfo> mods, bool allowAllMods);
    Task<IEnumerable<ModPolicy>> GetModPoliciesAsync();
    Task AddModPolicyAsync(string modId, string modName, string policyType, string reason);
    Task RemoveModPolicyAsync(string modId);
    Task<IEnumerable<ClientModManifest>> GetClientModsForUserAsync(long userId);

    // Village Treasury Ledger
    Task<int> RecordTreasuryTransactionAsync(long? userId, string transactionType, int amount, string notes);
    Task<int> GetTreasuryBalanceAsync();
    Task<IEnumerable<TreasuryRecord>> GetRecentTreasuryTransactionsAsync(int limit = 50);
    Task<int> EnsureInitialTreasuryGrantAsync(int defaultGrant = 1500);

    // Community Projects
    Task<IEnumerable<CommunityProject>> GetProjectsAsync();
    Task CreateProjectAsync(string name, int targetGold);

    // Bounty Contracts (Inter-Profession Economy)
    Task<IEnumerable<BountyContract>> GetBountyContractsAsync(string? status = null);
    Task<long> CreateBountyContractAsync(long creatorUserId, string itemName, int quantity, int rewardGold);
    Task<bool> ClaimBountyContractAsync(long contractId, long claimerUserId);
    Task<bool> CompleteBountyContractAsync(long contractId);
    Task<bool> CancelBountyContractAsync(long contractId, long requesterUserId);

    // Anti-Cheat Incidents & Forensic Audit (Tahap 5)
    Task RecordAntiCheatIncidentAsync(long farmerId, string username, string? hwid, string incidentType, int severityLevel, string actionTaken, string? details);
    Task<IEnumerable<AntiCheatIncident>> GetAntiCheatIncidentsAsync(int limit = 100);
}
