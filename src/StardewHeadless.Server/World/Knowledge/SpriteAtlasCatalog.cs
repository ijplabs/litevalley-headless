using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.Characters;
using StardewValley.Objects;
using StardewValley.TerrainFeatures;

namespace StardewHeadless.Server.World.Knowledge;

/// <summary>
/// Mendeskripsikan metadata pemetaan visual sebuah sprite ke dalam spritesheet asli game.
/// Selaras dengan LEGAL_COMPLIANCE.md: tidak mendistribusikan gambar binary di Git, melainkan
/// memetakan koordinat piksel (SourceRect), dimensi tile, dan warna semantik secara deterministik.
/// </summary>
public record SpriteDescriptor(
    string SpriteKey,
    string TextureName,
    int SourceX,
    int SourceY,
    int SourceWidth,
    int SourceHeight,
    int FootprintWidth,
    int FootprintHeight,
    int DrawOffsetX,
    int DrawOffsetY,
    string SemanticColorHex,
    string Category,
    string DisplayName
);

/// <summary>
/// Katalog resolusi texture atlas runtime untuk seluruh objek visual dunia Stardew Valley & SVE.
/// Memetakan pohon, tanaman per fase tumbuh, batu tambang, bijih mineral, bongkahan besar,
/// mesin Automate, peti, bangunan, hewan ternak, dan karakter.
/// </summary>
public static class SpriteAtlasCatalog
{
    private static readonly ConcurrentDictionary<string, SpriteDescriptor> Descriptors = new(StringComparer.OrdinalIgnoreCase);

    static SpriteAtlasCatalog()
    {
        InitializeStaticCatalog();
    }

