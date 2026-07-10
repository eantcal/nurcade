using System.Globalization;
using System.Text.Json;
using WinRaycastEditor.Core;

namespace WinRaycastEditor.Tests;

[TestClass]
public sealed class EditorKeyAndLayerUsabilityTests
{
    [TestMethod]
    public void SpriteMarkerSizeTracksRenderedScale()
    {
        var converter = new SpriteMapMarkerSizeConverter();

        var small = (double)converter.Convert(
            [100.0, 0.5], typeof(double), null!, CultureInfo.InvariantCulture);
        var large = (double)converter.Convert(
            [100.0, 1.5], typeof(double), null!, CultureInfo.InvariantCulture);

        Assert.AreEqual(34.0, small, 0.001);
        Assert.AreEqual(102.0, large, 0.001);
    }

    [TestMethod]
    public void SpriteMarkerSizeHandlesZeroWidthDuringTabLayout()
    {
        var converter = new SpriteMapMarkerSizeConverter();

        var size = (double)converter.Convert(
            [0.0, 0.5], typeof(double), null!, CultureInfo.InvariantCulture);

        Assert.AreEqual(6.0, size, 0.001);
    }

    [TestMethod]
    public void KeyColorSwitchChangesSpriteSetWithoutMovingTheKey()
    {
        var sprite = new EditorSpriteInstance
        {
            Name = "access_key",
            SpriteSet = "item_key_red",
            XCell = 4.5,
            YCell = 6.5
        };
        var viewModel = new SpriteInstanceViewModel(sprite);

        viewModel.KeyColor = "blue";

        Assert.AreEqual("item_key_blue", sprite.SpriteSet);
        Assert.AreEqual(4.5, sprite.XCell, 0.001);
        Assert.AreEqual(6.5, sprite.YCell, 0.001);
    }

    [TestMethod]
    public void CellClipboardSurvivesWorldLayerSwitch()
    {
        var viewModel = new MainWindowViewModel(loadDefaultWorld: false);
        viewModel.LoadWorldJsonFrom(DemoWorldPath());
        Assert.IsGreaterThanOrEqualTo(2, viewModel.WorldLayers.Count);

        var source = viewModel.Cells.First(cell => !string.IsNullOrWhiteSpace(cell.Cell.BlockId));
        var expected = EditorCellContent.Capture(source.Cell);
        viewModel.SelectedCell = source;
        viewModel.CopyCellCommand.Execute(null);

        viewModel.SelectedWorldLayer = viewModel.WorldLayers.First(
            layer => !ReferenceEquals(layer, viewModel.SelectedWorldLayer));
        var target = viewModel.Cells.First();
        viewModel.SelectedCell = target;
        viewModel.PasteCellCommand.Execute(null);

        Assert.AreEqual(expected.BlockId, target.Cell.BlockId);
        Assert.AreEqual(expected.Fields, target.Cell.Fields);
    }

    [TestMethod]
    public void ElevatorCellExposesAllConnectedFloorsForEditing()
    {
        var viewModel = new MainWindowViewModel(loadDefaultWorld: false);
        viewModel.LoadWorldJsonFrom(DemoWorldPath());
        viewModel.SelectedWorldLayer = viewModel.WorldLayers.Single(layer => layer.Id == "level_0");
        viewModel.SelectedCell = viewModel.Cells.Single(cell => cell.Row == 14 && cell.Column == 2);

        Assert.IsTrue(viewModel.SelectedCellIsElevator);
        Assert.HasCount(6, viewModel.SelectedCellLayerConnections);
        Assert.IsTrue(viewModel.SelectedCellLayerConnections.All(option => option.IsConnected));
        Assert.IsTrue(viewModel.SelectedCell!.TargetLayerLabel!.Contains("level_1"));
        Assert.IsTrue(viewModel.SelectedCell.TargetLayerLabel.Contains("level_final"));

        var armory = viewModel.SelectedCellLayerConnections.Single(option => option.LayerId == "level_1");
        armory.IsConnected = false;
        Assert.AreEqual(
            0,
            viewModel.Document!.LayerTransitions.Count(transition =>
                transition.FromLayer == "level_0" && transition.ToLayer == "level_1"));

        armory.IsConnected = true;
        Assert.AreEqual(
            1,
            viewModel.Document.LayerTransitions.Count(transition =>
                transition.FromLayer == "level_0" && transition.ToLayer == "level_1"));

        var vault = viewModel.SelectedCellLayerConnections.Single(option => option.LayerId == "level_final");
        vault.IsConnected = false;
        vault.IsConnected = true;
        Assert.AreEqual(
            "blue",
            viewModel.Document.LayerTransitions.Single(transition =>
                transition.FromLayer == "level_0" && transition.ToLayer == "level_final").RequiredKey);

        viewModel.SelectedWorldLayer = viewModel.WorldLayers.Single(layer => layer.Id == "level_final");
        viewModel.SelectedCell = viewModel.Cells.Single(cell => cell.Row == 14 && cell.Column == 2);
        Assert.IsTrue(viewModel.SelectedCellIsElevator);
        Assert.HasCount(6, viewModel.SelectedCellLayerConnections);
    }

