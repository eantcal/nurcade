using System.Windows;
using System.Windows.Threading;
using NuRcade.Editor;
using NuRcade.Editor.Core;

namespace NuRcade.Editor.Tests;

[TestClass]
public sealed class BlockPaletteViewModelTests
{
    [TestMethod]
    public void BlockInspectorEditsUnderlyingBlockDefinition()
    {
        var block = new WorldBlockDefinition();
        var viewModel = new BlockPaletteEntryViewModel("open", block);

        viewModel.Name = "Courtyard";
        viewModel.FloorTexture = "floor_moss";
        viewModel.FloorHeight = -128;
        viewModel.CeilingTexture = "ceiling_stone";
        viewModel.CeilingHeight = 768;
        viewModel.HorizonImage = "skyline.bmp";

        Assert.AreEqual("Courtyard", block.Name);
        Assert.IsNotNull(block.Floor);
        Assert.AreEqual("floor_moss", block.Floor.Texture);
        Assert.AreEqual(-128, block.Floor.Height);
        Assert.IsNotNull(block.Ceiling);
        Assert.AreEqual("ceiling_stone", block.Ceiling.Texture);
        Assert.AreEqual(768, block.Ceiling.Height);
        Assert.AreEqual("skyline.bmp", block.HorizonImage);
    }

    [TestMethod]
    public void WallSpanInspectorEditsUnderlyingSpan()
    {
        var span = new WorldWallSpan
        {
            Kind = "solid",
            Texture = "stone",
            Bottom = 0,
            Top = 512,
            Collision = true
        };
        var block = new WorldBlockDefinition();
        block.Walls.Add(span);
        var viewModel = new BlockPaletteEntryViewModel("wall", block);

        var wall = viewModel.Walls.Single();
        wall.Kind = "transparent";
        wall.Texture = "bars";
        wall.Bottom = 128;
        wall.Top = 1024;
        wall.Passable = true;
        wall.NorthTexture = "stone_north";
        wall.EastTexture = "stone_east";
        wall.SouthTexture = "stone_south";
        wall.WestTexture = "stone_west";

        Assert.AreEqual("transparent", span.Kind);
        Assert.AreEqual("bars", span.Texture);
        Assert.AreEqual(128, span.Bottom);
        Assert.AreEqual(1024, span.Top);
        Assert.IsFalse(span.Collision);
        Assert.IsTrue(span.Passable);
        Assert.IsNotNull(span.FaceTextures);
        Assert.AreEqual("stone_north", span.FaceTextures["north"]);
        Assert.AreEqual("stone_east", span.FaceTextures["east"]);
        Assert.AreEqual("stone_south", span.FaceTextures["south"]);
        Assert.AreEqual("stone_west", span.FaceTextures["west"]);

        wall.NorthTexture = string.Empty;

        Assert.IsFalse(span.FaceTextures.ContainsKey("north"));
    }

    [TestMethod]
    public void BlockInspectorCanAddAndRemoveWallSpans()
    {
        var block = new WorldBlockDefinition();
        var viewModel = new BlockPaletteEntryViewModel("wall", block);

        viewModel.AddWallSpanCommand.Execute(null);

        Assert.HasCount(1, block.Walls);
        Assert.HasCount(1, viewModel.Walls);
        Assert.AreSame(viewModel.Walls[0], viewModel.SelectedWallSpan);
        Assert.AreEqual("solid", block.Walls[0].Kind);
        Assert.AreEqual(0, block.Walls[0].Bottom);
        Assert.AreEqual(512, block.Walls[0].Top);

        viewModel.RemoveWallSpanCommand.Execute(null);

        Assert.IsEmpty(block.Walls);
        Assert.IsEmpty(viewModel.Walls);
        Assert.IsNull(viewModel.SelectedWallSpan);
    }

    [TestMethod]
    public void DoubleHeightBlockUsesTallerPreview()
    {
        var single = new BlockPaletteEntryViewModel("single", new WorldBlockDefinition {
            Walls = {
                new WorldWallSpan { Texture = "wall", Bottom = 0, Top = 512 }
            }
        });
        var doubled = new BlockPaletteEntryViewModel("double", new WorldBlockDefinition {
            Walls = {
                new WorldWallSpan { Texture = "wall", Bottom = 512, Top = 1024 }
            }
        });

        Assert.AreEqual(170.0, single.PreviewHeight, 1e-9);
        Assert.AreEqual(340.0, doubled.PreviewHeight, 1e-9);
    }