    private static void InitializeStaticCatalog()
    {
        // 1. Pohon Liar (Wild Trees) - Base Spritesheets: TerrainFeatures/tree1_spring, tree2_spring, etc.
        // TreeType: 1 = Oak, 2 = Maple, 3 = Pine, 7 = Mahogany, 8 = Mushroom, 10 = Mystic
        RegisterDescriptor(new SpriteDescriptor("tree:oak:stage5", "TerrainFeatures/tree1_spring", 0, 0, 48, 96, 1, 1, 0, -2, "#2d6a4f", "Tree", "Oak Tree (Dewasa)"));
        RegisterDescriptor(new SpriteDescriptor("tree:oak:stump", "TerrainFeatures/tree1_spring", 0, 96, 48, 32, 1, 1, 0, 0, "#7f4f24", "Tree", "Oak Stump"));
        RegisterDescriptor(new SpriteDescriptor("tree:maple:stage5", "TerrainFeatures/tree2_spring", 0, 0, 48, 96, 1, 1, 0, -2, "#52b788", "Tree", "Maple Tree (Dewasa)"));
        RegisterDescriptor(new SpriteDescriptor("tree:maple:stump", "TerrainFeatures/tree2_spring", 0, 96, 48, 32, 1, 1, 0, 0, "#936639", "Tree", "Maple Stump"));
        RegisterDescriptor(new SpriteDescriptor("tree:pine:stage5", "TerrainFeatures/tree3_spring", 0, 0, 48, 96, 1, 1, 0, -2, "#1b4332", "Tree", "Pine Tree (Dewasa)"));
        RegisterDescriptor(new SpriteDescriptor("tree:pine:stump", "TerrainFeatures/tree3_spring", 0, 96, 48, 32, 1, 1, 0, 0, "#582f0e", "Tree", "Pine Stump"));
        RegisterDescriptor(new SpriteDescriptor("tree:mahogany:stage5", "TerrainFeatures/tree7_spring", 0, 0, 48, 96, 1, 1, 0, -2, "#6f1d1b", "Tree", "Mahogany Tree (Dewasa)"));
        RegisterDescriptor(new SpriteDescriptor("tree:mahogany:stump", "TerrainFeatures/tree7_spring", 0, 96, 48, 32, 1, 1, 0, 0, "#4a154b", "Tree", "Mahogany Stump"));
        RegisterDescriptor(new SpriteDescriptor("tree:mushroom:stage5", "TerrainFeatures/mushroom_tree", 0, 0, 48, 96, 1, 1, 0, -2, "#e63946", "Tree", "Mushroom Tree"));
        RegisterDescriptor(new SpriteDescriptor("tree:mystic:stage5", "TerrainFeatures/tree_mystic", 0, 0, 48, 96, 1, 1, 0, -2, "#7209b7", "Tree", "Mystic Tree"));
        RegisterDescriptor(new SpriteDescriptor("tree:sapling", "TerrainFeatures/tree1_spring", 32, 96, 16, 16, 1, 1, 0, 0, "#74c69d", "Tree", "Bibit Pohon (Sapling)"));

        // 2. Rumput & Gulma (Grass & Weeds)
        RegisterDescriptor(new SpriteDescriptor("grass:normal", "TerrainFeatures/grass", 0, 0, 15, 20, 1, 1, 0, 0, "#40916c", "Grass", "Rumput Hijau Liar"));
        RegisterDescriptor(new SpriteDescriptor("grass:blue", "TerrainFeatures/grass", 0, 40, 15, 20, 1, 1, 0, 0, "#48cae4", "Grass", "Rumput Biru (Blue Grass 1.6)"));
        RegisterDescriptor(new SpriteDescriptor("debris:weeds", "Maps/springobjects", 0, 32, 16, 16, 1, 1, 0, 0, "#52b788", "Debris", "Gulma (Weeds)"));
        RegisterDescriptor(new SpriteDescriptor("debris:twig", "Maps/springobjects", 160, 176, 16, 16, 1, 1, 0, 0, "#a68a64", "Debris", "Ranting Kayu (Twig)"));
        RegisterDescriptor(new SpriteDescriptor("debris:stone", "Maps/springobjects", 208, 208, 16, 16, 1, 1, 0, 0, "#8d99ae", "Debris", "Batu Kerikil (Stone)"));

        // 3. Batuan Tambang & Bijih Mineral (Stones & Ore Nodes)
        RegisterDescriptor(new SpriteDescriptor("ore:copper", "Maps/springobjects", 112, 480, 16, 16, 1, 1, 0, 0, "#d97706", "Mineral", "Bijih Tembaga (Copper Node)"));
        RegisterDescriptor(new SpriteDescriptor("ore:iron", "Maps/springobjects", 160, 176, 16, 16, 1, 1, 0, 0, "#94a3b8", "Mineral", "Bijih Besi (Iron Node)"));
        RegisterDescriptor(new SpriteDescriptor("ore:gold", "Maps/springobjects", 112, 496, 16, 16, 1, 1, 0, 0, "#eab308", "Mineral", "Bijih Emas (Gold Node)"));
        RegisterDescriptor(new SpriteDescriptor("ore:iridium", "Maps/springobjects", 128, 496, 16, 16, 1, 1, 0, 0, "#a855f7", "Mineral", "Bijih Iridium (Iridium Node)"));
        RegisterDescriptor(new SpriteDescriptor("ore:cinder_shard", "Maps/springobjects", 112, 544, 16, 16, 1, 1, 0, 0, "#f97316", "Mineral", "Cinder Shard Node"));
        RegisterDescriptor(new SpriteDescriptor("ore:radioactive", "Maps/springobjects", 144, 496, 16, 16, 1, 1, 0, 0, "#84cc16", "Mineral", "Radioactive Node"));
        RegisterDescriptor(new SpriteDescriptor("ore:mystic_stone", "Maps/springobjects", 112, 512, 16, 16, 1, 1, 0, 0, "#06b6d4", "Mineral", "Mystic Stone"));
        RegisterDescriptor(new SpriteDescriptor("ore:gem_diamond", "Maps/springobjects", 0, 16, 16, 16, 1, 1, 0, 0, "#67e8f9", "Mineral", "Batu Berlian (Diamond Node)"));

        // 4. Bongkahan Besar (Resource Clumps & Giant Crops - 2x2 & 3x3 tiles)
        RegisterDescriptor(new SpriteDescriptor("clump:large_stump:600", "TileSheets/Craftables", 0, 0, 32, 32, 2, 2, 0, 0, "#78350f", "Clump", "Tunggul Kayu Keras (Large Stump)"));
        RegisterDescriptor(new SpriteDescriptor("clump:hollow_log:602", "TileSheets/Craftables", 32, 0, 32, 32, 2, 2, 0, 0, "#451a03", "Clump", "Batang Kayu Keras Raksasa (Hollow Log)"));
        RegisterDescriptor(new SpriteDescriptor("clump:boulder:672", "TileSheets/Craftables", 64, 0, 32, 32, 2, 2, 0, 0, "#64748b", "Clump", "Batu Cadas Raksasa (Boulder)"));
        RegisterDescriptor(new SpriteDescriptor("clump:meteorite:622", "TileSheets/Craftables", 96, 0, 32, 32, 2, 2, 0, 0, "#312e81", "Clump", "Batu Meteorit Langka (Meteorite)"));
        RegisterDescriptor(new SpriteDescriptor("clump:giant_cauliflower", "TileSheets/giantCrops", 0, 0, 48, 64, 3, 3, 0, -1, "#ecfccb", "Clump", "Kembang Kol Raksasa (Giant Cauliflower)"));
        RegisterDescriptor(new SpriteDescriptor("clump:giant_melon", "TileSheets/giantCrops", 48, 0, 48, 64, 3, 3, 0, -1, "#f43f5e", "Clump", "Melon Raksasa (Giant Melon)"));
        RegisterDescriptor(new SpriteDescriptor("clump:giant_pumpkin", "TileSheets/giantCrops", 96, 0, 48, 64, 3, 3, 0, -1, "#ea580c", "Clump", "Labu Raksasa (Giant Pumpkin)"));

        // 5. Mesin Pabrik, Peti, & Pagar (Machines, Chests, Gates)
        RegisterDescriptor(new SpriteDescriptor("machine:furnace", "TileSheets/Craftables", 208, 0, 16, 32, 1, 1, 0, -1, "#b45309", "Machine", "Furnace (Pelebur Logam)"));
        RegisterDescriptor(new SpriteDescriptor("machine:keg", "TileSheets/Craftables", 192, 16, 16, 32, 1, 1, 0, -1, "#854d0e", "Machine", "Keg (Tong Fermentasi)"));
        RegisterDescriptor(new SpriteDescriptor("machine:preserves_jar", "TileSheets/Craftables", 240, 16, 16, 32, 1, 1, 0, -1, "#a16207", "Machine", "Preserves Jar (Pembuat Selai)"));
        RegisterDescriptor(new SpriteDescriptor("machine:cheese_press", "TileSheets/Craftables", 256, 16, 16, 32, 1, 1, 0, -1, "#ca8a04", "Machine", "Cheese Press (Pembuat Keju)"));
        RegisterDescriptor(new SpriteDescriptor("machine:mayonnaise_machine", "TileSheets/Craftables", 384, 16, 16, 32, 1, 1, 0, -1, "#fef08a", "Machine", "Mayonnaise Maker"));
        RegisterDescriptor(new SpriteDescriptor("machine:cask", "TileSheets/Craftables", 16, 160, 16, 32, 1, 1, 0, -1, "#713f12", "Machine", "Cask (Penuaan Anggur/Keju)"));
        RegisterDescriptor(new SpriteDescriptor("machine:tapper", "TileSheets/Craftables", 144, 96, 16, 32, 1, 1, 0, -1, "#78350f", "Machine", "Tapper (Penyadap Pohon)"));
        RegisterDescriptor(new SpriteDescriptor("machine:fish_smoker", "TileSheets/Craftables", 0, 256, 16, 32, 1, 1, 0, -1, "#374151", "Machine", "Fish Smoker (Pengasap Ikan 1.6)"));

        RegisterDescriptor(new SpriteDescriptor("chest:wooden", "TileSheets/Craftables", 128, 0, 16, 32, 1, 1, 0, -1, "#92400e", "Chest", "Peti Kayu (Chest)"));
        RegisterDescriptor(new SpriteDescriptor("chest:stone", "TileSheets/Craftables", 144, 0, 16, 32, 1, 1, 0, -1, "#64748b", "Chest", "Peti Batu (Stone Chest)"));
        RegisterDescriptor(new SpriteDescriptor("chest:communal", "TileSheets/Craftables", 128, 0, 16, 32, 1, 1, 0, -1, "#2563eb", "Chest", "Peti Komunal Desa (Communal Chest)"));

        RegisterDescriptor(new SpriteDescriptor("fence:wood", "LooseSprites/Fence1", 0, 0, 16, 32, 1, 1, 0, -1, "#854d0e", "Fence", "Pagar Kayu (Wood Fence)"));
        RegisterDescriptor(new SpriteDescriptor("fence:gate:closed", "LooseSprites/Fence1", 48, 0, 16, 32, 1, 1, 0, -1, "#a16207", "Fence", "Pintu Pagar Tertutup"));
        RegisterDescriptor(new SpriteDescriptor("fence:gate:open", "LooseSprites/Fence1", 64, 0, 16, 32, 1, 1, 0, -1, "#ca8a04", "Fence", "Pintu Pagar Terbuka (AutoGate)"));

        // 6. Lahan Pertanian (HoeDirt & Crops)
        RegisterDescriptor(new SpriteDescriptor("dirt:dry", "TerrainFeatures/hoeDirt", 0, 0, 16, 16, 1, 1, 0, 0, "#a16207", "Crop", "Tanah Cangkulan (Kering)"));
        RegisterDescriptor(new SpriteDescriptor("dirt:watered", "TerrainFeatures/hoeDirt", 64, 0, 16, 16, 1, 1, 0, 0, "#713f12", "Crop", "Tanah Basah (Tersiram)"));
        RegisterDescriptor(new SpriteDescriptor("crop:growing", "TileSheets/crops", 0, 0, 16, 32, 1, 1, 0, -1, "#84cc16", "Crop", "Tanaman Sedang Tumbuh"));
        RegisterDescriptor(new SpriteDescriptor("crop:harvest_ready", "TileSheets/crops", 0, 96, 16, 32, 1, 1, 0, -1, "#eab308", "Crop", "Tanaman Siap Panen!"));
        RegisterDescriptor(new SpriteDescriptor("crop:dead", "TileSheets/crops", 0, 192, 16, 32, 1, 1, 0, -1, "#451a03", "Crop", "Tanaman Mati / Layu"));

        // 7. Bangunan Desa & Pertanian (Buildings)
        RegisterDescriptor(new SpriteDescriptor("bldg:farmhouse", "Buildings/houses", 0, 0, 144, 144, 9, 6, 0, 0, "#9a3412", "Building", "Rumah Utama Peternakan (Farmhouse)"));
        RegisterDescriptor(new SpriteDescriptor("bldg:cabin", "Buildings/houses", 160, 0, 80, 80, 5, 4, 0, 0, "#b45309", "Building", "Kabin Pemain (Farmhand Cabin)"));
        RegisterDescriptor(new SpriteDescriptor("bldg:barn", "Buildings/Barn", 0, 0, 112, 112, 7, 4, 0, 0, "#991b1b", "Building", "Kandang Sapi/Kambing (Barn)"));
        RegisterDescriptor(new SpriteDescriptor("bldg:coop", "Buildings/Coop", 0, 0, 96, 112, 6, 4, 0, 0, "#b45309", "Building", "Kandang Ayam/Bebek (Coop)"));
        RegisterDescriptor(new SpriteDescriptor("bldg:silo", "Buildings/Silo", 0, 0, 48, 144, 3, 3, 0, 0, "#78716c", "Building", "Silo Pakan Ternak"));
        RegisterDescriptor(new SpriteDescriptor("bldg:greenhouse", "Buildings/Greenhouse", 0, 0, 112, 112, 7, 6, 0, 0, "#047857", "Building", "Rumah Kaca (Greenhouse)"));
        RegisterDescriptor(new SpriteDescriptor("bldg:shed", "Buildings/Shed", 0, 0, 112, 112, 7, 3, 0, 0, "#a16207", "Building", "Gudang Kerja (Shed)"));
        RegisterDescriptor(new SpriteDescriptor("bldg:stable", "Buildings/Stable", 0, 0, 64, 80, 4, 2, 0, 0, "#78350f", "Building", "Kandang Kuda (Stable)"));
        RegisterDescriptor(new SpriteDescriptor("bldg:fish_pond", "Buildings/Fish Pond", 0, 0, 80, 80, 5, 5, 0, 0, "#0284c7", "Building", "Kolam Ikan (Fish Pond)"));

        // 8. Hewan Ternak & Karakter (Farm Animals, NPCs, Players)
        RegisterDescriptor(new SpriteDescriptor("animal:cow:white", "Animals/White Cow", 0, 0, 32, 32, 1, 1, 0, 0, "#f8fafc", "Animal", "Sapi Putih (White Cow)"));
        RegisterDescriptor(new SpriteDescriptor("animal:cow:brown", "Animals/Brown Cow", 0, 0, 32, 32, 1, 1, 0, 0, "#92400e", "Animal", "Sapi Cokelat (Brown Cow)"));
        RegisterDescriptor(new SpriteDescriptor("animal:goat", "Animals/Goat", 0, 0, 32, 32, 1, 1, 0, 0, "#e2e8f0", "Animal", "Kambing (Goat)"));
        RegisterDescriptor(new SpriteDescriptor("animal:pig", "Animals/Pig", 0, 0, 32, 32, 1, 1, 0, 0, "#f472b6", "Animal", "Babi Pencari Truffle (Pig)"));
        RegisterDescriptor(new SpriteDescriptor("animal:chicken:white", "Animals/White Chicken", 0, 0, 16, 16, 1, 1, 0, 0, "#ffffff", "Animal", "Ayam Putih (White Chicken)"));
        RegisterDescriptor(new SpriteDescriptor("animal:chicken:brown", "Animals/Brown Chicken", 0, 0, 16, 16, 1, 1, 0, 0, "#b45309", "Animal", "Ayam Cokelat (Brown Chicken)"));
        RegisterDescriptor(new SpriteDescriptor("animal:duck", "Animals/Duck", 0, 0, 16, 16, 1, 1, 0, 0, "#047857", "Animal", "Bebek (Duck)"));
        RegisterDescriptor(new SpriteDescriptor("animal:sheep", "Animals/Sheep", 0, 0, 32, 32, 1, 1, 0, 0, "#cbd5e1", "Animal", "Domba Penghasil Wol (Sheep)"));

        RegisterDescriptor(new SpriteDescriptor("player:farmer", "Characters/Farmer/farmer_base", 0, 0, 16, 32, 1, 1, 0, -1, "#16a34a", "Player", "Petani (Player)"));
        RegisterDescriptor(new SpriteDescriptor("npc:generic", "Characters/TownResidents", 0, 0, 16, 32, 1, 1, 0, -1, "#3b82f6", "NPC", "Penduduk Desa (Villager)"));
        RegisterDescriptor(new SpriteDescriptor("monster:generic", "Characters/Monsters", 0, 0, 16, 16, 1, 1, 0, 0, "#dc2626", "Monster", "Monster Tambang"));
    }

