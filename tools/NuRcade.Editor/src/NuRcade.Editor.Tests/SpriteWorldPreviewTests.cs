using System.Windows.Media;
using System.Windows.Media.Media3D;
using NuRcade.Editor.Core;

namespace NuRcade.Editor.Tests;

[TestClass]
public sealed class SpriteWorldPreviewTests
{
    [TestMethod]
    public void LoadingWorldJsonExposesDeclaredSpriteSetsForPreview()
    {
        var viewModel = new MainWindowViewModel(loadDefaultWorld: false);

        viewModel.LoadWorldJsonFrom(DemoWorldPath());

        CollectionAssert.Contains(
            viewModel.SpriteSetFiles,
            "sprites/sheet_brute/sheet_brute.sprite.json");
        CollectionAssert.Contains(
            viewModel.SpriteSetFiles,
            "sprites/missile_brute/missile_brute.sprite.json");
        Assert.AreEqual(
            "sprites/sheet_brute/sheet_brute.sprite.json",
            viewModel.SelectedSpriteSetFile);
        Assert.IsNotNull(viewModel.SelectedSpriteDirection);
        Assert.IsNotNull(viewModel.SelectedSpriteDirectionPreview);

        viewModel.SelectedSpriteSetFile = "sprites/missile_brute/missile_brute.sprite.json";

        Assert.IsNotNull(viewModel.SelectedSpriteDirection);
        Assert.IsNotNull(viewModel.SelectedSpriteDirectionPreview);
        StringAssert.Contains(viewModel.SpriteMetadataSummary, "missile_brute");
    }

    [TestMethod]
    public void LoadingWorldJsonExposesPlayerWeaponMetadataForPreview()
    {
        var viewModel = new MainWindowViewModel(loadDefaultWorld: false);

        viewModel.LoadWorldJsonFrom(DemoWorldPath());

        StringAssert.Contains(viewModel.PlayerWeaponSummary, "pistol.weapon.json");
        StringAssert.Contains(viewModel.WeaponMetadataSummary, "pistol");
        Assert.AreEqual("pistol", viewModel.WeaponName);
        Assert.AreEqual("PNG", viewModel.WeaponFormat);
        Assert.HasCount(3, viewModel.WeaponAnimations);
        Assert.IsNotNull(viewModel.SelectedWeaponAnimation);
        Assert.IsNotNull(viewModel.SelectedWeaponAnimationFrame);
        Assert.IsNotNull(viewModel.WeaponAnimationPlayback.CurrentPreview);
    }

    [TestMethod]
    public void LoadingWorldJsonBuildsEditorial3DPreview()
    {
        var viewModel = new MainWindowViewModel(loadDefaultWorld: false);

        viewModel.LoadWorldJsonFrom(DemoWorldPath());

        Assert.IsGreaterThan(3, viewModel.Preview3DModel.Children.Count);
        Assert.IsGreaterThan(3, viewModel.Preview3DHitTargets.Count);
        StringAssert.Contains(viewModel.Preview3DSummary, "wall span");
        StringAssert.Contains(viewModel.Preview3DSummary, "sprite billboard");
        StringAssert.Contains(viewModel.Preview3DSummary, "textured material");
        StringAssert.Contains(viewModel.Preview3DSummary, "selection highlight");
        Assert.IsGreaterThan(0.1, viewModel.Preview3DCamera.LookDirection.Length);
    }

    [TestMethod]
    public void PreviewCameraCommandsSwitchBetweenAuthoringViews()
    {
        var viewModel = new MainWindowViewModel(loadDefaultWorld: false);
        viewModel.LoadWorldJsonFrom(DemoWorldPath());

        viewModel.PreviewTopCameraCommand.Execute(null);

        Assert.AreEqual("Top", viewModel.Preview3DViewMode);
        Assert.IsLessThan(-0.1, viewModel.Preview3DCamera.LookDirection.Y);

        viewModel.PreviewPlayerCameraCommand.Execute(null);

        Assert.AreEqual("Perspective", viewModel.Preview3DViewMode);
        Assert.AreEqual(viewModel.Document!.PlayerStart.XCell, viewModel.Preview3DCamera.Position.X, 0.001);
        Assert.AreEqual(viewModel.Document.PlayerStart.YCell, viewModel.Preview3DCamera.Position.Z, 0.001);

        viewModel.PreviewAngledCameraCommand.Execute(null);

        Assert.AreEqual("Angled", viewModel.Preview3DViewMode);
        Assert.IsGreaterThan(1.0, viewModel.Preview3DCamera.Position.Y);
    }

