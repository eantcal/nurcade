using WinRaycastEditor.Core;

namespace WinRaycastEditor.Tests;

[TestClass]
public sealed class EditorProjectDocumentServiceTests
{
    [TestMethod]
    public void SaveAndLoadRoundTripPreservesProjectFields()
    {
        var directory = Directory.CreateTempSubdirectory("winraycast-editor-project-");
        var path = Path.Combine(directory.FullName, "demo.winrayproj.json");

        try {
            var project = new EditorProjectDocument {
                ProjectName = "demo_world",
                WorldFile = "world.world.json",
                TextureRoot = ".",
                PlayerStart = new WorldPlayerStart {
                    XCell = 2.5,
                    YCell = 3.5,
                    FacingDegrees = 45.0
                },
                PlayerWeapon = new WorldPlayerWeapon {
                    File = "weapons/super_shotgun/super_shotgun.weapon.json",
                    Visible = true,
                    ScreenHeightFraction = 0.34
                }
            };
            project.SpriteSets.Add("sprites/monster.sprite.json");
            project.SpriteInstances.Add(new EditorSpriteInstance {
                Name = "guard_01",
                SpriteSet = "doom_style_monster",
                XCell = 5.5,
                YCell = 4.5,
                FacingDegrees = 90.0,
                ScaleCells = 1.25,
                VerticalOffsetCells = 0.38,
                CollisionRadiusCells = 0.3,
                Visible = true,
                PassThroughWalls = false,
                ChasePlayer = true,
                SpeedCellsPerSecond = 0.75,
                DetectionRadiusCells = 8.0,
                PatrolRadiusCells = 5.0,
                EngagementHysteresisCells = 0.75,
                PatrolCircuit = true,
                StoppingDistanceCells = 0.65,
                AttackDamage = 12.0,
                RangedAttack = true,
                AttackRangeCells = 6.0,
                AttackCooldownSeconds = 1.25,
                AttackFovDegrees = 80.0,
                AttackBurstShots = 4,
                AttackBurstPauseSeconds = 1.75,
                SavePoint = true,
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

            EditorProjectDocumentService.Save(project, path);
            var loaded = EditorProjectDocumentService.Load(path);

            Assert.IsTrue(loaded.Success, string.Join(", ", loaded.Errors));
            Assert.IsNotNull(loaded.Document);
            Assert.AreEqual("demo_world", loaded.Document!.ProjectName);
            Assert.AreEqual("world.world.json", loaded.Document.WorldFile);
            Assert.AreEqual(".", loaded.Document.TextureRoot);
            Assert.IsNotNull(loaded.Document.PlayerStart);
            Assert.AreEqual(2.5, loaded.Document.PlayerStart.XCell, 1e-9);
            Assert.AreEqual(3.5, loaded.Document.PlayerStart.YCell, 1e-9);
            Assert.AreEqual(45.0, loaded.Document.PlayerStart.FacingDegrees, 1e-9);
            Assert.IsNotNull(loaded.Document.PlayerWeapon);
            Assert.AreEqual(
                "weapons/super_shotgun/super_shotgun.weapon.json",
                loaded.Document.PlayerWeapon!.File);
            Assert.IsTrue(loaded.Document.PlayerWeapon.Visible);
            Assert.AreEqual(0.34, loaded.Document.PlayerWeapon.ScreenHeightFraction, 1e-9);
            Assert.HasCount(1, loaded.Document.SpriteSets);
            Assert.AreEqual("sprites/monster.sprite.json", loaded.Document.SpriteSets[0]);
            Assert.HasCount(1, loaded.Document.SpriteInstances);

            var sprite = loaded.Document.SpriteInstances[0];
            Assert.AreEqual("guard_01", sprite.Name);
            Assert.AreEqual("doom_style_monster", sprite.SpriteSet);
            Assert.AreEqual(5.5, sprite.XCell, 1e-9);
            Assert.AreEqual(4.5, sprite.YCell, 1e-9);
            Assert.AreEqual(90.0, sprite.FacingDegrees, 1e-9);
            Assert.AreEqual(1.25, sprite.ScaleCells, 1e-9);
            Assert.AreEqual(0.38, sprite.VerticalOffsetCells, 1e-9);
            Assert.AreEqual(0.3, sprite.CollisionRadiusCells, 1e-9);
            Assert.IsTrue(sprite.Visible);
            Assert.IsFalse(sprite.PassThroughWalls);
            Assert.IsTrue(sprite.ChasePlayer);
            Assert.AreEqual(0.75, sprite.SpeedCellsPerSecond, 1e-9);
            Assert.AreEqual(8.0, sprite.DetectionRadiusCells, 1e-9);
            Assert.AreEqual(5.0, sprite.PatrolRadiusCells, 1e-9);
            Assert.AreEqual(0.75, sprite.EngagementHysteresisCells, 1e-9);
            Assert.IsTrue(sprite.PatrolCircuit);
            Assert.AreEqual(0.65, sprite.StoppingDistanceCells, 1e-9);
            Assert.AreEqual(12.0, sprite.AttackDamage, 1e-9);
            Assert.IsTrue(sprite.RangedAttack);
            Assert.AreEqual(6.0, sprite.AttackRangeCells, 1e-9);
            Assert.AreEqual(1.25, sprite.AttackCooldownSeconds, 1e-9);
            Assert.AreEqual(80.0, sprite.AttackFovDegrees, 1e-9);
            Assert.AreEqual(4, sprite.AttackBurstShots);
            Assert.AreEqual(1.75, sprite.AttackBurstPauseSeconds, 1e-9);
            Assert.IsTrue(sprite.SavePoint);
            Assert.IsNotNull(sprite.DamageResponse);
            Assert.AreEqual("break", sprite.DamageResponse!.Type);
            Assert.AreEqual(18.0, sprite.DamageResponse.HitPoints, 1e-9);
            Assert.AreEqual("vase_break_256", sprite.DamageResponse.EffectSpriteSet);
            Assert.AreEqual("break", sprite.DamageResponse.EffectAnimation);
            Assert.AreEqual(0.72, sprite.DamageResponse.EffectScaleCells, 1e-9);
            Assert.AreEqual("broken_vase_256", sprite.DamageResponse.DestroyedSpriteSet);
            Assert.AreEqual(0.58, sprite.DamageResponse.DestroyedScaleCells, 1e-9);
            Assert.AreEqual("effects/breaking/can_crush_0.wav", sprite.DamageResponse.Sound);
            Assert.AreEqual(0.0, sprite.DamageResponse.RadiusCells, 1e-9);
            Assert.AreEqual(0.0, sprite.DamageResponse.Damage, 1e-9);
        }
        finally {
            directory.Delete(recursive: true);
        }
    }

    [TestMethod]
    public void LoadReportsErrorForMissingFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"nonexistent-{Guid.NewGuid():N}.json");

        var loaded = EditorProjectDocumentService.Load(path);

        Assert.IsFalse(loaded.Success);
        Assert.IsTrue(loaded.Errors.Any(error => error.Contains("Cannot open")));
    }

