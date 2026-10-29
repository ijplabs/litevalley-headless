using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.Characters;
using StardewValley.Objects;
using StardewValley.TerrainFeatures;

namespace StardewHeadless.Server.World.Knowledge;

public record WorldEntityData(
    string EntityId,
    string Layer,
    string Category,
    int TileX,
    int TileY,
    string SpriteKey,
    string Name,
    string ColorHex,
    int FootprintWidth,
    int FootprintHeight,
    Dictionary<string, object?> State
);

public record WorldLocationSnapshot(
    string LocationName,
    string DisplayName,
    int MapWidth,
    int MapHeight,
    int TotalEntities,
    Dictionary<string, int> CategoryCounts,
    List<WorldEntityData> Entities
);

public record WorldKnowledgeSummary(
    int TotalLocations,
    int TotalCrops,
    int WateredCropsToday,
    int ReadyToHarvestCrops,
    int TotalTrees,
    int TotalOres,
    int TotalAnimals,
    int TotalMachines,
    int TotalChests,
    int TotalFarmersOnline
);

/// <summary>
/// Mesin pemindai seluruh entitas visual dan fungsional di dunia game Stardew Valley.
/// Menghasilkan knowledge komprehensif bagi API server, radar web interaktif, dan analitik desa.
/// </summary>
public static class WorldEntityScanner
{
    public static WorldLocationSnapshot ScanLocation(GameLocation location)
    {
        if (location == null)
        {
            return new WorldLocationSnapshot(
                LocationName: "Unknown",
                DisplayName: "Unknown",
                MapWidth: 80,
                MapHeight: 65,
                TotalEntities: 0,
                CategoryCounts: new Dictionary<string, int>(),
                Entities: new List<WorldEntityData>()
            );
        }

        string locName = location.NameOrUniqueName ?? location.Name ?? "Farm";
        int mapWidth = 80;
        int mapHeight = 65;

        try
        {
            if (location.Map?.Layers != null && location.Map.Layers.Count > 0 && location.Map.Layers[0] != null)
            {
                mapWidth = location.Map.Layers[0].LayerWidth;
                mapHeight = location.Map.Layers[0].LayerHeight;
            }
        }
        catch { }

        var entities = new List<WorldEntityData>();
        var categoryCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        void AddEntity(WorldEntityData entity)
        {
            entities.Add(entity);
            if (!categoryCounts.ContainsKey(entity.Category))
            {
                categoryCounts[entity.Category] = 0;
            }
            categoryCounts[entity.Category]++;
        }

        // 1. Pindai TerrainFeatures (Pohon, Lahan Pertanian, Rumput, Semak)
        if (location.terrainFeatures != null)
        {
            foreach (var kvp in location.terrainFeatures.Pairs)
            {
                var tile = kvp.Key;
                var feature = kvp.Value;
                if (feature == null) continue;

                int tx = (int)tile.X;
                int ty = (int)tile.Y;

                if (feature is Tree tree)
                {
                    var desc = SpriteAtlasCatalog.ResolveTree(tree);
                    var state = new Dictionary<string, object?>
                    {
                        ["growthStage"] = tree.growthStage?.Value ?? 5,
                        ["stump"] = tree.stump?.Value ?? false,
                        ["tapped"] = tree.tapped?.Value ?? false,
                        ["hasMoss"] = tree.hasMoss?.Value ?? false,
                        ["treeType"] = tree.treeType?.Value ?? "1",
                        ["health"] = tree.health?.Value ?? 10f
                    };

                    AddEntity(new WorldEntityData(
                        EntityId: $"tree_{tx}_{ty}",
                        Layer: "TerrainFeatures",
                        Category: "Tree",
                        TileX: tx,
                        TileY: ty,
                        SpriteKey: desc.SpriteKey,
                        Name: desc.DisplayName,
                        ColorHex: desc.SemanticColorHex,
                        FootprintWidth: desc.FootprintWidth,
                        FootprintHeight: desc.FootprintHeight,
                        State: state
                    ));
                }
                else if (feature is FruitTree fruitTree)
                {
                    var desc = SpriteAtlasCatalog.GetDescriptor("tree:sapling")!;
                    var state = new Dictionary<string, object?>
                    {
                        ["treeId"] = fruitTree.treeId?.Value ?? "fruit",
                        ["growthStage"] = fruitTree.growthStage?.Value ?? 4,
                        ["fruitsOnTree"] = fruitTree.fruit?.Count ?? 0,
                        ["struckByLightning"] = fruitTree.struckByLightningCountdown?.Value ?? 0
                    };

                    AddEntity(new WorldEntityData(
                        EntityId: $"fruittree_{tx}_{ty}",
                        Layer: "TerrainFeatures",
                        Category: "Tree",
                        TileX: tx,
                        TileY: ty,
                        SpriteKey: "tree:fruit",
                        Name: $"Pohon Buah ({fruitTree.treeId?.Value})",
                        ColorHex: "#15803d",
                        FootprintWidth: 1,
                        FootprintHeight: 1,
                        State: state
                    ));
                }
                else if (feature is Grass grass)
                {
                    int grassType = grass.grassType?.Value ?? 1;
                    string spriteKey = grassType == 7 ? "grass:blue" : "grass:normal";
                    var desc = SpriteAtlasCatalog.GetDescriptor(spriteKey)!;

                    AddEntity(new WorldEntityData(
                        EntityId: $"grass_{tx}_{ty}",
                        Layer: "TerrainFeatures",
                        Category: "Grass",
                        TileX: tx,
                        TileY: ty,
                        SpriteKey: desc.SpriteKey,
                        Name: desc.DisplayName,
                        ColorHex: desc.SemanticColorHex,
                        FootprintWidth: 1,
                        FootprintHeight: 1,
                        State: new Dictionary<string, object?>
                        {
                            ["grassType"] = grassType,
                            ["numberOfWeeds"] = grass.numberOfWeeds?.Value ?? 1
                        }
                    ));
                }
                else if (feature is HoeDirt dirt)
                {
                    var desc = SpriteAtlasCatalog.ResolveHoeDirt(dirt);
                    bool isWatered = (dirt.state?.Value ?? 0) == 1;
                    bool hasCrop = dirt.crop != null;
                    bool isReady = hasCrop && dirt.crop!.currentPhase?.Value >= (dirt.crop.phaseDays?.Count ?? 5) - 1;

                    var state = new Dictionary<string, object?>
                    {
                        ["isWatered"] = isWatered,
                        ["fertilizer"] = dirt.fertilizer?.Value,
                        ["hasCrop"] = hasCrop,
                        ["cropId"] = dirt.crop?.indexOfHarvest?.Value,
                        ["currentPhase"] = dirt.crop?.currentPhase?.Value,
                        ["isDead"] = dirt.crop?.dead?.Value ?? false,
                        ["isReadyToHarvest"] = isReady
                    };

                    AddEntity(new WorldEntityData(
                        EntityId: $"dirt_{tx}_{ty}",
                        Layer: "TerrainFeatures",
                        Category: "Crop",
                        TileX: tx,
                        TileY: ty,
                        SpriteKey: desc.SpriteKey,
                        Name: desc.DisplayName,
                        ColorHex: desc.SemanticColorHex,
                        FootprintWidth: 1,
                        FootprintHeight: 1,
                        State: state
                    ));
                }
                else if (feature is Bush bush)
                {
                    AddEntity(new WorldEntityData(
                        EntityId: $"bush_{tx}_{ty}",
                        Layer: "TerrainFeatures",
                        Category: "Tree",
                        TileX: tx,
                        TileY: ty,
                        SpriteKey: "bush:green",
                        Name: "Semak Daun (Bush)",
                        ColorHex: "#166534",
                        FootprintWidth: bush.size?.Value ?? 1,
                        FootprintHeight: 1,
                        State: new Dictionary<string, object?>
                        {
                            ["inBloom"] = bush.inBloom()
                        }
                    ));
                }
            }
        }

        // 2. Pindai Objects (Batu Tambang, Ranting, Gulma, Mesin Automate, Peti, Pagar)
        if (location.Objects != null)
        {
            foreach (var kvp in location.Objects.Pairs)
            {
                var tile = kvp.Key;
                var obj = kvp.Value;
                if (obj == null) continue;

                int tx = (int)tile.X;
                int ty = (int)tile.Y;

                var desc = SpriteAtlasCatalog.ResolveObject(obj);
                var state = new Dictionary<string, object?>
                {
                    ["itemId"] = obj.ItemId,
                    ["quality"] = obj.Quality,
                    ["stack"] = obj.Stack,
                    ["isBigCraftable"] = obj.bigCraftable?.Value ?? false
                };

                if (obj is Chest chest)
                {
                    state["isCommunal"] = chest.modData != null && chest.modData.ContainsKey("svheadless/is_communal");
                    state["itemCount"] = chest.Items?.Count(i => i != null) ?? 0;
                    state["ownerId"] = chest.modData != null && chest.modData.TryGetValue("svheadless/owner_id", out var oid) ? oid : null;
                }
                else if (obj.bigCraftable?.Value == true)
                {
                    state["heldItem"] = obj.heldObject?.Value?.Name;
                    state["minutesUntilReady"] = obj.MinutesUntilReady;
                    state["readyForHarvest"] = obj.readyForHarvest?.Value ?? false;
                }
                else if (obj is Fence fence)
                {
                    state["isGate"] = fence.isGate?.Value ?? false;
                    state["gatePosition"] = fence.gatePosition?.Value ?? 0;
                    state["health"] = fence.health?.Value ?? 100f;
                }

                AddEntity(new WorldEntityData(
                    EntityId: $"obj_{tx}_{ty}",
                    Layer: "Objects",
                    Category: desc.Category,
                    TileX: tx,
                    TileY: ty,
                    SpriteKey: desc.SpriteKey,
                    Name: desc.DisplayName,
                    ColorHex: desc.SemanticColorHex,
                    FootprintWidth: desc.FootprintWidth,
                    FootprintHeight: desc.FootprintHeight,
                    State: state
                ));
            }
        }

        // 3. Pindai Resource Clumps (Tunggul Besar, Batu Cadas, Meteorit)
        if (location.resourceClumps != null)
        {
            foreach (var clump in location.resourceClumps)
            {
                if (clump == null) continue;
                int tx = (int)clump.Tile.X;
                int ty = (int)clump.Tile.Y;

                var desc = SpriteAtlasCatalog.ResolveResourceClump(clump);
                AddEntity(new WorldEntityData(
                    EntityId: $"clump_{tx}_{ty}",
                    Layer: "Clumps",
                    Category: "Clump",
                    TileX: tx,
                    TileY: ty,
                    SpriteKey: desc.SpriteKey,
                    Name: desc.DisplayName,
                    ColorHex: desc.SemanticColorHex,
                    FootprintWidth: clump.width?.Value ?? desc.FootprintWidth,
                    FootprintHeight: clump.height?.Value ?? desc.FootprintHeight,
                    State: new Dictionary<string, object?>
                    {
                        ["sheetIndex"] = clump.parentSheetIndex?.Value ?? 600,
                        ["health"] = clump.health?.Value ?? 10f
                    }
                ));
            }
        }

        // 4. Pindai Bangunan (Buildings) pada Farm / BuildableGameLocation
        if (location.buildings != null)
        {
            foreach (var bldg in location.buildings)
            {
                if (bldg == null) continue;
                int tx = bldg.tileX?.Value ?? 0;
                int ty = bldg.tileY?.Value ?? 0;

                var desc = SpriteAtlasCatalog.ResolveBuilding(bldg);
                AddEntity(new WorldEntityData(
                    EntityId: $"bldg_{tx}_{ty}",
                    Layer: "Buildings",
                    Category: "Building",
                    TileX: tx,
                    TileY: ty,
                    SpriteKey: desc.SpriteKey,
                    Name: desc.DisplayName,
                    ColorHex: desc.SemanticColorHex,
                    FootprintWidth: bldg.tilesWide?.Value ?? desc.FootprintWidth,
                    FootprintHeight: bldg.tilesHigh?.Value ?? desc.FootprintHeight,
                    State: new Dictionary<string, object?>
                    {
                        ["buildingType"] = bldg.buildingType?.Value,
                        ["animalDoorOpen"] = bldg.animalDoorOpen?.Value ?? false,
                        ["hayCapacity"] = bldg.hayCapacity?.Value ?? 0
                    }
                ));
            }
        }

        // 5. Pindai Hewan Ternak (Farm Animals)
        if (location is Farm farm && farm.animals != null)
        {
            foreach (var kvp in farm.animals.Pairs)
            {
                var animal = kvp.Value;
                if (animal == null) continue;
                int tx = (int)animal.Tile.X;
                int ty = (int)animal.Tile.Y;

                var desc = SpriteAtlasCatalog.ResolveAnimal(animal);
                AddEntity(new WorldEntityData(
                    EntityId: $"animal_{animal.myID?.Value ?? kvp.Key}",
                    Layer: "Characters",
                    Category: "Animal",
                    TileX: tx,
                    TileY: ty,
                    SpriteKey: desc.SpriteKey,
                    Name: animal.Name ?? desc.DisplayName,
                    ColorHex: desc.SemanticColorHex,
                    FootprintWidth: 1,
                    FootprintHeight: 1,
                    State: new Dictionary<string, object?>
                    {
                        ["type"] = animal.type?.Value,
                        ["wasPet"] = animal.wasPet?.Value ?? false,
                        ["hasProduce"] = !string.IsNullOrEmpty(animal.currentProduce?.Value),
                        ["friendship"] = animal.friendshipTowardFarmer?.Value ?? 0,
                        ["happiness"] = animal.happiness?.Value ?? 255
                    }
                ));
            }
        }

        // 6. Pindai Karakter NPC & Petani (Farmers)
        if (location.characters != null)
        {
            foreach (var npc in location.characters)
            {
                if (npc == null) continue;
                int tx = (int)npc.Tile.X;
                int ty = (int)npc.Tile.Y;

                AddEntity(new WorldEntityData(
                    EntityId: $"npc_{npc.Name}",
                    Layer: "Characters",
                    Category: npc.IsVillager ? "NPC" : "Monster",
                    TileX: tx,
                    TileY: ty,
                    SpriteKey: npc.IsVillager ? "npc:generic" : "monster:generic",
                    Name: npc.Name ?? "Villager",
                    ColorHex: npc.IsVillager ? "#3b82f6" : "#dc2626",
                    FootprintWidth: 1,
                    FootprintHeight: 1,
                    State: new Dictionary<string, object?>
                    {
                        ["facingDirection"] = npc.FacingDirection,
                        ["isVillager"] = npc.IsVillager
                    }
                ));
            }
        }

        try
        {
            if (location.farmers != null)
            {
                foreach (var f in location.farmers)
                {
                    if (f == null) continue;
                    int tx = (int)f.Tile.X;
                    int ty = (int)f.Tile.Y;

                    string prof = "farmer";
                    if (f.modData != null && f.modData.TryGetValue("svheadless/profession", out var pVal))
                    {
                        prof = pVal;
                    }

                    AddEntity(new WorldEntityData(
                        EntityId: $"farmer_{f.UniqueMultiplayerID}",
                        Layer: "Characters",
                        Category: "Player",
                        TileX: tx,
                        TileY: ty,
                        SpriteKey: "player:farmer",
                        Name: f.Name ?? "Petani",
                        ColorHex: "#16a34a",
                        FootprintWidth: 1,
                        FootprintHeight: 1,
                        State: new Dictionary<string, object?>
                        {
                            ["farmerId"] = f.UniqueMultiplayerID,
                            ["profession"] = prof,
                            ["currentTool"] = f.CurrentTool?.Name,
                            ["facingDirection"] = f.FacingDirection
                        }
                    ));
                }
            }
        }
        catch
        {
            // Defensive: FarmerCollection enumerator di SDV dapat mengakses Game1.otherFarmers saat isolasi testing
        }

        string dispName = locName;
        try
        {
            dispName = location.DisplayName ?? locName;
        }
        catch
        {
            dispName = locName;
        }

        return new WorldLocationSnapshot(
            LocationName: locName,
            DisplayName: dispName,
            MapWidth: mapWidth,
            MapHeight: mapHeight,
            TotalEntities: entities.Count,
            CategoryCounts: categoryCounts,
            Entities: entities
        );
    }