    [TestMethod]
    public void PreviewOrbitCommandsRotateZoomAndFitTheWorld()
    {
        var viewModel = new MainWindowViewModel(loadDefaultWorld: false);
        viewModel.LoadWorldJsonFrom(DemoWorldPath());

        var initialX = viewModel.Preview3DCamera.Position.X;
        var initialZ = viewModel.Preview3DCamera.Position.Z;

        viewModel.PreviewRotateRightCommand.Execute(null);

        Assert.AreNotEqual(initialX, viewModel.Preview3DCamera.Position.X, 0.001);
        Assert.AreNotEqual(initialZ, viewModel.Preview3DCamera.Position.Z, 0.001);

        var rotatedY = viewModel.Preview3DCamera.Position.Y;

        viewModel.PreviewZoomOutCommand.Execute(null);

        Assert.IsGreaterThan(rotatedY, viewModel.Preview3DCamera.Position.Y);

        viewModel.PreviewFitAllCommand.Execute(null);

        Assert.AreEqual("Angled", viewModel.Preview3DViewMode);
        Assert.AreEqual(50, viewModel.Preview3DCamera.FieldOfView, 0.001);
    }

    [TestMethod]
    public void PreviewPerspectiveCommandsNavigateTheMap()
    {
        var viewModel = new MainWindowViewModel(loadDefaultWorld: false);
        viewModel.LoadWorldJsonFrom(DemoWorldPath());

        viewModel.PreviewPlayerCameraCommand.Execute(null);
        var startX = viewModel.Preview3DCamera.Position.X;
        var startZ = viewModel.Preview3DCamera.Position.Z;

        viewModel.PreviewMoveForwardCommand.Execute(null);

        Assert.AreEqual("Perspective", viewModel.Preview3DViewMode);
        Assert.IsTrue(
            Math.Abs(viewModel.Preview3DCamera.Position.X - startX) > 0.001
            || Math.Abs(viewModel.Preview3DCamera.Position.Z - startZ) > 0.001);

        var lookX = viewModel.Preview3DCamera.LookDirection.X;
        var lookZ = viewModel.Preview3DCamera.LookDirection.Z;

        viewModel.PreviewRotateRightCommand.Execute(null);

        Assert.IsTrue(
            Math.Abs(viewModel.Preview3DCamera.LookDirection.X - lookX) > 0.001
            || Math.Abs(viewModel.Preview3DCamera.LookDirection.Z - lookZ) > 0.001);

        var beforeFieldOfView = viewModel.Preview3DCamera.FieldOfView;

        viewModel.PreviewZoomInCommand.Execute(null);

        Assert.IsLessThan(beforeFieldOfView, viewModel.Preview3DCamera.FieldOfView);
    }

    [TestMethod]
    public void PreviewAngledMoveCommandsPanTheMapWindow()
    {
        var viewModel = new MainWindowViewModel(loadDefaultWorld: false);
        viewModel.LoadWorldJsonFrom(DemoWorldPath());

        var startX = viewModel.Preview3DCamera.Position.X;
        var startZ = viewModel.Preview3DCamera.Position.Z;
        var startLookX = viewModel.Preview3DCamera.LookDirection.X;
        var startLookZ = viewModel.Preview3DCamera.LookDirection.Z;

        viewModel.PreviewStrafeRightCommand.Execute(null);

        Assert.AreEqual("Angled", viewModel.Preview3DViewMode);
        Assert.IsGreaterThan(startX, viewModel.Preview3DCamera.Position.X);
        Assert.AreEqual(startLookX, viewModel.Preview3DCamera.LookDirection.X, 0.001);
        Assert.AreEqual(startLookZ, viewModel.Preview3DCamera.LookDirection.Z, 0.001);

        viewModel.PreviewMoveForwardCommand.Execute(null);

        Assert.AreEqual("Angled", viewModel.Preview3DViewMode);
        Assert.IsGreaterThan(startZ, viewModel.Preview3DCamera.Position.Z);
    }

    [TestMethod]
    public void PreviewRotateButtonsControlPlayerOutsideAngledView()
    {
        var viewModel = new MainWindowViewModel(loadDefaultWorld: false);
        viewModel.LoadWorldJsonFrom(DemoWorldPath());

        viewModel.PreviewTopCameraCommand.Execute(null);

        var topLookY = viewModel.Preview3DCamera.LookDirection.Y;

        viewModel.PreviewRotateRightCommand.Execute(null);

        Assert.AreEqual("Perspective", viewModel.Preview3DViewMode);
        Assert.IsGreaterThan(topLookY, viewModel.Preview3DCamera.LookDirection.Y);
    }

