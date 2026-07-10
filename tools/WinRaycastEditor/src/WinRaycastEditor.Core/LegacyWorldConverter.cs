using System.Globalization;

namespace WinRaycastEditor.Core;

public static class LegacyWorldConverter
{
    private const byte TransparentTextureKey = 0xff;
    public const string EmptyBlockId = WorldJsonDocumentService.EmptyBlockId;

    public static WorldDocument FromEditorMap(EditorMapDocument map, string name = "")
    {
        var defaultWallHeight = map.CellHeight > 0 ? map.CellHeight : 512;
        var world = new WorldDocument {
            Name = name,
            PlayerStart = new WorldPlayerStart {
                XCell = map.PlayerStart.XCell,
                YCell = map.PlayerStart.YCell,
                FacingDegrees = map.PlayerStart.FacingDegrees
            },
            PlayerStats = new WorldCombatStats {
                MaxHealth = map.PlayerStats.MaxHealth,
                Health = map.PlayerStats.Health
            },
            PlayerTurn = ClonePlayerTurn(map.PlayerTurn),
            Brightness = map.Brightness,
            DepthShading = map.DepthShading,
            DefaultHorizonImage = map.DefaultHorizonImage,
            Grid = new WorldGridDefinition {
                Columns = map.ColumnCount,
                Rows = map.RowCount,
                CellWidth = map.CellWidth,
                CellDepth = map.CellHeight,
                DefaultWallHeight = defaultWallHeight
            },
            PlayerWeapon = ClonePlayerWeapon(map.PlayerWeapon)
        };
        CopyPlayerWeapons(map.PlayerWeapons, world.PlayerWeapons);
        world.BackgroundMusic = CloneBackgroundMusic(map.BackgroundMusic);
        world.ActiveLayer = map.ActiveLayerId;
        world.StartLayer = map.ActiveLayerId;

        foreach (var texture in map.TextureMap.OrderBy(item => item.Key)) {
            var key = TextureKey(texture.Key);
            world.Textures[key] = new WorldTextureDefinition {
                Name = texture.Value,
                File = HasSupportedImageExtension(texture.Value)
                    ? texture.Value
                    : $"{texture.Value}.bmp"
            };
        }

        world.SpriteSets.AddRange(map.SpriteSetFiles);

        // Layer-owned sprites are written back to their layer below; only the
        // global (top-level) sprites belong in world.SpriteInstances.
        foreach (var sprite in map.SpriteInstances) {
            if (!map.ActiveLayerSprites.Contains(sprite)) {
                world.SpriteInstances.Add(sprite);
            }
        }

        foreach (var block in map.Blocks.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)) {
            world.Blocks[block.Key] = CloneBlock(block.Value);
        }

        if (!world.Blocks.ContainsKey(EmptyBlockId)) {
            world.Blocks[EmptyBlockId] = new WorldBlockDefinition { Name = "empty" };
        }

