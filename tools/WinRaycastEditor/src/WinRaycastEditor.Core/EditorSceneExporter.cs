namespace WinRaycastEditor.Core;

public sealed class EditorSceneExportResult
{
    public string ProjectPath { get; init; } = string.Empty;
    public string WorldPath { get; init; } = string.Empty;
    public string? EnginePath { get; init; }
    public string? RunScriptPath { get; init; }
}

public sealed class EditorSceneExportOptions
{
    public string? EngineExecutablePath { get; init; }
    public bool IncludeRuntime => !string.IsNullOrWhiteSpace(EngineExecutablePath);
}

public static class EditorSceneExporter
{
    public static EditorSceneExportResult Export(EditorMapDocument document, string projectPath)
    {
        return Export(document, projectPath, new EditorSceneExportOptions());
    }

    public static EditorSceneExportResult Export(
        EditorMapDocument document,
        string projectPath,
        EditorSceneExportOptions options)
    {
        var projectDirectory =
            Path.GetDirectoryName(Path.GetFullPath(projectPath)) ?? Environment.CurrentDirectory;
        Directory.CreateDirectory(projectDirectory);
        CopyWorldResourceBundle(document, projectDirectory);

        var worldPath = Path.Combine(projectDirectory, "world.world.json");
        var exportDocument = CreateExportDocument(document, projectDirectory);

        var project = EditorProjectDocumentService.FromMapDocument(exportDocument, "world.world.json");
        project.ProjectName = Path.GetFileNameWithoutExtension(projectPath);
        project.TextureRoot = ".";
        project.PlayerWeapons.Clear();
        exportDocument.PlayerWeapons.Clear();

        WorldPlayerWeapon? copiedDefaultWeapon = null;
        foreach (var weapon in document.PlayerWeapons) {
            if (string.IsNullOrWhiteSpace(weapon.File)) {
                continue;
            }

            var copiedWeapon = CopyPlayerWeapon(
                document,
                weapon,
                projectDirectory);
            exportDocument.PlayerWeapons.Add(copiedWeapon);
            project.PlayerWeapons.Add(copiedWeapon);

            if (document.PlayerWeapon is not null
                && string.Equals(
                    weapon.File,
                    document.PlayerWeapon.File,
                    StringComparison.OrdinalIgnoreCase)) {
                copiedDefaultWeapon = copiedWeapon;
            }
        }

        if (document.PlayerWeapon is not null
            && !string.IsNullOrWhiteSpace(document.PlayerWeapon.File)) {
            copiedDefaultWeapon ??= CopyPlayerWeapon(
                    document,
                    document.PlayerWeapon,
                    projectDirectory);
            exportDocument.PlayerWeapon = copiedDefaultWeapon;
            project.PlayerWeapon = copiedDefaultWeapon;
            if (!exportDocument.PlayerWeapons.Any(
                weapon => string.Equals(
                    weapon.File,
                    copiedDefaultWeapon.File,
                    StringComparison.OrdinalIgnoreCase))) {
                exportDocument.PlayerWeapons.Insert(0, copiedDefaultWeapon);
                project.PlayerWeapons.Insert(0, copiedDefaultWeapon);
            }
        }

        project.SpriteSets.Clear();
        exportDocument.SpriteSetFiles.Clear();
        foreach (var spriteSet in document.SpriteSetFiles) {
            var copiedSpriteSet = CopySpriteSet(document, spriteSet, projectDirectory);
            project.SpriteSets.Add(copiedSpriteSet);
            exportDocument.SpriteSetFiles.Add(copiedSpriteSet);
        }

        var world = LegacyWorldConverter.FromEditorMap(
            exportDocument,
            Path.GetFileNameWithoutExtension(worldPath));
        WorldJsonDocumentService.Save(world, worldPath);
        EditorProjectDocumentService.Save(project, projectPath);
        var enginePath = options.IncludeRuntime
            ? CopyRuntime(options.EngineExecutablePath!, projectDirectory)
            : null;
        var runScriptPath = enginePath is null
            ? null
            : WriteRunScript(projectDirectory, Path.GetFileName(projectPath));

        return new EditorSceneExportResult {
            ProjectPath = Path.GetFullPath(projectPath),
            WorldPath = worldPath,
            EnginePath = enginePath,
            RunScriptPath = runScriptPath
        };
    }