    [TestMethod]
    public void PreviewLayerSwitchesHideSelectedRenderGroups()
    {
        var viewModel = new MainWindowViewModel(loadDefaultWorld: false);
        viewModel.LoadWorldJsonFrom(DemoWorldPath());

        StringAssert.Contains(viewModel.Preview3DSummary, "floor cell(s)");
        StringAssert.Contains(viewModel.Preview3DSummary, "wall span(s)");
        StringAssert.Contains(viewModel.Preview3DSummary, "ceiling cell(s)");
        StringAssert.Contains(viewModel.Preview3DSummary, "sprite billboard(s)");

        viewModel.PreviewShowFloors = false;
        viewModel.PreviewShowWalls = false;
        viewModel.PreviewShowCeilings = false;
        viewModel.PreviewShowSprites = false;

        StringAssert.Contains(viewModel.Preview3DSummary, "0 floor cell(s)");
        StringAssert.Contains(viewModel.Preview3DSummary, "0 wall span(s)");
        StringAssert.Contains(viewModel.Preview3DSummary, "0 ceiling cell(s)");
        StringAssert.Contains(viewModel.Preview3DSummary, "0 sprite billboard(s)");
        Assert.IsFalse(viewModel.Preview3DHitTargets.Any(item => item.Value.Kind == WorldPreview3DHitKind.Sprite));
    }

    [TestMethod]
    public void PreviewWallCubesFillTheWholeCellWithoutVisibleGaps()
    {
        var viewModel = new MainWindowViewModel(loadDefaultWorld: false);
        viewModel.LoadWorldJsonFrom(DemoWorldPath());

        var wallCoordinateCount = 0;
        foreach (var (model, target) in viewModel.Preview3DHitTargets) {
            if (target.Kind != WorldPreview3DHitKind.Cell
                || model is not GeometryModel3D { Geometry: MeshGeometry3D mesh }
                || mesh.Positions.Count == 0) {
                continue;
            }

            var minY = mesh.Positions.Min(point => point.Y);
            var maxY = mesh.Positions.Max(point => point.Y);
            if (maxY - minY < 0.2) {
                continue;
            }

            foreach (var point in mesh.Positions) {
                Assert.IsTrue(IsWholeCellCoordinate(point.X), $"Inset wall x coordinate: {point.X}");
                Assert.IsTrue(IsWholeCellCoordinate(point.Z), $"Inset wall z coordinate: {point.Z}");
                wallCoordinateCount += 2;
            }
        }

        Assert.IsGreaterThan(0, wallCoordinateCount);
    }

    [TestMethod]
    public void PreviewCompositesLockedDoorOverlayOverWallTexture()
    {
        var viewModel = new MainWindowViewModel(loadDefaultWorld: false);
        viewModel.LoadWorldJsonFrom(DemoWorldPath());

        var lockedDoorOverlayFace = viewModel.Preview3DHitTargets.Keys
            .OfType<GeometryModel3D>()
            .FirstOrDefault(model =>
                model.Material is DiffuseMaterial { Brush: DrawingBrush });

        Assert.IsNotNull(lockedDoorOverlayFace);
    }

    [TestMethod]
    public void PreviewHitTargetsSelectCellsAndSprites()
    {
        var viewModel = new MainWindowViewModel(loadDefaultWorld: false);
        viewModel.LoadWorldJsonFrom(DemoWorldPath());

        var cellHit = viewModel.Preview3DHitTargets.First(
            item => item.Value.Kind == WorldPreview3DHitKind.Cell);
        viewModel.SelectPreview3DTarget(cellHit.Key);

        Assert.IsNotNull(viewModel.SelectedCell);
        Assert.AreEqual(cellHit.Value.Row, viewModel.SelectedCell.Row);
        Assert.AreEqual(cellHit.Value.Column, viewModel.SelectedCell.Column);

        var spriteHit = viewModel.Preview3DHitTargets.First(
            item => item.Value.Kind == WorldPreview3DHitKind.Sprite);
        viewModel.SelectPreview3DTarget(spriteHit.Key);

        Assert.AreEqual("Sprites", viewModel.SelectedLayer);
        Assert.IsNotNull(viewModel.SelectedSprite);
        Assert.AreSame(spriteHit.Value.Sprite, viewModel.SelectedSprite.Sprite);
    }