    [TestMethod]
    public void LoadReportsErrorForInvalidJson()
    {
        var directory = Directory.CreateTempSubdirectory("winraycast-editor-project-");
        var path = Path.Combine(directory.FullName, "broken.json");
        try {
            File.WriteAllText(path, "{ not valid json");

            var loaded = EditorProjectDocumentService.Load(path);

            Assert.IsFalse(loaded.Success);
            Assert.IsTrue(loaded.Errors.Any(error => error.Contains("Invalid JSON")));
        }
        finally {
            directory.Delete(recursive: true);
        }
    }

    [TestMethod]
    public void LoadReportsErrorForLegacyWorldFile()
    {
        var directory = Directory.CreateTempSubdirectory("winraycast-editor-project-");
        var path = Path.Combine(directory.FullName, "legacy.winrayproj.json");
        try {
            File.WriteAllText(path, """
                {
                    "project": "legacy",
                    "worldFile": "world.ini"
                }
                """);

            var loaded = EditorProjectDocumentService.Load(path);

            Assert.IsFalse(loaded.Success);
            Assert.IsTrue(loaded.Errors.Any(error => error.Contains("legacy INI maps")));
        }
        finally {
            directory.Delete(recursive: true);
        }
    }

    [TestMethod]
    public void LoadAppliesDefaultsForMissingSpriteFields()
    {
        var directory = Directory.CreateTempSubdirectory("winraycast-editor-project-");
        var path = Path.Combine(directory.FullName, "minimal.json");
        try {
            File.WriteAllText(path, """
                {
                    "project": "minimal",
                    "spriteInstances": [
                        { "name": "guard", "spriteSet": "doom" }
                    ]
                }
                """);

            var loaded = EditorProjectDocumentService.Load(path);

            Assert.IsTrue(loaded.Success);
            Assert.AreEqual(".", loaded.Document!.TextureRoot);
            var sprite = loaded.Document.SpriteInstances.Single();
            Assert.AreEqual(1.0, sprite.ScaleCells, 1e-9);
            Assert.AreEqual(0.2, sprite.CollisionRadiusCells, 1e-9);
            Assert.IsTrue(sprite.Visible);
            Assert.IsFalse(sprite.PassThroughWalls);
            Assert.IsFalse(sprite.ChasePlayer);
            Assert.AreEqual(0.0, sprite.SpeedCellsPerSecond, 1e-9);
            Assert.AreEqual(0.0, sprite.DetectionRadiusCells, 1e-9);
            Assert.AreEqual(0.0, sprite.PatrolRadiusCells, 1e-9);
            Assert.AreEqual(0.5, sprite.EngagementHysteresisCells, 1e-9);
            Assert.IsFalse(sprite.PatrolCircuit);
            Assert.AreEqual(0.0, sprite.StoppingDistanceCells, 1e-9);
            Assert.AreEqual(0.0, sprite.AttackDamage, 1e-9);
            Assert.IsFalse(sprite.RangedAttack);
            Assert.AreEqual(0.0, sprite.AttackRangeCells, 1e-9);
            Assert.AreEqual(1.0, sprite.AttackCooldownSeconds, 1e-9);
            Assert.AreEqual(70.0, sprite.AttackFovDegrees, 1e-9);
            Assert.AreEqual(3, sprite.AttackBurstShots);
            Assert.AreEqual(1.2, sprite.AttackBurstPauseSeconds, 1e-9);
        }
        finally {
            directory.Delete(recursive: true);
        }
    }