    [TestMethod]
    public void BlockInspectorCanEditBlockAnimationsAndStructureTree()
    {
        var block = new WorldBlockDefinition {
            Name = "animated_panel",
            Floor = new WorldSurface { Texture = "floor", Height = 0 },
            Door = new WorldDoorDefinition {
                Frames = [ "door_closed", "door_open" ]
            },
            Walls = {
                new WorldWallSpan {
                    Kind = "solid",
                    Texture = "wall_base",
                    FaceTextures = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
                        ["north"] = "wall_north"
                    },
                    Bottom = 0,
                    Top = 512
                }
            }
        };
        var viewModel = new BlockPaletteEntryViewModel("0a", block);

        Assert.HasCount(1, viewModel.StructureTree);
        Assert.IsTrue(viewModel.StructureTree[0].Children.Any(node => node.Label.StartsWith("Walls")));

        viewModel.AddAnimationCommand.Execute(null);

        Assert.IsNotNull(block.Animations);
        Assert.HasCount(1, block.Animations!);
        Assert.IsNotNull(viewModel.SelectedAnimation);

        viewModel.SelectedAnimation!.Name = "warning_lights";
        viewModel.SelectedAnimation.Target = "wall";
        viewModel.SelectedAnimation.WallIndexText = "0";
        viewModel.SelectedAnimation.Face = "north";
        viewModel.SelectedAnimation.FrameDurationMs = 80.0;
        viewModel.SelectedAnimation.Loop = false;
        viewModel.SelectedAnimation.FramesText = "wall_base, wall_north";

        Assert.AreEqual("warning_lights", block.Animations[0].Name);
        Assert.AreEqual("wall", block.Animations[0].Target);
        Assert.AreEqual(0, block.Animations[0].WallIndex);
        Assert.AreEqual("north", block.Animations[0].Face);
        Assert.AreEqual(80.0, block.Animations[0].FrameDurationMs, 1e-9);
        Assert.IsFalse(block.Animations[0].Loop);
        CollectionAssert.AreEqual(
            new[] { "wall_base", "wall_north" },
            block.Animations[0].Frames);
        Assert.IsTrue(viewModel.StructureTree[0].Children
            .SelectMany(node => node.Children)
            .Any(node => node.Label.Contains("warning_lights")));

        viewModel.RemoveAnimationCommand.Execute(null);