    public static WorldKnowledgeSummary GenerateWorldSummary(GameLocation? farm = null)
    {
        GameLocation? targetFarm = farm;
        if (targetFarm == null)
        {
            try
            {
                targetFarm = Game1.getFarm();
            }
            catch
            {
                targetFarm = null;
            }
        }

        int totalCrops = 0;
        int wateredCrops = 0;
        int readyCrops = 0;
        int totalTrees = 0;
        int totalOres = 0;
        int totalAnimals = 0;
        int totalMachines = 0;
        int totalChests = 0;
        int farmersOnline = Game1.otherFarmers?.Count ?? 0;

        if (targetFarm != null)
        {
            if (targetFarm.terrainFeatures != null)
            {
                foreach (var feature in targetFarm.terrainFeatures.Values)
                {
                    if (feature is Tree) totalTrees++;
                    else if (feature is HoeDirt dirt && dirt.crop != null)
                    {
                        totalCrops++;
                        if ((dirt.state?.Value ?? 0) == 1) wateredCrops++;
                        if (dirt.crop.currentPhase?.Value >= (dirt.crop.phaseDays?.Count ?? 5) - 1) readyCrops++;
                    }
                }
            }

            if (targetFarm.Objects != null)
            {
                foreach (var obj in targetFarm.Objects.Values)
                {
                    if (obj is Chest) totalChests++;
                    else if (obj.bigCraftable?.Value == true) totalMachines++;
                    else if (obj.Name?.Contains("Node", StringComparison.OrdinalIgnoreCase) == true ||
                             obj.Name?.Contains("Ore", StringComparison.OrdinalIgnoreCase) == true ||
                             obj.ItemId is "751" or "290" or "764" or "765" or "843" or "844" or "848" or "850") totalOres++;
                }
            }

            if (targetFarm is Farm f && f.animals != null)
            {
                totalAnimals = f.animals.Count();
            }
        }

        int totalLocations = 1;
        try
        {
            totalLocations = Game1.locations?.Count ?? 1;
        }
        catch
        {
            totalLocations = 1;
        }

        return new WorldKnowledgeSummary(
            TotalLocations: totalLocations,
            TotalCrops: totalCrops,
            WateredCropsToday: wateredCrops,
            ReadyToHarvestCrops: readyCrops,
            TotalTrees: totalTrees,
            TotalOres: totalOres,
            TotalAnimals: totalAnimals,
            TotalMachines: totalMachines,
            TotalChests: totalChests,
            TotalFarmersOnline: farmersOnline
        );
    }
}