    public static void RegisterDescriptor(SpriteDescriptor desc)
    {
        Descriptors[desc.SpriteKey] = desc;
    }

    public static SpriteDescriptor? GetDescriptor(string spriteKey)
    {
        if (Descriptors.TryGetValue(spriteKey, out var desc))
        {
            return desc;
        }
        return null;
    }

    public static IReadOnlyDictionary<string, SpriteDescriptor> GetAllDescriptors()
    {
        return Descriptors;
    }

    /// <summary>
    /// Menyelesaikan SpriteKey spesifik secara dinamis dari objek Tree SDV 1.6
    /// </summary>
    public static SpriteDescriptor ResolveTree(Tree tree)
    {
        string typeStr = tree.treeType?.Value ?? "1";
        string typeName = typeStr switch
        {
            "1" => "oak",
            "2" => "maple",
            "3" => "pine",
            "7" => "mahogany",
            "8" => "mushroom",
            "10" => "mystic",
            _ => "oak"
        };

        if (tree.stump?.Value == true)
        {
            string stumpKey = $"tree:{typeName}:stump";
            return GetDescriptor(stumpKey) ?? GetDescriptor("tree:oak:stump")!;
        }

        int stage = tree.growthStage?.Value ?? 5;
        if (stage < 5)
        {
            return GetDescriptor("tree:sapling")!;
        }

        string fullKey = $"tree:{typeName}:stage5";
        return GetDescriptor(fullKey) ?? GetDescriptor("tree:oak:stage5")!;
    }

