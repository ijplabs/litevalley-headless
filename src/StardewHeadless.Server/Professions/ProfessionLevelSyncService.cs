using System;
using System.Threading.Tasks;
using StardewHeadless.Protocol.Logging;
using StardewHeadless.Protocol.Packets;
using StardewHeadless.Server.Database;
using StardewValley;
using StardewValley.Network;

namespace StardewHeadless.Server.Professions;

/// <summary>
/// Layanan sinkronisasi skill level profesi dan distribusi packet buff 0xA0 ke klien.
/// Menyelesaikan BUG-15 (SkillLevel Profesi Selalu 0).
/// </summary>
public static class ProfessionLevelSyncService
{
    private static StructuredLogger? _logger;

    public static void Initialize(StructuredLogger? logger)
    {
        _logger = logger;
    }

    public static ProfessionBuffSyncPacket BuildBuffSyncPacket(Farmer farmer, string profession)
    {
        int level = ProfessionPerkEngine.ResolveSkillLevel(farmer, profession);
        int tier = ProfessionPerkEngine.GetTier(level);
        var prof = profession?.ToLowerInvariant() ?? string.Empty;

        var packet = new ProfessionBuffSyncPacket
        {
            FarmerId = farmer.UniqueMultiplayerID,
            Profession = prof,
            SkillLevel = level,
            Tier = tier
        };

        switch (prof)
        {
            case "farmer":
                packet.ToolStaminaDiscountPercent = ProfessionPerkEngine.GetFarmerToolStaminaDiscountPercent(tier);
                break;
            case "miner":
                packet.ToolStaminaDiscountPercent = ProfessionPerkEngine.GetMinerPickaxeStaminaDiscountPercent(tier);
                packet.MinesDefenseBonus = ProfessionPerkEngine.GetMinesDefenseBonus(tier);
                break;
            case "fisherman":
                packet.BobberBarExtraPixels = ProfessionPerkEngine.GetBobberBarExtraPixels(tier);
                break;
            case "rancher":
                // Rancher perks are primarily handled in animal mechanics & shipping
                break;
            case "forager":
                packet.ToolStaminaDiscountPercent = ProfessionPerkEngine.GetForagerAxeStaminaDiscountPercent(tier);
                packet.ForestSpeedBonus = ProfessionPerkEngine.GetForestSpeedBonus(tier);
                break;
        }

        return packet;
    }

    public static async Task SyncFarmerSkillLevelAsync(Farmer farmer, IGameServer? server, IDatabaseService? db)
    {
        if (farmer == null) return;

        string prof = string.Empty;
        if (farmer.modData != null && farmer.modData.TryGetValue("svheadless/profession", out var pVal))
        {
            prof = pVal;
        }

        if (string.IsNullOrWhiteSpace(prof)) return;

        int skillLevel = ProfessionPerkEngine.ResolveSkillLevel(farmer, prof);
        int tier = ProfessionPerkEngine.GetTier(skillLevel);

        // 1. Simpan ke SQLite (Menyelesaikan BUG-15)
        if (db != null)
        {
            try
            {
                var user = await db.GetUserByLinkedFarmerAsync(farmer.UniqueMultiplayerID, farmer.Name) ?? await db.GetUserAsync(farmer.Name);
                if (user != null)
                {
                    await db.UpdateProfessionSkillLevelAsync(user.Id, skillLevel);
                }
            }
            catch (Exception ex)
            {
                _logger?.Warn("Professions", $"Gagal sinkronisasi skill level ke DB untuk '{farmer.Name}': {ex.Message}");
            }
        }

        // 2. Kirim paket 0xA0 ProfessionBuffSyncPacket ke client
        if (server != null)
        {
            try
            {
                var packet = BuildBuffSyncPacket(farmer, prof);
                byte[] payload = packet.Serialize();
                server.sendMessage(farmer.UniqueMultiplayerID, new OutgoingMessage(33, Game1.player.UniqueMultiplayerID, (object)payload));
            }
            catch (Exception ex)
            {
                _logger?.Warn("Professions", $"Gagal mengirim 0xA0 buff sync packet ke '{farmer.Name}': {ex.Message}");
            }
        }
    }

    public static async Task SyncAllConnectedFarmersAsync(IGameServer? server, IDatabaseService? db)
    {
        if (Game1.otherFarmers == null) return;

        foreach (var kvp in Game1.otherFarmers)
        {
            var farmer = kvp.Value;
            if (farmer != null)
            {
                await SyncFarmerSkillLevelAsync(farmer, server, db);
            }
        }
    }
}