    [TestMethod]
    public void SpriteLayerSelectionTracksCellSpritesAndPreview()
    {
        var viewModel = new MainWindowViewModel(loadDefaultWorld: false);
        viewModel.LoadWorldJsonFrom(DemoWorldPath());

        viewModel.SelectedLayer = "Sprites";
        viewModel.SelectedSprite = viewModel.SpriteInstances.First();

        Assert.IsTrue(viewModel.IsSpriteLayerSelected);
        Assert.IsFalse(viewModel.IsCellEditingLayerSelected);
        Assert.IsNotNull(viewModel.SelectedCell);
        Assert.IsNotNull(viewModel.SelectedSprite);
        CollectionAssert.Contains(viewModel.SelectedCellSprites, viewModel.SelectedSprite);
        Assert.IsNotNull(viewModel.SelectedSpritePreview);

        viewModel.SelectedLayer = "Floor";

        Assert.IsFalse(viewModel.IsSpriteLayerSelected);
        Assert.IsTrue(viewModel.IsCellEditingLayerSelected);
        Assert.IsFalse(viewModel.IsWallLayerSelected);
        Assert.IsTrue(viewModel.IsFloorLayerSelected);
        Assert.IsFalse(viewModel.IsCeilingLayerSelected);

        viewModel.SelectedLayer = "Walls";

        Assert.IsTrue(viewModel.IsCellEditingLayerSelected);
        Assert.IsTrue(viewModel.IsWallLayerSelected);
    }