    /// <summary>
    /// Menyelesaikan SpriteKey spesifik secara dinamis dari objek HoeDirt & Crop
    /// </summary>
    public static SpriteDescriptor ResolveHoeDirt(HoeDirt dirt)
    {
        if (dirt.crop != null)
        {
            if (dirt.crop.dead?.Value == true)
            {
                return GetDescriptor("crop:dead")!;
            }
            if (dirt.crop.currentPhase?.Value >= dirt.crop.phaseDays?.Count - 1)
            {
                return GetDescriptor("crop:harvest_ready")!;
            }
            return GetDescriptor("crop:growing")!;
        }

        bool isWatered = (dirt.state?.Value ?? 0) == 1;
        return isWatered ? GetDescriptor("dirt:watered")! : GetDescriptor("dirt:dry")!;
    }

    /// <summary>
    /// Menyelesaikan SpriteKey spesifik dari objek batuan / bijih tambang / mesin / peti
    /// </summary>
    public static SpriteDescriptor ResolveObject(StardewValley.Object obj)
    {
        if (obj is Chest chest)
        {
            if (chest.modData != null && chest.modData.ContainsKey("svheadless/is_communal"))
            {
                return GetDescriptor("chest:communal")!;
            }
            return GetDescriptor("chest:wooden")!;
        }

        if (obj.bigCraftable?.Value == true)
        {
            string name = (obj.Name ?? "").ToLowerInvariant();
            if (name.Contains("furnace")) return GetDescriptor("machine:furnace")!;
            if (name.Contains("keg")) return GetDescriptor("machine:keg")!;
            if (name.Contains("preserves")) return GetDescriptor("machine:preserves_jar")!;
            if (name.Contains("cheese")) return GetDescriptor("machine:cheese_press")!;
            if (name.Contains("mayo")) return GetDescriptor("machine:mayonnaise_machine")!;
            if (name.Contains("cask")) return GetDescriptor("machine:cask")!;
            if (name.Contains("tapper")) return GetDescriptor("machine:tapper")!;
            if (name.Contains("smoker")) return GetDescriptor("machine:fish_smoker")!;
            return GetDescriptor("machine:keg")!;
        }

        if (obj.IsTwig()) return GetDescriptor("debris:twig")!;
        if (obj.IsWeeds()) return GetDescriptor("debris:weeds")!;

        string itemId = obj.ItemId ?? "";
        if (itemId == "751" || obj.Name?.Contains("Copper", StringComparison.OrdinalIgnoreCase) == true) return GetDescriptor("ore:copper")!;
        if (itemId == "290" || obj.Name?.Contains("Iron", StringComparison.OrdinalIgnoreCase) == true) return GetDescriptor("ore:iron")!;
        if (itemId == "764" || obj.Name?.Contains("Gold", StringComparison.OrdinalIgnoreCase) == true) return GetDescriptor("ore:gold")!;
        if (itemId == "765" || obj.Name?.Contains("Iridium", StringComparison.OrdinalIgnoreCase) == true) return GetDescriptor("ore:iridium")!;
        if (itemId == "843" || itemId == "844") return GetDescriptor("ore:cinder_shard")!;
        if (itemId == "95") return GetDescriptor("ore:radioactive")!;

        return GetDescriptor("debris:stone")!;
    }

