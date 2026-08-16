using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using NuRcade.Editor.Core;

namespace NuRcade.Editor;

public sealed record WorldPreview3DScene(
    Model3DGroup Model,
    string Summary,
    IReadOnlyDictionary<Model3D, WorldPreview3DHitTarget> HitTargets);

public sealed record WorldPreview3DLayers
{
    public bool ShowGrid { get; init; } = true;
    public bool ShowFloors { get; init; } = true;
    public bool ShowCeilings { get; init; } = true;
    public bool ShowWalls { get; init; } = true;
    public bool ShowSprites { get; init; } = true;
    public bool ShowPlayer { get; init; } = true;
}

public enum WorldPreview3DHitKind
{
    Cell,
    Sprite,
    Player
}

public sealed record WorldPreview3DHitTarget(
    WorldPreview3DHitKind Kind,
    int Row,
    int Column,
    EditorSpriteInstance? Sprite,
    string? Face = null,
    int WallSpanIndex = -1)
{
    public static WorldPreview3DHitTarget Cell(int row, int column, string? face = null, int wallSpanIndex = -1)
    {
        return new WorldPreview3DHitTarget(WorldPreview3DHitKind.Cell, row, column, null, face, wallSpanIndex);
    }

    public static WorldPreview3DHitTarget SpriteInstance(EditorSpriteInstance sprite)
    {
        return new WorldPreview3DHitTarget(
            WorldPreview3DHitKind.Sprite,
            (int)Math.Floor(sprite.YCell),
            (int)Math.Floor(sprite.XCell),
            sprite,
            null,
            -1);
    }

    public static WorldPreview3DHitTarget PlayerMarker(int row, int column)
    {
        return new WorldPreview3DHitTarget(WorldPreview3DHitKind.Player, row, column, null, null, -1);
    }
}

public static class WorldPreview3DBuilder
{
    private const double CellSize = 1.0;
    private const double WallInset = 0.0;
    private const double GridLineWidth = 0.015;

