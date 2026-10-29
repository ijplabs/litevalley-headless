using System;
using StardewValley;

namespace StardewHeadless.Server.World;

public static class FarmAnimalManager
{
    public static void ApplyPetBonus(FarmAnimal animal, Farmer player)
    {
        if (animal == null || player == null) return;

        // Cek profesi pemain
        bool isRancher = player.modData.TryGetValue("svheadless/profession", out var prof) &&
                         prof.Equals("rancher", StringComparison.OrdinalIgnoreCase);

        if (isRancher)
        {
            // Bonus Peternak: Pertumbuhan friendship & kebahagiaan berlipat ganda
            animal.friendshipTowardFarmer.Value = Math.Min(1000, animal.friendshipTowardFarmer.Value + 40);
            animal.happiness.Value = 255; // Kebahagiaan maksimal
            animal.wasPet.Value = true;
        }
    }
}
