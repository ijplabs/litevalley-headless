using System;
using StardewValley;
using StardewValley.TerrainFeatures;

namespace StardewHeadless.Server.World;

public static class CropProtectionService
{
    public static void RecordPlanting(HoeDirt dirt, Farmer planter)
    {
        if (dirt == null || planter == null) return;

        dirt.modData["svheadless/planter_id"] = planter.UniqueMultiplayerID.ToString();
        dirt.modData["svheadless/planter_name"] = planter.Name;
    }

    public static bool CanPlayerModifyCrop(Farmer player, HoeDirt dirt)
    {
        if (player == null || dirt == null) return false;

        // Jika tidak ada tanaman yang sedang tumbuh, cangkul tanah bebas dilakukan
        if (dirt.crop == null) return true;

        // 0. Tanaman publik desa (tidak memiliki planter_id atau dilepas akibat pembersihan akun)
        if (!dirt.modData.TryGetValue("svheadless/planter_id", out var planterIdStr) || 
            string.IsNullOrWhiteSpace(planterIdStr) || 
            planterIdStr == "0" || 
            dirt.modData.ContainsKey("svheadless/public_crop"))
        {
            return true;
        }

        // 1. Pemilik tanaman asli selalu diizinkan merawat atau memanen
        if (long.TryParse(planterIdStr, out var planterId) && planterId == player.UniqueMultiplayerID)
        {
            return true;
        }

        // 2. Pemain dengan spesialisasi profesi "Farmer" memiliki otoritas agronomi desa
        if (player.modData.TryGetValue("svheadless/profession", out var prof) &&
            prof.Equals("farmer", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // 3. Jika tanaman sudah siap panen (mature), siapa pun diizinkan membantu memanen
        if (dirt.crop.currentPhase.Value >= dirt.crop.phaseDays.Count - 1)
        {
            return true;
        }

        // 4. Mencegah pemain bukan pemilik dan bukan Petani merusak bibit yang sedang tumbuh
        return false;
    }
}
