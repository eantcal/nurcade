namespace WinRaycastEditor.Core;

public sealed class EditorCellContent
{
    public MapCellFields Fields { get; set; }
    public string BlockId { get; set; } = string.Empty;
    public string? HorizonImage { get; set; }

    public static EditorCellContent Capture(EditorMapCell cell)
    {
        return new EditorCellContent {
            Fields = cell.Fields,
            BlockId = cell.BlockId,
            HorizonImage = cell.HorizonImage
        };
    }

    public void ApplyTo(EditorMapCell cell)
    {
        cell.Fields = Fields;
        cell.BlockId = BlockId;
        cell.HorizonImage = HorizonImage;
    }
}