    [TestMethod]
    public void FromMapDocumentCopiesSpriteSetsAndInstances()
    {
        var map = new EditorMapDocument();
        map.PlayerWeapon = new WorldPlayerWeapon {
            File = "weapons/fist/fist.weapon.json",
            Visible = true,
            ScreenHeightFraction = 0.2
        };
        map.SpriteSetFiles.Add("sprites/monster.sprite.json");
        var sprite = new EditorSpriteInstance { Name = "g1", SpriteSet = "monster" };
        map.SpriteInstances.Add(sprite);

        var project = EditorProjectDocumentService.FromMapDocument(map, "world.world.json");

        Assert.AreEqual("world.world.json", project.WorldFile);
        Assert.IsNotNull(project.PlayerWeapon);
        Assert.AreEqual("weapons/fist/fist.weapon.json", project.PlayerWeapon!.File);
        Assert.AreEqual(0.2, project.PlayerWeapon.ScreenHeightFraction, 1e-9);
        CollectionAssert.AreEqual(new[] { "sprites/monster.sprite.json" }, project.SpriteSets);
        Assert.AreSame(sprite, project.SpriteInstances.Single());
    }

    [TestMethod]
    public void SceneExporterWritesWorldAndProjectFiles()
    {
        var sourceDirectory = Directory.CreateTempSubdirectory("winraycast-editor-source-");
        var exportDirectory = Directory.CreateTempSubdirectory("winraycast-editor-export-");
        var sourceWorldPath = Path.Combine(sourceDirectory.FullName, "world.world.json");
        var projectPath = Path.Combine(exportDirectory.FullName, "demo.winrayproj.json");

        try {
            var map = new EditorMapDocument { SourcePath = sourceWorldPath };
            map.Rows.Add([new EditorMapCell(0, 0, 0x01)]);
            map.TextureMap[0x01] = "wall";
            File.WriteAllBytes(Path.Combine(sourceDirectory.FullName, "wall.bmp"), [0x42]);
            File.WriteAllBytes(Path.Combine(sourceDirectory.FullName, "texture_sky_clouds.png"), [0x42]);
            var weaponDirectory = Directory.CreateDirectory(
                Path.Combine(sourceDirectory.FullName, "weapons", "test_weapon"));
            File.WriteAllText(
                Path.Combine(weaponDirectory.FullName, "test.weapon.json"),
                """{ "weapon": "test", "screenHeightFraction": 0.34 }""");
            File.WriteAllBytes(Path.Combine(weaponDirectory.FullName, "idle.png"), [0x42]);
            map.PlayerWeapon = new WorldPlayerWeapon {
                File = "weapons/test_weapon/test.weapon.json",
                Visible = true,
                ScreenHeightFraction = 0.34
            };
            WriteTestSpriteSet(sourceDirectory.FullName, "sprite_test.sprite.json");
            map.SpriteSetFiles.Add("sprite_test.sprite.json");
            map.SpriteInstances.Add(new EditorSpriteInstance {
                Name = "sprite_test_1",
                SpriteSet = "sprite_test",
                XCell = 0.5,
                YCell = 0.5
            });

            var result = EditorSceneExporter.Export(map, projectPath);
            var loaded = EditorProjectDocumentService.Load(projectPath);

            Assert.IsTrue(File.Exists(result.WorldPath));
            Assert.IsTrue(File.Exists(result.ProjectPath));
            Assert.IsTrue(loaded.Success, string.Join(Environment.NewLine, loaded.Errors));
            Assert.AreEqual("world.world.json", loaded.Document!.WorldFile);
            var loadedWorld = WorldJsonDocumentService.Load(result.WorldPath);
            Assert.IsTrue(loadedWorld.Success, string.Join(Environment.NewLine, loadedWorld.Errors));
            Assert.AreEqual("textures/01_wall.bmp", loadedWorld.Document!.Textures["01"].File);
            Assert.AreEqual("textures/sky_texture_sky_clouds.png", loadedWorld.Document.DefaultHorizonImage);
            Assert.HasCount(1, loaded.Document.SpriteSets);
            Assert.AreEqual("sprites/sprite_test.sprite.json", loaded.Document.SpriteSets[0]);
            Assert.IsTrue(File.Exists(Path.Combine(exportDirectory.FullName, "textures", "sky_texture_sky_clouds.png")));
            Assert.IsTrue(File.Exists(Path.Combine(exportDirectory.FullName, "textures", "01_wall.bmp")));
            Assert.IsNotNull(loaded.Document.PlayerWeapon);
            Assert.AreEqual(
                "weapons/test_weapon/test.weapon.json",
                loaded.Document.PlayerWeapon!.File);
            Assert.IsTrue(File.Exists(Path.Combine(
                exportDirectory.FullName,
                "weapons",
                "test_weapon",
                "idle.png")));
            Assert.IsTrue(File.Exists(Path.Combine(exportDirectory.FullName, "sprites", "sprite_test.sprite.json")));
            Assert.HasCount(1, loaded.Document.SpriteInstances);
        }
        finally {
            sourceDirectory.Delete(recursive: true);
            exportDirectory.Delete(recursive: true);
        }
    }

