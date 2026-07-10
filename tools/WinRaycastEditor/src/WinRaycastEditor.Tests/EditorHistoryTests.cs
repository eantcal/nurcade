using WinRaycastEditor.Core;

namespace WinRaycastEditor.Tests;

[TestClass]
public sealed class EditorHistoryTests
{
    [TestMethod]
    public void EditingSpriteCoordinatesKeepsSpriteAndInspectorSelection()
    {
        var viewModel = new MainWindowViewModel(loadDefaultWorld: false);
        viewModel.LoadWorldJsonFrom(DemoWorldPath());
        Assert.IsNotNull(viewModel.Document);

        var sprite = viewModel.SpriteInstances.First();
        viewModel.SelectedSprite = sprite;
        var originalY = sprite.YCell;
        var originalScale = sprite.ScaleCells;
        var targetColumn = ((int)Math.Floor(sprite.XCell) + 1)
            % viewModel.Document!.ColumnCount;

        sprite.XCell = targetColumn + 0.5;

        Assert.AreSame(sprite, viewModel.SelectedSprite);
        Assert.AreEqual(originalY, sprite.YCell, 1e-9);
        Assert.AreEqual(originalScale, sprite.ScaleCells, 1e-9);
        Assert.IsNotNull(viewModel.SelectedCell);
        Assert.AreEqual(targetColumn, viewModel.SelectedCell!.Column);
        Assert.Contains(sprite, viewModel.SelectedCellSprites);
    }

    [TestMethod]
    public void FinalCellCanBeAssignedAndUndoneFromGoalLayer()
    {
        var viewModel = new MainWindowViewModel(loadDefaultWorld: false);
        viewModel.LoadWorldJsonFrom(DemoWorldPath());
        Assert.IsNotNull(viewModel.Document);
        Assert.IsNotNull(viewModel.Document!.GameGoal);

        var original = viewModel.Document.GameGoal!;
        var originalLayer = original.Layer;
        var originalRow = original.Row;
        var originalColumn = original.Column;
        var target = viewModel.Cells.First(cell =>
            cell.Row != originalRow || cell.Column != originalColumn);

        viewModel.SelectedLayer = "Goal";
        viewModel.SelectedCell = target;
        viewModel.SetSelectedCellAsGameGoalCommand.Execute(null);

        Assert.AreEqual(viewModel.Document.ActiveLayerId, viewModel.Document.GameGoal!.Layer);
        Assert.AreEqual(target.Row, viewModel.Document.GameGoal.Row);
        Assert.AreEqual(target.Column, viewModel.Document.GameGoal.Column);
        Assert.IsTrue(target.HasGameGoal);
        Assert.IsTrue(target.ShowGameGoalMarker);

        viewModel.UndoCommand.Execute(null);

        Assert.AreEqual(originalLayer, viewModel.Document.GameGoal!.Layer);
        Assert.AreEqual(originalRow, viewModel.Document.GameGoal.Row);
        Assert.AreEqual(originalColumn, viewModel.Document.GameGoal.Column);

        viewModel.RedoCommand.Execute(null);

        Assert.AreEqual(target.Row, viewModel.Document.GameGoal!.Row);
        Assert.AreEqual(target.Column, viewModel.Document.GameGoal.Column);
        Assert.IsTrue(target.HasGameGoal);
    }

    [TestMethod]
    public void CopyPasteCellContentCanBeUndoneAndRedone()
    {
        var viewModel = new MainWindowViewModel(loadDefaultWorld: false);
        viewModel.LoadWorldJsonFrom(DemoWorldPath());

        var source = viewModel.Cells[0];
        var target = viewModel.Cells[1];
        var targetBefore = EditorCellContent.Capture(target.Cell);

        viewModel.SelectedCell = source;
        viewModel.CopyCellCommand.Execute(null);
        viewModel.SelectedCell = target;
        viewModel.PasteCellCommand.Execute(null);

        Assert.AreEqual(source.Cell.Fields, target.Cell.Fields);
        Assert.AreEqual(source.Cell.HorizonImage, target.Cell.HorizonImage);

        viewModel.UndoCommand.Execute(null);

        Assert.AreEqual(targetBefore.Fields, target.Cell.Fields);
        Assert.AreEqual(targetBefore.HorizonImage, target.Cell.HorizonImage);

        viewModel.RedoCommand.Execute(null);

        Assert.AreEqual(source.Cell.Fields, target.Cell.Fields);
        Assert.AreEqual(source.Cell.HorizonImage, target.Cell.HorizonImage);
    }