    private static EditorMapDocument CreateExportDocument(EditorMapDocument document, string projectDirectory)
    {
        var exportDocument = CloneStructure(document);
        var textureDirectory = Path.Combine(projectDirectory, "textures");
        Directory.CreateDirectory(textureDirectory);

        foreach (var item in document.TextureMap.OrderBy(item => item.Key)) {
            var sourcePath = ResolveTexturePath(document, item.Value);
            if (!File.Exists(sourcePath)) {
                throw new FileNotFoundException($"Missing texture image for key 0x{item.Key:x2}.", sourcePath);
            }

            var destinationName = $"{item.Key:x2}_{Path.GetFileName(sourcePath)}";
            var destinationPath = Path.Combine(textureDirectory, destinationName);
            File.Copy(sourcePath, destinationPath, overwrite: true);

            var exportedName = Path.Combine("textures", destinationName)
                .Replace('\\', '/');
            exportDocument.TextureMap[item.Key] = exportedName;
        }

        exportDocument.DefaultHorizonImage =
            CopyWorldBackgroundImage(document, exportDocument, textureDirectory);
        CopyBlockHorizonImages(document, exportDocument.Blocks.Values, textureDirectory);
        exportDocument.BackgroundMusic =
            CopyBackgroundMusic(document, document.BackgroundMusic, projectDirectory);
        CopySpriteDamageResponseSounds(document, exportDocument.SpriteInstances, projectDirectory);
        CopyBlockDoorSounds(document, exportDocument.Blocks.Values, projectDirectory);
        CopyRuntimeSupportAssets(document, projectDirectory);
        foreach (var layer in exportDocument.Layers) {
            layer.DefaultHorizonImage =
                CopyOptionalHorizonImage(
                    document,
                    layer.DefaultHorizonImage,
                    textureDirectory,
                    fallback: exportDocument.DefaultHorizonImage);
            layer.BackgroundMusic =
                CopyBackgroundMusic(document, layer.BackgroundMusic, projectDirectory);
            CopySpriteDamageResponseSounds(document, layer.SpriteInstances, projectDirectory);
        }

        return exportDocument;
    }

    private static void CopyWorldResourceBundle(EditorMapDocument document, string projectDirectory)
    {
        CopyDirectoryContents(
            WorldDirectory(document),
            projectDirectory,
            projectDirectory);
    }