    public static WorldPreview3DScene Build(
        EditorMapDocument? document,
        string? assetBasePath = null,
        IReadOnlyDictionary<string, ImageSource?>? spritePreviews = null,
        Vector3D? cameraLookDirection = null,
        WorldPreview3DLayers? layers = null,
        int? selectedRow = null,
        int? selectedColumn = null,
        EditorSpriteInstance? selectedSprite = null)
    {
        var model = new Model3DGroup();
        var hitTargets = new Dictionary<Model3D, WorldPreview3DHitTarget>();
        AddLights(model);

        if (document is null || document.RowCount == 0 || document.ColumnCount == 0) {
            return new WorldPreview3DScene(model, "No map loaded", hitTargets);
        }

        layers ??= new WorldPreview3DLayers();
        var defaultWallHeight = Math.Max(1, document.CellHeight);
        var materials = new PreviewMaterialLibrary(document, assetBasePath);
        var wallCount = 0;
        var floorCount = 0;
        var ceilingCount = 0;
        var spriteBillboardCount = 0;
        var selectionHighlightCount = 0;

        if (layers.ShowGrid) {
            var gridMaterial = Material(Color.FromArgb(150, 54, 61, 66));
            AddGrid(model, document.RowCount, document.ColumnCount, gridMaterial);
        }

        for (var row = 0; row < document.RowCount; ++row) {
            for (var column = 0; column < document.ColumnCount; ++column) {
                var cell = document.Rows[row][column];
                var block = ResolveBlock(document, cell, defaultWallHeight);
                var x0 = column * CellSize;
                var x1 = x0 + CellSize;
                var z0 = row * CellSize;
                var z1 = z0 + CellSize;

                var cellTarget = WorldPreview3DHitTarget.Cell(row, column);
                if (layers.ShowFloors) {
                    hitTargets[AddFloor(model, block, cell, x0, x1, z0, z1, defaultWallHeight, materials)]
                        = WorldPreview3DHitTarget.Cell(row, column, "floor");
                    ++floorCount;
                }

                if (layers.ShowCeilings && (block.Ceiling is not null || cell.Fields.CeilingTexture != 0)) {
                    var height = NormalizeHeight(block.Ceiling?.Height ?? defaultWallHeight, defaultWallHeight);
                    var material = materials.Surface(
                        block.Ceiling?.Texture,
                        cell.Fields.CeilingTexture,
                        Color.FromArgb(105, 150, 180, 190),
                        0.82);
                    hitTargets[AddQuad(
                        model,
                        new Point3D(x0, height, z0),
                        new Point3D(x1, height, z0),
                        new Point3D(x1, height, z1),
                        new Point3D(x0, height, z1),
                        material)] = WorldPreview3DHitTarget.Cell(row, column, "ceiling");
                    ++ceilingCount;
                }

                if (layers.ShowWalls) {
                    var spanIndex = 0;
                    foreach (var wall in block.Walls) {
                        var bottom = NormalizeHeight(wall.Bottom, defaultWallHeight);
                        var top = NormalizeHeight(wall.Top, defaultWallHeight);
                        if (top <= bottom) {
                            top = bottom + 1.0;
                        }

                        var boxFaces = AddWallSpanBox(
                            model,
                            x0 + WallInset,
                            x1 - WallInset,
                            bottom,
                            top,
                            z0 + WallInset,
                            z1 - WallInset,
                            block,
                            wall,
                            spanIndex,
                            materials,
                            out var faceNames);
                        for (var f = 0; f < boxFaces.Count; ++f) {
                            hitTargets[boxFaces[f]] = WorldPreview3DHitTarget.Cell(row, column, faceNames[f], spanIndex);
                        }
                        ++wallCount;
                        ++spanIndex;
                    }
                }
            }
        }

        var billboardRight = BillboardRightVector(cameraLookDirection);
        if (layers.ShowSprites) {
            foreach (var sprite in document.SpriteInstances.Where(sprite => sprite.Visible)) {
                var spriteTarget = WorldPreview3DHitTarget.SpriteInstance(sprite);
                foreach (var face in AddSpriteMarker(model, sprite, materials, spritePreviews, billboardRight)) {
                    hitTargets[face] = spriteTarget;
                }
                ++spriteBillboardCount;
            }
        }

        if (layers.ShowPlayer) {
            var playerRow = (int)Math.Floor(document.PlayerStart.YCell);
            var playerCol = (int)Math.Floor(document.PlayerStart.XCell);
            var playerTarget = WorldPreview3DHitTarget.PlayerMarker(playerRow, playerCol);
            foreach (var geom in AddPlayerMarker(model, document.PlayerStart)) {
                hitTargets[geom] = playerTarget;
            }
        }

        AddCardinalDirectionMarkers(model, document.RowCount, document.ColumnCount, billboardRight);

        if (selectedRow.HasValue
            && selectedColumn.HasValue
            && selectedRow.Value >= 0
            && selectedColumn.Value >= 0
            && selectedRow.Value < document.RowCount
            && selectedColumn.Value < document.ColumnCount) {
            var cellTarget = WorldPreview3DHitTarget.Cell(selectedRow.Value, selectedColumn.Value);
            foreach (var face in AddSelectedCellHighlight(model, selectedRow.Value, selectedColumn.Value)) {
                hitTargets[face] = cellTarget;
            }

            ++selectionHighlightCount;
        }

        if (layers.ShowSprites && selectedSprite is { Visible: true }) {
            var spriteTarget = WorldPreview3DHitTarget.SpriteInstance(selectedSprite);
            foreach (var face in AddSelectedSpriteHighlight(model, selectedSprite, billboardRight)) {
                hitTargets[face] = spriteTarget;
            }

            ++selectionHighlightCount;
        }

        return new WorldPreview3DScene(
            model,
            $"{document.ColumnCount} x {document.RowCount} cells, {floorCount} floor cell(s), {wallCount} wall span(s), "
                + $"{ceilingCount} ceiling cell(s), {spriteBillboardCount} sprite billboard(s), "
                + $"{materials.ResolvedTextureCount} textured material(s), "
                + $"{selectionHighlightCount} selection highlight(s)",
            hitTargets);
    }

    private static void AddLights(Model3DGroup model)
    {
        model.Children.Add(new AmbientLight(Color.FromRgb(112, 112, 112)));
        model.Children.Add(new DirectionalLight(
            Color.FromRgb(230, 230, 220),
            new Vector3D(-0.45, -0.85, -0.35)));
        model.Children.Add(new DirectionalLight(
            Color.FromRgb(110, 130, 160),
            new Vector3D(0.7, -0.35, 0.6)));
    }