    [TestMethod]
    public void SceneExporterPreservesPngTextureExtensions()
    {
        var sourceDirectory = Directory.CreateTempSubdirectory("winraycast-editor-source-");
        var exportDirectory = Directory.CreateTempSubdirectory("winraycast-editor-export-");
        var sourceWorldPath = Path.Combine(sourceDirectory.FullName, "world.world.json");
        var projectPath = Path.Combine(exportDirectory.FullName, "demo.winrayproj.json");

        try {
            var map = new EditorMapDocument { SourcePath = sourceWorldPath };
            map.Rows.Add([new EditorMapCell(0, 0, 0x01)]);
            map.TextureMap[0x01] = "wall.png";
            File.WriteAllBytes(Path.Combine(sourceDirectory.FullName, "wall.png"), [0x42]);
            File.WriteAllBytes(Path.Combine(sourceDirectory.FullName, "texture_sky_clouds.png"), [0x42]);

            var result = EditorSceneExporter.Export(map, projectPath);

            var loadedWorld = WorldJsonDocumentService.Load(result.WorldPath);
            Assert.IsTrue(loadedWorld.Success, string.Join(Environment.NewLine, loadedWorld.Errors));
            Assert.AreEqual("textures/01_wall.png", loadedWorld.Document!.Textures["01"].File);
            Assert.AreEqual("textures/sky_texture_sky_clouds.png", loadedWorld.Document.DefaultHorizonImage);
            Assert.IsTrue(File.Exists(Path.Combine(exportDirectory.FullName, "textures", "01_wall.png")));
            Assert.IsTrue(File.Exists(Path.Combine(exportDirectory.FullName, "textures", "sky_texture_sky_clouds.png")));
        }
        finally {
            sourceDirectory.Delete(recursive: true);
            exportDirectory.Delete(recursive: true);
        }
    }

