namespace NuRcade.Editor.Core;

public sealed class EditorMapDocument
{
    public string? SourcePath { get; set; }
    public int CellWidth { get; set; } = 512;
    public int CellHeight { get; set; } = 512;
    public List<List<EditorMapCell>> Rows { get; } = [];
    public Dictionary<byte, string> TextureMap { get; } = [];
    public List<string> SpriteSetFiles { get; } = [];
    public List<EditorSpriteInstance> SpriteInstances { get; } = [];

    /// <summary>
    /// Subset of <see cref="SpriteInstances"/> that belongs to the active layer
    /// rather than to the shared, top-level (global) sprite set. The game merges
    /// the top-level sprites with the active layer's sprites, so the editor shows
    /// both at once; this set records which displayed sprites must be written back
    /// to the layer on save instead of to the top-level list.
    /// </summary>
    public HashSet<EditorSpriteInstance> ActiveLayerSprites { get; } = [];
    public Dictionary<string, WorldBlockDefinition> Blocks { get; } =
        new(StringComparer.OrdinalIgnoreCase);
    public List<WorldLayerDefinition> Layers { get; } = [];
    public List<WorldLayerTransition> LayerTransitions { get; } = [];
    public WorldGameGoal? GameGoal { get; set; }
    public string? ActiveLayerId { get; set; }
    public string? DefaultHorizonImage { get; set; }
    public WorldPlayerStart PlayerStart { get; set; } = new();
    public WorldCombatStats PlayerStats { get; set; } = new();
    public WorldPlayerTurn PlayerTurn { get; set; } = new();
    public double Brightness { get; set; } = 1.0;
    public double DepthShading { get; set; } = 100.0;
    public WorldPlayerWeapon? PlayerWeapon { get; set; }
    public List<WorldPlayerWeapon> PlayerWeapons { get; } = [];
    public WorldBackgroundMusic? BackgroundMusic { get; set; }

    public int RowCount => Rows.Count;
    public int ColumnCount => Rows.Count == 0 ? 0 : Rows[0].Count;

    public EditorMapCell? CellAt(int row, int column)
    {
        if (row < 0 || column < 0 || row >= RowCount || column >= ColumnCount) {
            return null;
        }

        return Rows[row][column];
    }

    public bool AddSpriteInstance(EditorSpriteInstance sprite, int row, int column)
    {
        var cell = CellAt(row, column);
        if (cell is null) {
            return false;
        }

        if (!SpriteInstances.Contains(sprite)) {
            SpriteInstances.Add(sprite);
        }

        if (!cell.Sprites.Contains(sprite)) {
            cell.Sprites.Add(sprite);
        }

        return true;
    }

    public bool RemoveSpriteInstance(EditorSpriteInstance sprite)
    {
        var removed = SpriteInstances.Remove(sprite);
        ActiveLayerSprites.Remove(sprite);
        foreach (var row in Rows) {
            foreach (var cell in row) {
                removed = cell.Sprites.Remove(sprite) || removed;
            }
        }

        return removed;
    }

    public bool RelocateSpriteInstance(EditorSpriteInstance sprite)
    {
        if (!SpriteInstances.Contains(sprite)) {
            return false;
        }

        foreach (var row in Rows) {
            foreach (var cell in row) {
                cell.Sprites.Remove(sprite);
            }
        }

        var targetRow = (int)Math.Floor(sprite.YCell);
        var targetColumn = (int)Math.Floor(sprite.XCell);
        var targetCell = CellAt(targetRow, targetColumn);
        if (targetCell is null) {
            return false;
        }

        targetCell.Sprites.Add(sprite);
        return true;
    }
}