    private static void AddCardinalDirectionMarkers(
        Model3DGroup model,
        int rowCount,
        int columnCount,
        Vector3D billboardRight)
    {
        if (rowCount <= 0 || columnCount <= 0) {
            return;
        }

        var halfX = columnCount * CellSize * 0.5;
        var halfZ = rowCount * CellSize * 0.5;
        var arrowDistance = Math.Max(halfX, halfZ) + 1.6;
        var arrowY = 0.05;

        // Z-axis convention used by builder: row 0 -> z=0 (north), row N -> z=N (south)
        // Colors: North = red, South = blue, East = green, West = yellow
        var north = Color.FromRgb(220, 60, 90);
        var south = Color.FromRgb(70, 130, 230);
        var east = Color.FromRgb(80, 200, 110);
        var west = Color.FromRgb(240, 200, 60);

        var northTip = new Point3D(halfX, arrowY, -arrowDistance + halfZ);
        var southTip = new Point3D(halfX, arrowY, arrowDistance + halfZ);
        var eastTip = new Point3D(arrowDistance + halfX, arrowY, halfZ);
        var westTip = new Point3D(-arrowDistance + halfX, arrowY, halfZ);

        AddDirectionArrow(model, northTip, new Vector3D(0, 0, -1), north);
        AddDirectionArrow(model, southTip, new Vector3D(0, 0, 1), south);
        AddDirectionArrow(model, eastTip, new Vector3D(1, 0, 0), east);
        AddDirectionArrow(model, westTip, new Vector3D(-1, 0, 0), west);

        // A labelled, camera-facing badge next to each arrow tip so the direction is
        // identifiable by letter, not colour alone.
        AddDirectionLabel(model, northTip, new Vector3D(0, 0, -1), "N", north, billboardRight);
        AddDirectionLabel(model, southTip, new Vector3D(0, 0, 1), "S", south, billboardRight);
        AddDirectionLabel(model, eastTip, new Vector3D(1, 0, 0), "E", east, billboardRight);
        AddDirectionLabel(model, westTip, new Vector3D(-1, 0, 0), "W", west, billboardRight);
    }

    private static void AddDirectionLabel(
        Model3DGroup model,
        Point3D tip,
        Vector3D direction,
        string letter,
        Color color,
        Vector3D billboardRight)
    {
        direction.Normalize();
        var half = 0.42;
        var center = tip + direction * 0.6;

        var right = billboardRight;
        if (right.Length < 0.001) {
            right = new Vector3D(1, 0, 0);
        }

        right.Normalize();

        // billboardRight points to screen-left in this right-handed scene, so the
        // glyph quad would face away and read mirrored. Flip it so the textured
        // front face turns toward the camera and the letter is not reversed.
        right *= -half;
        var up = new Vector3D(0, half, 0);
        var centerPoint = new Point3D(center.X, 0.05 + half, center.Z);

        var bottomLeft = centerPoint - right - up;
        var bottomRight = centerPoint + right - up;
        var topRight = centerPoint + right + up;
        var topLeft = centerPoint - right + up;

        var brush = new ImageBrush(CreateLabelTexture(letter, color)) {
            Stretch = Stretch.Uniform
        };
        brush.Freeze();

        // Diffuse honours the badge's transparent corners (like sprite billboards);
        // emissive keeps the glyph bright and readable under any scene lighting.
        var material = new MaterialGroup();
        material.Children.Add(new DiffuseMaterial(brush));
        material.Children.Add(new EmissiveMaterial(brush));
        material.Freeze();

        AddQuad(model, bottomLeft, bottomRight, topRight, topLeft, material);
    }

