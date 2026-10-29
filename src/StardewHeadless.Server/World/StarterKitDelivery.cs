using System;
using System.Collections.Generic;
using StardewHeadless.Protocol.Packets;
using StardewValley;
using StardewValley.Tools;

namespace StardewHeadless.Server.World;

/// <summary>
/// Layanan pengiriman paket peralatan awal (Starter Gift Kit v2) untuk 5 profesi RPG.
/// Memperbaiki item Rancher (MilkPail + Shears, BUG-08), bibit adaptif musim untuk Farmer,
/// dan integrasi toolbelt slot 48..57 serta paket otoritatif 0x9A.
/// </summary>
public static class StarterKitDelivery
{
    public static string ResolveCurrentSeason()
    {
        try
        {
            if (Game1.currentSeason != null)
            {
                return Game1.currentSeason.ToLowerInvariant();
            }
        }
        catch
        {
            // Fallback jika dipanggil di luar konteks game aktif
        }
        return "spring";
    }

    /// <summary>
    /// Menghasilkan entri StarterKitItemEntry terstruktur untuk transmisi paket 0x9A.
    /// </summary>
    public static List<StarterKitItemEntry> GetStarterKitEntries(string profession, string? season = null)
    {
        var entries = new List<StarterKitItemEntry>();
        var prof = profession?.ToLowerInvariant() ?? "farmer";
        var activeSeason = season?.ToLowerInvariant() ?? ResolveCurrentSeason();

        switch (prof)
        {
            case "farmer":
                // Bibit menyesuaikan musim aktif
                string seedId = activeSeason switch
                {
                    "summer" => "481", // Blueberry Seeds
                    "fall" => "493",   // Cranberry Seeds
                    "winter" => "PowdermelonSeeds", // Powdermelon Seeds (1.6)
                    _ => "472"         // Parsnip Seeds (Spring)
                };

                entries.Add(new StarterKitItemEntry { ItemId = "645", ItemType = "(O)", Stack = 1 }); // Quality Sprinkler
                entries.Add(new StarterKitItemEntry { ItemId = seedId, ItemType = "(O)", Stack = 15 }); // 15x Bibit Musim
                entries.Add(new StarterKitItemEntry { ItemId = "368", ItemType = "(O)", Stack = 15 }); // 15x Basic Fertilizer
                entries.Add(new StarterKitItemEntry { ItemId = "8", ItemType = "(BC)", Stack = 1 });   // Scarecrow
                break;

            case "miner":
                entries.Add(new StarterKitItemEntry { ItemId = "Pickaxe", ItemType = "(T)", Stack = 1, UpgradeLevel = 1, TargetToolbelt = true }); // Copper Pickaxe
                entries.Add(new StarterKitItemEntry { ItemId = "93", ItemType = "(O)", Stack = 15 });  // 15x Torch
                entries.Add(new StarterKitItemEntry { ItemId = "286", ItemType = "(O)", Stack = 5 });  // 5x Cherry Bomb
                entries.Add(new StarterKitItemEntry { ItemId = "243", ItemType = "(O)", Stack = 2 });  // 2x Miner's Treat
                entries.Add(new StarterKitItemEntry { ItemId = "13", ItemType = "(BC)", Stack = 1 });  // 1x Furnace
                break;

            case "fisherman":
                entries.Add(new StarterKitItemEntry { ItemId = "FishingRod", ItemType = "(T)", Stack = 1, UpgradeLevel = 2, TargetToolbelt = true }); // Fiberglass Rod
                entries.Add(new StarterKitItemEntry { ItemId = "685", ItemType = "(O)", Stack = 30 }); // 30x Bait
                entries.Add(new StarterKitItemEntry { ItemId = "710", ItemType = "(O)", Stack = 2 });  // 2x Crab Pot
                entries.Add(new StarterKitItemEntry { ItemId = "242", ItemType = "(O)", Stack = 2 });  // 2x Dish o' The Sea
                entries.Add(new StarterKitItemEntry { ItemId = "20", ItemType = "(BC)", Stack = 1 });  // 1x Recycling Machine
                break;

            case "rancher":
                // FIX KRITIS BUG-08: MilkPail & Shears sebagai Tool, bukan Salmonberry/GrassStarter
                entries.Add(new StarterKitItemEntry { ItemId = "MilkPail", ItemType = "(T)", Stack = 1, TargetToolbelt = true });
                entries.Add(new StarterKitItemEntry { ItemId = "Shears", ItemType = "(T)", Stack = 1, TargetToolbelt = true });
                entries.Add(new StarterKitItemEntry { ItemId = "178", ItemType = "(O)", Stack = 40 }); // 40x Hay
                entries.Add(new StarterKitItemEntry { ItemId = "297", ItemType = "(O)", Stack = 10 }); // 10x Grass Starter
                entries.Add(new StarterKitItemEntry { ItemId = "104", ItemType = "(BC)", Stack = 1 }); // 1x Heater
                break;

            case "forager":
                entries.Add(new StarterKitItemEntry { ItemId = "Axe", ItemType = "(T)", Stack = 1, UpgradeLevel = 1, TargetToolbelt = true }); // Copper Axe
                entries.Add(new StarterKitItemEntry { ItemId = "105", ItemType = "(BC)", Stack = 1 }); // 1x Tapper
                entries.Add(new StarterKitItemEntry { ItemId = "114", ItemType = "(BC)", Stack = 1 }); // 1x Charcoal Kiln
                entries.Add(new StarterKitItemEntry { ItemId = "805", ItemType = "(O)", Stack = 8 });  // 8x Tree Fertilizer
                entries.Add(new StarterKitItemEntry { ItemId = "403", ItemType = "(O)", Stack = 5 });  // 5x Field Snack
                break;
        }

        return entries;
    }

