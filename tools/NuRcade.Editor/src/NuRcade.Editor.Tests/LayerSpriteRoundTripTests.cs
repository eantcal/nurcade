using NuRcade.Editor.Core;

namespace NuRcade.Editor.Tests;

[TestClass]
public sealed class LayerSpriteRoundTripTests
{
    [TestMethod]
    public void ToEditorMapSurfacesActiveLayerSpritesAlongsideGlobalOnes()
    {
        var map = LegacyWorldConverter.ToEditorMap(BuildLayeredWorld(), "demo.world.json");

        Assert.HasCount(2, map.SpriteInstances);
        var item = map.SpriteInstances.Single(s => s.SpriteSet == "item_medikit");
        var guard = map.SpriteInstances.Single(s => s.SpriteSet == "soldier");

        // The active layer's sprite is flagged as layer-owned; the top-level one is not.
        Assert.Contains(item, map.ActiveLayerSprites);
        Assert.DoesNotContain(guard, map.ActiveLayerSprites);

        // Both are placed in their cells so the map preview can render them.
        Assert.Contains(item, map.CellAt(0, 1)!.Sprites);
        Assert.Contains(guard, map.CellAt(0, 0)!.Sprites);
    }

    [TestMethod]
    public void RoundTripWritesGlobalToTopLevelAndItemBackToActiveLayer()
    {
        var map = LegacyWorldConverter.ToEditorMap(BuildLayeredWorld(), "demo.world.json");

        var world = LegacyWorldConverter.FromEditorMap(map, "demo");

        Assert.AreEqual("soldier", world.SpriteInstances.Single().SpriteSet);
        var level1 = world.Layers.Single(layer => layer.Id == "level_1");
        Assert.AreEqual("item_medikit", level1.SpriteInstances.Single().SpriteSet);
        var level2 = world.Layers.Single(layer => layer.Id == "level_2");
        Assert.HasCount(0, level2.SpriteInstances);
    }

    [TestMethod]
    public void WorldsWithoutLayersKeepAllSpritesAtTopLevel()
    {
        var world = new WorldDocument {
            Grid = new WorldGridDefinition {
                Columns = 1, Rows = 1, CellWidth = 512, CellDepth = 512, DefaultWallHeight = 512
            }
        };
        world.Blocks["00"] = new WorldBlockDefinition { Name = "empty" };
        world.Cells.Add(["00"]);
        world.SpriteInstances.Add(new EditorSpriteInstance {
            Name = "guard", SpriteSet = "soldier", XCell = 0.5, YCell = 0.5
        });

        var map = LegacyWorldConverter.ToEditorMap(world, "flat.world.json");
        Assert.HasCount(0, map.ActiveLayerSprites);

        var roundTripped = LegacyWorldConverter.FromEditorMap(map, "flat");
        Assert.AreEqual("soldier", roundTripped.SpriteInstances.Single().SpriteSet);
        Assert.HasCount(0, roundTripped.Layers);
    }

    private static WorldDocument BuildLayeredWorld()
    {
        var world = new WorldDocument {
            Grid = new WorldGridDefinition {
                Columns = 2, Rows = 1, CellWidth = 512, CellDepth = 512, DefaultWallHeight = 512
            },
            ActiveLayer = "level_1",
            StartLayer = "level_1"
        };
        world.Blocks["00"] = new WorldBlockDefinition { Name = "empty" };
        world.Cells.Add(["00", "00"]);

        // Global (top-level) enemy, shared by every layer.
        world.SpriteInstances.Add(new EditorSpriteInstance {
            Name = "guard",
            SpriteSet = "soldier",
            XCell = 0.5,
            YCell = 0.5,
            MaxHealth = 100,
            Health = 100,
            AttackDamage = 8
        });

        var level1 = new WorldLayerDefinition {
            Id = "level_1",
            Name = "Level 1",
            Grid = new WorldGridDefinition {
                Columns = 2, Rows = 1, CellWidth = 512, CellDepth = 512, DefaultWallHeight = 512
            },
            Cells = [["00", "00"]]
        };
        level1.SpriteInstances.Add(new EditorSpriteInstance {
            Name = "medkit",
            SpriteSet = "item_medikit",
            XCell = 1.5,
            YCell = 0.5,
            PassThroughWalls = true
        });
        world.Layers.Add(level1);

        world.Layers.Add(new WorldLayerDefinition {
            Id = "level_2",
            Name = "Level 2",
            Grid = new WorldGridDefinition {
                Columns = 2, Rows = 1, CellWidth = 512, CellDepth = 512, DefaultWallHeight = 512
            },
            Cells = [["00", "00"]]
        });

        return world;
    }
}