    [TestMethod]
    public void SceneExporterCopiesLayerAndBlockHorizonImages()
    {
        var sourceDirectory = Directory.CreateTempSubdirectory("winraycast-editor-source-");
        var exportDirectory = Directory.CreateTempSubdirectory("winraycast-editor-export-");
        var sourceWorldPath = Path.Combine(sourceDirectory.FullName, "world.world.json");
        var projectPath = Path.Combine(exportDirectory.FullName, "demo.winrayproj.json");

        try {
            var map = new EditorMapDocument { SourcePath = sourceWorldPath };
            map.Rows.Add([new EditorMapCell(0, 0, 0x01)]);
            map.TextureMap[0x01] = "wall.bmp";
            map.ActiveLayerId = "base";
            map.Blocks["01"] = new WorldBlockDefinition {
                Name = "horizon_block",
                HorizonImage = "block_horizon.png"
            };
            map.Layers.Add(new WorldLayerDefinition {
                Id = "base",
                Name = "Base",
                Cells = [["01"]]
            });
            map.Layers.Add(new WorldLayerDefinition {
                Id = "upper",
                Name = "Upper",
                DefaultHorizonImage = "layer_horizon.png",
                Cells = [["01"]]
            });
            File.WriteAllBytes(Path.Combine(sourceDirectory.FullName, "wall.bmp"), [0x42]);
            File.WriteAllBytes(Path.Combine(sourceDirectory.FullName, "texture_sky_clouds.png"), [0x42]);
            File.WriteAllBytes(Path.Combine(sourceDirectory.FullName, "layer_horizon.png"), [0x43]);
            File.WriteAllBytes(Path.Combine(sourceDirectory.FullName, "block_horizon.png"), [0x44]);

            var result = EditorSceneExporter.Export(map, projectPath);
            var loadedWorld = WorldJsonDocumentService.Load(result.WorldPath);

            Assert.IsTrue(loadedWorld.Success, string.Join(Environment.NewLine, loadedWorld.Errors));
            Assert.AreEqual(
                "textures/sky_layer_horizon.png",
                loadedWorld.Document!.Layers.Single(layer => layer.Id == "upper").DefaultHorizonImage);
            Assert.AreEqual(
                "textures/sky_block_horizon.png",
                loadedWorld.Document.Blocks["01"].HorizonImage);
            Assert.IsTrue(File.Exists(Path.Combine(
                exportDirectory.FullName,
                "textures",
                "sky_layer_horizon.png")));
            Assert.IsTrue(File.Exists(Path.Combine(
                exportDirectory.FullName,
                "textures",
                "sky_block_horizon.png")));
        }
        finally {
            sourceDirectory.Delete(recursive: true);
            exportDirectory.Delete(recursive: true);
        }
    }

    [TestMethod]
    public void SceneExporterUsesFallbacksForUnresolvedOptionalHorizonImages()
    {
        var sourceDirectory = Directory.CreateTempSubdirectory("winraycast-editor-source-");
        var exportDirectory = Directory.CreateTempSubdirectory("winraycast-editor-export-");
        var sourceWorldPath = Path.Combine(sourceDirectory.FullName, "world.world.json");
        var projectPath = Path.Combine(exportDirectory.FullName, "demo.winrayproj.json");

        try {
            var map = new EditorMapDocument { SourcePath = sourceWorldPath };
            map.Rows.Add([new EditorMapCell(0, 0, 0x01)]);
            map.TextureMap[0x01] = "wall.bmp";
            map.Blocks["01"] = new WorldBlockDefinition {
                Name = "horizon_block",
                HorizonImage = "missing_block_horizon.png"
            };
            map.Layers.Add(new WorldLayerDefinition {
                Id = "base",
                Name = "Base",
                DefaultHorizonImage = "missing_layer_horizon.png",
                Cells = [["01"]]
            });
            File.WriteAllBytes(Path.Combine(sourceDirectory.FullName, "wall.bmp"), [0x42]);
            File.WriteAllBytes(Path.Combine(sourceDirectory.FullName, "texture_sky_clouds.png"), [0x42]);

            var result = EditorSceneExporter.Export(map, projectPath);
            var loadedWorld = WorldJsonDocumentService.Load(result.WorldPath);

            Assert.IsTrue(loadedWorld.Success, string.Join(Environment.NewLine, loadedWorld.Errors));
            Assert.AreEqual(
                loadedWorld.Document!.DefaultHorizonImage,
                loadedWorld.Document.Layers.Single().DefaultHorizonImage);
            Assert.IsNull(loadedWorld.Document.Blocks["01"].HorizonImage);
        }
        finally {
            sourceDirectory.Delete(recursive: true);
            exportDirectory.Delete(recursive: true);
        }
    }