    /// <summary>
    /// Membuat paket otoritatif 0x9A StarterKitGrantPacket.
    /// </summary>
    public static StarterKitGrantPacket CreateGrantPacket(long farmerId, string profession, string? season = null, string authSignature = "")
    {
        var activeSeason = season?.ToLowerInvariant() ?? ResolveCurrentSeason();
        return new StarterKitGrantPacket
        {
            FarmerId = farmerId,
            Profession = profession?.ToLowerInvariant() ?? "farmer",
            Season = activeSeason,
            AuthSignature = authSignature,
            Items = GetStarterKitEntries(profession ?? "farmer", activeSeason)
        };
    }

    /// <summary>
    /// Mengirimkan Starter Kit langsung ke karakter Farmer di memori server.
    /// Memastikan alat kerja otomatis masuk ke slot Dedicated Toolbelt (48..57) jika tersedia.
    /// </summary>
    public static List<string> DeliverKit(Farmer farmer, string profession, string? season = null)
    {
        var deliveredItems = new List<string>();
        if (farmer == null) return deliveredItems;

        var prof = profession?.ToLowerInvariant() ?? "farmer";
        var activeSeason = season?.ToLowerInvariant() ?? ResolveCurrentSeason();

        try
        {
            switch (prof)
            {
                case "farmer":
                {
                    string seedId = activeSeason switch
                    {
                        "summer" => "481",
                        "fall" => "493",
                        "winter" => "PowdermelonSeeds",
                        _ => "472"
                    };

                    AddItem(farmer, ItemRegistry.Create("(O)645", 1));
                    AddItem(farmer, CreateSeasonSeedItem(seedId, 15));
                    AddItem(farmer, ItemRegistry.Create("(O)368", 15));
                    AddItem(farmer, ItemRegistry.Create("(BC)8", 1));
                    deliveredItems.AddRange(new[] { "1x Quality Sprinkler", $"15x {activeSeason} Seeds", "15x Basic Fertilizer", "1x Scarecrow" });
                    break;
                }

                case "miner":
                {
                    var pickaxe = new Pickaxe { UpgradeLevel = 1 };
                    AddToolToToolbeltOrInventory(farmer, pickaxe);
                    AddItem(farmer, ItemRegistry.Create("(O)93", 15));
                    AddItem(farmer, ItemRegistry.Create("(O)286", 5));
                    AddItem(farmer, ItemRegistry.Create("(O)243", 2));
                    AddItem(farmer, ItemRegistry.Create("(BC)13", 1));
                    deliveredItems.AddRange(new[] { "1x Copper Pickaxe (Toolbelt)", "15x Torch", "5x Cherry Bomb", "2x Miner's Treat", "1x Furnace" });
                    break;
                }

                case "fisherman":
                {
                    var rod = new FishingRod(2);
                    AddToolToToolbeltOrInventory(farmer, rod);
                    AddItem(farmer, ItemRegistry.Create("(O)685", 30));
                    AddItem(farmer, ItemRegistry.Create("(O)710", 2));
                    AddItem(farmer, ItemRegistry.Create("(O)242", 2));
                    AddItem(farmer, ItemRegistry.Create("(BC)20", 1));
                    deliveredItems.AddRange(new[] { "1x Fiberglass Rod (Toolbelt)", "30x Bait", "2x Crab Pot", "2x Dish o' The Sea", "1x Recycling Machine" });
                    break;
                }

                case "rancher":
                {
                    // FIX KRITIS BUG-08: MilkPail & Shears
                    var pail = new MilkPail();
                    var shears = new Shears();
                    AddToolToToolbeltOrInventory(farmer, pail);
                    AddToolToToolbeltOrInventory(farmer, shears);
                    AddItem(farmer, ItemRegistry.Create("(O)178", 40));
                    AddItem(farmer, ItemRegistry.Create("(O)297", 10));
                    AddItem(farmer, ItemRegistry.Create("(BC)104", 1));
                    deliveredItems.AddRange(new[] { "1x Milk Pail (Toolbelt)", "1x Shears (Toolbelt)", "40x Hay", "10x Grass Starter", "1x Heater" });
                    break;
                }

                case "forager":
                {
                    var axe = new Axe { UpgradeLevel = 1 };
                    AddToolToToolbeltOrInventory(farmer, axe);
                    AddItem(farmer, ItemRegistry.Create("(BC)105", 1));
                    AddItem(farmer, ItemRegistry.Create("(BC)114", 1));
                    AddItem(farmer, ItemRegistry.Create("(O)805", 8));
                    AddItem(farmer, ItemRegistry.Create("(O)403", 5));
                    deliveredItems.AddRange(new[] { "1x Copper Axe (Toolbelt)", "1x Tapper", "1x Charcoal Kiln", "8x Tree Fertilizer", "5x Field Snack" });
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[StarterKit] Fallback graceful headless untuk {farmer?.Name}: {ex.Message}");
        }

        return deliveredItems;
    }

    private static Item CreateSeasonSeedItem(string seedId, int stack)
    {
        try
        {
            var item = ItemRegistry.Create($"(O){seedId}", stack);
            if (item != null) return item;
        }
        catch
        {
            // Fallback jika PowdermelonSeeds tidak ditemukan
        }
        return ItemRegistry.Create("(O)472", stack); // Fallback ke Parsnip Seeds
    }

    private static void EnsureInventoryCapacity(Farmer farmer)
    {
        if (farmer == null) return;
        if (farmer.maxItems.Value <= 0)
        {
            farmer.maxItems.Value = 36;
        }
        while (farmer.Items.Count < farmer.maxItems.Value)
        {
            farmer.Items.Add(null);
        }
    }

    private static void AddItem(Farmer farmer, Item item)
    {
        if (item == null || farmer == null) return;
        EnsureInventoryCapacity(farmer);

        // Langsung masukkan ke slot kosong pertama di memori server
        if (farmer.Items == null) return;
        for (int i = 0; i < farmer.Items.Count; i++)
        {
            if (farmer.Items[i] == null)
            {
                farmer.Items[i] = item;
                return;
            }
        }
        farmer.Items.Add(item);
    }

    private static void AddToolToToolbeltOrInventory(Farmer farmer, Tool tool)
    {
        if (tool == null || farmer == null) return;
        EnsureInventoryCapacity(farmer);

        // Cek apakah farmer memiliki 58 slot (Tahap 1 Native Toolbelt 48..57)
        if (farmer.Items != null && farmer.Items.Count >= 58)
        {
            for (int i = 48; i < 58; i++)
            {
                if (farmer.Items[i] == null)
                {
                    farmer.Items[i] = tool;
                    return;
                }
            }
        }

        // Fallback: Masukkan ke slot tas biasa yang masih kosong
        if (farmer.Items == null) return;
        for (int i = 0; i < farmer.Items.Count; i++)
        {
            if (farmer.Items[i] == null)
            {
                farmer.Items[i] = tool;
                return;
            }
        }
        farmer.Items.Add(tool);
    }
}