    [TestMethod]
    public void LayerDisplayNameIsEditableAndUsedByElevatorDestinations()
    {
        var viewModel = new MainWindowViewModel(loadDefaultWorld: false);
        viewModel.LoadWorldJsonFrom(DemoWorldPath());
        viewModel.SelectedWorldLayer = viewModel.WorldLayers.Single(layer => layer.Id == "level_0");

        viewModel.SelectedWorldLayerDisplayName = "Research Laboratory";

        Assert.AreEqual("Research Laboratory", viewModel.SelectedWorldLayer.Name);
        Assert.AreEqual("level_0", viewModel.SelectedWorldLayer.Id);

        viewModel.SelectedWorldLayer = viewModel.WorldLayers.Single(layer => layer.Id == "level_1");
        viewModel.SelectedCell = viewModel.Cells.Single(cell => cell.Row == 14 && cell.Column == 2);
        var laboratory = viewModel.SelectedCellLayerConnections.Single(option => option.LayerId == "level_0");
        Assert.AreEqual("Research Laboratory (level_0)", laboratory.Label);
    }

    [TestMethod]
    public void DemoWorldHasUniqueScaledKeysAndBlueLockedFinalGoal()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(DemoWorldPath()));
        var root = json.RootElement;
        var layers = root.GetProperty("layers").EnumerateArray().ToList();
        var keys = layers.SelectMany(layer => layer.GetProperty("spriteInstances")
            .EnumerateArray()
            .Where(sprite => sprite.GetProperty("spriteSet").GetString()!.StartsWith("item_key_"))
            .Select(sprite => (
                Layer: layer.GetProperty("id").GetString(),
                Set: sprite.GetProperty("spriteSet").GetString(),
                Scale: sprite.GetProperty("scaleCells").GetDouble())))
            .ToList();

        Assert.HasCount(3, keys);
        Assert.IsTrue(keys.All(key => Math.Abs(key.Scale - 0.25) < 0.001));
        CollectionAssert.AreEquivalent(
            new[] { "item_key_red", "item_key_green", "item_key_blue" },
            keys.Select(key => key.Set).ToArray());
        Assert.AreEqual("level_final", root.GetProperty("gameGoal").GetProperty("layer").GetString());
        Assert.AreEqual(14, root.GetProperty("gameGoal").GetProperty("column").GetInt32());
        var finalTransitions = root.GetProperty("layerTransitions").EnumerateArray().Where(transition =>
            transition.GetProperty("toLayer").GetString() == "level_final"
            && transition.GetProperty("requiredKey").GetString() == "blue").ToList();
        Assert.HasCount(6, finalTransitions);
        Assert.IsTrue(finalTransitions.All(transition =>
            transition.GetProperty("trigger").GetProperty("row").GetInt32() == 14
            && transition.GetProperty("trigger").GetProperty("column").GetInt32() == 2));

        var library = layers.Single(layer => layer.GetProperty("id").GetString() == "level_2");
        var libraryRows = library.GetProperty("cells").EnumerateArray().ToList();
        Assert.AreEqual("86", libraryRows[3].EnumerateArray().ElementAt(2).GetString());
        Assert.AreEqual("e1", libraryRows[14].EnumerateArray().ElementAt(2).GetString());

        var finalLayer = layers.Single(layer => layer.GetProperty("id").GetString() == "level_final");
        var finalRows = finalLayer.GetProperty("cells").EnumerateArray().ToList();
        Assert.AreEqual("b0", finalRows[14].EnumerateArray().ElementAt(2).GetString());
        Assert.AreEqual("21", finalRows[3].EnumerateArray().ElementAt(14).GetString());
        Assert.AreNotEqual("21", finalRows[3].EnumerateArray().ElementAt(15).GetString());
        Assert.AreEqual(
            "audio/demo_theme.mp3",
            finalLayer.GetProperty("backgroundMusic").GetProperty("file").GetString());

        Assert.HasCount(7, layers);
        Assert.HasCount(42, root.GetProperty("layerTransitions").EnumerateArray());
    }

    private static string DemoWorldPath()
    {
        return Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "..", "..",
            "res", "worlds", "demo_embedded", "demo.world.json"));
    }
}