        Assert.IsNull(block.Animations);
        Assert.IsEmpty(viewModel.Animations);
    }

    [TestMethod]
    public void BlockInspectorCanEditDoorKeyAndLockedOverlays()
    {
        var block = new WorldBlockDefinition {
            Door = new WorldDoorDefinition {
                Frames = [ "door_closed", "door_open" ]
            }
        };
        var viewModel = new BlockPaletteEntryViewModel("05", block);

        viewModel.DoorRequiredKey = "blue";
        viewModel.DoorGreenOverlayTexture = "overlay_green";
        viewModel.DoorBlueOverlayTexture = "overlay_blue";
        viewModel.DoorRedOverlayTexture = "overlay_red";

        Assert.IsNotNull(block.Door);
        Assert.AreEqual("blue", block.Door!.RequiredKey);
        Assert.IsNotNull(block.Door.LockedOverlays);
        Assert.AreEqual("overlay_green", block.Door.LockedOverlays!["green"]);
        Assert.AreEqual("overlay_blue", block.Door.LockedOverlays["blue"]);
        Assert.AreEqual("overlay_red", block.Door.LockedOverlays["red"]);
        Assert.IsTrue(viewModel.StructureTree[0].Children
            .SelectMany(node => node.Children)
            .Any(node => node.Label.Contains("locked overlay blue: overlay_blue")));

        viewModel.DoorGreenOverlayTexture = string.Empty;

        Assert.IsFalse(block.Door.LockedOverlays.ContainsKey("green"));
    }

    [TestMethod]
    public void EditorLoadsDisplaysAndSavesDoorLockMetadata()
    {
        var viewModel = new MainWindowViewModel(loadDefaultWorld: false);
        viewModel.LoadWorldJsonFrom(DemoWorldPath());
        var doorBlock = viewModel.Blocks.First(block => block.Id == "05");

        viewModel.SelectedBlock = doorBlock;

        Assert.AreEqual("green", doorBlock.DoorRequiredKey);
        Assert.AreEqual("f0", doorBlock.DoorGreenOverlayTexture);
        Assert.AreEqual("f1", doorBlock.DoorBlueOverlayTexture);
        Assert.AreEqual("f2", doorBlock.DoorRedOverlayTexture);
        StringAssert.Contains(doorBlock.DoorSummary, "key green");
        Assert.IsTrue(doorBlock.DoorFrames.All(frame => frame.HasPreview));

        doorBlock.DoorRequiredKey = "red";
        doorBlock.DoorGreenOverlayTexture = "f2";
        doorBlock.DoorBlueOverlayTexture = string.Empty;

        var path = Path.Combine(
            Path.GetTempPath(),
            $"nurcade-door-metadata-{Guid.NewGuid():N}.world.json");
        try {
            viewModel.SaveWorldJsonTo(path);
            var loaded = WorldJsonDocumentService.Load(path);

            Assert.IsTrue(loaded.Success, string.Join(Environment.NewLine, loaded.Errors));
            var savedDoor = loaded.Document!.Blocks["05"].Door;
            Assert.IsNotNull(savedDoor);
            Assert.AreEqual("red", savedDoor!.RequiredKey);
            Assert.IsNotNull(savedDoor.LockedOverlays);
            Assert.AreEqual("f2", savedDoor.LockedOverlays!["green"]);
            Assert.IsFalse(savedDoor.LockedOverlays.ContainsKey("blue"));
            Assert.AreEqual("f2", savedDoor.LockedOverlays["red"]);
        }
        finally {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void CellInspectorCanEditDoorLockMetadataOnSharedTemplate()
    {
        var viewModel = new MainWindowViewModel(loadDefaultWorld: false);
        viewModel.LoadWorldJsonFrom(DemoWorldPath());
        var doorCell = viewModel.Cells.First(cell =>
            !string.IsNullOrWhiteSpace(cell.Cell.BlockId)
            && viewModel.Document!.Blocks.TryGetValue(cell.Cell.BlockId, out var block)
            && !string.IsNullOrWhiteSpace(block.Door?.RequiredKey));
        viewModel.SelectedCell = doorCell;
        viewModel.SelectedCellEditScope = "Shared template";
        var blockId = doorCell.Cell.BlockId;

        var originalKey = viewModel.SelectedCellDoorRequiredKey;
        var originalGreenOverlay = viewModel.SelectedCellDoorGreenOverlayTexture;
        var originalBlueOverlay = viewModel.SelectedCellDoorBlueOverlayTexture;
        var newKey = string.Equals(originalKey, "blue", StringComparison.OrdinalIgnoreCase) ? "green" : "blue";
        var newOverlay = string.Equals(newKey, "blue", StringComparison.OrdinalIgnoreCase) ? "f1" : "f0";

        Assert.IsFalse(string.IsNullOrWhiteSpace(originalKey));
        StringAssert.Contains(viewModel.SelectedCellDoorSummary, $"key {originalKey}");

        viewModel.SelectedCellDoorRequiredKey = newKey;
        if (string.Equals(newKey, "blue", StringComparison.OrdinalIgnoreCase)) {
            viewModel.SelectedCellDoorBlueOverlayTexture = newOverlay;
            viewModel.SelectedCellDoorGreenOverlayTexture = string.Empty;
        }
        else {
            viewModel.SelectedCellDoorGreenOverlayTexture = newOverlay;
            viewModel.SelectedCellDoorBlueOverlayTexture = string.Empty;
        }

        var door = viewModel.Document!.Blocks[blockId].Door;
        Assert.IsNotNull(door);
        Assert.AreEqual(newKey, door!.RequiredKey);
        Assert.IsNotNull(door.LockedOverlays);
        Assert.AreEqual(newOverlay, door.LockedOverlays![newKey]);

        viewModel.UndoCommand.Execute(null);
        viewModel.UndoCommand.Execute(null);
        viewModel.UndoCommand.Execute(null);

        door = viewModel.Document.Blocks[blockId].Door;
        Assert.IsNotNull(door);
        Assert.AreEqual(originalKey, door!.RequiredKey);
        if (string.IsNullOrWhiteSpace(originalGreenOverlay)) {
            Assert.IsFalse(door.LockedOverlays?.ContainsKey("green") ?? false);
        }
        else {
            Assert.AreEqual(originalGreenOverlay, door.LockedOverlays!["green"]);
        }

        if (string.IsNullOrWhiteSpace(originalBlueOverlay)) {
            Assert.IsFalse(door.LockedOverlays?.ContainsKey("blue") ?? false);
        }
        else {
            Assert.AreEqual(originalBlueOverlay, door.LockedOverlays!["blue"]);
        }
    }

    [TestMethod]
    public void BlockSignatureDistinguishesDoorLockMetadata()
    {
        var greenDoor = new WorldBlockDefinition {
            Door = new WorldDoorDefinition {
                RequiredKey = "green",
                Frames = [ "b0", "b1" ],
                LockedOverlays = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
                    ["green"] = "f0"
                }
            }
        };
        var redDoor = new WorldBlockDefinition {
            Door = new WorldDoorDefinition {
                RequiredKey = "red",
                Frames = [ "b0", "b1" ],
                LockedOverlays = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
                    ["red"] = "f2"
                }
            }
        };

        Assert.AreNotEqual(
            LegacyWorldConverter.BlockSignature(greenDoor),
            LegacyWorldConverter.BlockSignature(redDoor));
    }

    [TestMethod]
    public void EditorCanCloneSelectedBlockIntoPalette()
    {
        var viewModel = new MainWindowViewModel(loadDefaultWorld: false);
        viewModel.LoadWorldJsonFrom(DemoWorldPath());
        var originalCount = viewModel.Blocks.Count;
        viewModel.SelectedBlock = viewModel.Blocks.First(block => block.Id == "05");

        viewModel.CloneSelectedBlockCommand.Execute(null);

        Assert.HasCount(originalCount + 1, viewModel.Blocks);
        Assert.IsNotNull(viewModel.SelectedBlock);
        Assert.AreNotEqual("05", viewModel.SelectedBlock!.Id);
        Assert.AreEqual("sliding_corridor_door_copy", viewModel.SelectedBlock.Name);
        Assert.HasCount(2, viewModel.SelectedBlock.Walls);
        Assert.AreEqual("transparent", viewModel.SelectedBlock.Walls[1].Kind);
    }

    [TestMethod]
    public void CellInspectorCanOpenSelectedCellBlockTab()
    {
        var viewModel = new MainWindowViewModel(loadDefaultWorld: false);
        viewModel.LoadWorldJsonFrom(DemoWorldPath());
        var cell = viewModel.Cells.First(item => !string.IsNullOrWhiteSpace(item.Cell.BlockId));
        viewModel.SelectedCell = cell;

        viewModel.OpenSelectedCellBlockCommand.Execute(null);

        Assert.IsNotNull(viewModel.SelectedBlock);
        Assert.AreEqual(cell.Cell.BlockId, viewModel.SelectedBlock!.Id);
        Assert.AreEqual(6, viewModel.SelectedInspectorTabIndex);
    }

    [TestMethod]
    public void CellTextureEditorCreatesANewBlockForTheSelectedCell()
    {
        var viewModel = new MainWindowViewModel(loadDefaultWorld: false);
        viewModel.LoadWorldJsonFrom(DemoWorldPath());
        var cell = viewModel.Cells.First(item =>
            !string.IsNullOrWhiteSpace(item.Cell.BlockId)
            && viewModel.Document!.Blocks[item.Cell.BlockId].Walls.Any(wall => wall.Bottom == 0));
        viewModel.SelectedCell = cell;
        var originalBlockId = cell.Cell.BlockId;
        var originalCount = viewModel.Document!.Blocks.Count;
        var originalBlock = viewModel.Document.Blocks[originalBlockId];
        var originalLowerTexture = originalBlock.Walls.First(wall => wall.Bottom == 0).Texture;
        var replacementTexture = viewModel.TextureChoices.First(choice =>
            !string.IsNullOrEmpty(choice.Key) && choice.Key != originalLowerTexture).Key;

        viewModel.SelectedCellLowerWallTextureKey = replacementTexture;

        Assert.AreNotEqual(originalBlockId, cell.Cell.BlockId);
        Assert.HasCount(originalCount + 1, viewModel.Document.Blocks);
        Assert.AreEqual(
            replacementTexture,
            viewModel.Document.Blocks[cell.Cell.BlockId].Walls.First(wall => wall.Bottom == 0).Texture);
        Assert.AreEqual(originalLowerTexture, originalBlock.Walls.First(wall => wall.Bottom == 0).Texture);
        Assert.IsGreaterThan(0, viewModel.SelectedCellPreview3DModel.Children.Count);
    }

    [TestMethod]
    public void CellTextureEditorReusesUniqueCellBlockOnLaterEdits()
    {
        var viewModel = new MainWindowViewModel(loadDefaultWorld: false);
        viewModel.LoadWorldJsonFrom(DemoWorldPath());
        var cell = viewModel.Cells.First(item =>
            !string.IsNullOrWhiteSpace(item.Cell.BlockId)
            && viewModel.Document!.Blocks[item.Cell.BlockId].Walls.Any(wall => wall.Bottom == 0));
        viewModel.SelectedCell = cell;
        var originalCount = viewModel.Document!.Blocks.Count;
        var originalBlockId = cell.Cell.BlockId;
        var originalTexture = viewModel.Document.Blocks[originalBlockId].Walls.First(wall => wall.Bottom == 0).Texture;
        var replacementTextures = viewModel.TextureChoices
            .Where(choice => !string.IsNullOrEmpty(choice.Key) && choice.Key != originalTexture)
            .Take(2)
            .Select(choice => choice.Key)
            .ToArray();

        viewModel.SelectedCellLowerWallTextureKey = replacementTextures[0];
        var uniqueBlockId = cell.Cell.BlockId;
        viewModel.SelectedCellLowerWallTextureKey = replacementTextures[1];

        Assert.AreNotEqual(originalBlockId, uniqueBlockId);
        Assert.AreEqual(uniqueBlockId, cell.Cell.BlockId);
        Assert.HasCount(originalCount + 1, viewModel.Document.Blocks);
        Assert.AreEqual(
            replacementTextures[1],
            viewModel.Document.Blocks[uniqueBlockId].Walls.First(wall => wall.Bottom == 0).Texture);

        viewModel.UndoCommand.Execute(null);
        Assert.AreEqual(
            replacementTextures[0],
            viewModel.Document.Blocks[uniqueBlockId].Walls.First(wall => wall.Bottom == 0).Texture);

        viewModel.RedoCommand.Execute(null);
        Assert.AreEqual(
            replacementTextures[1],
            viewModel.Document.Blocks[uniqueBlockId].Walls.First(wall => wall.Bottom == 0).Texture);
    }

    [TestMethod]
    public void CellTextureEditorCanEditSharedTemplateFromSelectedCell()
    {
        var viewModel = new MainWindowViewModel(loadDefaultWorld: false);
        viewModel.LoadWorldJsonFrom(DemoWorldPath());
        var sharedCells = viewModel.Cells
            .Where(cell => !string.IsNullOrWhiteSpace(cell.Cell.BlockId)
                && viewModel.Document!.Blocks[cell.Cell.BlockId].Walls.Any(wall => wall.Bottom == 0))
            .GroupBy(cell => cell.Cell.BlockId)
            .First(group => group.Count() > 1)
            .ToList();
        var cell = sharedCells[0];
        viewModel.SelectedCell = cell;
        viewModel.SelectedCellEditScope = "Shared template";
        var blockId = cell.Cell.BlockId;
        var originalCount = viewModel.Document!.Blocks.Count;
        var originalTexture = viewModel.Document.Blocks[blockId].Walls.First(wall => wall.Bottom == 0).Texture;
        var replacementTexture = viewModel.TextureChoices.First(choice =>
            !string.IsNullOrEmpty(choice.Key) && choice.Key != originalTexture).Key;

        viewModel.SelectedCellLowerWallTextureKey = replacementTexture;

        Assert.AreEqual(blockId, cell.Cell.BlockId);
        Assert.HasCount(originalCount, viewModel.Document.Blocks);
        Assert.AreEqual(
            replacementTexture,
            viewModel.Document.Blocks[blockId].Walls.First(wall => wall.Bottom == 0).Texture);
        Assert.IsTrue(sharedCells.All(item =>
            item.Cell.Fields.SolidWallTexture == byte.Parse(replacementTexture, System.Globalization.NumberStyles.HexNumber)));

        viewModel.UndoCommand.Execute(null);

        Assert.AreEqual(
            originalTexture,
            viewModel.Document.Blocks[blockId].Walls.First(wall => wall.Bottom == 0).Texture);
        Assert.IsTrue(sharedCells.All(item =>
            item.Cell.Fields.SolidWallTexture == byte.Parse(originalTexture, System.Globalization.NumberStyles.HexNumber)));
    }

    [TestMethod]
    public void CellInspectorCanExplicitlyCreateUniqueBlockForSelectedCell()
    {
        var viewModel = new MainWindowViewModel(loadDefaultWorld: false);
        viewModel.LoadWorldJsonFrom(DemoWorldPath());
        var cell = viewModel.Cells.First(item => !string.IsNullOrWhiteSpace(item.Cell.BlockId));
        viewModel.SelectedCell = cell;
        var originalBlockId = cell.Cell.BlockId;
        var originalCount = viewModel.Document!.Blocks.Count;

        viewModel.MakeSelectedCellUniqueBlockCommand.Execute(null);

        Assert.AreNotEqual(originalBlockId, cell.Cell.BlockId);
        Assert.HasCount(originalCount + 1, viewModel.Document.Blocks);
        Assert.AreEqual(cell.Cell.BlockId, viewModel.SelectedBlock!.Id);
        Assert.IsTrue(viewModel.IsSelectedCellBlockUnique);
    }

    [TestMethod]
    public void CellInspectorCanEditUniqueBlockFromSelectedCell()
    {
        var viewModel = new MainWindowViewModel(loadDefaultWorld: false);
        viewModel.LoadWorldJsonFrom(DemoWorldPath());
        var cell = viewModel.Cells
            .Where(item => !string.IsNullOrWhiteSpace(item.Cell.BlockId))
            .GroupBy(item => item.Cell.BlockId)
            .First(group => group.Count() > 1)
            .First();
        viewModel.SelectedCell = cell;
        var originalBlockId = cell.Cell.BlockId;
        var originalCount = viewModel.Document!.Blocks.Count;

        viewModel.EditSelectedCellUniqueBlockCommand.Execute(null);

        Assert.AreNotEqual(originalBlockId, cell.Cell.BlockId);
        Assert.HasCount(originalCount + 1, viewModel.Document.Blocks);
        Assert.IsNotNull(viewModel.SelectedBlock);
        Assert.AreEqual(cell.Cell.BlockId, viewModel.SelectedBlock!.Id);
        Assert.IsTrue(viewModel.IsSelectedCellBlockUnique);
        Assert.AreEqual(6, viewModel.SelectedInspectorTabIndex);
    }

    [TestMethod]
    public void EditorCanRemoveUnusedBlocksWithoutRemovingTransitionTriggers()
    {
        var viewModel = new MainWindowViewModel(loadDefaultWorld: false);
        viewModel.LoadWorldJsonFrom(DemoWorldPath());
        var originalCount = viewModel.Document!.Blocks.Count;
        var unusedBlock = new WorldBlockDefinition {
            Name = "unused_test_block"
        };
        var transitionBlock = new WorldBlockDefinition {
            Name = "transition_only_test_block"
        };
        viewModel.Document.Blocks["f8"] = unusedBlock;
        viewModel.Document.Blocks["f9"] = transitionBlock;
        viewModel.Document.LayerTransitions.Add(new WorldLayerTransition {
            FromLayer = "level_1",
            ToLayer = "level_2",
            TriggerBlockId = "f9"
        });

        viewModel.RemoveUnusedBlocksCommand.Execute(null);

        Assert.IsTrue(viewModel.Document.Blocks.Count < originalCount + 2);
        Assert.IsFalse(viewModel.Document.Blocks.ContainsKey("f8"));
        Assert.IsTrue(viewModel.Document.Blocks.ContainsKey("f9"));
    }

    [TestMethod]
    public void InspectorPreviewCommandsControlCellAndBlockCameras()
    {
        var viewModel = new MainWindowViewModel(loadDefaultWorld: false);
        viewModel.LoadWorldJsonFrom(DemoWorldPath());

        var cellStartX = viewModel.SelectedCellPreview3DCamera.Position.X;
        var cellStartZ = viewModel.SelectedCellPreview3DCamera.Position.Z;

        viewModel.InspectorPreviewRotateRightCommand.Execute("Cell");

        Assert.AreNotEqual(cellStartX, viewModel.SelectedCellPreview3DCamera.Position.X, 0.001);
        Assert.AreNotEqual(cellStartZ, viewModel.SelectedCellPreview3DCamera.Position.Z, 0.001);

        var block = viewModel.Blocks.First(item => item.Block.Walls.Count > 0);
        viewModel.SelectedBlock = block;
        Assert.IsGreaterThan(0, viewModel.SelectedBlockPreview3DModel.Children.Count);
        var blockStartX = viewModel.SelectedBlockPreview3DCamera.Position.X;

        viewModel.InspectorPreviewShiftRightCommand.Execute("Block");

        Assert.IsGreaterThan(blockStartX, viewModel.SelectedBlockPreview3DCamera.Position.X);
        var shiftedBlockX = viewModel.SelectedBlockPreview3DCamera.Position.X;

        viewModel.InspectorPreviewFitCommand.Execute("Block");

        Assert.AreNotEqual(shiftedBlockX, viewModel.SelectedBlockPreview3DCamera.Position.X, 0.001);
    }

    [TestMethod]
    public void WorldPreviewDoesNotShowCeilingsByDefault()
    {
        var viewModel = new MainWindowViewModel(loadDefaultWorld: false);

        Assert.IsFalse(viewModel.PreviewShowCeilings);
    }

    [TestMethod]
    public void BlockInspectorEditsRefreshBlockPreview()
    {
        var viewModel = new MainWindowViewModel(loadDefaultWorld: false);
        viewModel.LoadWorldJsonFrom(DemoWorldPath());
        viewModel.SelectedBlock = viewModel.Blocks.First(item => item.Walls.Count > 0);
        var before = viewModel.SelectedBlockPreview3DModel;
        var texture = viewModel.TextureChoices.First(choice =>
            !string.IsNullOrEmpty(choice.Key)
            && choice.Key != viewModel.SelectedBlock.Walls[0].Texture).Key;

        viewModel.SelectedBlock.Walls[0].Texture = texture;
        DrainDispatcher();

        Assert.AreNotSame(before, viewModel.SelectedBlockPreview3DModel);
    }

    [TestMethod]
    public void SwitchingWorldLayersKeepsCurrentLayerCellEdits()
    {
        var viewModel = new MainWindowViewModel(loadDefaultWorld: false);
        viewModel.LoadWorldJsonFrom(DemoWorldPath());
        Assert.IsGreaterThan(1, viewModel.WorldLayers.Count);
        Assert.IsNotNull(viewModel.SelectedWorldLayer);

        var firstLayer = viewModel.SelectedWorldLayer!;
        var secondLayer = viewModel.WorldLayers.First(layer =>
            !string.Equals(layer.Id, firstLayer.Id, StringComparison.OrdinalIgnoreCase));
        var editedBlockId = viewModel.Blocks.First(block =>
            !string.Equals(block.Id, viewModel.Cells[0].Cell.BlockId, StringComparison.OrdinalIgnoreCase)).Id;

        viewModel.Cells[0].Cell.BlockId = editedBlockId;
        viewModel.SelectedWorldLayer = secondLayer;
        DrainDispatcher();
        viewModel.SelectedWorldLayer = firstLayer;
        DrainDispatcher();

        Assert.AreEqual(editedBlockId, viewModel.Cells[0].Cell.BlockId);
    }

    [TestMethod]
    public void CloneActiveWorldLayerCreatesIndependentLayerCopy()
    {
        var viewModel = new MainWindowViewModel(loadDefaultWorld: false);
        viewModel.LoadWorldJsonFrom(DemoWorldPath());
        Assert.IsNotNull(viewModel.SelectedWorldLayer);
        Assert.IsNotNull(viewModel.Document);

        var sourceLayer = viewModel.SelectedWorldLayer!;
        var sourceId = sourceLayer.Id;
        var sourceFirstBlock = viewModel.Cells[0].Cell.BlockId;
        var sourceLayerCount = viewModel.WorldLayers.Count;

        viewModel.CloneSelectedWorldLayerCommand.Execute(null);
        DrainDispatcher();

        Assert.AreEqual(sourceLayerCount + 1, viewModel.WorldLayers.Count);
        Assert.IsNotNull(viewModel.SelectedWorldLayer);
        Assert.AreNotEqual(sourceId, viewModel.SelectedWorldLayer!.Id);
        Assert.AreEqual(sourceFirstBlock, viewModel.Cells[0].Cell.BlockId);

        var replacementBlock = viewModel.Blocks.First(block =>
            !string.Equals(block.Id, sourceFirstBlock, StringComparison.OrdinalIgnoreCase)).Id;
        viewModel.Cells[0].Cell.BlockId = replacementBlock;
        viewModel.SelectedWorldLayer = sourceLayer;
        DrainDispatcher();

        Assert.AreEqual(sourceId, viewModel.SelectedWorldLayer!.Id);
        Assert.AreEqual(sourceFirstBlock, viewModel.Cells[0].Cell.BlockId);
    }

    [TestMethod]
    public void RenameWorldLayerUpdatesLayerTransitions()
    {
        var viewModel = new MainWindowViewModel(loadDefaultWorld: false);
        viewModel.LoadWorldJsonFrom(DemoWorldPath());
        Assert.IsNotNull(viewModel.Document);
        Assert.IsNotNull(viewModel.SelectedWorldLayer);
        Assert.IsTrue(viewModel.WorldLayers.Count > 1);

        var source = viewModel.SelectedWorldLayer!;
        var oldId = source.Id;
        var target = viewModel.WorldLayers.First(layer =>
            !string.Equals(layer.Id, oldId, StringComparison.OrdinalIgnoreCase));
        viewModel.Document!.LayerTransitions.Add(new WorldLayerTransition {
            FromLayer = oldId,
            ToLayer = target.Id,
            Trigger = new WorldLayerTransitionTrigger {
                BlockId = "e1",
                Row = 1,
                Column = 1
            }
        });
        viewModel.Document.LayerTransitions.Add(new WorldLayerTransition {
            FromLayer = target.Id,
            ToLayer = oldId,
            Trigger = new WorldLayerTransitionTrigger {
                BlockId = "e1",
                Row = 2,
                Column = 2
            }
        });

        Assert.IsTrue(viewModel.TryRenameSelectedWorldLayer("level_renamed_for_test", out var message), message);

        Assert.AreEqual("level_renamed_for_test", source.Id);
        Assert.AreEqual("level_renamed_for_test", viewModel.Document.ActiveLayerId);
        Assert.IsFalse(viewModel.Document.LayerTransitions.Any(transition =>
            string.Equals(transition.FromLayer, oldId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(transition.ToLayer, oldId, StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(viewModel.Document.LayerTransitions.Any(transition =>
            string.Equals(transition.FromLayer, "level_renamed_for_test", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(viewModel.Document.LayerTransitions.Any(transition =>
            string.Equals(transition.ToLayer, "level_renamed_for_test", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void DeleteWorldLayerRemovesTransitionsAndSelectsReplacement()
    {
        var viewModel = new MainWindowViewModel(loadDefaultWorld: false);
        viewModel.LoadWorldJsonFrom(DemoWorldPath());
        Assert.IsNotNull(viewModel.Document);
        Assert.IsTrue(viewModel.WorldLayers.Count > 1);

        var layerToDelete = viewModel.WorldLayers.Last();
        var remaining = viewModel.WorldLayers.First(layer =>
            !ReferenceEquals(layer, layerToDelete));
        viewModel.SelectedWorldLayer = layerToDelete;
        DrainDispatcher();
        viewModel.Document!.LayerTransitions.Add(new WorldLayerTransition {
            FromLayer = remaining.Id,
            ToLayer = layerToDelete.Id,
            Trigger = new WorldLayerTransitionTrigger {
                BlockId = "e1",
                Row = 1,
                Column = 1
            }
        });
        viewModel.Document.LayerTransitions.Add(new WorldLayerTransition {
            FromLayer = layerToDelete.Id,
            ToLayer = remaining.Id,
            Trigger = new WorldLayerTransitionTrigger {
                BlockId = "e1",
                Row = 2,
                Column = 2
            }
        });
        var originalCount = viewModel.WorldLayers.Count;

        Assert.IsTrue(viewModel.TryDeleteWorldLayer(layerToDelete.Id, out var message), message);

        Assert.AreEqual(originalCount - 1, viewModel.WorldLayers.Count);
        Assert.IsFalse(viewModel.WorldLayers.Any(layer =>
            string.Equals(layer.Id, layerToDelete.Id, StringComparison.OrdinalIgnoreCase)));
        Assert.IsFalse(viewModel.Document.LayerTransitions.Any(transition =>
            string.Equals(transition.FromLayer, layerToDelete.Id, StringComparison.OrdinalIgnoreCase)
            || string.Equals(transition.ToLayer, layerToDelete.Id, StringComparison.OrdinalIgnoreCase)));
        Assert.IsNotNull(viewModel.SelectedWorldLayer);
        Assert.IsFalse(string.Equals(
            viewModel.SelectedWorldLayer!.Id,
            layerToDelete.Id,
            StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void JsonEditorReplaceAllUpdatesText()
    {
        var viewModel = new MainWindowViewModel(loadDefaultWorld: false);
        var panel = viewModel.JsonPanel;
        panel.JsonText = "{ \"activeLayer\": \"level_1\", \"startLayer\": \"level_1\" }";
        panel.FindText = "level_1";
        panel.ReplaceText = "level_alpha";

        panel.ReplaceAllCommand.Execute(null);

        Assert.AreEqual(
            "{ \"activeLayer\": \"level_alpha\", \"startLayer\": \"level_alpha\" }",
            panel.JsonText);
        Assert.IsTrue(panel.StatusMessage.Contains("Replaced 2", StringComparison.Ordinal));
    }

    private static void DrainDispatcher(int passes = 4)
    {
        var dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        for (var pass = 0; pass < passes; ++pass) {
            var frame = new DispatcherFrame();
            dispatcher.BeginInvoke(
                DispatcherPriority.ApplicationIdle,
                new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
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
}