    [TestMethod]
    public void SceneExporterSkipsUnresolvedOptionalAudioAssets()
    {
        var sourceDirectory = Directory.CreateTempSubdirectory("winraycast-editor-source-");
        var exportDirectory = Directory.CreateTempSubdirectory("winraycast-editor-export-");
        var sourceWorldPath = Path.Combine(sourceDirectory.FullName, "world.world.json");
        var projectPath = Path.Combine(exportDirectory.FullName, "demo.winrayproj.json");

        try {
            var map = new EditorMapDocument { SourcePath = sourceWorldPath };
            map.Rows.Add([new EditorMapCell(0, 0, 0x01)]);
            map.TextureMap[0x01] = "wall.bmp";
            map.Blocks["01"] = new WorldBlockDefinition {
                Name = "silent_door",
                Door = new WorldDoorDefinition {
                    Enabled = true,
                    OpenSound = "missing_door.wav"
                }
            };
            map.SpriteInstances.Add(new EditorSpriteInstance {
                Name = "silent_breakable",
                SpriteSet = "crate",
                XCell = 0.5,
                YCell = 0.5,
                DamageResponse = new EditorSpriteDamageResponse {
                    Type = "break",
                    Sound = "missing_break.wav"
                }
            });
            File.WriteAllBytes(Path.Combine(sourceDirectory.FullName, "wall.bmp"), [0x42]);
            File.WriteAllBytes(Path.Combine(sourceDirectory.FullName, "texture_sky_clouds.png"), [0x42]);

            var result = EditorSceneExporter.Export(map, projectPath);
            var loadedWorld = WorldJsonDocumentService.Load(result.WorldPath);

            Assert.IsTrue(loadedWorld.Success, string.Join(Environment.NewLine, loadedWorld.Errors));
            Assert.IsNull(loadedWorld.Document!.Blocks["01"].Door!.OpenSound);
            Assert.IsNull(loadedWorld.Document.SpriteInstances.Single().DamageResponse!.Sound);
        }
        finally {
            sourceDirectory.Delete(recursive: true);
            exportDirectory.Delete(recursive: true);
        }
    }

    [TestMethod]
    public void SceneExporterCopiesWholeWorldDirectoryContent()
    {
        var sourceDirectory = Directory.CreateTempSubdirectory("winraycast-editor-source-");
        var exportDirectory = Directory.CreateTempSubdirectory("winraycast-editor-export-");
        var sourceWorldPath = Path.Combine(sourceDirectory.FullName, "world.world.json");
        var projectPath = Path.Combine(exportDirectory.FullName, "demo.winrayproj.json");

        try {
            var map = new EditorMapDocument { SourcePath = sourceWorldPath };
            map.Rows.Add([new EditorMapCell(0, 0, 0x01)]);
            map.TextureMap[0x01] = "wall.bmp";
            File.WriteAllBytes(Path.Combine(sourceDirectory.FullName, "wall.bmp"), [0x42]);
            File.WriteAllBytes(Path.Combine(sourceDirectory.FullName, "texture_sky_clouds.png"), [0x42]);

            var hudDirectory = Directory.CreateDirectory(
                Path.Combine(sourceDirectory.FullName, "hud", "player_status"));
            File.WriteAllBytes(Path.Combine(hudDirectory.FullName, "status_100.png"), [0x43]);
            var effectsDirectory = Directory.CreateDirectory(
                Path.Combine(sourceDirectory.FullName, "effects", "pickups"));
            File.WriteAllBytes(Path.Combine(effectsDirectory.FullName, "bling1.mp3"), [0x44]);
            var extraSpriteDirectory = Directory.CreateDirectory(
                Path.Combine(sourceDirectory.FullName, "sprites", "unused_bundle"));
            File.WriteAllText(Path.Combine(extraSpriteDirectory.FullName, "metadata.json"), "{}");
            var arbitraryDirectory = Directory.CreateDirectory(
                Path.Combine(sourceDirectory.FullName, "custom_payload", "nested"));
            File.WriteAllText(Path.Combine(arbitraryDirectory.FullName, "note.txt"), "keep me");
            File.WriteAllText(Path.Combine(sourceDirectory.FullName, "loose_asset.dat"), "root asset");

            EditorSceneExporter.Export(map, projectPath);

            Assert.IsTrue(File.Exists(Path.Combine(
                exportDirectory.FullName,
                "hud",
                "player_status",
                "status_100.png")));
            Assert.IsTrue(File.Exists(Path.Combine(
                exportDirectory.FullName,
                "effects",
                "pickups",
                "bling1.mp3")));
            Assert.IsTrue(File.Exists(Path.Combine(
                exportDirectory.FullName,
                "sprites",
                "unused_bundle",
                "metadata.json")));
            Assert.IsTrue(File.Exists(Path.Combine(
                exportDirectory.FullName,
                "custom_payload",
                "nested",
                "note.txt")));
            Assert.IsTrue(File.Exists(Path.Combine(
                exportDirectory.FullName,
                "loose_asset.dat")));
        }
        finally {
            sourceDirectory.Delete(recursive: true);
            exportDirectory.Delete(recursive: true);
        }
    }

