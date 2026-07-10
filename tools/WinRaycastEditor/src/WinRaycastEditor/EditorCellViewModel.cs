using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using WinRaycastEditor.Core;

namespace WinRaycastEditor;

public sealed class EditorCellViewModel : INotifyPropertyChanged
{
    private readonly IReadOnlyDictionary<byte, ImageSource?> m_texturePreviews;
    private readonly IReadOnlyDictionary<string, ImageSource?> m_spritePreviews;
    private string m_selectedLayer = "Walls";
    private bool m_hasPlayerStart;
    private bool m_hasGameGoal;
    private string? m_targetLayerLabel;

    public EditorCellViewModel(
        EditorMapCell cell,
        IReadOnlyDictionary<byte, ImageSource?> texturePreviews,
        IReadOnlyDictionary<string, ImageSource?> spritePreviews)
    {
        Cell = cell;
        m_texturePreviews = texturePreviews;
        m_spritePreviews = spritePreviews;
        ClearWallsCommand = new RelayCommand(_ => ClearWalls());
        ClearSurfacesCommand = new RelayCommand(_ => ClearSurfaces());
    }

    public EditorMapCell Cell { get; }
    public RelayCommand ClearWallsCommand { get; }
    public RelayCommand ClearSurfacesCommand { get; }
    public int Row => Cell.Row;
    public int Column => Cell.Column;
    public string PackedValue => Cell.PackedValue.ToString("x10");
    public string Coordinates => $"{Column}, {Row}";
    public string WallTexture => $"0x{Cell.Fields.SolidWallTexture:x2}";
    public string CeilingTexture => $"0x{Cell.Fields.CeilingTexture:x2}";
    public string FloorTexture => $"0x{Cell.Fields.FloorTexture:x2}";
    public string TransparentWallTexture => $"0x{Cell.Fields.TransparentWallTexture:x2}";
    public string UpperWallTexture => $"0x{Cell.Fields.UpperWallTexture:x2}";
    public string ActiveTextureLabel => SelectedLayer switch
    {
        "Walls" => ActiveWallTextureLabel,
        "Floor" => FloorTexture,
        "Ceiling" => CeilingTexture,
        _ => PackedValue
    };

    public ImageSource? ActiveTexturePreview => SelectedLayer switch
    {
        "Walls" => TexturePreview(ActiveWallTextureId),
        "Floor" => TexturePreview(Cell.Fields.FloorTexture),
        "Ceiling" => TexturePreview(Cell.Fields.CeilingTexture),
        _ => null
    };

