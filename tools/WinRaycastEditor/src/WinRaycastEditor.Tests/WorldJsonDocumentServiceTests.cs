using WinRaycastEditor.Core;

namespace WinRaycastEditor.Tests;

[TestClass]
public sealed class WorldJsonDocumentServiceTests
{
    [TestMethod]
    public void SaveAndLoadRoundTripPreservesBlockPaletteWithVariableHeightWalls()
    {
        var directory = Directory.CreateTempSubdirectory("winraycast-world-json-");
        var path = Path.Combine(directory.FullName, "demo.world.json");

        try {
            var world = new WorldDocument {
                Name = "demo",
                Brightness = 0.9,
                DepthShading = 175.0,
                Grid = new WorldGridDefinition {
                    Columns = 2,
                    Rows = 1,
                    CellWidth = 384,
                    CellDepth = 640,
                    DefaultWallHeight = 768
                },
                PlayerStart = new WorldPlayerStart {
                    XCell = 0.5,
                    YCell = 0.5,
                    FacingDegrees = 90.0
                },
                PlayerStats = new WorldCombatStats {
                    MaxHealth = 125.0,
                    Health = 100.0
                },
                DefaultHorizonImage = "textures/sky.png",
                PlayerWeapon = new WorldPlayerWeapon {
                    File = "weapons/super_shotgun/super_shotgun.weapon.json",
                    Visible = true,
                    ScreenHeightFraction = 0.34
                },
                BackgroundMusic = new WorldBackgroundMusic {
                    File = "audio/holst_mars_15s_no_fadein.ogg",
                    Enabled = true,
                    Loop = true,
                    VolumePercent = 65
                }
            };
            world.Textures["brick"] = new WorldTextureDefinition {
                Name = "Brick",
                File = "textures/brick.bmp"
            };
            world.Textures["brick_north"] = new WorldTextureDefinition {
                Name = "Brick north",
                File = "textures/brick_north.bmp"
            };
            world.Textures["door_closed"] = new WorldTextureDefinition {
                Name = "Door closed",
                File = "textures/door_closed.png"
            };
            world.Textures["door_open"] = new WorldTextureDefinition {
                Name = "Door open",
                File = "textures/door_open.png"
            };
            world.Textures["key_green_overlay"] = new WorldTextureDefinition {
                Name = "Green key overlay",
                File = "textures/key_green_overlay.png"
            };
            world.Textures["tvcc_0"] = new WorldTextureDefinition {
                Name = "TVCC overlay 0",
                File = "textures/tvcc_0.png"
            };
            world.Textures["tvcc_1"] = new WorldTextureDefinition {
                Name = "TVCC overlay 1",
                File = "textures/tvcc_1.png"
            };
            world.Blocks["00"] = new WorldBlockDefinition { Name = "empty" };
            world.Blocks["01"] = new WorldBlockDefinition {
                Name = "ledge",
                Floor = new WorldSurface { Texture = "brick", Height = 0 },
                Ceiling = new WorldSurface { Texture = "brick", Height = 768 },
                Walls = {
                    new WorldWallSpan {
                        Kind = "solid",
                        Texture = "brick",
                        FaceTextures = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
                            ["north"] = "brick_north"
                        },
                        Bottom = 128,
                        Top = 640,
                        Collision = true
                    },
                    new WorldWallSpan {
                        Kind = "solid",
                        Texture = "brick",
                        Bottom = 640,
                        Top = 1280,
                        Passable = true
                    }
                },
                Door = new WorldDoorDefinition {
                    Enabled = true,
                    BlocksWhenClosed = true,
                    RequiredKey = "green",
                    TriggerDistanceCells = 1.75,
                    OpenTimeSeconds = 0.35,
                    CloseDelaySeconds = 1.25,
                    OpenSound = "effects/Elevator_Opening_Sequence.mp3",
                    OpenSoundVolumePercent = 100,
                    Frames = [ "door_closed", "door_open" ],
                    LockedOverlays = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
                        ["green"] = "key_green_overlay"
                    }
                },
                Animations = [
                    new WorldBlockAnimationDefinition {
                        Name = "north_panel_flash",
                        Target = "wall",
                        WallIndex = 0,
                        Face = "north",
                        FrameDurationMs = 90.0,
                        Loop = true,
                        Frames = [ "brick", "brick_north" ]
                    },
                    new WorldBlockAnimationDefinition {
                        Name = "tvcc_cycle",
                        Target = "wallOverlay",
                        WallIndex = 0,
                        Face = "north",
                        FrameDurationMs = 2500.0,
                        Loop = true,
                        Frames = [ "tvcc_0", "tvcc_1" ]
                    }
                ]
            };
            world.Cells.Add(["00", "01"]);
            world.SpriteSets.Add("sprites/monster/monster.sprite.json");
            world.SpriteInstances.Add(new EditorSpriteInstance {
                Name = "monster_01",
                SpriteSet = "monster",
                XCell = 1.5,
                YCell = 0.5,
                VerticalOffsetCells = 0.38,
                MaxHealth = 80.0,
                Health = 75.0,
                AttackDamage = 9.0,
                RangedAttack = true,
                AttackRangeCells = 5.5,
                AttackCooldownSeconds = 1.1,
                AttackFovDegrees = 75.0,
                AttackBurstShots = 4,
                AttackBurstPauseSeconds = 1.6,
                Explosive = true,
                ExplosiveHitPoints = 45.0,
                ExplosionRadiusCells = 2.2,
                ExplosionDamage = 60.0,
                ExplosionScaleCells = 1.35,
                ExplosionSpriteSet = "explosion_512",
                DestroyedSpriteSet = "ash_pile",
                DestroyedScaleCells = 0.48,
                DamageResponse = new EditorSpriteDamageResponse {
                    Type = "break",
                    HitPoints = 18.0,
                    EffectSpriteSet = "vase_break_256",
                    EffectAnimation = "break",
                    EffectScaleCells = 0.72,
                    DestroyedSpriteSet = "broken_vase_256",
                    DestroyedScaleCells = 0.58,
                    Sound = "effects/breaking/can_crush_0.wav",
                    RadiusCells = 0.0,
                    Damage = 0.0
                }
            });