    private static ImageSource CreateLabelTexture(string letter, Color color)
    {
        const int size = 96;
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen()) {
            // Dark, opaque badge with a bright white glyph for maximum legibility;
            // the rim keeps the per-direction colour for quick association.
            var background = new SolidColorBrush(Color.FromRgb(26, 28, 34));
            background.Freeze();
            var rim = new SolidColorBrush(color);
            rim.Freeze();
            var border = new Pen(rim, 10.0);
            border.Freeze();
            context.DrawRoundedRectangle(background, border, new Rect(7, 7, size - 14, size - 14), 16, 16);

            var typeface = new Typeface(
                new FontFamily("Segoe UI"),
                FontStyles.Normal,
                FontWeights.Bold,
                FontStretches.Normal);
            var text = new FormattedText(
                letter,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                typeface,
                64.0,
                Brushes.White,
                1.0) {
                TextAlignment = TextAlignment.Center
            };
            var origin = new Point(size / 2.0, (size - text.Height) / 2.0);
            context.DrawText(text, origin);
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static void AddDirectionArrow(
        Model3DGroup model,
        Point3D tip,
        Vector3D direction,
        Color color)
    {
        direction.Normalize();
        var shaftLength = 1.4;
        var shaftHalfWidth = 0.12;
        var headLength = 0.5;
        var headHalfWidth = 0.35;

        // Cross direction perpendicular to direction on XZ plane
        var cross = new Vector3D(-direction.Z, 0, direction.X);
        cross.Normalize();

        var material = Material(color);

        // Shaft: a flat quad on the ground from tip-direction*(shaftLength+headLength) toward tip-direction*headLength
        var shaftBack = tip - direction * (shaftLength + headLength);
        var shaftFront = tip - direction * headLength;
        var shaftBackLeft = shaftBack - cross * shaftHalfWidth;
        var shaftBackRight = shaftBack + cross * shaftHalfWidth;
        var shaftFrontLeft = shaftFront - cross * shaftHalfWidth;
        var shaftFrontRight = shaftFront + cross * shaftHalfWidth;

        AddQuad(model, shaftBackLeft, shaftBackRight, shaftFrontRight, shaftFrontLeft, material);

        // Arrow head: triangle
        var headLeft = shaftFront - cross * headHalfWidth;
        var headRight = shaftFront + cross * headHalfWidth;
        AddTriangle(model, headLeft, headRight, tip, material);
    }

    private static GeometryModel3D AddFloor(
        Model3DGroup model,
        WorldBlockDefinition block,
        EditorMapCell cell,
        double x0,
        double x1,
        double z0,
        double z1,
        int defaultWallHeight,
        PreviewMaterialLibrary materials)
    {
        var height = NormalizeHeight(block.Floor?.Height ?? 0, defaultWallHeight);
        var material = materials.Surface(
            block.Floor?.Texture,
            cell.Fields.FloorTexture,
            Color.FromArgb(155, 82, 91, 82),
            0.96);

        return AddQuad(
            model,
            new Point3D(x0, height, z0),
            new Point3D(x0, height, z1),
            new Point3D(x1, height, z1),
            new Point3D(x1, height, z0),
            material);
    }

    private static void AddGrid(Model3DGroup model, int rows, int columns, Material material)
    {
        for (var column = 0; column <= columns; ++column) {
            var x = column * CellSize;
            AddBox(
                model,
                x - GridLineWidth * 0.5,
                x + GridLineWidth * 0.5,
                0.003,
                0.01,
                0,
                rows * CellSize,
                material);
        }

        for (var row = 0; row <= rows; ++row) {
            var z = row * CellSize;
            AddBox(
                model,
                0,
                columns * CellSize,
                0.003,
                0.01,
                z - GridLineWidth * 0.5,
                z + GridLineWidth * 0.5,
                material);
        }
    }

    private static List<GeometryModel3D> AddSpriteMarker(
        Model3DGroup model,
        EditorSpriteInstance sprite,
        PreviewMaterialLibrary materials,
        IReadOnlyDictionary<string, ImageSource?>? spritePreviews,
        Vector3D billboardRight)
    {
        var x = sprite.XCell;
        var z = sprite.YCell;
        var height = Math.Clamp(sprite.ScaleCells, 0.25, 3.0);
        var halfWidth = height * 0.42;
        var bottom = 0.02;
        var top = Math.Max(0.35, height);
        var preview = spritePreviews is not null
            && spritePreviews.TryGetValue(sprite.SpriteSet, out var image)
                ? image
                : null;
        var material = materials.Sprite(preview, Color.FromArgb(220, 232, 82, 64), 1.0);
        var shadow = Material(Color.FromArgb(95, 15, 20, 18));
        var right = billboardRight * halfWidth;

        return [
            AddQuad(
            model,
            new Point3D(x - right.X, bottom, z - right.Z),
            new Point3D(x + right.X, bottom, z + right.Z),
            new Point3D(x + right.X, top, z + right.Z),
            new Point3D(x - right.X, top, z - right.Z),
            material),
            AddQuad(
            model,
            new Point3D(x - halfWidth, 0.012, z - halfWidth),
            new Point3D(x - halfWidth, 0.012, z + halfWidth),
            new Point3D(x + halfWidth, 0.012, z + halfWidth),
            new Point3D(x + halfWidth, 0.012, z - halfWidth),
            shadow)
        ];
    }

    private static Vector3D BillboardRightVector(Vector3D? cameraLookDirection)
    {
        var look = cameraLookDirection ?? new Vector3D(0.0, 0.0, 1.0);
        look.Y = 0.0;
        if (look.Length < 0.001) {
            return new Vector3D(1.0, 0.0, 0.0);
        }

        look.Normalize();
        return new Vector3D(look.Z, 0.0, -look.X);
    }

    private static List<GeometryModel3D> AddPlayerMarker(Model3DGroup model, WorldPlayerStart playerStart)
    {
        var geometries = new List<GeometryModel3D>();
        var x = playerStart.XCell;
        var z = playerStart.YCell;
        var bodyMaterial = Material(Color.FromArgb(235, 40, 96, 210));
        geometries.AddRange(AddBox(
            model,
            x - 0.12,
            x + 0.12,
            0.02,
            0.34,
            z - 0.12,
            z + 0.12,
            bodyMaterial));

        var radians = playerStart.FacingDegrees * Math.PI / 180.0;
        var dx = Math.Cos(radians);
        var dz = Math.Sin(radians);
        var sideX = -dz * 0.11;
        var sideZ = dx * 0.11;
        var tip = new Point3D(x + dx * 0.42, 0.12, z + dz * 0.42);
        var left = new Point3D(x + sideX, 0.12, z + sideZ);
        var right = new Point3D(x - sideX, 0.12, z - sideZ);
        geometries.Add(AddTriangle(model, tip, left, right, Material(Color.FromArgb(245, 255, 221, 66))));
        return geometries;
    }

    private static List<GeometryModel3D> AddSelectedCellHighlight(Model3DGroup model, int row, int column)
    {
        var x0 = column * CellSize;
        var x1 = x0 + CellSize;
        var z0 = row * CellSize;
        var z1 = z0 + CellSize;
        var y0 = 0.026;
        var y1 = 0.055;
        var material = HighlightMaterial(Color.FromRgb(255, 221, 0));

        return [
            .. AddBox(model, x0, x1, y0, y1, z0, z0 + 0.035, material),
            .. AddBox(model, x0, x1, y0, y1, z1 - 0.035, z1, material),
            .. AddBox(model, x0, x0 + 0.035, y0, y1, z0, z1, material),
            .. AddBox(model, x1 - 0.035, x1, y0, y1, z0, z1, material)
        ];
    }

    private static List<GeometryModel3D> AddSelectedSpriteHighlight(
        Model3DGroup model,
        EditorSpriteInstance sprite,
        Vector3D billboardRight)
    {
        var x = sprite.XCell;
        var z = sprite.YCell;
        var height = Math.Clamp(sprite.ScaleCells, 0.25, 3.0);
        var halfWidth = height * 0.5;
        var top = Math.Max(0.35, height) + 0.08;
        var bottom = 0.01;
        var right = billboardRight * halfWidth;
        var material = HighlightMaterial(Color.FromRgb(255, 245, 82));

        return [
            AddQuad(
                model,
                new Point3D(x - right.X, bottom, z - right.Z),
                new Point3D(x + right.X, bottom, z + right.Z),
                new Point3D(x + right.X, bottom + 0.04, z + right.Z),
                new Point3D(x - right.X, bottom + 0.04, z - right.Z),
                material),
            AddQuad(
                model,
                new Point3D(x - right.X, top - 0.04, z - right.Z),
                new Point3D(x + right.X, top - 0.04, z + right.Z),
                new Point3D(x + right.X, top, z + right.Z),
                new Point3D(x - right.X, top, z - right.Z),
                material),
            AddQuad(
                model,
                new Point3D(x - right.X, bottom, z - right.Z),
                new Point3D(x - right.X * 0.88, bottom, z - right.Z * 0.88),
                new Point3D(x - right.X * 0.88, top, z - right.Z * 0.88),
                new Point3D(x - right.X, top, z - right.Z),
                material),
            AddQuad(
                model,
                new Point3D(x + right.X * 0.88, bottom, z + right.Z * 0.88),
                new Point3D(x + right.X, bottom, z + right.Z),
                new Point3D(x + right.X, top, z + right.Z),
                new Point3D(x + right.X * 0.88, top, z + right.Z * 0.88),
                material)
        ];
    }

    private static WorldBlockDefinition ResolveBlock(
        EditorMapDocument document,
        EditorMapCell cell,
        int defaultWallHeight)
    {
        if (!string.IsNullOrWhiteSpace(cell.BlockId)
            && document.Blocks.TryGetValue(cell.BlockId, out var block)) {
            return block;
        }

        var fallback = new WorldBlockDefinition();
        if (cell.Fields.FloorTexture != 0) {
            fallback.Floor = new WorldSurface {
                Texture = TextureKey(cell.Fields.FloorTexture),
                Height = 0
            };
        }

        if (cell.Fields.CeilingTexture != 0) {
            fallback.Ceiling = new WorldSurface {
                Texture = TextureKey(cell.Fields.CeilingTexture),
                Height = defaultWallHeight
            };
        }

        if (cell.Fields.SolidWallTexture != 0) {
            fallback.Walls.Add(new WorldWallSpan {
                Kind = "solid",
                Texture = TextureKey(cell.Fields.SolidWallTexture),
                Bottom = 0,
                Top = defaultWallHeight,
                Collision = true
            });
        }

        if (cell.Fields.UpperWallTexture != 0) {
            fallback.Walls.Add(new WorldWallSpan {
                Kind = "solid",
                Texture = TextureKey(cell.Fields.UpperWallTexture),
                Bottom = defaultWallHeight,
                Top = defaultWallHeight * 2,
                Collision = true
            });
        }

        if (cell.Fields.TransparentWallTexture != 0) {
            fallback.Walls.Add(new WorldWallSpan {
                Kind = "transparent",
                Texture = TextureKey(cell.Fields.TransparentWallTexture),
                Bottom = 0,
                Top = defaultWallHeight,
                Collision = false
            });
        }

        return fallback;
    }

    private static List<GeometryModel3D> AddWallSpanBox(
        Model3DGroup model,
        double x0,
        double x1,
        double y0,
        double y1,
        double z0,
        double z1,
        WorldBlockDefinition block,
        WorldWallSpan wall,
        int wallIndex,
        PreviewMaterialLibrary materials,
        out IReadOnlyList<string> faceNames)
    {
        var faces = new List<GeometryModel3D>(6);
        var names = new List<string>(6);

        void AddFace(string name, Point3D p0, Point3D p1, Point3D p2, Point3D p3)
        {
            if (!WallFaceEnabled(wall, name)) {
                return;
            }

            faces.Add(AddQuad(model, p0, p1, p2, p3, WallMaterial(block, wall, wallIndex, name, materials)));
            names.Add(name);
        }

        AddFace("north", new Point3D(x0, y0, z0), new Point3D(x1, y0, z0), new Point3D(x1, y1, z0), new Point3D(x0, y1, z0));
        AddFace("south", new Point3D(x1, y0, z1), new Point3D(x0, y0, z1), new Point3D(x0, y1, z1), new Point3D(x1, y1, z1));
        AddFace("west", new Point3D(x0, y0, z1), new Point3D(x0, y0, z0), new Point3D(x0, y1, z0), new Point3D(x0, y1, z1));
        AddFace("east", new Point3D(x1, y0, z0), new Point3D(x1, y0, z1), new Point3D(x1, y1, z1), new Point3D(x1, y1, z0));

        faces.Add(AddQuad(model, new Point3D(x0, y1, z0), new Point3D(x1, y1, z0), new Point3D(x1, y1, z1), new Point3D(x0, y1, z1), WallMaterial(block, wall, wallIndex, null, materials)));
        names.Add("wall_top");
        faces.Add(AddQuad(model, new Point3D(x0, y0, z1), new Point3D(x1, y0, z1), new Point3D(x1, y0, z0), new Point3D(x0, y0, z0), WallMaterial(block, wall, wallIndex, null, materials)));
        names.Add("wall_bottom");

        faceNames = names;
        return faces;
    }

    private static List<GeometryModel3D> AddBox(
        Model3DGroup model,
        double x0,
        double x1,
        double y0,
        double y1,
        double z0,
        double z1,
        Material material)
    {
        return [
            AddQuad(model, new Point3D(x0, y0, z0), new Point3D(x1, y0, z0), new Point3D(x1, y1, z0), new Point3D(x0, y1, z0), material),
            AddQuad(model, new Point3D(x1, y0, z1), new Point3D(x0, y0, z1), new Point3D(x0, y1, z1), new Point3D(x1, y1, z1), material),
            AddQuad(model, new Point3D(x0, y0, z1), new Point3D(x0, y0, z0), new Point3D(x0, y1, z0), new Point3D(x0, y1, z1), material),
            AddQuad(model, new Point3D(x1, y0, z0), new Point3D(x1, y0, z1), new Point3D(x1, y1, z1), new Point3D(x1, y1, z0), material),
            AddQuad(model, new Point3D(x0, y1, z0), new Point3D(x1, y1, z0), new Point3D(x1, y1, z1), new Point3D(x0, y1, z1), material),
            AddQuad(model, new Point3D(x0, y0, z1), new Point3D(x1, y0, z1), new Point3D(x1, y0, z0), new Point3D(x0, y0, z0), material)
        ];
    }

    private static GeometryModel3D AddQuad(
        Model3DGroup model,
        Point3D p0,
        Point3D p1,
        Point3D p2,
        Point3D p3,
        Material material)
    {
        var mesh = new MeshGeometry3D {
            Positions = new Point3DCollection([p0, p1, p2, p3]),
            TextureCoordinates = new PointCollection([
                new Point(0.0, 1.0),
                new Point(1.0, 1.0),
                new Point(1.0, 0.0),
                new Point(0.0, 0.0)
            ]),
            TriangleIndices = new Int32Collection([0, 1, 2, 0, 2, 3])
        };
        var geometry = new GeometryModel3D(mesh, material) {
            BackMaterial = material
        };
        model.Children.Add(geometry);
        return geometry;
    }

    private static GeometryModel3D AddTriangle(
        Model3DGroup model,
        Point3D p0,
        Point3D p1,
        Point3D p2,
        Material material)
    {
        var mesh = new MeshGeometry3D {
            Positions = new Point3DCollection([p0, p1, p2]),
            TriangleIndices = new Int32Collection([0, 1, 2])
        };
        var geometry = new GeometryModel3D(mesh, material) {
            BackMaterial = material
        };
        model.Children.Add(geometry);
        return geometry;
    }

    private static Material Material(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        var material = new DiffuseMaterial(brush);
        material.Freeze();
        return material;
    }

    private static Material HighlightMaterial(Color color)
    {
        var material = new MaterialGroup();
        var diffuseBrush = new SolidColorBrush(Color.FromArgb(190, color.R, color.G, color.B));
        diffuseBrush.Freeze();
        var glowBrush = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
        glowBrush.Freeze();
        material.Children.Add(new DiffuseMaterial(diffuseBrush));
        material.Children.Add(new EmissiveMaterial(glowBrush));
        material.Freeze();
        return material;
    }

    private static bool IsTransparentWall(WorldWallSpan wall)
    {
        return string.Equals(wall.Kind, "transparent", StringComparison.OrdinalIgnoreCase);
    }

    private static bool WallFaceEnabled(WorldWallSpan wall, string face)
    {
        return wall.FacesEnabled is null
            || !wall.FacesEnabled.TryGetValue(face, out var enabled)
            || enabled;
    }

    private static Material WallMaterial(
        WorldBlockDefinition block,
        WorldWallSpan wall,
        int wallIndex,
        string? face,
        PreviewMaterialLibrary materials)
    {
        var transparent = IsTransparentWall(wall);
        var texture = PreviewWallTexture(wall, face);
        var color = TextureColor(texture, transparent ? (byte)135 : (byte)235);
        return materials.TextureWithOverlay(
            texture,
            AnimatedWallOverlayTexture(block, wallIndex, face) ?? LockedDoorOverlayTexture(block),
            color,
            transparent ? 0.58 : 1.0);
    }

    private static string? AnimatedWallOverlayTexture(
        WorldBlockDefinition block,
        int wallIndex,
        string? face)
    {
        if (face is null || block.Animations is null) {
            return null;
        }

        WorldBlockAnimationDefinition? allFaces = null;
        foreach (var animation in block.Animations) {
            if (!TargetsWallOverlay(animation.Target)
                || animation.WallIndex != wallIndex
                || animation.Frames.Count == 0) {
                continue;
            }

            if (string.Equals(animation.Face, face, StringComparison.OrdinalIgnoreCase)) {
                return animation.Frames.FirstOrDefault(frame => !string.IsNullOrWhiteSpace(frame));
            }

            if (string.Equals(animation.Face, "all", StringComparison.OrdinalIgnoreCase)) {
                allFaces ??= animation;
            }
        }

        return allFaces?.Frames.FirstOrDefault(frame => !string.IsNullOrWhiteSpace(frame));
    }

    private static bool TargetsWallOverlay(string target)
    {
        return string.Equals(target, "wallOverlay", StringComparison.OrdinalIgnoreCase)
            || string.Equals(target, "overlay", StringComparison.OrdinalIgnoreCase);
    }

    private static string? LockedDoorOverlayTexture(WorldBlockDefinition block)
    {
        if (block.Door is not { Enabled: true } door
            || string.IsNullOrWhiteSpace(door.RequiredKey)
            || door.LockedOverlays is null) {
            return null;
        }

        return door.LockedOverlays.TryGetValue(door.RequiredKey.Trim(), out var texture)
            && !string.IsNullOrWhiteSpace(texture)
                ? texture
                : door.LockedOverlays.TryGetValue("default", out var fallback)
                    && !string.IsNullOrWhiteSpace(fallback)
                        ? fallback
                        : null;
    }

    private static string PreviewWallTexture(WorldWallSpan wall)
    {
        return PreviewWallTexture(wall, null);
    }

    private static string PreviewWallTexture(WorldWallSpan wall, string? face)
    {
        if (!string.IsNullOrWhiteSpace(wall.Texture)) {
            if (face is null
                || wall.FaceTextures is null
                || !wall.FaceTextures.TryGetValue(face, out var faceTexture)
                || string.IsNullOrWhiteSpace(faceTexture)) {
                return wall.Texture;
            }

            return faceTexture;
        }

        if (wall.FaceTextures is null || wall.FaceTextures.Count == 0) {
            return string.Empty;
        }

        if (face is not null
            && wall.FaceTextures.TryGetValue(face, out var texture)
            && !string.IsNullOrWhiteSpace(texture)) {
            return texture;
        }

        foreach (var candidateFace in new[] { "north", "east", "south", "west" }) {
            if (wall.FaceTextures.TryGetValue(candidateFace, out var candidateTexture)
                && !string.IsNullOrWhiteSpace(candidateTexture)) {
                return candidateTexture;
            }
        }

        return wall.FaceTextures.Values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
            ?? string.Empty;
    }

    private static double NormalizeHeight(int height, int defaultWallHeight)
    {
        return height / (double)Math.Max(1, defaultWallHeight);
    }

    private static Color TextureColor(string textureKey, byte alpha)
    {
        var hash = StringComparer.OrdinalIgnoreCase.GetHashCode(textureKey);
        var r = (byte)(80 + (hash & 0x7f));
        var g = (byte)(80 + ((hash >> 7) & 0x7f));
        var b = (byte)(80 + ((hash >> 14) & 0x7f));
        return Color.FromArgb(alpha, r, g, b);
    }

    private static string TextureKey(byte key)
    {
        return key.ToString("x2", CultureInfo.InvariantCulture);
    }

    private sealed class PreviewMaterialLibrary
    {
        private readonly Dictionary<string, TextureAsset> m_assets = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Material> m_texturedMaterials = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<ImageSource, Material> m_spriteMaterials = [];

        public PreviewMaterialLibrary(EditorMapDocument document, string? assetBasePath)
        {
            if (string.IsNullOrWhiteSpace(assetBasePath)) {
                assetBasePath = document.SourcePath ?? Environment.CurrentDirectory;
            }

            foreach (var asset in TexturePaletteBuilder.Build(document, assetBasePath)) {
                m_assets[TextureKey(asset.Key)] = asset;
                if (!string.IsNullOrWhiteSpace(asset.Name)) {
                    m_assets[asset.Name] = asset;
                }
            }
        }

        public int ResolvedTextureCount { get; private set; }

        public Material Surface(string? textureKey, byte fallbackTextureKey, Color fallback, double opacity)
        {
            if (!string.IsNullOrWhiteSpace(textureKey)) {
                return Texture(textureKey, TextureColor(textureKey, fallback.A), opacity);
            }

            return fallbackTextureKey != 0
                ? Texture(TextureKey(fallbackTextureKey), TextureColor(TextureKey(fallbackTextureKey), fallback.A), opacity)
                : Material(fallback);
        }

        public Material Texture(string textureKey, Color fallback, double opacity)
        {
            var normalizedKey = NormalizeTextureKey(textureKey);
            var cacheKey = $"{normalizedKey}:{opacity:0.###}";
            if (m_texturedMaterials.TryGetValue(cacheKey, out var material)) {
                return material;
            }

            if (!m_assets.TryGetValue(normalizedKey, out var asset) || !asset.Exists) {
                return Material(fallback);
            }

            try {
                var bitmap = LoadBitmap(asset);

                var brush = new ImageBrush(bitmap) {
                    Stretch = Stretch.Fill,
                    Opacity = opacity,
                    TileMode = TileMode.None
                };
                brush.Freeze();

                material = new DiffuseMaterial(brush);
                material.Freeze();
                m_texturedMaterials[cacheKey] = material;
                ++ResolvedTextureCount;
                return material;
            }
            catch (IOException) {
                return Material(fallback);
            }
            catch (NotSupportedException) {
                return Material(fallback);
            }
        }

        public Material TextureWithOverlay(
            string textureKey,
            string? overlayTextureKey,
            Color fallback,
            double opacity)
        {
            if (string.IsNullOrWhiteSpace(overlayTextureKey)) {
                return Texture(textureKey, fallback, opacity);
            }

            var normalizedKey = NormalizeTextureKey(textureKey);
            var normalizedOverlayKey = NormalizeTextureKey(overlayTextureKey);
            var cacheKey = $"{normalizedKey}+{normalizedOverlayKey}:{opacity:0.###}";
            if (m_texturedMaterials.TryGetValue(cacheKey, out var material)) {
                return material;
            }

            if (!m_assets.TryGetValue(normalizedKey, out var asset)
                || !asset.Exists
                || !m_assets.TryGetValue(normalizedOverlayKey, out var overlayAsset)
                || !overlayAsset.Exists) {
                return Texture(textureKey, fallback, opacity);
            }

            try {
                var drawing = new DrawingGroup();
                drawing.Children.Add(new ImageDrawing(LoadBitmap(asset), new Rect(0, 0, 1, 1)));
                drawing.Children.Add(new ImageDrawing(LoadBitmap(overlayAsset), new Rect(0, 0, 1, 1)));
                drawing.Freeze();

                var brush = new DrawingBrush(drawing) {
                    Stretch = Stretch.Fill,
                    Opacity = opacity,
                    TileMode = TileMode.None
                };
                brush.Freeze();

                material = new DiffuseMaterial(brush);
                material.Freeze();
                m_texturedMaterials[cacheKey] = material;
                ++ResolvedTextureCount;
                return material;
            }
            catch (IOException) {
                return Texture(textureKey, fallback, opacity);
            }
            catch (NotSupportedException) {
                return Texture(textureKey, fallback, opacity);
            }
        }

        public Material Sprite(ImageSource? image, Color fallback, double opacity)
        {
            if (image is null) {
                return Material(fallback);
            }

            if (m_spriteMaterials.TryGetValue(image, out var material)) {
                return material;
            }

            var brush = new ImageBrush(image) {
                Stretch = Stretch.Uniform,
                Opacity = opacity,
                TileMode = TileMode.None
            };
            brush.Freeze();

            material = new DiffuseMaterial(brush);
            material.Freeze();
            m_spriteMaterials[image] = material;
            return material;
        }

        private static string NormalizeTextureKey(string textureKey)
        {
            return byte.TryParse(textureKey, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value)
                ? TextureKey(value)
                : textureKey;
        }

        private static BitmapImage LoadBitmap(TextureAsset asset)
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 256;
            bitmap.UriSource = new Uri(asset.FullPath, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
    }
}