    [TestMethod]
    public void CopyPasteCellContentDoesNotDependOnSelectedSpriteLayer()
    {
        var viewModel = new MainWindowViewModel(loadDefaultWorld: false);
        viewModel.LoadWorldJsonFrom(DemoWorldPath());

        var source = viewModel.Cells.First(cell => !string.IsNullOrWhiteSpace(cell.Cell.BlockId));
        var target = viewModel.Cells.First(cell => !ReferenceEquals(cell, source));

        viewModel.SelectedLayer = "Sprites";
        viewModel.SelectedCell = source;
        viewModel.CopyCellCommand.Execute(null);
        viewModel.SelectedCell = target;
        viewModel.PasteCellCommand.Execute(null);

        Assert.AreEqual(source.Cell.BlockId, target.Cell.BlockId);
        Assert.AreEqual(source.Cell.Fields, target.Cell.Fields);
        Assert.AreEqual(source.Cell.HorizonImage, target.Cell.HorizonImage);
    }

    [TestMethod]
    public void CopyPasteMultipleCellsCanBeUndoneAndRedone()
    {
        var viewModel = new MainWindowViewModel(loadDefaultWorld: false);
        viewModel.LoadWorldJsonFrom(DemoWorldPath());
        Assert.IsNotNull(viewModel.Document);

        var columns = viewModel.Document!.ColumnCount;
        var sourceCells = new[] {
            viewModel.Cells[0],
            viewModel.Cells[1],
            viewModel.Cells[columns],
            viewModel.Cells[columns + 1]
        };
        var targetCells = new[] {
            viewModel.Cells[(2 * columns) + 2],
            viewModel.Cells[(2 * columns) + 3],
            viewModel.Cells[(3 * columns) + 2],
            viewModel.Cells[(3 * columns) + 3]
        };
        var targetBefore = targetCells
            .Select(cell => EditorCellContent.Capture(cell.Cell))
            .ToList();

        viewModel.SelectedCell = sourceCells[0];
        viewModel.SetSelectedMapCells(sourceCells);
        viewModel.CopyCellCommand.Execute(null);
        viewModel.SelectedCell = targetCells[0];
        viewModel.SetSelectedMapCells([targetCells[0]]);
        viewModel.PasteCellCommand.Execute(null);

        for (var index = 0; index < sourceCells.Length; ++index) {
            Assert.AreEqual(sourceCells[index].Cell.BlockId, targetCells[index].Cell.BlockId);
            Assert.AreEqual(sourceCells[index].Cell.Fields, targetCells[index].Cell.Fields);
            Assert.AreEqual(sourceCells[index].Cell.HorizonImage, targetCells[index].Cell.HorizonImage);
        }

        viewModel.UndoCommand.Execute(null);

        for (var index = 0; index < targetCells.Length; ++index) {
            Assert.AreEqual(targetBefore[index].BlockId, targetCells[index].Cell.BlockId);
            Assert.AreEqual(targetBefore[index].Fields, targetCells[index].Cell.Fields);
            Assert.AreEqual(targetBefore[index].HorizonImage, targetCells[index].Cell.HorizonImage);
        }

        viewModel.RedoCommand.Execute(null);

        for (var index = 0; index < sourceCells.Length; ++index) {
            Assert.AreEqual(sourceCells[index].Cell.BlockId, targetCells[index].Cell.BlockId);
            Assert.AreEqual(sourceCells[index].Cell.Fields, targetCells[index].Cell.Fields);
            Assert.AreEqual(sourceCells[index].Cell.HorizonImage, targetCells[index].Cell.HorizonImage);
        }
    }

