namespace NuRcade.Editor.Core;

public sealed class EditorMapCell
{
    public EditorMapCell(int row, int column, ulong packedValue)
    {
        Row = row;
        Column = column;
        Fields = MapCellFields.Decode(packedValue);
    }

    public int Row { get; }
    public int Column { get; }
    public MapCellFields Fields { get; set; }
    public string BlockId { get; set; } = string.Empty;
    public string? HorizonImage { get; set; }
    public List<EditorSpriteInstance> Sprites { get; } = [];

    public bool IsTwoBlockHighWall => Fields.HasSolidWall && Fields.HasUpperWall;
    public bool HasFloorOrCeiling => Fields.FloorTexture != 0 || Fields.CeilingTexture != 0;
    public bool UsesHorizon => IsOpenSpace && !string.IsNullOrWhiteSpace(HorizonImage);
    public bool IsOpenSpace => Fields.IsEmptySpace;
    public ulong PackedValue => Fields.Encode();
}