    /// <summary>
    /// Menyelesaikan SpriteKey spesifik dari bongkahan besar (ResourceClump)
    /// </summary>
    public static SpriteDescriptor ResolveResourceClump(ResourceClump clump)
    {
        int sheetIdx = clump.parentSheetIndex?.Value ?? 600;
        return sheetIdx switch
        {
            600 => GetDescriptor("clump:large_stump:600")!,
            602 => GetDescriptor("clump:hollow_log:602")!,
            672 or 752 or 754 or 756 or 758 => GetDescriptor("clump:boulder:672")!,
            622 => GetDescriptor("clump:meteorite:622")!,
            _ => GetDescriptor("clump:boulder:672")!
        };
    }

    /// <summary>
    /// Menyelesaikan SpriteKey spesifik dari bangunan (Building)
    /// </summary>
    public static SpriteDescriptor ResolveBuilding(Building bldg)
    {
        string type = (bldg.buildingType?.Value ?? "").ToLowerInvariant();
        if (type.Contains("cabin")) return GetDescriptor("bldg:cabin")!;
        if (type.Contains("barn")) return GetDescriptor("bldg:barn")!;
        if (type.Contains("coop")) return GetDescriptor("bldg:coop")!;
        if (type.Contains("silo")) return GetDescriptor("bldg:silo")!;
        if (type.Contains("greenhouse")) return GetDescriptor("bldg:greenhouse")!;
        if (type.Contains("shed")) return GetDescriptor("bldg:shed")!;
        if (type.Contains("stable")) return GetDescriptor("bldg:stable")!;
        if (type.Contains("fish pond") || type.Contains("fishpond")) return GetDescriptor("bldg:fish_pond")!;

        return GetDescriptor("bldg:farmhouse")!;
    }

    /// <summary>
    /// Menyelesaikan SpriteKey spesifik dari hewan ternak (FarmAnimal)
    /// </summary>
    public static SpriteDescriptor ResolveAnimal(FarmAnimal animal)
    {
        string type = (animal.type?.Value ?? "").ToLowerInvariant();
        if (type.Contains("white cow")) return GetDescriptor("animal:cow:white")!;
        if (type.Contains("brown cow") || type.Contains("cow")) return GetDescriptor("animal:cow:brown")!;
        if (type.Contains("goat")) return GetDescriptor("animal:goat")!;
        if (type.Contains("pig")) return GetDescriptor("animal:pig")!;
        if (type.Contains("sheep")) return GetDescriptor("animal:sheep")!;
        if (type.Contains("duck")) return GetDescriptor("animal:duck")!;
        if (type.Contains("brown chicken")) return GetDescriptor("animal:chicken:brown")!;
        if (type.Contains("chicken")) return GetDescriptor("animal:chicken:white")!;

        return GetDescriptor("animal:cow:white")!;
    }
}