        var blockBySignature = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var block in world.Blocks) {
            blockBySignature.TryAdd(BlockSignature(block.Value), block.Key);
        }

        var nextBlockKey = (byte)1;
        for (var row = 0; row < map.RowCount; ++row) {
            var rowIds = new List<string>(map.ColumnCount);
            for (var column = 0; column < map.ColumnCount; ++column) {
                var cell = map.Rows[row][column];
                if (!string.IsNullOrWhiteSpace(cell.BlockId)
                    && map.Blocks.TryGetValue(cell.BlockId, out var declaredBlock)) {
                    rowIds.Add(cell.BlockId);
                    continue;
                }

                var block = BuildBlock(map.Rows[row][column], defaultWallHeight);
                var signature = BlockSignature(block);
                if (!blockBySignature.TryGetValue(signature, out var id)) {
                    id = AllocateBlockId(world.Blocks, ref nextBlockKey);
                    if (string.IsNullOrEmpty(block.Name)) {
                        block.Name = SuggestBlockName(block);
                    }
                    world.Blocks[id] = block;
                    blockBySignature[signature] = id;
                }

                rowIds.Add(id);
            }

            world.Cells.Add(rowIds);
        }

        foreach (var layer in map.Layers) {
            world.Layers.Add(CloneLayer(layer));
        }

        foreach (var transition in map.LayerTransitions) {
            world.LayerTransitions.Add(CloneLayerTransition(transition));
        }
        world.GameGoal = CloneGameGoal(map.GameGoal);

        if (world.Layers.Count > 0) {
            var activeLayer = world.Layers.FirstOrDefault(
                layer => string.Equals(layer.Id, map.ActiveLayerId, StringComparison.OrdinalIgnoreCase))
                ?? world.Layers[0];
            activeLayer.Cells = CloneCells(world.Cells);
            activeLayer.Grid = CloneGrid(world.Grid);
            activeLayer.PlayerStart = ClonePlayerStart(world.PlayerStart);
            activeLayer.DefaultHorizonImage = world.DefaultHorizonImage;
            activeLayer.BackgroundMusic = CloneBackgroundMusic(world.BackgroundMusic);
            activeLayer.SpriteInstances = map.SpriteInstances
                .Where(map.ActiveLayerSprites.Contains)
                .Select(CloneSpriteInstance)
                .ToList();
            world.ActiveLayer = activeLayer.Id;
            world.StartLayer ??= activeLayer.Id;
        }

        return world;
    }

    public static EditorMapDocument ToEditorMap(WorldDocument world, string? sourcePath = null)
    {
        var activeLayer = SelectActiveLayer(world);
        var activeGrid = activeLayer?.Grid ?? world.Grid;
        var activeCells = activeLayer is not null && activeLayer.Cells.Count > 0
            ? activeLayer.Cells
            : world.Cells;
        var activePlayerStart = activeLayer?.PlayerStart ?? world.PlayerStart;

        var map = new EditorMapDocument {
            SourcePath = sourcePath,
            CellWidth = activeGrid.CellWidth,
            CellHeight = activeGrid.CellDepth,
            DefaultHorizonImage = activeLayer?.DefaultHorizonImage ?? world.DefaultHorizonImage,
            PlayerStart = new WorldPlayerStart {
                XCell = activePlayerStart.XCell,
                YCell = activePlayerStart.YCell,
                FacingDegrees = activePlayerStart.FacingDegrees
            },
            PlayerStats = new WorldCombatStats {
                MaxHealth = world.PlayerStats.MaxHealth,
                Health = world.PlayerStats.Health
            },
            PlayerTurn = ClonePlayerTurn(world.PlayerTurn),
            Brightness = world.Brightness,
            DepthShading = world.DepthShading,
            PlayerWeapon = ClonePlayerWeapon(world.PlayerWeapon),
            BackgroundMusic = CloneBackgroundMusic(activeLayer?.BackgroundMusic ?? world.BackgroundMusic)
        };
        CopyPlayerWeapons(world.PlayerWeapons, map.PlayerWeapons);
        map.ActiveLayerId = activeLayer?.Id ?? world.ActiveLayer ?? world.StartLayer;
        foreach (var layer in world.Layers) {
            map.Layers.Add(CloneLayer(layer));
        }

        foreach (var transition in world.LayerTransitions) {
            map.LayerTransitions.Add(CloneLayerTransition(transition));
        }
        map.GameGoal = CloneGameGoal(world.GameGoal);

        var textureKeys = BuildTextureKeyMap(world, map);
        var defaultWallHeight = activeGrid.DefaultWallHeight > 0
            ? activeGrid.DefaultWallHeight
            : 512;

        for (var row = 0; row < activeGrid.Rows; ++row) {
            var mapRow = new List<EditorMapCell>();
            for (var column = 0; column < activeGrid.Columns; ++column) {
                var blockId = row < activeCells.Count && column < activeCells[row].Count
                    ? activeCells[row][column]
                    : EmptyBlockId;
                var block = world.Blocks.TryGetValue(blockId, out var found)
                    ? found
                    : new WorldBlockDefinition();
                mapRow.Add(ConvertCell(block, blockId, row, column, defaultWallHeight, textureKeys));
            }

            map.Rows.Add(mapRow);
        }

        foreach (var entry in world.Blocks) {
            map.Blocks[entry.Key] = entry.Value;
        }

        map.SpriteSetFiles.AddRange(world.SpriteSets);

        // Top-level sprites are global (shared by every layer).
        foreach (var sprite in world.SpriteInstances) {
            map.SpriteInstances.Add(sprite);
            PlaceSpriteInCell(map, sprite);
        }

        // The game merges the top-level sprites with the active layer's own
        // sprites, so surface the active layer's sprites in the working set too
        // and flag them as layer-owned. Their copies live on the cloned layer in
        // map.Layers; move them out so the working set is the single source of
        // truth (FromEditorMap writes them back to the active layer on save).
        if (activeLayer is not null) {
            var clonedActiveLayer = map.Layers.FirstOrDefault(
                layer => string.Equals(layer.Id, activeLayer.Id, StringComparison.OrdinalIgnoreCase));
            if (clonedActiveLayer is not null) {
                foreach (var sprite in clonedActiveLayer.SpriteInstances) {
                    map.SpriteInstances.Add(sprite);
                    map.ActiveLayerSprites.Add(sprite);
                    PlaceSpriteInCell(map, sprite);
                }

                clonedActiveLayer.SpriteInstances = [];
            }
        }

        return map;
    }

    private static void PlaceSpriteInCell(EditorMapDocument map, EditorSpriteInstance sprite)
    {
        var row = Math.Clamp((int)Math.Floor(sprite.YCell), 0, Math.Max(0, map.RowCount - 1));
        var column = Math.Clamp((int)Math.Floor(sprite.XCell), 0, Math.Max(0, map.ColumnCount - 1));
        map.CellAt(row, column)?.Sprites.Add(sprite);
    }

    private static WorldBlockDefinition BuildBlock(EditorMapCell cell, int defaultWallHeight)
    {
        var block = new WorldBlockDefinition {
            HorizonImage = cell.HorizonImage
        };

        if (cell.Fields.FloorTexture != 0) {
            block.Floor = new WorldSurface {
                Texture = TextureKey(cell.Fields.FloorTexture),
                Height = 0
            };
        }

        if (cell.Fields.CeilingTexture != 0) {
            block.Ceiling = new WorldSurface {
                Texture = TextureKey(cell.Fields.CeilingTexture),
                Height = defaultWallHeight
            };
        }

        if (cell.Fields.SolidWallTexture != 0) {
            block.Walls.Add(new WorldWallSpan {
                Kind = "solid",
                Texture = TextureKey(cell.Fields.SolidWallTexture),
                Bottom = 0,
                Top = defaultWallHeight,
                Collision = true
            });
        }

        if (cell.Fields.UpperWallTexture != 0) {
            block.Walls.Add(new WorldWallSpan {
                Kind = "solid",
                Texture = TextureKey(cell.Fields.UpperWallTexture),
                Bottom = defaultWallHeight,
                Top = defaultWallHeight * 2,
                Collision = true
            });
        }

        if (cell.Fields.TransparentWallTexture != 0) {
            block.Walls.Add(new WorldWallSpan {
                Kind = "transparent",
                Texture = TextureKey(cell.Fields.TransparentWallTexture),
                Bottom = 0,
                Top = defaultWallHeight,
                Collision = false
            });
        }

        return block;
    }

    private static EditorMapCell ConvertCell(
        WorldBlockDefinition block,
        string blockId,
        int row,
        int column,
        int defaultWallHeight,
        IReadOnlyDictionary<string, byte> textureKeys)
    {
        var fields = new MapCellFields(
            SolidWallTexture: 0,
            CeilingTexture: TextureByte(block.Ceiling?.Texture, textureKeys),
            FloorTexture: TextureByte(block.Floor?.Texture, textureKeys),
            TransparentWallTexture: 0,
            UpperWallTexture: 0);

        foreach (var wall in block.Walls) {
            var texture = TextureByte(PrimaryWallTexture(wall), textureKeys);
            if (texture == 0) {
                continue;
            }

            if (string.Equals(wall.Kind, "transparent", StringComparison.OrdinalIgnoreCase)) {
                fields = fields with { TransparentWallTexture = texture };
            }
            else if (wall.Bottom >= defaultWallHeight) {
                fields = fields with { UpperWallTexture = texture };
            }
            else if (fields.SolidWallTexture == 0) {
                fields = fields with { SolidWallTexture = texture };
            }
        }

        return new EditorMapCell(row, column, fields.Encode()) {
            BlockId = blockId,
            HorizonImage = block.HorizonImage
        };
    }

    private static WorldBlockDefinition CloneBlock(WorldBlockDefinition block)
    {
        var clone = new WorldBlockDefinition {
            Name = block.Name,
            HorizonImage = block.HorizonImage
        };

        if (block.Floor is not null) {
            clone.Floor = new WorldSurface {
                Texture = block.Floor.Texture,
                Height = block.Floor.Height
            };
        }

        if (block.Ceiling is not null) {
            clone.Ceiling = new WorldSurface {
                Texture = block.Ceiling.Texture,
                Height = block.Ceiling.Height
            };
        }

        if (block.Door is not null) {
            clone.Door = CloneDoor(block.Door);
        }

        if (block.Animations is not null) {
            clone.Animations = block.Animations.Select(CloneBlockAnimation).ToList();
        }

        foreach (var wall in block.Walls) {
            clone.Walls.Add(new WorldWallSpan {
                Kind = wall.Kind,
                Texture = wall.Texture,
                FaceTextures = CloneFaceTextures(wall.FaceTextures),
                FacesEnabled = CloneFacesEnabled(wall.FacesEnabled),
                InteriorTexture = wall.InteriorTexture,
                Bottom = wall.Bottom,
                Top = wall.Top,
                Collision = wall.Collision
            });
        }

        return clone;
    }

    private static WorldLayerDefinition? SelectActiveLayer(WorldDocument world)
    {
        if (world.Layers.Count == 0) {
            return null;
        }

        var selected = world.ActiveLayer ?? world.StartLayer;
        if (!string.IsNullOrWhiteSpace(selected)) {
            var layer = world.Layers.FirstOrDefault(
                candidate => string.Equals(candidate.Id, selected, StringComparison.OrdinalIgnoreCase));
            if (layer is not null) {
                return layer;
            }
        }

        return world.Layers[0];
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
            Cells = CloneCells(layer.Cells),
            SpriteInstances = layer.SpriteInstances.Select(CloneSpriteInstance).ToList()
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
                : CloneLayerTransitionTrigger(transition.Trigger),
            WaitSeconds = transition.WaitSeconds,
            TargetPlayerStart = transition.TargetPlayerStart is null
                ? null
                : ClonePlayerStart(transition.TargetPlayerStart)
        };
    }

    private static WorldGameGoal? CloneGameGoal(WorldGameGoal? goal)
    {
        return goal is null ? null : new WorldGameGoal {
            Layer = goal.Layer,
            Row = goal.Row,
            Column = goal.Column,
            RequiredKey = goal.RequiredKey
        };
    }

    private static WorldLayerTransitionTrigger CloneLayerTransitionTrigger(
        WorldLayerTransitionTrigger trigger)
    {
        return new WorldLayerTransitionTrigger {
            BlockId = trigger.BlockId,
            Row = trigger.Row,
            Column = trigger.Column
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

    private static WorldPlayerTurn ClonePlayerTurn(WorldPlayerTurn? playerTurn)
    {
        playerTurn ??= new WorldPlayerTurn();
        return new WorldPlayerTurn {
            BaseDegreesPerSecond = playerTurn.BaseDegreesPerSecond,
            MaxDegreesPerSecond = playerTurn.MaxDegreesPerSecond,
            AccelerationDegreesPerSecondSquared = playerTurn.AccelerationDegreesPerSecondSquared
        };
    }

    private static List<List<string>> CloneCells(List<List<string>> cells)
    {
        return cells.Select(row => row.ToList()).ToList();
    }

    private static EditorSpriteInstance CloneSpriteInstance(EditorSpriteInstance sprite)
    {
        return new EditorSpriteInstance {
            Name = sprite.Name,
            SpriteSet = sprite.SpriteSet,
            XCell = sprite.XCell,
            YCell = sprite.YCell,
            FacingDegrees = sprite.FacingDegrees,
            ScaleCells = sprite.ScaleCells,
            VerticalOffsetCells = sprite.VerticalOffsetCells,
            CollisionRadiusCells = sprite.CollisionRadiusCells,
            Visible = sprite.Visible,
            PassThroughWalls = sprite.PassThroughWalls,
            ChasePlayer = sprite.ChasePlayer,
            SpeedCellsPerSecond = sprite.SpeedCellsPerSecond,
            DetectionRadiusCells = sprite.DetectionRadiusCells,
            PatrolRadiusCells = sprite.PatrolRadiusCells,
            EngagementHysteresisCells = sprite.EngagementHysteresisCells,
            PatrolCircuit = sprite.PatrolCircuit,
            StoppingDistanceCells = sprite.StoppingDistanceCells,
            MaxHealth = sprite.MaxHealth,
            Health = sprite.Health,
            AttackDamage = sprite.AttackDamage,
            RangedAttack = sprite.RangedAttack,
            AttackRangeCells = sprite.AttackRangeCells,
            AttackCooldownSeconds = sprite.AttackCooldownSeconds,
            AttackFovDegrees = sprite.AttackFovDegrees,
            AttackBurstShots = sprite.AttackBurstShots,
            AttackBurstPauseSeconds = sprite.AttackBurstPauseSeconds,
            PickupHealth = sprite.PickupHealth,
            UnlocksMap = sprite.UnlocksMap,
            SavePoint = sprite.SavePoint,
            PickupWeapon = sprite.PickupWeapon,
            Explosive = sprite.Explosive,
            ExplosiveHitPoints = sprite.ExplosiveHitPoints,
            ExplosionRadiusCells = sprite.ExplosionRadiusCells,
            ExplosionDamage = sprite.ExplosionDamage,
            ExplosionScaleCells = sprite.ExplosionScaleCells,
            ExplosionSpriteSet = sprite.ExplosionSpriteSet,
            DestroyedSpriteSet = sprite.DestroyedSpriteSet,
            DestroyedScaleCells = sprite.DestroyedScaleCells,
            DamageResponse = CloneDamageResponse(sprite.DamageResponse)
        };
    }

    private static EditorSpriteDamageResponse? CloneDamageResponse(EditorSpriteDamageResponse? response)
    {
        if (response is null) {
            return null;
        }

        return new EditorSpriteDamageResponse {
            Type = response.Type,
            HitPoints = response.HitPoints,
            EffectSpriteSet = response.EffectSpriteSet,
            EffectAnimation = response.EffectAnimation,
            EffectScaleCells = response.EffectScaleCells,
            DestroyedSpriteSet = response.DestroyedSpriteSet,
            DestroyedScaleCells = response.DestroyedScaleCells,
            Sound = response.Sound,
            RadiusCells = response.RadiusCells,
            Damage = response.Damage
        };
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

    private static Dictionary<string, byte> BuildTextureKeyMap(WorldDocument world, EditorMapDocument map)
    {
        var textureKeys = new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        var nextGeneratedKey = (byte)1;

        foreach (var texture in world.Textures.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)) {
            var key = ParseTextureKey(texture.Key);
            if (key == 0) {
                while (nextGeneratedKey == TransparentTextureKey || map.TextureMap.ContainsKey(nextGeneratedKey)) {
                    ++nextGeneratedKey;
                }

                key = nextGeneratedKey++;
            }

            textureKeys[texture.Key] = key;
            map.TextureMap[key] = TextureName(texture.Value);
        }

        return textureKeys;
    }

    private static byte TextureByte(string? key, IReadOnlyDictionary<string, byte> textureKeys)
    {
        if (string.IsNullOrWhiteSpace(key)) {
            return 0;
        }

        return textureKeys.GetValueOrDefault(key);
    }

    private static byte ParseTextureKey(string key)
    {
        if (byte.TryParse(key, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value)) {
            return value;
        }

        return 0;
    }

    private static string TextureName(WorldTextureDefinition texture)
    {
        return string.IsNullOrWhiteSpace(texture.File)
            ? texture.Name
            : texture.File;
    }

    private static bool HasSupportedImageExtension(string path)
    {
        return path.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".png", StringComparison.OrdinalIgnoreCase);
    }

    private static string TextureKey(byte key)
    {
        return key.ToString("x2", CultureInfo.InvariantCulture);
    }

    private static string AllocateBlockId(
        IDictionary<string, WorldBlockDefinition> blocks,
        ref byte cursor)
    {
        while (cursor != 0xff && blocks.ContainsKey(BlockKey(cursor))) {
            ++cursor;
        }

        if (cursor == 0xff && blocks.ContainsKey(BlockKey(cursor))) {
            throw new InvalidOperationException(
                "World block palette is full (max 256 unique block definitions).");
        }

        return BlockKey(cursor++);
    }

    private static string BlockKey(byte id)
    {
        return id.ToString("x2", CultureInfo.InvariantCulture);
    }

    private static string SuggestBlockName(WorldBlockDefinition block)
    {
        if (block.Walls.Count == 0
            && block.Floor is null
            && block.Ceiling is null
            && string.IsNullOrEmpty(block.HorizonImage)) {
            return "empty";
        }

        var primaryWall = block.Walls.FirstOrDefault(w =>
            string.Equals(w.Kind, "solid", StringComparison.OrdinalIgnoreCase));

        if (primaryWall is not null && block.Walls.Count == 1) {
            return $"wall_{primaryWall.Texture}";
        }

        if (block.Walls.Count > 1) {
            return $"stack_{block.Walls.Count}";
        }

        if (block.Floor is not null || block.Ceiling is not null) {
            return "open";
        }

        return string.Empty;
    }

    /// <summary>
    /// A canonical fingerprint of a block's appearance and behaviour, ignoring its
    /// name. Two blocks with the same signature render and behave identically, which
    /// is what the editor uses both to deduplicate on save and to merge duplicate
    /// palette entries on request.
    /// </summary>
    public static string BlockSignature(WorldBlockDefinition block)
    {
        var parts = new List<string> {
            $"f:{block.Floor?.Texture ?? string.Empty}:{block.Floor?.Height ?? 0}",
            $"c:{block.Ceiling?.Texture ?? string.Empty}:{block.Ceiling?.Height ?? 0}",
            $"h:{block.HorizonImage ?? string.Empty}"
        };

        foreach (var wall in block.Walls) {
            var faceParts = wall.FaceTextures is null
                ? string.Empty
                : string.Join(
                    ',',
                    wall.FaceTextures
                        .OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
                        .Select(entry => $"{entry.Key}:{entry.Value}"));
            var faceEnabledParts = wall.FacesEnabled is null
                ? string.Empty
                : string.Join(
                    ',',
                    wall.FacesEnabled
                        .OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
                        .Select(entry => $"{entry.Key}:{(entry.Value ? 1 : 0)}"));
            parts.Add(
                $"w:{wall.Kind}:{wall.Texture}:{faceParts}:{faceEnabledParts}:{wall.InteriorTexture ?? string.Empty}:{wall.Bottom}:{wall.Top}:{(wall.Collision ? 1 : 0)}");
        }

        if (block.Door is not null) {
            var overlayParts = block.Door.LockedOverlays is null
                ? string.Empty
                : string.Join(
                    ',',
                    block.Door.LockedOverlays
                        .OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
                        .Select(entry => $"{entry.Key}:{entry.Value}"));
            parts.Add("d:"
                + $"{(block.Door.Enabled ? 1 : 0)}:"
                + $"{(block.Door.BlocksWhenClosed ? 1 : 0)}:"
                + $"{block.Door.RequiredKey ?? string.Empty}:"
                + $"{block.Door.TriggerDistanceCells}:"
                + $"{block.Door.OpenTimeSeconds}:"
                + $"{block.Door.CloseDelaySeconds}:"
                + $"{block.Door.OpenSound ?? string.Empty}:"
                + $"{block.Door.OpenSoundVolumePercent}:"
                + $"{overlayParts}:"
                + string.Join(',', block.Door.Frames));
        }

        if (block.Animations is not null) {
            foreach (var animation in block.Animations) {
                parts.Add("a:"
                    + $"{animation.Name}:"
                    + $"{animation.Target}:"
                    + $"{animation.WallIndex?.ToString(CultureInfo.InvariantCulture) ?? string.Empty}:"
                    + $"{animation.Face}:"
                    + $"{animation.FrameDurationMs}:"
                    + $"{(animation.Loop ? 1 : 0)}:"
                    + string.Join(',', animation.Frames));
            }
        }

        return string.Join('|', parts);
    }

    private static string? PrimaryWallTexture(WorldWallSpan wall)
    {
        if (!string.IsNullOrWhiteSpace(wall.Texture)) {
            return wall.Texture;
        }

        if (wall.FaceTextures is null || wall.FaceTextures.Count == 0) {
            return null;
        }

        foreach (var face in new[] { "north", "east", "south", "west" }) {
            if (wall.FaceTextures.TryGetValue(face, out var texture)
                && !string.IsNullOrWhiteSpace(texture)) {
                return texture;
            }
        }

        return wall.FaceTextures.Values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }

    private static Dictionary<string, string>? CloneFaceTextures(Dictionary<string, string>? faceTextures)
    {
        return faceTextures is null
            ? null
            : new Dictionary<string, string>(faceTextures, StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, bool>? CloneFacesEnabled(Dictionary<string, bool>? facesEnabled)
    {
        return facesEnabled is null
            ? null
            : new Dictionary<string, bool>(facesEnabled, StringComparer.OrdinalIgnoreCase);
    }

    private static WorldDoorDefinition CloneDoor(WorldDoorDefinition door)
    {
        return new WorldDoorDefinition {
            Enabled = door.Enabled,
            BlocksWhenClosed = door.BlocksWhenClosed,
            RequiredKey = door.RequiredKey,
            TriggerDistanceCells = door.TriggerDistanceCells,
            OpenTimeSeconds = door.OpenTimeSeconds,
            CloseDelaySeconds = door.CloseDelaySeconds,
            OpenSound = door.OpenSound,
            OpenSoundVolumePercent = door.OpenSoundVolumePercent,
            Frames = [..door.Frames],
            LockedOverlays = door.LockedOverlays is null
                ? null
                : new Dictionary<string, string>(door.LockedOverlays, StringComparer.OrdinalIgnoreCase)
        };
    }

    private static WorldBlockAnimationDefinition CloneBlockAnimation(WorldBlockAnimationDefinition animation)
    {
        return new WorldBlockAnimationDefinition {
            Name = animation.Name,
            Target = animation.Target,
            WallIndex = animation.WallIndex,
            Face = animation.Face,
            FrameDurationMs = animation.FrameDurationMs,
            Loop = animation.Loop,
            Frames = [..animation.Frames]
        };
    }
}
