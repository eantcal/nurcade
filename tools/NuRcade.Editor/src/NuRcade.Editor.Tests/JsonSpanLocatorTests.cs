using System.Text;
using System.Text.Json;
using NuRcade.Editor.Core;

namespace NuRcade.Editor.Tests;

[TestClass]
public sealed class JsonSpanLocatorTests
{
    private static string SampleWorldJson()
    {
        var world = new WorldDocument {
            Name = "sample",
            Grid = new WorldGridDefinition { Columns = 2, Rows = 2 }
        };
        world.Blocks["00"] = new WorldBlockDefinition { Name = "empty" };
        world.Blocks["01"] = new WorldBlockDefinition { Name = "wall" };
        world.Blocks["02"] = new WorldBlockDefinition { Name = "ledge" };
        world.Cells.Add(["00", "01"]);
        world.Cells.Add(["02", "00"]);
        world.SpriteInstances.Add(new EditorSpriteInstance { Name = "ogre", SpriteSet = "monster" });
        world.SpriteInstances.Add(new EditorSpriteInstance { Name = "imp", SpriteSet = "monster" });
        return WorldJsonDocumentService.Serialize(world);
    }

    private static string Slice(string json, int startByte, int lengthByte)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        return Encoding.UTF8.GetString(bytes, startByte, lengthByte);
    }

    [TestMethod]
    public void LocatesCellTokenInCellsMatrix()
    {
        var json = SampleWorldJson();

        Assert.IsTrue(JsonSpanLocator.TryLocateCell(json, null, 1, 0, out var start, out var length));
        Assert.AreEqual("\"02\"", Slice(json, start, length));
    }

    [TestMethod]
    public void LocatesSpriteObjectByIndex()
    {
        var json = SampleWorldJson();

        Assert.IsTrue(JsonSpanLocator.TryLocateSprite(json, null, 1, out var start, out var length));

        var fragment = Slice(json, start, length);
        using var parsed = JsonDocument.Parse(fragment);
        Assert.AreEqual("imp", parsed.RootElement.GetProperty("name").GetString());
    }

    [TestMethod]
    public void LocatesBlockObjectByKey()
    {
        var json = SampleWorldJson();

        Assert.IsTrue(JsonSpanLocator.TryLocateBlock(json, "02", out var start, out var length));

        var fragment = Slice(json, start, length);
        using var parsed = JsonDocument.Parse(fragment);
        Assert.AreEqual("ledge", parsed.RootElement.GetProperty("name").GetString());
    }

    [TestMethod]
    public void ReturnsFalseForMissingPath()
    {
        var json = SampleWorldJson();

        Assert.IsFalse(JsonSpanLocator.TryLocateCell(json, null, 9, 9, out _, out _));
        Assert.IsFalse(JsonSpanLocator.TryLocateBlock(json, "zz", out _, out _));
        Assert.IsFalse(JsonSpanLocator.TryLocateSprite(json, null, 5, out _, out _));
    }

    [TestMethod]
    public void ReturnsFalseForMalformedJson()
    {
        Assert.IsFalse(
            JsonSpanLocator.TryLocate(
                "{ not valid json",
                [JsonPathSegment.Property("cells")],
                out _,
                out _));
    }
}