    [TestMethod]
    public void SceneExporterPreservesSpriteAnimationFrameDirectories()
    {
        var sourceDirectory = Directory.CreateTempSubdirectory("winraycast-editor-source-");
        var exportDirectory = Directory.CreateTempSubdirectory("winraycast-editor-export-");
        var sourceWorldPath = Path.Combine(sourceDirectory.FullName, "world.world.json");
        var projectPath = Path.Combine(exportDirectory.FullName, "demo.winrayproj.json");

        try {
            var map = new EditorMapDocument { SourcePath = sourceWorldPath };
            map.Rows.Add([new EditorMapCell(0, 0, 0x01)]);
            map.TextureMap[0x01] = "wall.bmp";
            File.WriteAllBytes(Path.Combine(sourceDirectory.FullName, "wall.bmp"), [0x42]);
            File.WriteAllBytes(Path.Combine(sourceDirectory.FullName, "texture_sky_clouds.png"), [0x42]);

            WriteAnimatedSpriteSet(sourceDirectory.FullName, "animated.sprite.json");
            map.SpriteSetFiles.Add("animated.sprite.json");

            var result = EditorSceneExporter.Export(map, projectPath);
            var exportedSpritePath = Path.Combine(
                Path.GetDirectoryName(result.ProjectPath)!,
                "sprites",
                "animated.sprite.json");
            var loadResult = SpriteMetadataLoader.Load(exportedSpritePath);

            Assert.IsTrue(loadResult.Success, string.Join(Environment.NewLine, loadResult.Errors));
            var walk = loadResult.Document!.Animations.Single(animation => animation.Name == "walk");
            Assert.HasCount(2, walk.Frames);

            var firstFront = walk.Frames[0].Directions.Single(direction => direction.Name == "front").Files[64];
            var secondFront = walk.Frames[1].Directions.Single(direction => direction.Name == "front").Files[64];
            Assert.AreNotEqual(firstFront, secondFront);
            Assert.IsTrue(File.Exists(Path.Combine(exportDirectory.FullName, "sprites", firstFront)));
            Assert.IsTrue(File.Exists(Path.Combine(exportDirectory.FullName, "sprites", secondFront)));
        }
        finally {
            sourceDirectory.Delete(recursive: true);
            exportDirectory.Delete(recursive: true);
        }
    }

    [TestMethod]
    public void SceneExporterCanWritePlayableDemoPackage()
    {
        var sourceDirectory = Directory.CreateTempSubdirectory("winraycast-editor-source-");
        var exportDirectory = Directory.CreateTempSubdirectory("winraycast-editor-export-");
        var runtimeDirectory = Directory.CreateTempSubdirectory("winraycast-editor-runtime-");
        var sourceWorldPath = Path.Combine(sourceDirectory.FullName, "world.world.json");
        var projectPath = Path.Combine(exportDirectory.FullName, "demo.winrayproj.json");
        var enginePath = Path.Combine(runtimeDirectory.FullName, "WinRayCast.exe");

        try {
            var map = new EditorMapDocument { SourcePath = sourceWorldPath };
            map.Rows.Add([new EditorMapCell(0, 0, 0x01)]);
            map.TextureMap[0x01] = "wall";
            File.WriteAllBytes(Path.Combine(sourceDirectory.FullName, "wall.bmp"), [0x42]);
            File.WriteAllBytes(Path.Combine(sourceDirectory.FullName, "texture_sky_clouds.png"), [0x42]);
            File.WriteAllBytes(Path.Combine(sourceDirectory.FullName, "break.wav"), [0x52]);
            File.WriteAllBytes(enginePath, [0x4d, 0x5a]);
            WriteTestSpriteSet(sourceDirectory.FullName, "sprite_test.sprite.json");
            map.SpriteSetFiles.Add("sprite_test.sprite.json");
            map.SpriteInstances.Add(new EditorSpriteInstance {
                Name = "breakable_1",
                SpriteSet = "sprite_test",
                XCell = 0.5,
                YCell = 0.5,
                DamageResponse = new EditorSpriteDamageResponse {
                    Type = "break",
                    Sound = "break.wav"
                }
            });

            var result = EditorSceneExporter.Export(
                map,
                projectPath,
                new EditorSceneExportOptions {
                    EngineExecutablePath = enginePath
                });
            var loaded = EditorProjectDocumentService.Load(projectPath);

            Assert.IsTrue(File.Exists(result.EnginePath));
            Assert.IsTrue(File.Exists(result.RunScriptPath));
            Assert.IsTrue(File.Exists(Path.Combine(exportDirectory.FullName, "WinRayCastPlayer.exe")));
            Assert.IsTrue(File.Exists(Path.Combine(exportDirectory.FullName, "run_demo.bat")));
            StringAssert.Contains(File.ReadAllText(result.RunScriptPath!), "WinRayCastPlayer.exe \"%~dp0demo.winrayproj.json\"");
            Assert.IsTrue(File.Exists(Path.Combine(exportDirectory.FullName, "audio", "effects", "break.wav")));
            Assert.IsTrue(loaded.Success, string.Join(Environment.NewLine, loaded.Errors));
            Assert.AreEqual(
                "audio/effects/break.wav",
                loaded.Document!.SpriteInstances.Single().DamageResponse!.Sound);
        }
        finally {
            sourceDirectory.Delete(recursive: true);
            exportDirectory.Delete(recursive: true);
            runtimeDirectory.Delete(recursive: true);
        }
    }