    [TestMethod]
    public void DemoWorldSurfacesActiveLayerItemSprites()
    {
        var viewModel = new MainWindowViewModel(loadDefaultWorld: false);
        viewModel.LoadWorldJsonFrom(DemoWorldPath());

        // The active layer (level_1) carries the item_* pickups; they must show up
        // in the working set alongside the global top-level enemies.
        Assert.IsTrue(viewModel.SpriteInstances.Any(sprite =>
            sprite.SpriteSet.StartsWith("item_", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(viewModel.SpriteInstances.Any(sprite => sprite.IsItem));
    }

    [TestMethod]
    public void UnsavedChangesAreDetectedAndClearedOnSave()
    {
        var viewModel = new MainWindowViewModel(loadDefaultWorld: false);
        viewModel.LoadWorldJsonFrom(DemoWorldPath());
        Assert.IsFalse(viewModel.HasUnsavedChanges);

        viewModel.SelectedLayer = "Sprites";
        viewModel.SelectedCell = viewModel.Cells.First();
        Assert.IsTrue(viewModel.AddSpriteToSelectedCellCommand.CanExecute(null));
        viewModel.AddSpriteToSelectedCellCommand.Execute(null);

        Assert.IsTrue(viewModel.HasUnsavedChanges);

        var tempPath = Path.Combine(
            Path.GetTempPath(),
            $"nurcade_test_{Guid.NewGuid():N}.world.json");
        try {
            viewModel.SaveWorldJsonTo(tempPath);
            Assert.IsFalse(viewModel.HasUnsavedChanges);
        }
        finally {
            if (File.Exists(tempPath)) {
                File.Delete(tempPath);
            }
        }
    }

    [TestMethod]
    public void AddTexturesFromFilesCopiesImageAndExtendsPalette()
    {
        var viewModel = new MainWindowViewModel(loadDefaultWorld: false);
        viewModel.LoadWorldJsonFrom(DemoWorldPath());

        var demoDirectory = Path.GetDirectoryName(DemoWorldPath())!;
        var samplePng = Directory
            .GetFiles(Path.Combine(demoDirectory, "textures"), "*.png")
            .First();

        var tempDirectory = Directory.CreateTempSubdirectory().FullName;
        try {
            // Redirect the world directory to a temp folder so the test never writes
            // into the repository's demo assets.
            viewModel.Document!.SourcePath = Path.Combine(tempDirectory, "demo.world.json");
            var sourceImage = Path.Combine(tempDirectory, $"new_{Guid.NewGuid():N}.png");
            File.Copy(samplePng, sourceImage);

            var beforeTextures = viewModel.Textures.Count;
            var beforeMappings = viewModel.Document.TextureMap.Count;

            viewModel.AddTexturesFromFiles(new[] { sourceImage });

            Assert.HasCount(beforeTextures + 1, viewModel.Textures);
            Assert.HasCount(beforeMappings + 1, viewModel.Document.TextureMap);
            Assert.IsTrue(File.Exists(
                Path.Combine(tempDirectory, "textures", Path.GetFileName(sourceImage))));
            Assert.IsTrue(viewModel.HasUnsavedChanges);
        }
        finally {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [TestMethod]
    public void RemoveDuplicateBlocksMergesIdenticalPaletteEntriesAndKeepsCellsValid()
    {
        var viewModel = new MainWindowViewModel(loadDefaultWorld: false);
        viewModel.LoadWorldJsonFrom(DemoWorldPath());

        // Cloning a block produces a palette entry identical to its source.
        viewModel.SelectedBlock = viewModel.Blocks.First();
        viewModel.CloneSelectedBlockCommand.Execute(null);
        var afterClone = viewModel.Document!.Blocks.Count;

        viewModel.RemoveDuplicateBlocksCommand.Execute(null);

        Assert.IsLessThan(
            afterClone,
            viewModel.Document.Blocks.Count,
            "expected duplicate blocks to be removed");

        var signatures = viewModel.Document.Blocks.Values
            .Select(LegacyWorldConverter.BlockSignature)
            .ToList();
        Assert.AreEqual(
            signatures.Count,
            signatures.Distinct().Count(),
            "palette still contains duplicate block definitions");

        // Every cell must still reference an existing block.
        foreach (var row in viewModel.Document.Rows) {
            foreach (var cell in row) {
                if (!string.IsNullOrEmpty(cell.BlockId)) {
                    Assert.IsTrue(
                        viewModel.Document.Blocks.ContainsKey(cell.BlockId),
                        $"cell references missing block {cell.BlockId}");
                }
            }
        }
    }

    [TestMethod]
    public void SelectedSpriteDrivesSpriteAnimationPreviewSet()
    {
        var viewModel = new MainWindowViewModel(loadDefaultWorld: false);
        viewModel.LoadWorldJsonFrom(DemoWorldPath());

        viewModel.SelectedSprite = viewModel.SpriteInstances.First(sprite =>
            string.Equals(sprite.SpriteSet, "missile_brute", StringComparison.OrdinalIgnoreCase));

        Assert.AreEqual(
            "sprites/missile_brute/missile_brute.sprite.json",
            viewModel.SelectedSpriteSetFile);
        StringAssert.Contains(viewModel.SpriteMetadataSummary, "missile_brute");
        Assert.IsNotNull(viewModel.SelectedSpriteAnimation);
        Assert.IsNotNull(viewModel.AnimationPlayback.CurrentPreview);
    }

    [TestMethod]
    public void SavingDemoWorldKeepsExplosiveItemMetadata()
    {
        var original = WorldJsonDocumentService.Load(DemoWorldPath());
        Assert.IsTrue(original.Success, string.Join(Environment.NewLine, original.Errors));
        var expectedExplosiveItemCount = original.Document!
            .Layers
            .SelectMany(layer => layer.SpriteInstances)
            .Count(sprite =>
                sprite.SpriteSet is "item_ammo_box"
                    or "item_oxygen_tank"
                    or "item_hazard_barrel");

        var viewModel = new MainWindowViewModel(loadDefaultWorld: false);
        viewModel.LoadWorldJsonFrom(DemoWorldPath());

        var tempPath = Path.Combine(
            Path.GetTempPath(),
            $"nurcade_explosive_roundtrip_{Guid.NewGuid():N}.world.json");

        try {
            viewModel.SaveWorldJsonTo(tempPath);
            var loaded = WorldJsonDocumentService.Load(tempPath);

            Assert.IsTrue(loaded.Success, string.Join(Environment.NewLine, loaded.Errors));
            var explosiveItems = loaded.Document!
                .Layers
                .SelectMany(layer => layer.SpriteInstances)
                .Where(sprite =>
                    sprite.SpriteSet is "item_ammo_box"
                        or "item_oxygen_tank"
                        or "item_hazard_barrel")
                .ToList();

            Assert.HasCount(expectedExplosiveItemCount, explosiveItems);
            foreach (var sprite in explosiveItems) {
                Assert.IsTrue(sprite.Explosive, $"{sprite.Name} lost explosive=true");
                Assert.AreEqual(45.0, sprite.ExplosiveHitPoints, 1e-9, sprite.Name);
                Assert.AreEqual(2.2, sprite.ExplosionRadiusCells, 1e-9, sprite.Name);
                Assert.AreEqual(60.0, sprite.ExplosionDamage, 1e-9, sprite.Name);
                Assert.AreEqual(1.35, sprite.ExplosionScaleCells, 1e-9, sprite.Name);
                Assert.AreEqual("explosion_512", sprite.ExplosionSpriteSet, sprite.Name);
                Assert.AreEqual("ash_pile", sprite.DestroyedSpriteSet, sprite.Name);
                Assert.AreEqual(0.48, sprite.DestroyedScaleCells, 1e-9, sprite.Name);
            }
        }
        finally {
            if (File.Exists(tempPath)) {
                File.Delete(tempPath);
            }
        }
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

    private static bool IsWholeCellCoordinate(double value)
    {
        return Math.Abs(value - Math.Round(value)) < 0.0001;
    }
}