    [TestMethod]
    public void DirectCellTextureEditCanBeUndoneAndRedone()
    {
        var viewModel = new MainWindowViewModel(loadDefaultWorld: false);
        viewModel.LoadWorldJsonFrom(DemoWorldPath());

        var cell = viewModel.Cells.First(item => !item.Cell.Fields.HasSolidWall);
        var before = EditorCellContent.Capture(cell.Cell);

        cell.SolidWallTexture = 0x01;

        Assert.AreEqual(0x01, cell.Cell.Fields.SolidWallTexture);

        viewModel.UndoCommand.Execute(null);

        Assert.AreEqual(before.Fields, cell.Cell.Fields);

        viewModel.RedoCommand.Execute(null);

        Assert.AreEqual(0x01, cell.Cell.Fields.SolidWallTexture);
    }

    [TestMethod]
    public void PlayerStartEditCanBeUndoneAndRedone()
    {
        var viewModel = new MainWindowViewModel(loadDefaultWorld: false);
        viewModel.LoadWorldJsonFrom(DemoWorldPath());
        Assert.IsNotNull(viewModel.PlayerStart);

        var beforeX = viewModel.PlayerStart!.XCell;
        var beforeY = viewModel.PlayerStart.YCell;
        var beforeFacing = viewModel.PlayerStart.FacingDegrees;

        viewModel.PlayerStart.XCell = beforeX + 1.0;
        viewModel.PlayerStart.YCell = beforeY + 1.0;
        viewModel.PlayerStart.FacingDegrees = beforeFacing + 45.0;

        viewModel.UndoCommand.Execute(null);
        Assert.AreEqual(beforeFacing, viewModel.PlayerStart.FacingDegrees, 1e-9);

        viewModel.UndoCommand.Execute(null);
        Assert.AreEqual(beforeY, viewModel.PlayerStart.YCell, 1e-9);

        viewModel.UndoCommand.Execute(null);
        Assert.AreEqual(beforeX, viewModel.PlayerStart.XCell, 1e-9);

        viewModel.RedoCommand.Execute(null);
        viewModel.RedoCommand.Execute(null);
        viewModel.RedoCommand.Execute(null);

        Assert.AreEqual(beforeX + 1.0, viewModel.PlayerStart.XCell, 1e-9);
        Assert.AreEqual(beforeY + 1.0, viewModel.PlayerStart.YCell, 1e-9);
        Assert.AreEqual(beforeFacing + 45.0, viewModel.PlayerStart.FacingDegrees, 1e-9);
    }

    [TestMethod]
    public void DragDropMovesPlayerToTargetCellWithUndo()
    {
        var viewModel = new MainWindowViewModel(loadDefaultWorld: false);
        viewModel.LoadWorldJsonFrom(DemoWorldPath());
        Assert.IsNotNull(viewModel.PlayerStart);

        var beforeX = viewModel.PlayerStart!.XCell;
        var beforeY = viewModel.PlayerStart.YCell;

        var target = viewModel.Cells.First(cell =>
            cell.Row != (int)Math.Floor(beforeY) || cell.Column != (int)Math.Floor(beforeX));

        viewModel.MovePlayerToCell(target);

        Assert.AreEqual(target.Column + 0.5, viewModel.PlayerStart.XCell, 1e-9);
        Assert.AreEqual(target.Row + 0.5, viewModel.PlayerStart.YCell, 1e-9);

        viewModel.UndoCommand.Execute(null);

        Assert.AreEqual(beforeX, viewModel.PlayerStart.XCell, 1e-9);
        Assert.AreEqual(beforeY, viewModel.PlayerStart.YCell, 1e-9);
    }

    private static string DemoWorldPath()
    {
        return Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "..",
            "..",
            "res",
            "worlds",
            "demo_embedded",
            "demo.world.json"));
    }
}