            WorldJsonDocumentService.Save(world, path);
            var savedText = File.ReadAllText(path);
            StringAssert.Contains(savedText, "\"passable\": true");
            StringAssert.Contains(savedText, "\"faceTextures\"");
            StringAssert.Contains(savedText, "\"door\"");
            StringAssert.Contains(savedText, "\"requiredKey\": \"green\"");
            StringAssert.Contains(savedText, "\"lockedOverlays\"");
            StringAssert.Contains(savedText, "\"animations\"");
            StringAssert.Contains(savedText, "\"backgroundMusic\"");
            StringAssert.Contains(savedText, "\"damageResponse\"");
            var loaded = WorldJsonDocumentService.Load(path);

            Assert.IsTrue(loaded.Success, string.Join(Environment.NewLine, loaded.Errors));
            Assert.AreEqual("winraycast.world", loaded.Document!.Format);
            Assert.AreEqual(0.9, loaded.Document.Brightness, 1e-9);
            Assert.AreEqual(175.0, loaded.Document.DepthShading, 1e-9);
            Assert.AreEqual(2, loaded.Document.Version);
            Assert.AreEqual(384, loaded.Document.Grid.CellWidth);
            Assert.AreEqual(640, loaded.Document.Grid.CellDepth);
            Assert.AreEqual(768, loaded.Document.Grid.DefaultWallHeight);
            Assert.AreEqual(90.0, loaded.Document.PlayerStart.FacingDegrees, 1e-9);
            Assert.AreEqual(125.0, loaded.Document.PlayerStats.MaxHealth, 1e-9);
            Assert.AreEqual(100.0, loaded.Document.PlayerStats.Health, 1e-9);
            Assert.AreEqual("textures/sky.png", loaded.Document.DefaultHorizonImage);
            Assert.IsNotNull(loaded.Document.PlayerWeapon);
            Assert.AreEqual(
                "weapons/super_shotgun/super_shotgun.weapon.json",
                loaded.Document.PlayerWeapon!.File);
            Assert.AreEqual(0.34, loaded.Document.PlayerWeapon.ScreenHeightFraction, 1e-9);
            Assert.IsNotNull(loaded.Document.BackgroundMusic);
            Assert.AreEqual("audio/holst_mars_15s_no_fadein.ogg", loaded.Document.BackgroundMusic!.File);
            Assert.IsTrue(loaded.Document.BackgroundMusic.Enabled);
            Assert.IsTrue(loaded.Document.BackgroundMusic.Loop);
            Assert.AreEqual(65, loaded.Document.BackgroundMusic.VolumePercent);