    private static void CopyDirectoryContents(
        string sourceDirectory,
        string destinationDirectory,
        string projectDirectory)
    {
        if (!Directory.Exists(sourceDirectory)) {
            return;
        }

        var sourceRoot = Path.GetFullPath(sourceDirectory);
        var destinationRoot = Path.GetFullPath(destinationDirectory);
        var projectRoot = Path.GetFullPath(projectDirectory);

        foreach (var sourceFile in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories)) {
            var fullSourceFile = Path.GetFullPath(sourceFile);
            if (IsUnderDirectory(fullSourceFile, projectRoot)) {
                continue;
            }

            var relativePath = Path.GetRelativePath(sourceRoot, fullSourceFile);
            var destinationPath = Path.Combine(destinationRoot, relativePath);
            if (string.Equals(fullSourceFile, Path.GetFullPath(destinationPath), StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath) ?? destinationRoot);
            File.Copy(fullSourceFile, destinationPath, overwrite: true);
        }
    }

    private static bool IsUnderDirectory(string path, string directory)
    {
        var relative = Path.GetRelativePath(directory, path);
        return !relative.StartsWith("..", StringComparison.Ordinal)
            && !Path.IsPathRooted(relative);
    }

    private static EditorMapDocument CloneStructure(EditorMapDocument document)
    {
        var clone = new EditorMapDocument {
            CellWidth = document.CellWidth,
            CellHeight = document.CellHeight,
            Brightness = document.Brightness,
            DepthShading = document.DepthShading,
            DefaultHorizonImage = document.DefaultHorizonImage,
            PlayerStart = new WorldPlayerStart {
                XCell = document.PlayerStart.XCell,
                YCell = document.PlayerStart.YCell,
                FacingDegrees = document.PlayerStart.FacingDegrees
            },
            PlayerStats = new WorldCombatStats {
                MaxHealth = document.PlayerStats.MaxHealth,
                Health = document.PlayerStats.Health
            },
            PlayerWeapon = ClonePlayerWeapon(document.PlayerWeapon),
            BackgroundMusic = CloneBackgroundMusic(document.BackgroundMusic),
            ActiveLayerId = document.ActiveLayerId,
            GameGoal = document.GameGoal is null ? null : new WorldGameGoal {
                Layer = document.GameGoal.Layer,
                Row = document.GameGoal.Row,
                Column = document.GameGoal.Column,
                RequiredKey = document.GameGoal.RequiredKey
            }
        };
        CopyPlayerWeapons(document.PlayerWeapons, clone.PlayerWeapons);

        foreach (var row in document.Rows) {
            var clonedRow = new List<EditorMapCell>();
            foreach (var cell in row) {
                clonedRow.Add(new EditorMapCell(cell.Row, cell.Column, cell.PackedValue) {
                    BlockId = cell.BlockId,
                    Fields = cell.Fields,
                    HorizonImage = cell.HorizonImage
                });
            }

            clone.Rows.Add(clonedRow);
        }

        foreach (var sprite in document.SpriteInstances) {
            clone.SpriteInstances.Add(sprite);
        }

        foreach (var block in document.Blocks) {
            clone.Blocks[block.Key] = block.Value;
        }

        foreach (var layer in document.Layers) {
            clone.Layers.Add(CloneLayer(layer));
        }

        foreach (var transition in document.LayerTransitions) {
            clone.LayerTransitions.Add(CloneLayerTransition(transition));
        }

        return clone;
    }

    private static string CopySpriteSet(
        EditorMapDocument document,
        string spriteSet,
        string projectDirectory)
    {
        var absoluteSpriteSet = Path.IsPathRooted(spriteSet)
            ? spriteSet
            : Path.Combine(WorldDirectory(document), spriteSet);

        var loadResult = SpriteMetadataLoader.Load(absoluteSpriteSet);
        if (!loadResult.Success || loadResult.Document is null) {
            throw new InvalidOperationException(
                $"Cannot export sprite set '{absoluteSpriteSet}': {string.Join("; ", loadResult.Errors)}");
        }

        var spriteDirectory = Path.Combine(projectDirectory, "sprites");
        Directory.CreateDirectory(spriteDirectory);

        var sourceDirectory =
            Path.GetDirectoryName(Path.GetFullPath(absoluteSpriteSet)) ?? Environment.CurrentDirectory;
        var destinationFileName = Path.GetFileName(absoluteSpriteSet);
        var destinationPath = Path.Combine(spriteDirectory, destinationFileName);
        var assetDirectoryName = Path.GetFileNameWithoutExtension(destinationFileName) + "_assets";
        var assetDirectory = Path.Combine(spriteDirectory, assetDirectoryName);
        Directory.CreateDirectory(assetDirectory);

        var exported = CloneSpriteMetadata(loadResult.Document);
        foreach (var direction in exported.Directions) {
            CopyDirectionAssets(
                loadResult.Document.SpriteSet,
                direction,
                sourceDirectory,
                assetDirectory,
                assetDirectoryName);
        }

        foreach (var animation in exported.Animations) {
            foreach (var direction in animation.Directions) {
                CopyDirectionAssets(
                    loadResult.Document.SpriteSet,
                    direction,
                    sourceDirectory,
                    assetDirectory,
                    assetDirectoryName);
            }

            foreach (var frame in animation.Frames) {
                foreach (var direction in frame.Directions) {
                    CopyDirectionAssets(
                        loadResult.Document.SpriteSet,
                        direction,
                        sourceDirectory,
                        assetDirectory,
                        assetDirectoryName);
                }
            }
        }

        SpriteMetadataWriter.Save(exported, destinationPath);
        return Path.GetRelativePath(projectDirectory, destinationPath).Replace('\\', '/');
    }

    private static WorldPlayerWeapon CopyPlayerWeapon(
        EditorMapDocument document,
        WorldPlayerWeapon weapon,
        string projectDirectory)
    {
        var sourcePath = Path.IsPathRooted(weapon.File)
            ? weapon.File
            : Path.Combine(WorldDirectory(document), weapon.File);

        if (!File.Exists(sourcePath)) {
            throw new FileNotFoundException("Missing player weapon metadata.", sourcePath);
        }

        var sourceDirectory =
            Path.GetDirectoryName(Path.GetFullPath(sourcePath)) ?? Environment.CurrentDirectory;
        var destinationRoot = Path.Combine(projectDirectory, "weapons");
        var destinationDirectory = Path.Combine(
            destinationRoot,
            Path.GetFileName(sourceDirectory));

        CopyDirectory(sourceDirectory, destinationDirectory);

        var destinationPath = Path.Combine(destinationDirectory, Path.GetFileName(sourcePath));
        return new WorldPlayerWeapon {
            File = Path.GetRelativePath(projectDirectory, destinationPath).Replace('\\', '/'),
            Visible = weapon.Visible,
            Unlocked = weapon.Unlocked,
            ScreenHeightFraction = weapon.ScreenHeightFraction
        };
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        if (string.Equals(
                Path.GetFullPath(sourceDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(destinationDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase)) {
            return;
        }

        Directory.CreateDirectory(destinationDirectory);

        foreach (var file in Directory.EnumerateFiles(sourceDirectory)) {
            File.Copy(
                file,
                Path.Combine(destinationDirectory, Path.GetFileName(file)),
                overwrite: true);
        }

        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory)) {
            CopyDirectory(
                directory,
                Path.Combine(destinationDirectory, Path.GetFileName(directory)));
        }
    }

    private static void CopyDirectionAssets(
        string spriteSetName,
        SpriteDirectionMetadata direction,
        string sourceDirectory,
        string assetDirectory,
        string assetDirectoryName)
    {
        var sourceFiles = direction.Files.ToArray();
        direction.Files.Clear();

        foreach (var file in sourceFiles.OrderBy(item => item.Key)) {
            var sourcePath = Path.GetFullPath(Path.Combine(sourceDirectory, file.Value));
            if (!File.Exists(sourcePath)) {
                throw new FileNotFoundException(
                    $"Missing sprite image for {spriteSetName}/{direction.Name}/{file.Key}.",
                    sourcePath);
            }

            var destinationName = Path.GetRelativePath(sourceDirectory, sourcePath);
            var destinationPathForBitmap = Path.Combine(assetDirectory, destinationName);
            Directory.CreateDirectory(
                Path.GetDirectoryName(destinationPathForBitmap) ?? assetDirectory);
            File.Copy(sourcePath, destinationPathForBitmap, overwrite: true);
            direction.Files[file.Key] =
                Path.Combine(assetDirectoryName, destinationName).Replace('\\', '/');
        }
    }

    private static SpriteMetadataDocument CloneSpriteMetadata(SpriteMetadataDocument document)
    {
        var clone = new SpriteMetadataDocument {
            SpriteSet = document.SpriteSet,
            Format = document.Format,
            TransparentColor = document.TransparentColor.ToArray(),
            DefaultResolution = document.DefaultResolution,
            MaxResolution = document.MaxResolution
        };

        clone.SupportedResolutions.AddRange(document.SupportedResolutions);
        foreach (var direction in document.Directions) {
            clone.Directions.Add(CloneDirection(direction));
        }

        foreach (var animation in document.Animations) {
            var clonedAnimation = new SpriteAnimationMetadata {
                Name = animation.Name,
                FrameDurationMs = animation.FrameDurationMs,
                Loop = animation.Loop
            };

            clonedAnimation.Directions.AddRange(animation.Directions.Select(CloneDirection));
            foreach (var frame in animation.Frames) {
                clonedAnimation.Frames.Add(new SpriteAnimationFrameMetadata {
                    Directions = frame.Directions.Select(CloneDirection).ToList()
                });
            }

            clone.Animations.Add(clonedAnimation);
        }

        foreach (var rule in document.Lod) {
            clone.Lod.Add(new SpriteLodMetadata {
                MaxDistance = rule.MaxDistance,
                Resolution = rule.Resolution
            });
        }

        return clone;
    }

    private static WorldPlayerWeapon? ClonePlayerWeapon(WorldPlayerWeapon? weapon)
    {
        if (weapon is null) {
            return null;
        }

        return new WorldPlayerWeapon {
            File = weapon.File,
            Visible = weapon.Visible,
            Unlocked = weapon.Unlocked,
            ScreenHeightFraction = weapon.ScreenHeightFraction
        };
    }

    private static void CopyPlayerWeapons(
        IEnumerable<WorldPlayerWeapon> source,
        ICollection<WorldPlayerWeapon> target)
    {
        foreach (var weapon in source) {
            var clone = ClonePlayerWeapon(weapon);
            if (clone is not null) {
                target.Add(clone);
            }
        }
    }

    private static WorldBackgroundMusic? CloneBackgroundMusic(WorldBackgroundMusic? music)
    {
        if (music is null) {
            return null;
        }

        return new WorldBackgroundMusic {
            File = music.File,
            Enabled = music.Enabled,
            Loop = music.Loop,
            VolumePercent = music.VolumePercent
        };
    }

    private static WorldLayerDefinition CloneLayer(WorldLayerDefinition layer)
    {
        return new WorldLayerDefinition {
            Id = layer.Id,
            Name = layer.Name,
            Brightness = layer.Brightness,
            DepthShading = layer.DepthShading,
            Grid = layer.Grid is null ? null : CloneGrid(layer.Grid),
            PlayerStart = layer.PlayerStart is null ? null : ClonePlayerStart(layer.PlayerStart),
            DefaultHorizonImage = layer.DefaultHorizonImage,
            BackgroundMusic = CloneBackgroundMusic(layer.BackgroundMusic),
            Cells = layer.Cells.Select(row => row.ToList()).ToList(),
            SpriteInstances = layer.SpriteInstances.ToList()
        };
    }

    private static WorldLayerTransition CloneLayerTransition(WorldLayerTransition transition)
    {
        return new WorldLayerTransition {
            FromLayer = transition.FromLayer,
            ToLayer = transition.ToLayer,
            RequiredKey = transition.RequiredKey,
            TriggerBlockId = transition.TriggerBlockId,
            Trigger = transition.Trigger is null
                ? null
                : new WorldLayerTransitionTrigger {
                    BlockId = transition.Trigger.BlockId,
                    Row = transition.Trigger.Row,
                    Column = transition.Trigger.Column
                },
            WaitSeconds = transition.WaitSeconds,
            TargetPlayerStart = transition.TargetPlayerStart is null
                ? null
                : ClonePlayerStart(transition.TargetPlayerStart)
        };
    }

    private static WorldGridDefinition CloneGrid(WorldGridDefinition grid)
    {
        return new WorldGridDefinition {
            Columns = grid.Columns,
            Rows = grid.Rows,
            CellWidth = grid.CellWidth,
            CellDepth = grid.CellDepth,
            DefaultWallHeight = grid.DefaultWallHeight
        };
    }

    private static WorldPlayerStart ClonePlayerStart(WorldPlayerStart playerStart)
    {
        return new WorldPlayerStart {
            XCell = playerStart.XCell,
            YCell = playerStart.YCell,
            FacingDegrees = playerStart.FacingDegrees
        };
    }

    private static SpriteDirectionMetadata CloneDirection(SpriteDirectionMetadata direction)
    {
        var clone = new SpriteDirectionMetadata {
            Name = direction.Name,
            Angle = direction.Angle
        };

        foreach (var file in direction.Files) {
            clone.Files[file.Key] = file.Value;
        }

        return clone;
    }

    private static string ResolveTexturePath(EditorMapDocument document, string textureName)
    {
        var relativePath = ResolveTextureRelativePath(document, textureName);
        return Path.GetFullPath(Path.Combine(WorldDirectory(document), relativePath));
    }

    private static bool HasSupportedImageExtension(string path)
    {
        return path.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".png", StringComparison.OrdinalIgnoreCase);
    }

    private static string CopyWorldBackgroundImage(
        EditorMapDocument document,
        EditorMapDocument exportDocument,
        string textureDirectory)
    {
        if (!string.IsNullOrWhiteSpace(document.DefaultHorizonImage)) {
            return CopyImageToTextures(
                ResolveRequiredImagePath(
                    document,
                    document.DefaultHorizonImage,
                    "world default horizon image"),
                textureDirectory,
                prefix: "sky_");
        }

        if (exportDocument.TextureMap.TryGetValue(0xff, out var textureSky)
            && !string.IsNullOrWhiteSpace(textureSky)) {
            return textureSky;
        }

        return CopyFallbackSkyTexture(document, textureDirectory);
    }

    private static string? CopyOptionalHorizonImage(
        EditorMapDocument document,
        string? horizonImage,
        string textureDirectory,
        string? fallback = null)
    {
        if (string.IsNullOrWhiteSpace(horizonImage)) {
            return horizonImage;
        }

        if (!TryResolveImagePath(document, horizonImage, out var sourcePath)) {
            return fallback;
        }

        return CopyImageToTextures(
            sourcePath,
            textureDirectory,
            prefix: "sky_");
    }

    private static void CopyBlockHorizonImages(
        EditorMapDocument document,
        IEnumerable<WorldBlockDefinition> blocks,
        string textureDirectory)
    {
        foreach (var block in blocks) {
            block.HorizonImage =
                CopyOptionalHorizonImage(document, block.HorizonImage, textureDirectory);
        }
    }

    private static string CopyFallbackSkyTexture(EditorMapDocument document, string textureDirectory)
    {
        var sourcePath = Path.Combine(WorldDirectory(document), "texture_sky_clouds.png");
        if (!File.Exists(sourcePath)) {
            sourcePath = Path.Combine(WorldDirectory(document), "clouds.bmp");
        }

        if (!File.Exists(sourcePath)) {
            sourcePath = FindRepoFile("res", "worlds", "demo_embedded", "textures", "texture_sky_clouds.png")
                ?? FindRepoFile("res", "worlds", "demo_embedded", "textures", "clouds.bmp")
                ?? throw new FileNotFoundException("Missing sky texture.", sourcePath);
        }

        return CopyImageToTextures(sourcePath, textureDirectory, prefix: "sky_");
    }

    private static WorldBackgroundMusic? CopyBackgroundMusic(
        EditorMapDocument document,
        WorldBackgroundMusic? music,
        string projectDirectory)
    {
        if (music is null) {
            return null;
        }

        if (string.IsNullOrWhiteSpace(music.File)) {
            return CloneBackgroundMusic(music);
        }

        if (!TryResolveAudioPath(document, music.File, out var sourcePath)) {
            throw new FileNotFoundException(
                $"Missing background music asset: {music.File}.",
                Path.GetFullPath(Path.Combine(WorldDirectory(document), music.File)));
        }

        var audioDirectory = Path.Combine(projectDirectory, "audio");
        Directory.CreateDirectory(audioDirectory);
        var destinationName = Path.GetFileName(sourcePath);
        var destinationPath = Path.Combine(audioDirectory, destinationName);

        if (!string.Equals(
                Path.GetFullPath(sourcePath),
                Path.GetFullPath(destinationPath),
                StringComparison.OrdinalIgnoreCase)) {
            File.Copy(sourcePath, destinationPath, overwrite: true);
        }

        return new WorldBackgroundMusic {
            File = Path.Combine("audio", destinationName).Replace('\\', '/'),
            Enabled = music.Enabled,
            Loop = music.Loop,
            VolumePercent = music.VolumePercent
        };
    }

    private static void CopySpriteDamageResponseSounds(
        EditorMapDocument document,
        IEnumerable<EditorSpriteInstance> sprites,
        string projectDirectory)
    {
        foreach (var sprite in sprites) {
            var response = sprite.DamageResponse;
            if (response is null || string.IsNullOrWhiteSpace(response.Sound)) {
                continue;
            }

            response.Sound = CopyOptionalAudioAsset(
                document,
                response.Sound,
                projectDirectory,
                Path.Combine("audio", "effects"));
        }
    }

    private static void CopyBlockDoorSounds(
        EditorMapDocument document,
        IEnumerable<WorldBlockDefinition> blocks,
        string projectDirectory)
    {
        foreach (var block in blocks) {
            var door = block.Door;
            if (door is null || string.IsNullOrWhiteSpace(door.OpenSound)) {
                continue;
            }

            door.OpenSound = CopyOptionalAudioAsset(
                document,
                door.OpenSound,
                projectDirectory,
                Path.Combine("audio", "doors"));
        }
    }

    private static string? CopyOptionalAudioAsset(
        EditorMapDocument document,
        string audioName,
        string projectDirectory,
        string relativeDestinationDirectory)
    {
        if (!TryResolveAudioPath(document, audioName, out var sourcePath)) {
            return null;
        }

        var destinationDirectory = Path.Combine(projectDirectory, relativeDestinationDirectory);
        Directory.CreateDirectory(destinationDirectory);

        var destinationName = Path.GetFileName(sourcePath);
        var destinationPath = Path.Combine(destinationDirectory, destinationName);
        if (!string.Equals(
                Path.GetFullPath(sourcePath),
                Path.GetFullPath(destinationPath),
                StringComparison.OrdinalIgnoreCase)) {
            File.Copy(sourcePath, destinationPath, overwrite: true);
        }

        return Path.GetRelativePath(projectDirectory, destinationPath).Replace('\\', '/');
    }

    private static void CopyRuntimeSupportAssets(EditorMapDocument document, string projectDirectory)
    {
        CopyRuntimeSupportDirectory(document, projectDirectory, "hud");
        CopyRuntimeSupportDirectory(document, projectDirectory, "effects");
    }

    private static void CopyRuntimeSupportDirectory(
        EditorMapDocument document,
        string projectDirectory,
        string relativeDirectory)
    {
        var sourceDirectory = FindRuntimeSupportDirectory(document, relativeDirectory);
        if (sourceDirectory is null) {
            return;
        }

        var destinationDirectory = Path.Combine(projectDirectory, relativeDirectory);
        foreach (var sourceFile in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories)) {
            var relativeFile = Path.GetRelativePath(sourceDirectory, sourceFile);
            var destinationPath = Path.Combine(destinationDirectory, relativeFile);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath) ?? destinationDirectory);
            if (!string.Equals(
                    Path.GetFullPath(sourceFile),
                    Path.GetFullPath(destinationPath),
                    StringComparison.OrdinalIgnoreCase)) {
                File.Copy(sourceFile, destinationPath, overwrite: true);
            }
        }
    }

    private static string? FindRuntimeSupportDirectory(EditorMapDocument document, string relativeDirectory)
    {
        var worldCandidate = Path.Combine(WorldDirectory(document), relativeDirectory);
        if (Directory.Exists(worldCandidate)) {
            return worldCandidate;
        }

        return FindRepoDirectory("res", "worlds", "demo_embedded", relativeDirectory);
    }

    private static string CopyRuntime(string engineExecutablePath, string projectDirectory)
    {
        if (!File.Exists(engineExecutablePath)) {
            throw new FileNotFoundException("Missing WinRayCast Player executable.", engineExecutablePath);
        }

        var destinationPath = Path.Combine(projectDirectory, "WinRayCastPlayer.exe");
        if (!string.Equals(
                Path.GetFullPath(engineExecutablePath),
                Path.GetFullPath(destinationPath),
                StringComparison.OrdinalIgnoreCase)) {
            File.Copy(engineExecutablePath, destinationPath, overwrite: true);
        }

        CopyRuntimeSidecarDlls(engineExecutablePath, projectDirectory);
        return destinationPath;
    }

    private static void CopyRuntimeSidecarDlls(string engineExecutablePath, string projectDirectory)
    {
        var engineDirectory =
            Path.GetDirectoryName(Path.GetFullPath(engineExecutablePath)) ?? Environment.CurrentDirectory;
        foreach (var dll in Directory.EnumerateFiles(engineDirectory, "*.dll")) {
            File.Copy(
                dll,
                Path.Combine(projectDirectory, Path.GetFileName(dll)),
                overwrite: true);
        }
    }

    private static string WriteRunScript(string projectDirectory, string projectFileName)
    {
        var scriptPath = Path.Combine(projectDirectory, "run_demo.bat");
        File.WriteAllText(
            scriptPath,
            string.Join(
                Environment.NewLine,
                "@echo off",
                "setlocal",
                "pushd \"%~dp0\"",
                "WinRayCastPlayer.exe \"%~dp0" + projectFileName + "\"",
                "set EXITCODE=%ERRORLEVEL%",
                "popd",
                "exit /b %EXITCODE%",
                string.Empty));
        return scriptPath;
    }

    private static string CopyImageToTextures(string sourcePath, string textureDirectory, string prefix)
    {
        if (!File.Exists(sourcePath)) {
            throw new FileNotFoundException("Missing image asset.", sourcePath);
        }

        Directory.CreateDirectory(textureDirectory);
        var destinationName = $"{prefix}{Path.GetFileName(sourcePath)}";
        var destinationPath = Path.Combine(textureDirectory, destinationName);
        File.Copy(sourcePath, destinationPath, overwrite: true);
        return Path.Combine("textures", destinationName).Replace('\\', '/');
    }

    private static string ResolveTextureRelativePath(EditorMapDocument document, string textureName)
    {
        if (HasSupportedImageExtension(textureName)) {
            return textureName;
        }

        var pngPath = $"{textureName}.png";
        if (File.Exists(Path.Combine(WorldDirectory(document), pngPath))) {
            return pngPath;
        }

        return $"{textureName}.bmp";
    }

    private static bool TryResolveAudioPath(
        EditorMapDocument document,
        string audioName,
        out string path)
    {
        foreach (var candidate in CandidateAudioPaths(document, audioName)) {
            if (File.Exists(candidate)) {
                path = Path.GetFullPath(candidate);
                return true;
            }
        }

        path = string.Empty;
        return false;
    }

    private static string ResolveRequiredImagePath(
        EditorMapDocument document,
        string imageName,
        string description)
    {
        if (TryResolveImagePath(document, imageName, out var path)) {
            return path;
        }

        throw new FileNotFoundException(
            $"Missing image asset for {description}: {imageName}.",
            Path.GetFullPath(Path.Combine(WorldDirectory(document), ResolveImageRelativePath(document, imageName))));
    }

    private static bool TryResolveImagePath(
        EditorMapDocument document,
        string imageName,
        out string path)
    {
        foreach (var candidate in CandidateImagePaths(document, imageName)) {
            if (File.Exists(candidate)) {
                path = Path.GetFullPath(candidate);
                return true;
            }
        }

        path = string.Empty;
        return false;
    }

    private static string ResolveImageRelativePath(EditorMapDocument document, string imageName)
    {
        if (HasSupportedImageExtension(imageName)) {
            return imageName;
        }

        var pngPath = $"{imageName}.png";
        if (File.Exists(Path.Combine(WorldDirectory(document), pngPath))) {
            return pngPath;
        }

        return $"{imageName}.bmp";
    }

    private static IEnumerable<string> CandidateImagePaths(EditorMapDocument document, string imageName)
    {
        if (Path.IsPathRooted(imageName)) {
            yield return Path.GetFullPath(imageName);
            yield break;
        }

        var worldDirectory = WorldDirectory(document);
        var relativePath = ResolveImageRelativePath(document, imageName);
        yield return Path.GetFullPath(Path.Combine(worldDirectory, relativePath));

        var fileName = Path.GetFileName(relativePath);
        if (!string.IsNullOrWhiteSpace(fileName)) {
            yield return Path.GetFullPath(Path.Combine(worldDirectory, "textures", fileName));
        }

        var normalizedParts = relativePath
            .Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        if (normalizedParts.Length > 0) {
            var repoRelative = FindRepoFile(
                ["res", "worlds", "demo_embedded", .. normalizedParts]);
            if (repoRelative is not null) {
                yield return repoRelative;
            }
        }

        if (!string.IsNullOrWhiteSpace(fileName)) {
            var repoTexture = FindRepoFile(
                "res",
                "worlds",
                "demo_embedded",
                "textures",
                fileName);
            if (repoTexture is not null) {
                yield return repoTexture;
            }
        }
    }

    private static IEnumerable<string> CandidateAudioPaths(EditorMapDocument document, string audioName)
    {
        if (Path.IsPathRooted(audioName)) {
            yield return Path.GetFullPath(audioName);
            yield break;
        }

        var worldDirectory = WorldDirectory(document);
        yield return Path.GetFullPath(Path.Combine(worldDirectory, audioName));

        var fileName = Path.GetFileName(audioName);
        if (!string.IsNullOrWhiteSpace(fileName)) {
            yield return Path.GetFullPath(Path.Combine(worldDirectory, "audio", fileName));
            yield return Path.GetFullPath(Path.Combine(worldDirectory, "effects", fileName));
        }

        var normalizedParts = audioName
            .Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        if (normalizedParts.Length > 0) {
            var repoRelative = FindRepoFile(
                ["res", "worlds", "demo_embedded", .. normalizedParts]);
            if (repoRelative is not null) {
                yield return repoRelative;
            }
        }

        if (!string.IsNullOrWhiteSpace(fileName)) {
            foreach (var repoDirectory in new[] { "audio", "effects" }) {
                var repoAudio = FindRepoFile(
                    "res",
                    "worlds",
                    "demo_embedded",
                    repoDirectory,
                    fileName);
                if (repoAudio is not null) {
                    yield return repoAudio;
                }
            }
        }
    }

    private static string? FindRepoFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null) {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate)) {
                return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static string? FindRepoDirectory(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null) {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
            if (Directory.Exists(candidate)) {
                return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static string WorldDirectory(EditorMapDocument document)
    {
        return string.IsNullOrWhiteSpace(document.SourcePath)
            ? Environment.CurrentDirectory
            : Path.GetDirectoryName(Path.GetFullPath(document.SourcePath)) ?? Environment.CurrentDirectory;
    }
}