    public bool HasActiveTexturePreview => ActiveTexturePreview is not null;
    public bool ShowSpriteLayer => SelectedLayer == "Sprites" && HasSprites;
    public bool ShowSpritePreview => ShowSpriteLayer && SpritePreview is not null;
    public bool ShowSpriteMarker => ShowSpriteLayer && !ShowSpritePreview;
    public string SpriteMarkerText => HasSprites ? SpriteCount.ToString() : string.Empty;
    public ImageSource? SpritePreview => FirstVisibleSprite is { } sprite
        && m_spritePreviews.TryGetValue(sprite.SpriteSet, out var preview)
            ? preview
            : null;
    public double SpritePreviewScaleCells => FirstVisibleSprite?.ScaleCells ?? 1.0;
    public double SpritePreviewOffsetX => FirstVisibleSprite is { } sprite
        ? ClampCellOffset(sprite.XCell - Column - 0.5)
        : 0.0;
    public double SpritePreviewOffsetY => FirstVisibleSprite is { } sprite
        ? ClampCellOffset(sprite.YCell - Row - 0.5)
        : 0.0;
    public bool ShowHorizonMarker => SelectedLayer == "Horizon" && HasHorizon;
    public bool ShowPlayerMarker => HasPlayerStart;
    public bool ShowGameGoalMarker => SelectedLayer == "Goal" && HasGameGoal;
    public int SpriteCount => Cell.Sprites.Count;
    public bool HasSprites => SpriteCount > 0;
    public bool HasPlayerStart
    {
        get => m_hasPlayerStart;
        set
        {
            if (m_hasPlayerStart == value) {
                return;
            }

            m_hasPlayerStart = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowPlayerMarker));
            OnPropertyChanged(nameof(Badges));
        }
    }
    public bool HasHorizon => Cell.UsesHorizon;
    public bool HasGameGoal
    {
        get => m_hasGameGoal;
        set
        {
            if (m_hasGameGoal == value) {
                return;
            }

            m_hasGameGoal = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowGameGoalMarker));
            OnPropertyChanged(nameof(Badges));
        }
    }
    public bool IsTwoBlockHighWall => Cell.IsTwoBlockHighWall;
    public bool HasTransparentWall => Cell.Fields.HasTransparentWall;

    public string? TargetLayerLabel
    {
        get => m_targetLayerLabel;
        set
        {
            if (m_targetLayerLabel == value) {
                return;
            }

            m_targetLayerLabel = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasTargetLayerLabel));
        }
    }

    public bool HasTargetLayerLabel => !string.IsNullOrWhiteSpace(m_targetLayerLabel);

    public int SolidWallTexture
    {
        get => Cell.Fields.SolidWallTexture;
        set => UpdateFields(Cell.Fields with { SolidWallTexture = ToTextureId(value) });
    }

    public int CeilingTextureId
    {
        get => Cell.Fields.CeilingTexture;
        set => UpdateFields(Cell.Fields with { CeilingTexture = ToTextureId(value) });
    }

    public int FloorTextureId
    {
        get => Cell.Fields.FloorTexture;
        set => UpdateFields(Cell.Fields with { FloorTexture = ToTextureId(value) });
    }

    public int TransparentWallTextureId
    {
        get => Cell.Fields.TransparentWallTexture;
        set => UpdateFields(Cell.Fields with { TransparentWallTexture = ToTextureId(value) });
    }

    public int UpperWallTextureId
    {
        get => Cell.Fields.UpperWallTexture;
        set => UpdateFields(Cell.Fields with { UpperWallTexture = ToTextureId(value) });
    }

    public string SelectedLayer
    {
        get => m_selectedLayer;
        set
        {
            if (m_selectedLayer == value) {
                return;
            }

            m_selectedLayer = value;
            NotifyLayerChanged();
        }
    }

    public Brush Background => Kind switch
    {
        "Tall wall" => new SolidColorBrush(Color.FromRgb(94, 99, 112)),
        "Wall" => new SolidColorBrush(Color.FromRgb(138, 145, 158)),
        "Transparent" => new SolidColorBrush(Color.FromRgb(137, 181, 199)),
        _ when Cell.HasFloorOrCeiling => new SolidColorBrush(Color.FromRgb(220, 232, 211)),
        _ => new SolidColorBrush(Color.FromRgb(239, 241, 244))
    };

    public Brush Foreground => Kind is "Tall wall" or "Wall"
        ? Brushes.White
        : Brushes.Black;

    public string Badges
    {
        get
        {
            var badges = new List<string>();
            if (IsTwoBlockHighWall) {
                badges.Add("2H");
            }

            if (HasTransparentWall) {
                badges.Add("T");
            }

            if (HasSprites) {
                badges.Add($"S{SpriteCount}");
            }

            if (HasHorizon) {
                badges.Add("H");
            }

            if (HasPlayerStart) {
                badges.Add("P");
            }

            if (HasGameGoal) {
                badges.Add("G");
            }

            return string.Join(" ", badges);
        }
    }

    public string Kind
    {
        get
        {
            if (Cell.IsTwoBlockHighWall) {
                return "Tall wall";
            }

            if (Cell.Fields.HasSolidWall) {
                return "Wall";
            }

            if (Cell.Fields.HasTransparentWall) {
                return "Transparent";
            }

            return "Open";
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<CellContentChangedEventArgs>? ContentChanged;

    private byte ActiveWallTextureId
    {
        get
        {
            if (Cell.Fields.HasSolidWall) {
                return Cell.Fields.SolidWallTexture;
            }

            if (Cell.Fields.HasTransparentWall) {
                return Cell.Fields.TransparentWallTexture;
            }

            return Cell.Fields.UpperWallTexture;
        }
    }

    private string ActiveWallTextureLabel
    {
        get
        {
            if (Cell.Fields.HasSolidWall) {
                return WallTexture;
            }

            if (Cell.Fields.HasTransparentWall) {
                return TransparentWallTexture;
            }

            return UpperWallTexture;
        }
    }

    private EditorSpriteInstance? FirstVisibleSprite =>
        Cell.Sprites.FirstOrDefault(sprite => sprite.Visible);

    public void AddSprite(EditorSpriteInstance sprite)
    {
        Cell.Sprites.Add(sprite);
        NotifySpriteCollectionChanged();
    }

    public void NotifySpriteCollectionChanged()
    {
        OnPropertyChanged(nameof(SpriteCount));
        OnPropertyChanged(nameof(HasSprites));
        OnPropertyChanged(nameof(Badges));
        OnPropertyChanged(nameof(ShowSpriteLayer));
        OnPropertyChanged(nameof(ShowSpritePreview));
        OnPropertyChanged(nameof(ShowSpriteMarker));
        OnPropertyChanged(nameof(SpriteMarkerText));
        OnPropertyChanged(nameof(SpritePreview));
        OnPropertyChanged(nameof(SpritePreviewScaleCells));
        OnPropertyChanged(nameof(SpritePreviewOffsetX));
        OnPropertyChanged(nameof(SpritePreviewOffsetY));
        OnPropertyChanged(nameof(ShowHorizonMarker));
        OnPropertyChanged(nameof(ShowPlayerMarker));
        OnPropertyChanged(nameof(ShowGameGoalMarker));
    }

    public void ApplyContent(EditorCellContent content)
    {
        content.ApplyTo(Cell);
        NotifyCellChanged();
    }

    private void ClearWalls()
    {
        UpdateFields(Cell.Fields with {
            SolidWallTexture = 0,
            TransparentWallTexture = 0,
            UpperWallTexture = 0
        });
    }

    private void ClearSurfaces()
    {
        UpdateFields(Cell.Fields with {
            CeilingTexture = 0,
            FloorTexture = 0
        });
    }

    private void UpdateFields(MapCellFields fields)
    {
        if (Cell.Fields == fields) {
            return;
        }

        var before = EditorCellContent.Capture(Cell);
        Cell.Fields = fields;
        Cell.BlockId = string.Empty;
        NotifyCellChanged();
        ContentChanged?.Invoke(this, new CellContentChangedEventArgs(
            this,
            before,
            EditorCellContent.Capture(Cell)));
    }

    private void NotifyCellChanged()
    {
        OnPropertyChanged(nameof(PackedValue));
        OnPropertyChanged(nameof(WallTexture));
        OnPropertyChanged(nameof(CeilingTexture));
        OnPropertyChanged(nameof(FloorTexture));
        OnPropertyChanged(nameof(TransparentWallTexture));
        OnPropertyChanged(nameof(UpperWallTexture));
        OnPropertyChanged(nameof(SolidWallTexture));
        OnPropertyChanged(nameof(CeilingTextureId));
        OnPropertyChanged(nameof(FloorTextureId));
        OnPropertyChanged(nameof(TransparentWallTextureId));
        OnPropertyChanged(nameof(UpperWallTextureId));
        OnPropertyChanged(nameof(ActiveTextureLabel));
        OnPropertyChanged(nameof(ActiveTexturePreview));
        OnPropertyChanged(nameof(HasActiveTexturePreview));
        OnPropertyChanged(nameof(IsTwoBlockHighWall));
        OnPropertyChanged(nameof(HasTransparentWall));
        OnPropertyChanged(nameof(HasHorizon));
        OnPropertyChanged(nameof(Kind));
        OnPropertyChanged(nameof(Background));
        OnPropertyChanged(nameof(Foreground));
        OnPropertyChanged(nameof(Badges));
        OnPropertyChanged(nameof(ShowHorizonMarker));
    }

    private void NotifyLayerChanged()
    {
        OnPropertyChanged(nameof(SelectedLayer));
        OnPropertyChanged(nameof(ActiveTextureLabel));
        OnPropertyChanged(nameof(ActiveTexturePreview));
        OnPropertyChanged(nameof(HasActiveTexturePreview));
        OnPropertyChanged(nameof(ShowSpriteLayer));
        OnPropertyChanged(nameof(ShowSpritePreview));
        OnPropertyChanged(nameof(ShowSpriteMarker));
        OnPropertyChanged(nameof(SpriteMarkerText));
        OnPropertyChanged(nameof(SpritePreview));
        OnPropertyChanged(nameof(SpritePreviewOffsetX));
        OnPropertyChanged(nameof(SpritePreviewOffsetY));
        OnPropertyChanged(nameof(ShowHorizonMarker));
        OnPropertyChanged(nameof(ShowPlayerMarker));
        OnPropertyChanged(nameof(ShowGameGoalMarker));
    }

    private ImageSource? TexturePreview(byte textureId)
    {
        return textureId != 0 && m_texturePreviews.TryGetValue(textureId, out var preview)
            ? preview
            : null;
    }

    private static byte ToTextureId(int value)
    {
        return (byte)Math.Clamp(value, byte.MinValue, byte.MaxValue);
    }

    private static double ClampCellOffset(double value)
    {
        return Math.Clamp(value, -0.42, 0.42);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class CellContentChangedEventArgs : EventArgs
{
    public CellContentChangedEventArgs(
        EditorCellViewModel cell,
        EditorCellContent before,
        EditorCellContent after)
    {
        Cell = cell;
        Before = before;
        After = after;
    }

    public EditorCellViewModel Cell { get; }
    public EditorCellContent Before { get; }
    public EditorCellContent After { get; }
}