            Assert.HasCount(2, loaded.Document.Blocks);
            var ledge = loaded.Document.Blocks["01"];
            Assert.AreEqual("ledge", ledge.Name);
            Assert.HasCount(2, ledge.Walls);
            Assert.AreEqual(128, ledge.Walls[0].Bottom);
            Assert.AreEqual(640, ledge.Walls[0].Top);
            Assert.IsNotNull(ledge.Walls[0].FaceTextures);
            Assert.AreEqual("brick_north", ledge.Walls[0].FaceTextures!["north"]);
            Assert.AreEqual(640, ledge.Walls[1].Bottom);
            Assert.AreEqual(1280, ledge.Walls[1].Top);
            Assert.IsFalse(ledge.Walls[1].Collision);
            Assert.IsTrue(ledge.Walls[1].Passable);
            Assert.IsNotNull(ledge.Door);
            Assert.AreEqual("green", ledge.Door!.RequiredKey);
            Assert.AreEqual(1.75, ledge.Door!.TriggerDistanceCells, 1e-9);
            Assert.AreEqual(0.35, ledge.Door.OpenTimeSeconds, 1e-9);
            Assert.AreEqual(1.25, ledge.Door.CloseDelaySeconds, 1e-9);
            Assert.AreEqual("effects/Elevator_Opening_Sequence.mp3", ledge.Door.OpenSound);
            Assert.AreEqual(100, ledge.Door.OpenSoundVolumePercent);
            CollectionAssert.AreEqual(
                new[] { "door_closed", "door_open" },
                ledge.Door.Frames);
            Assert.IsNotNull(ledge.Door.LockedOverlays);
            Assert.AreEqual("key_green_overlay", ledge.Door.LockedOverlays!["green"]);
            Assert.IsNotNull(ledge.Animations);
            Assert.HasCount(2, ledge.Animations!);
            Assert.AreEqual("north_panel_flash", ledge.Animations[0].Name);
            Assert.AreEqual("wall", ledge.Animations[0].Target);
            Assert.AreEqual(0, ledge.Animations[0].WallIndex);
            Assert.AreEqual("north", ledge.Animations[0].Face);
            Assert.AreEqual(90.0, ledge.Animations[0].FrameDurationMs, 1e-9);
            CollectionAssert.AreEqual(
                new[] { "brick", "brick_north" },
                ledge.Animations[0].Frames);
            Assert.AreEqual("tvcc_cycle", ledge.Animations[1].Name);
            Assert.AreEqual("wallOverlay", ledge.Animations[1].Target);
            Assert.AreEqual(0, ledge.Animations[1].WallIndex);
            Assert.AreEqual("north", ledge.Animations[1].Face);
            Assert.AreEqual(2500.0, ledge.Animations[1].FrameDurationMs, 1e-9);
            CollectionAssert.AreEqual(
                new[] { "tvcc_0", "tvcc_1" },
                ledge.Animations[1].Frames);
            Assert.AreEqual("00", loaded.Document.Cells[0][0]);
            Assert.AreEqual("01", loaded.Document.Cells[0][1]);
            Assert.AreEqual("sprites/monster/monster.sprite.json", loaded.Document.SpriteSets.Single());
            Assert.AreEqual("monster", loaded.Document.SpriteInstances.Single().SpriteSet);
            Assert.AreEqual(0.38, loaded.Document.SpriteInstances.Single().VerticalOffsetCells, 1e-9);
            Assert.AreEqual(80.0, loaded.Document.SpriteInstances.Single().MaxHealth, 1e-9);
            Assert.AreEqual(75.0, loaded.Document.SpriteInstances.Single().Health, 1e-9);
            Assert.AreEqual(9.0, loaded.Document.SpriteInstances.Single().AttackDamage, 1e-9);
            Assert.IsTrue(loaded.Document.SpriteInstances.Single().RangedAttack);
            Assert.AreEqual(5.5, loaded.Document.SpriteInstances.Single().AttackRangeCells, 1e-9);
            Assert.AreEqual(1.1, loaded.Document.SpriteInstances.Single().AttackCooldownSeconds, 1e-9);
            Assert.AreEqual(75.0, loaded.Document.SpriteInstances.Single().AttackFovDegrees, 1e-9);
            Assert.AreEqual(4, loaded.Document.SpriteInstances.Single().AttackBurstShots);
            Assert.AreEqual(1.6, loaded.Document.SpriteInstances.Single().AttackBurstPauseSeconds, 1e-9);
            Assert.IsTrue(loaded.Document.SpriteInstances.Single().Explosive);
            Assert.AreEqual(45.0, loaded.Document.SpriteInstances.Single().ExplosiveHitPoints, 1e-9);
            Assert.AreEqual(2.2, loaded.Document.SpriteInstances.Single().ExplosionRadiusCells, 1e-9);
            Assert.AreEqual(60.0, loaded.Document.SpriteInstances.Single().ExplosionDamage, 1e-9);
            Assert.AreEqual(1.35, loaded.Document.SpriteInstances.Single().ExplosionScaleCells, 1e-9);
            Assert.AreEqual("explosion_512", loaded.Document.SpriteInstances.Single().ExplosionSpriteSet);
            Assert.AreEqual("ash_pile", loaded.Document.SpriteInstances.Single().DestroyedSpriteSet);
            Assert.AreEqual(0.48, loaded.Document.SpriteInstances.Single().DestroyedScaleCells, 1e-9);
            Assert.IsNotNull(loaded.Document.SpriteInstances.Single().DamageResponse);
            Assert.AreEqual("break", loaded.Document.SpriteInstances.Single().DamageResponse!.Type);
            Assert.AreEqual(18.0, loaded.Document.SpriteInstances.Single().DamageResponse!.HitPoints, 1e-9);
            Assert.AreEqual("vase_break_256", loaded.Document.SpriteInstances.Single().DamageResponse!.EffectSpriteSet);
            Assert.AreEqual("break", loaded.Document.SpriteInstances.Single().DamageResponse!.EffectAnimation);
            Assert.AreEqual(0.72, loaded.Document.SpriteInstances.Single().DamageResponse!.EffectScaleCells, 1e-9);
            Assert.AreEqual("broken_vase_256", loaded.Document.SpriteInstances.Single().DamageResponse!.DestroyedSpriteSet);
            Assert.AreEqual(0.58, loaded.Document.SpriteInstances.Single().DamageResponse!.DestroyedScaleCells, 1e-9);
            Assert.AreEqual("effects/breaking/can_crush_0.wav", loaded.Document.SpriteInstances.Single().DamageResponse!.Sound);
            Assert.AreEqual(0.0, loaded.Document.SpriteInstances.Single().DamageResponse!.RadiusCells, 1e-9);
            Assert.AreEqual(0.0, loaded.Document.SpriteInstances.Single().DamageResponse!.Damage, 1e-9);
        }
        finally {
            directory.Delete(recursive: true);
        }
    }

    [TestMethod]
    public void LegacyWorldConversionDerivesBlockPaletteFromUniquePackedShapes()
    {
        var map = new EditorMapDocument {
            CellWidth = 256,
            CellHeight = 512,
            DefaultHorizonImage = "textures/sky.png"
        };
        map.TextureMap[0x01] = "wall";
        map.TextureMap[0x02] = "ceiling";
        map.TextureMap[0x03] = "floor";
        map.TextureMap[0x04] = "transparent";
        map.TextureMap[0x05] = "upper";
        map.Rows.Add([
            new EditorMapCell(0, 0, 0x0000000000UL),
            new EditorMapCell(0, 1, 0x0504030201UL),
            new EditorMapCell(0, 2, 0x0504030201UL)
        ]);

        var world = LegacyWorldConverter.FromEditorMap(map, "legacy");

        Assert.AreEqual("legacy", world.Name);
        Assert.AreEqual(3, world.Grid.Columns);
        Assert.AreEqual(1, world.Grid.Rows);
        Assert.AreEqual(256, world.Grid.CellWidth);
        Assert.AreEqual(512, world.Grid.CellDepth);
        Assert.AreEqual(512, world.Grid.DefaultWallHeight);
        Assert.AreEqual("textures/sky.png", world.DefaultHorizonImage);
        Assert.AreEqual("wall.bmp", world.Textures["01"].File);

        Assert.HasCount(2, world.Blocks);
        Assert.IsTrue(world.Blocks.ContainsKey("00"));
        Assert.AreEqual("00", world.Cells[0][0]);
        var richBlockId = world.Cells[0][1];
        Assert.AreEqual(richBlockId, world.Cells[0][2]);
        Assert.AreNotEqual("00", richBlockId);

        var rich = world.Blocks[richBlockId];
        Assert.AreEqual("03", rich.Floor!.Texture);
        Assert.AreEqual("02", rich.Ceiling!.Texture);
        Assert.HasCount(3, rich.Walls);
        Assert.AreEqual(0, rich.Walls[0].Bottom);
        Assert.AreEqual(512, rich.Walls[0].Top);
        Assert.AreEqual(512, rich.Walls[1].Bottom);
        Assert.AreEqual(1024, rich.Walls[1].Top);
        Assert.AreEqual("transparent", rich.Walls[2].Kind);
        Assert.IsFalse(rich.Walls[2].Collision);
    }

    [TestMethod]
    public void WorldJsonConversionBuildsEditorMapAndPreservesPaletteForRoundTrip()
    {
        var world = new WorldDocument {
            PlayerStart = new WorldPlayerStart {
                XCell = 0.25,
                YCell = 0.75,
                FacingDegrees = 180.0
            },
            PlayerWeapon = new WorldPlayerWeapon {
                File = "weapons/fist/fist.weapon.json",
                Visible = true,
                ScreenHeightFraction = 0.25
            },
            Grid = new WorldGridDefinition {
                Columns = 1,
                Rows = 1,
                CellWidth = 320,
                CellDepth = 640,
                DefaultWallHeight = 640
            }
        };
        world.Textures["01"] = new WorldTextureDefinition {
            Name = "Brick",
            File = "textures/brick.bmp"
        };
        world.Textures["7f"] = new WorldTextureDefinition {
            Name = "Upper",
            File = "textures/upper.bmp"
        };
        world.Blocks["00"] = new WorldBlockDefinition { Name = "empty" };
        world.Blocks["01"] = new WorldBlockDefinition {
            Name = "stack",
            Floor = new WorldSurface { Texture = "01", Height = 0 },
            Walls = {
                new WorldWallSpan {
                    Kind = "solid",
                    Texture = "01",
                    Bottom = 0,
                    Top = 640
                },
                new WorldWallSpan {
                    Kind = "solid",
                    Texture = "7f",
                    Bottom = 640,
                    Top = 960
                }
            }
        };
        world.Cells.Add(["01"]);

        var map = LegacyWorldConverter.ToEditorMap(world, "demo.world.json");

        Assert.AreEqual("demo.world.json", map.SourcePath);
        Assert.AreEqual(320, map.CellWidth);
        Assert.AreEqual(640, map.CellHeight);
        Assert.AreEqual(0.25, map.PlayerStart.XCell, 1e-9);
        Assert.AreEqual(0.75, map.PlayerStart.YCell, 1e-9);
        Assert.AreEqual(180.0, map.PlayerStart.FacingDegrees, 1e-9);
        Assert.IsNotNull(map.PlayerWeapon);
        Assert.AreEqual("weapons/fist/fist.weapon.json", map.PlayerWeapon!.File);
        Assert.AreEqual(0.25, map.PlayerWeapon.ScreenHeightFraction, 1e-9);
        Assert.AreEqual(1, map.RowCount);
        Assert.AreEqual(1, map.ColumnCount);
        Assert.AreEqual("textures/brick.bmp", map.TextureMap[0x01]);
        Assert.AreEqual("textures/upper.bmp", map.TextureMap[0x7f]);
        Assert.AreEqual(0x01, map.Rows[0][0].Fields.SolidWallTexture);
        Assert.AreEqual(0x7f, map.Rows[0][0].Fields.UpperWallTexture);
        Assert.AreEqual(0x01, map.Rows[0][0].Fields.FloorTexture);

        Assert.IsTrue(map.Blocks.ContainsKey("01"));
        Assert.HasCount(2, map.Blocks["01"].Walls);
    }

    [TestMethod]
    public void WorldJsonConversionPreservesImageFileExtensionsForRoundTrip()
    {
        var world = new WorldDocument {
            Grid = new WorldGridDefinition {
                Columns = 1,
                Rows = 1,
                CellWidth = 512,
                CellDepth = 512,
                DefaultWallHeight = 512
            }
        };
        world.Textures["01"] = new WorldTextureDefinition {
            Name = "Brick",
            File = "textures/brick.png"
        };
        world.Blocks["00"] = new WorldBlockDefinition { Name = "empty" };
        world.Blocks["01"] = new WorldBlockDefinition {
            Name = "wall",
            Walls = {
                new WorldWallSpan {
                    Kind = "solid",
                    Texture = "01",
                    Bottom = 0,
                    Top = 512
                }
            }
        };
        world.Cells.Add(["01"]);

        var map = LegacyWorldConverter.ToEditorMap(world, "demo.world.json");
        var roundTripped = LegacyWorldConverter.FromEditorMap(map, "demo");

        Assert.AreEqual("textures/brick.png", map.TextureMap[0x01]);
        Assert.AreEqual("textures/brick.png", roundTripped.Textures["01"].File);
        Assert.AreEqual("01", roundTripped.Cells.Single().Single());
    }

    [TestMethod]
    public void WorldJsonConversionPreservesDeclaredZeroBlockAndUnusedPaletteBlocks()
    {
        var world = new WorldDocument {
            Grid = new WorldGridDefinition {
                Columns = 1,
                Rows = 1,
                CellWidth = 512,
                CellDepth = 512,
                DefaultWallHeight = 512
            }
        };
        world.Textures["01"] = new WorldTextureDefinition {
            Name = "Brick",
            File = "textures/brick.png"
        };
        world.Textures["02"] = new WorldTextureDefinition {
            Name = "Upper",
            File = "textures/upper.png"
        };
        world.Blocks["00"] = new WorldBlockDefinition {
            Name = "zero_is_a_real_wall",
            Walls = {
                new WorldWallSpan {
                    Kind = "solid",
                    Texture = "01",
                    Bottom = 0,
                    Top = 512,
                    Collision = true
                },
                new WorldWallSpan {
                    Kind = "solid",
                    Texture = "02",
                    Bottom = 512,
                    Top = 1024,
                    Collision = true
                }
            }
        };
        world.Blocks["0d"] = new WorldBlockDefinition {
            Name = "unused_palette_wall",
            Walls = {
                new WorldWallSpan {
                    Kind = "solid",
                    Texture = "02",
                    Bottom = 0,
                    Top = 512,
                    Collision = true
                }
            }
        };
        world.Cells.Add(["00"]);

        var map = LegacyWorldConverter.ToEditorMap(world, "demo.world.json");
        var roundTripped = LegacyWorldConverter.FromEditorMap(map, "demo");

        Assert.AreEqual("00", roundTripped.Cells.Single().Single());
        AssertWorldBlocksEqual(world.Blocks["00"], roundTripped.Blocks["00"]);
        AssertWorldBlocksEqual(world.Blocks["0d"], roundTripped.Blocks["0d"]);
    }

    [TestMethod]
    public void ShippedDemoWorldEditorRoundTripPreservesCellsAndTextureFiles()
    {
        var loaded = WorldJsonDocumentService.Load(DemoWorldPath());
        Assert.IsTrue(loaded.Success, string.Join(Environment.NewLine, loaded.Errors));

        var document = loaded.Document!;
        var map = LegacyWorldConverter.ToEditorMap(document, DemoWorldPath());
        var roundTripped = LegacyWorldConverter.FromEditorMap(map, "demo_roundtrip");
        var activeLayer = document.Layers.FirstOrDefault(layer =>
            string.Equals(layer.Id, document.ActiveLayer, StringComparison.OrdinalIgnoreCase)
            || string.Equals(layer.Id, document.StartLayer, StringComparison.OrdinalIgnoreCase));
        var expectedCells = activeLayer is not null && activeLayer.Cells.Count > 0
            ? activeLayer.Cells
            : document.Cells;

        CollectionAssert.AreEqual(
            expectedCells.SelectMany(row => row).ToList(),
            roundTripped.Cells.SelectMany(row => row).ToList());

        foreach (var texture in document.Textures) {
            Assert.AreEqual(texture.Value.File, roundTripped.Textures[texture.Key].File);
        }

        CollectionAssert.AreEquivalent(
            document.Blocks.Keys.Order(StringComparer.OrdinalIgnoreCase).ToList(),
            roundTripped.Blocks.Keys.Order(StringComparer.OrdinalIgnoreCase).ToList());

        foreach (var blockId in document.Blocks.Keys) {
            AssertWorldBlocksEqual(document.Blocks[blockId], roundTripped.Blocks[blockId]);
        }
    }

    [TestMethod]
    public void SaveAndLoadRoundTripPreservesLayerTransitionTriggerCell()
    {
        var directory = Directory.CreateTempSubdirectory("winraycast-layer-transition-");
        var path = Path.Combine(directory.FullName, "layered.world.json");

        try {
            var world = new WorldDocument {
                Name = "layered",
                Grid = new WorldGridDefinition {
                    Columns = 1,
                    Rows = 1,
                    CellWidth = 512,
                    CellDepth = 512,
                    DefaultWallHeight = 512
                },
                ActiveLayer = "level_1",
                StartLayer = "level_1",
                GameGoal = new WorldGameGoal {
                    Layer = "level_2",
                    Row = 0,
                    Column = 0,
                    RequiredKey = "blue"
                }
            };
            world.Blocks["00"] = new WorldBlockDefinition { Name = "empty" };
            world.Blocks["e1"] = new WorldBlockDefinition { Name = "level_elevator" };
            world.Layers.Add(new WorldLayerDefinition {
                Id = "level_1",
                Brightness = 0.75,
                DepthShading = 225.0,
                Cells = { new List<string> { "e1" } }
            });
            world.Layers.Add(new WorldLayerDefinition {
                Id = "level_2",
                Cells = { new List<string> { "00" } }
            });
            world.LayerTransitions.Add(new WorldLayerTransition {
                FromLayer = "level_1",
                ToLayer = "level_2",
                RequiredKey = "blue",
                Trigger = new WorldLayerTransitionTrigger {
                    BlockId = "e1",
                    Row = 0,
                    Column = 0
                },
                TargetPlayerStart = new WorldPlayerStart {
                    XCell = 0.5,
                    YCell = 0.5,
                    FacingDegrees = 180.0
                }
            });

            WorldJsonDocumentService.Save(world, path);
            var loaded = WorldJsonDocumentService.Load(path);

            Assert.IsTrue(loaded.Success, string.Join(Environment.NewLine, loaded.Errors));
            var transition = loaded.Document!.LayerTransitions.Single();
            Assert.AreEqual("e1", transition.EffectiveTriggerBlockId);
            Assert.AreEqual("blue", transition.RequiredKey);
            Assert.IsNotNull(transition.Trigger);
            Assert.AreEqual(0, transition.Trigger.Row);
            Assert.AreEqual(0, transition.Trigger.Column);
            Assert.AreEqual(180.0, transition.TargetPlayerStart!.FacingDegrees);
            Assert.IsNotNull(loaded.Document.GameGoal);
            Assert.AreEqual("level_2", loaded.Document.GameGoal!.Layer);
            Assert.AreEqual("blue", loaded.Document.GameGoal.RequiredKey);
            Assert.AreEqual(0.75, loaded.Document.Layers[0].Brightness!.Value, 1e-9);
            Assert.AreEqual(225.0, loaded.Document.Layers[0].DepthShading!.Value, 1e-9);
        }
        finally {
            directory.Delete(recursive: true);
        }
    }

    [TestMethod]
    public void ValidationRejectsGameGoalOutsideItsLayer()
    {
        var world = new WorldDocument {
            Grid = new WorldGridDefinition {
                Columns = 1,
                Rows = 1,
                CellWidth = 512,
                CellDepth = 512,
                DefaultWallHeight = 512
            },
            ActiveLayer = "level_1",
            StartLayer = "level_1",
            GameGoal = new WorldGameGoal {
                Layer = "level_1",
                Row = 3,
                Column = 0
            }
        };
        world.Blocks["00"] = new WorldBlockDefinition { Name = "empty" };
        world.Layers.Add(new WorldLayerDefinition {
            Id = "level_1",
            Cells = { new List<string> { "00" } }
        });

        var errors = new List<string>();
        WorldJsonDocumentService.Validate(world, errors);

        Assert.IsTrue(errors.Any(error =>
            error.Contains("Game goal cell", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void LegacyWorldConversionPreservesPlayerStart()
    {
        var map = new EditorMapDocument {
            PlayerStart = new WorldPlayerStart {
                XCell = 2.25,
                YCell = 0.75,
                FacingDegrees = 135.0
            }
        };
        map.Rows.Add([
            new EditorMapCell(0, 0, 0),
            new EditorMapCell(0, 1, 0),
            new EditorMapCell(0, 2, 0)
        ]);

        var world = LegacyWorldConverter.FromEditorMap(map, "player_test");

        Assert.AreEqual(2.25, world.PlayerStart.XCell, 1e-9);
        Assert.AreEqual(0.75, world.PlayerStart.YCell, 1e-9);
        Assert.AreEqual(135.0, world.PlayerStart.FacingDegrees, 1e-9);
    }

    [TestMethod]
    public void LoadReportsInvalidWallSpanInBlockDefinition()
    {
        var directory = Directory.CreateTempSubdirectory("winraycast-world-json-");
        var path = Path.Combine(directory.FullName, "broken.world.json");

        try {
            File.WriteAllText(path, """
                {
                  "format": "winraycast.world",
                  "version": 2,
                  "grid": {
                    "columns": 1,
                    "rows": 1,
                    "cellWidth": 512,
                    "cellDepth": 512,
                    "defaultWallHeight": 512
                  },
                  "textures": {
                    "01": { "name": "wall", "file": "wall.bmp" }
                  },
                  "blocks": {
                    "00": { "name": "empty" },
                    "01": {
                      "name": "broken",
                      "walls": [
                        { "kind": "solid", "texture": "01", "bottom": 512, "top": 128 }
                      ]
                    }
                  },
                  "cells": [
                    [ "01" ]
                  ]
                }
                """);

            var loaded = WorldJsonDocumentService.Load(path);

            Assert.IsFalse(loaded.Success);
            Assert.IsTrue(loaded.Errors.Any(error => error.Contains("invalid wall span")));
        }
        finally {
            directory.Delete(recursive: true);
        }
    }

    [TestMethod]
    public void LoadReportsInvalidBlockAnimationFrameReferences()
    {
        var directory = Directory.CreateTempSubdirectory("winraycast-world-json-");
        var path = Path.Combine(directory.FullName, "broken-animation.world.json");

        try {
            File.WriteAllText(path, """
                {
                  "format": "winraycast.world",
                  "version": 2,
                  "grid": { "columns": 1, "rows": 1, "cellWidth": 512, "cellDepth": 512, "defaultWallHeight": 512 },
                  "textures": {
                    "01": { "name": "wall", "file": "wall.png" }
                  },
                  "blocks": {
                    "00": { "name": "empty" },
                    "01": {
                      "name": "animated",
                      "walls": [
                        { "kind": "solid", "texture": "01", "bottom": 0, "top": 512 }
                      ],
                      "animations": [
                        {
                          "name": "broken",
                          "target": "wall",
                          "wallIndex": 4,
                          "face": "diagonal",
                          "frameDurationMs": 0,
                          "frames": [ "missing" ]
                        }
                      ]
                    }
                  },
                  "cells": [ [ "01" ] ]
                }
                """);

            var loaded = WorldJsonDocumentService.Load(path);

            Assert.IsFalse(loaded.Success);
            Assert.IsTrue(loaded.Errors.Any(error => error.Contains("invalid wall index")));
            Assert.IsTrue(loaded.Errors.Any(error => error.Contains("unknown wall face")));
            Assert.IsTrue(loaded.Errors.Any(error => error.Contains("non-positive frame duration")));
            Assert.IsTrue(loaded.Errors.Any(error => error.Contains("unknown frame texture")));
        }
        finally {
            directory.Delete(recursive: true);
        }
    }

    [TestMethod]
    public void LoadReportsUnknownBlockIdReferencedByCell()
    {
        var directory = Directory.CreateTempSubdirectory("winraycast-world-json-");
        var path = Path.Combine(directory.FullName, "missing.world.json");

        try {
            File.WriteAllText(path, """
                {
                  "format": "winraycast.world",
                  "version": 2,
                  "grid": { "columns": 1, "rows": 1, "cellWidth": 512, "cellDepth": 512, "defaultWallHeight": 512 },
                  "textures": {},
                  "blocks": { "00": { "name": "empty" } },
                  "cells": [ [ "ff" ] ]
                }
                """);

            var loaded = WorldJsonDocumentService.Load(path);

            Assert.IsFalse(loaded.Success);
            Assert.IsTrue(loaded.Errors.Any(error => error.Contains("unknown block id")));
        }
        finally {
            directory.Delete(recursive: true);
        }
    }

    [TestMethod]
    public void TryParseAndValidateAcceptsValidWorld()
    {
        var world = new WorldDocument {
            Grid = new WorldGridDefinition { Columns = 1, Rows = 1 }
        };
        world.Blocks["00"] = new WorldBlockDefinition { Name = "empty" };
        world.Cells.Add(["00"]);
        var json = WorldJsonDocumentService.Serialize(world);

        var ok = WorldJsonDocumentService.TryParseAndValidate(json, out var document, out var errors);

        Assert.IsTrue(ok, string.Join(Environment.NewLine, errors));
        Assert.IsNotNull(document);
        Assert.IsEmpty(errors);
    }

    [TestMethod]
    public void TryParseAndValidateReportsSyntaxErrorWithoutDocument()
    {
        var ok = WorldJsonDocumentService.TryParseAndValidate("{ not json", out var document, out var errors);

        Assert.IsFalse(ok);
        Assert.IsNull(document);
        Assert.IsTrue(errors.Any(error => error.Contains("Invalid world JSON")));
    }

    [TestMethod]
    public void TryParseAndValidateReturnsDocumentWithSemanticErrors()
    {
        // Parses cleanly but fails validation: missing empty block, zero grid.
        var json = WorldJsonDocumentService.Serialize(new WorldDocument());

        var ok = WorldJsonDocumentService.TryParseAndValidate(json, out var document, out var errors);

        Assert.IsFalse(ok);
        Assert.IsNotNull(document);
        Assert.IsNotEmpty(errors);
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

    private static void AssertWorldBlocksEqual(
        WorldBlockDefinition expected,
        WorldBlockDefinition actual)
    {
        Assert.AreEqual(expected.Name, actual.Name);
        Assert.AreEqual(expected.HorizonImage, actual.HorizonImage);
        AssertWorldSurfaceEqual(expected.Floor, actual.Floor);
        AssertWorldSurfaceEqual(expected.Ceiling, actual.Ceiling);
        Assert.HasCount(expected.Walls.Count, actual.Walls);
        Assert.HasCount(expected.Animations?.Count ?? 0, actual.Animations ?? []);

        for (var index = 0; index < expected.Walls.Count; ++index) {
            var expectedWall = expected.Walls[index];
            var actualWall = actual.Walls[index];
            Assert.AreEqual(expectedWall.Kind, actualWall.Kind);
            Assert.AreEqual(expectedWall.Texture, actualWall.Texture);
            Assert.AreEqual(expectedWall.Bottom, actualWall.Bottom);
            Assert.AreEqual(expectedWall.Top, actualWall.Top);
            Assert.AreEqual(expectedWall.Collision, actualWall.Collision);
        }

        for (var index = 0; index < (expected.Animations?.Count ?? 0); ++index) {
            var expectedAnimation = expected.Animations![index];
            var actualAnimation = actual.Animations![index];
            Assert.AreEqual(expectedAnimation.Name, actualAnimation.Name);
            Assert.AreEqual(expectedAnimation.Target, actualAnimation.Target);
            Assert.AreEqual(expectedAnimation.WallIndex, actualAnimation.WallIndex);
            Assert.AreEqual(expectedAnimation.Face, actualAnimation.Face);
            Assert.AreEqual(expectedAnimation.FrameDurationMs, actualAnimation.FrameDurationMs);
            Assert.AreEqual(expectedAnimation.Loop, actualAnimation.Loop);
            CollectionAssert.AreEqual(expectedAnimation.Frames, actualAnimation.Frames);
        }
    }

    private static void AssertWorldSurfaceEqual(
        WorldSurface? expected,
        WorldSurface? actual)
    {
        if (expected is null || actual is null) {
            Assert.AreSame(expected, actual);
            return;
        }

        Assert.AreEqual(expected.Texture, actual.Texture);
        Assert.AreEqual(expected.Height, actual.Height);
    }
}
