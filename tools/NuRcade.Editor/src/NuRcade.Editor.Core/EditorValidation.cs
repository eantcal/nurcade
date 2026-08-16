namespace NuRcade.Editor.Core;

public static class EditorValidation
{
    public static IReadOnlyList<string> Validate(EditorMapDocument document)
    {
        return Validate(document, worldFilePath: null);
    }

    public static IReadOnlyList<string> Validate(EditorMapDocument document, string? worldFilePath)
    {
        var errors = new List<string>();

        if (document.RowCount == 0 || document.ColumnCount == 0) {
            errors.Add("Map must contain at least one cell.");
            return errors;
        }

        foreach (var row in document.Rows) {
            if (row.Count != document.ColumnCount) {
                errors.Add("All map rows must have the same width.");
            }
        }

        foreach (var row in document.Rows) {
            foreach (var cell in row) {
                ValidateTexture(cell.Fields.SolidWallTexture, document, errors, "solid wall", cell);
                ValidateTexture(cell.Fields.CeilingTexture, document, errors, "ceiling", cell);
                ValidateTexture(cell.Fields.FloorTexture, document, errors, "floor", cell);
                ValidateTexture(cell.Fields.TransparentWallTexture, document, errors, "transparent wall", cell);
                ValidateTexture(cell.Fields.UpperWallTexture, document, errors, "upper wall", cell);
            }
        }

        if (document.PlayerStart.XCell < 0 || document.PlayerStart.YCell < 0
            || document.PlayerStart.XCell >= document.ColumnCount
            || document.PlayerStart.YCell >= document.RowCount) {
            errors.Add("Player start is outside the map bounds.");
        }
        else {
            var playerCell = document.CellAt(
                (int)Math.Floor(document.PlayerStart.YCell),
                (int)Math.Floor(document.PlayerStart.XCell));
            if (playerCell is not null && playerCell.Fields.HasSolidWall) {
                errors.Add("Player start is inside a solid wall cell.");
            }
        }

        if (document.PlayerStats.MaxHealth <= 0.0) {
            errors.Add("Player max health must be positive.");
        }

        if (document.PlayerStats.Health < 0.0
            || document.PlayerStats.Health > document.PlayerStats.MaxHealth) {
            errors.Add("Player health must stay between zero and max health.");
        }

        if (!string.IsNullOrWhiteSpace(worldFilePath)) {
            foreach (var texture in TexturePaletteBuilder.Build(document, worldFilePath)) {
                if (!texture.Exists) {
                    errors.Add($"Texture 0x{texture.Key:x2} references missing image file: {texture.RelativePath}.");
                }
            }

            ValidateDefaultHorizonImage(document, worldFilePath, errors);
        }

        var spriteNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sprite in document.SpriteInstances) {
            if (string.IsNullOrWhiteSpace(sprite.Name)) {
                errors.Add("Sprite instance name cannot be empty.");
            }
            else if (!spriteNames.Add(sprite.Name)) {
                errors.Add($"Duplicate sprite instance name: {sprite.Name}.");
            }

            if (sprite.XCell < 0 || sprite.YCell < 0
                || sprite.XCell >= document.ColumnCount
                || sprite.YCell >= document.RowCount) {
                errors.Add($"Sprite '{sprite.Name}' is outside the map bounds.");
            }

            if (sprite.ScaleCells <= 0.0) {
                errors.Add($"Sprite '{sprite.Name}' scale must be positive.");
            }

            if (sprite.CollisionRadiusCells < 0.0) {
                errors.Add($"Sprite '{sprite.Name}' collision radius cannot be negative.");
            }

            if (sprite.MaxHealth < 0.0) {
                errors.Add($"Sprite '{sprite.Name}' max health cannot be negative.");
            }

            if (sprite.Health < 0.0
                || (sprite.MaxHealth > 0.0 && sprite.Health > sprite.MaxHealth)) {
                errors.Add($"Sprite '{sprite.Name}' health must stay between zero and max health.");
            }

            if (sprite.AttackDamage < 0.0) {
                errors.Add($"Sprite '{sprite.Name}' attack damage cannot be negative.");
            }

            var cell = document.CellAt((int)Math.Floor(sprite.YCell), (int)Math.Floor(sprite.XCell));
            if (cell is not null
                && cell.Fields.HasSolidWall
                && !sprite.PassThroughWalls) {
                errors.Add($"Sprite '{sprite.Name}' is inside a solid wall cell.");
            }
        }

        return errors;
    }

    private static void ValidateTexture(
        byte texture,
        EditorMapDocument document,
        List<string> errors,
        string fieldName,
        EditorMapCell cell)
    {
        if (texture == 0 || texture == 0xff) {
            return;
        }

        if (!document.TextureMap.ContainsKey(texture)) {
            errors.Add($"Cell ({cell.Row},{cell.Column}) references missing {fieldName} texture 0x{texture:x2}.");
        }
    }

    private static void ValidateDefaultHorizonImage(
        EditorMapDocument document,
        string worldFilePath,
        List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(document.DefaultHorizonImage)) {
            return;
        }

        var worldDirectory =
            Path.GetDirectoryName(Path.GetFullPath(worldFilePath)) ?? Environment.CurrentDirectory;
        var relativePath = ResolveImageRelativePath(worldDirectory, document.DefaultHorizonImage);
        var fullPath = Path.GetFullPath(Path.Combine(worldDirectory, relativePath));
        if (!File.Exists(fullPath)) {
            errors.Add($"Default horizon image references missing image file: {relativePath}.");
        }
    }

    private static bool HasSupportedImageExtension(string path)
    {
        return path.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".png", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveImageRelativePath(string worldDirectory, string name)
    {
        if (HasSupportedImageExtension(name)) {
            return name;
        }

        var pngPath = $"{name}.png";
        if (File.Exists(Path.GetFullPath(Path.Combine(worldDirectory, pngPath)))) {
            return pngPath;
        }

        return $"{name}.bmp";
    }
}