    private static void WriteTestSpriteSet(string directory, string fileName)
    {
        var document = new SpriteMetadataDocument {
            SpriteSet = "sprite_test",
            Format = "BMP",
            TransparentColor = [0, 0, 0],
            DefaultResolution = 64,
            MaxResolution = 64
        };

        document.SupportedResolutions.Add(64);
        foreach (var item in new (string Name, int Angle)[] {
            ("front", 0),
            ("front_right", 45),
            ("right", 90),
            ("back_right", 135),
            ("back", 180),
            ("back_left", 225),
            ("left", 270),
            ("front_left", 315)
        }) {
            var bitmapName = $"{item.Name}.bmp";
            File.WriteAllBytes(Path.Combine(directory, bitmapName), [0x42]);
            document.Directions.Add(new SpriteDirectionMetadata {
                Name = item.Name,
                Angle = item.Angle,
                Files = { [64] = bitmapName }
            });
        }

        document.Lod.Add(new SpriteLodMetadata {
            MaxDistance = 9999.0,
            Resolution = 64
        });

        SpriteMetadataWriter.Save(document, Path.Combine(directory, fileName));
    }

    private static void WriteAnimatedSpriteSet(string directory, string fileName)
    {
        var document = new SpriteMetadataDocument {
            SpriteSet = "animated",
            Format = "BMP",
            TransparentColor = [0, 0, 0],
            DefaultResolution = 64,
            MaxResolution = 64
        };
        document.SupportedResolutions.Add(64);

        document.Animations.Add(new SpriteAnimationMetadata {
            Name = "idle",
            FrameDurationMs = 160,
            Loop = true,
            Frames = {
                new SpriteAnimationFrameMetadata {
                    Directions = WriteSpriteDirections(directory, "64")
                }
            }
        });
        document.Animations.Add(new SpriteAnimationMetadata {
            Name = "walk",
            FrameDurationMs = 120,
            Loop = true,
            Frames = {
                new SpriteAnimationFrameMetadata {
                    Directions = WriteSpriteDirections(directory, "64_walk_0")
                },
                new SpriteAnimationFrameMetadata {
                    Directions = WriteSpriteDirections(directory, "64_walk_1")
                }
            }
        });
        foreach (var animation in document.Animations) {
            animation.Directions.AddRange(animation.Frames[0].Directions.Select(direction =>
                new SpriteDirectionMetadata {
                    Name = direction.Name,
                    Angle = direction.Angle,
                    Files = direction.Files.ToDictionary(item => item.Key, item => item.Value)
                }));
        }

        document.Lod.Add(new SpriteLodMetadata {
            MaxDistance = 9999.0,
            Resolution = 64
        });

        SpriteMetadataWriter.Save(document, Path.Combine(directory, fileName));
    }

    private static List<SpriteDirectionMetadata> WriteSpriteDirections(string directory, string frameDirectory)
    {
        var fullFrameDirectory = Directory.CreateDirectory(Path.Combine(directory, frameDirectory));
        return [
            WriteSpriteDirection(fullFrameDirectory.FullName, frameDirectory, "front", 0),
            WriteSpriteDirection(fullFrameDirectory.FullName, frameDirectory, "front_right", 45),
            WriteSpriteDirection(fullFrameDirectory.FullName, frameDirectory, "right", 90),
            WriteSpriteDirection(fullFrameDirectory.FullName, frameDirectory, "back_right", 135),
            WriteSpriteDirection(fullFrameDirectory.FullName, frameDirectory, "back", 180),
            WriteSpriteDirection(fullFrameDirectory.FullName, frameDirectory, "back_left", 225),
            WriteSpriteDirection(fullFrameDirectory.FullName, frameDirectory, "left", 270),
            WriteSpriteDirection(fullFrameDirectory.FullName, frameDirectory, "front_left", 315)
        ];
    }

    private static SpriteDirectionMetadata WriteSpriteDirection(
        string fullFrameDirectory,
        string frameDirectory,
        string name,
        int angle)
    {
        File.WriteAllBytes(Path.Combine(fullFrameDirectory, $"{name}.bmp"), [0x42]);
        return new SpriteDirectionMetadata {
            Name = name,
            Angle = angle,
            Files = { [64] = $"{frameDirectory}/{name}.bmp" }
        };
    }
}
