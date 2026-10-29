using System;
using System.Collections.Generic;
using StardewValley;
using StardewValley.Tools;

namespace StardewHeadless.Server.World;

public static class StarterKitDelivery
{
    public static List<string> DeliverKit(Farmer farmer, string profession)
    {
        var deliveredItems = new List<string>();
        if (farmer == null) return deliveredItems;

        var prof = profession.ToLowerInvariant();

        try
        {
            switch (prof)
            {
                case "farmer":
                    // Quality Sprinkler (645), 15x Seeds (472), 15x Fertilizer (368)
                    farmer.addItemToInventory(ItemRegistry.Create("(O)645", 1));
                    farmer.addItemToInventory(ItemRegistry.Create("(O)472", 15));
                    farmer.addItemToInventory(ItemRegistry.Create("(O)368", 15));
                    deliveredItems.AddRange(new[] { "1x Quality Sprinkler", "15x Seasonal Seeds", "15x Fertilizer" });
                    break;

                case "miner":
                    // Copper Pickaxe, 10x Torch (93), 3x Cherry Bomb (286), 1x Miner's Treat (243)
                    var pickaxe = new Pickaxe();
                    pickaxe.UpgradeLevel = 1; // Copper
                    farmer.addItemToInventory(pickaxe);
                    farmer.addItemToInventory(ItemRegistry.Create("(O)93", 10));
                    farmer.addItemToInventory(ItemRegistry.Create("(O)286", 3));
                    farmer.addItemToInventory(ItemRegistry.Create("(O)243", 1));
                    deliveredItems.AddRange(new[] { "1x Copper Pickaxe", "10x Torch", "3x Cherry Bomb", "1x Miner's Treat" });
                    break;

                case "fisherman":
                    // Fiberglass Rod, 20x Bait (685), 2x Crab Pot (710), 1x Dish o' The Sea (242)
                    var rod = new FishingRod(2); // Fiberglass Rod
                    farmer.addItemToInventory(rod);
                    farmer.addItemToInventory(ItemRegistry.Create("(O)685", 20));
                    farmer.addItemToInventory(ItemRegistry.Create("(O)710", 2));
                    farmer.addItemToInventory(ItemRegistry.Create("(O)242", 1));
                    deliveredItems.AddRange(new[] { "1x Fiberglass Rod", "20x Bait", "2x Crab Pot", "1x Dish o' The Sea" });
                    break;

                case "rancher":
                    // 30x Hay (178), 1x Milk Pail (296), 1x Shears (297)
                    farmer.addItemToInventory(ItemRegistry.Create("(O)178", 30));
                    farmer.addItemToInventory(ItemRegistry.Create("(O)296", 1));
                    farmer.addItemToInventory(ItemRegistry.Create("(O)297", 1));
                    deliveredItems.AddRange(new[] { "30x Hay", "1x Milk Pail", "1x Shears" });
                    break;

                case "forager":
                    // Copper Axe, Charcoal Kiln (114), 5x Tree Fertilizer (805), 5x Field Snack (403)
                    var axe = new Axe();
                    axe.UpgradeLevel = 1; // Copper
                    farmer.addItemToInventory(axe);
                    farmer.addItemToInventory(ItemRegistry.Create("(BC)114", 1));
                    farmer.addItemToInventory(ItemRegistry.Create("(O)805", 5));
                    farmer.addItemToInventory(ItemRegistry.Create("(O)403", 5));
                    deliveredItems.AddRange(new[] { "1x Copper Axe", "1x Charcoal Kiln", "5x Tree Fertilizer", "5x Field Snack" });
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[StarterKit] Gagal menambahkan item starter kit untuk {farmer.Name}: {ex.Message}");
        }

        return deliveredItems;
    }
}
