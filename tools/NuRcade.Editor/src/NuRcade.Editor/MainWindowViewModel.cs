using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using Microsoft.VisualBasic;
using Microsoft.Win32;
using NuRcade.Editor.Core;

namespace NuRcade.Editor;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private const int BlockInspectorTabIndex = 6;
    private const string CellInstanceEditScope = "Selected cell";
    private const string BlockTemplateEditScope = "Shared template";
    private readonly Stack<IEditorUndoAction> m_undoStack = new();
    private readonly Stack<IEditorUndoAction> m_redoStack = new();
    private readonly Dictionary<string, System.Windows.Media.ImageSource?> m_spriteMapPreviews =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<byte, System.Windows.Media.ImageSource?> m_texturePreviews = [];
    private readonly Dictionary<string, string> m_spriteSetFilesByName =
        new(StringComparer.OrdinalIgnoreCase);
    private bool m_isApplyingHistory;
    private string? m_savedWorldSnapshot;
    private CellSelectionClipboard? m_copiedCellSelection;
    private IReadOnlyList<EditorCellViewModel> m_selectedMapCells = [];
    private EditorSpriteInstance? m_copiedSprite;
    private bool m_isSpriteCutPending;
    private bool m_isRefreshingSelectedCellSprites;
    private string m_assetBasePath = Environment.CurrentDirectory;
    private string m_preview3DViewMode = "Angled";
    private double m_previewOrbitYawDegrees;
    private double m_previewOrbitPitchDegrees = 32.0;
    private double m_previewOrbitZoom = 1.0;
    private double m_previewOrbitPanX;
    private double m_previewOrbitPanZ;
    private readonly InspectorPreviewCameraState m_selectedCellPreviewCameraState = new();
    private readonly InspectorPreviewCameraState m_selectedBlockPreviewCameraState = new();
    private double m_previewPerspectiveX = 1.5;
    private double m_previewPerspectiveY = 0.62;
    private double m_previewPerspectiveZ = 1.5;
    private double m_previewPerspectiveYawDegrees;
    private double m_previewPerspectivePitchDegrees;
    private bool m_previewShowGrid = true;
    private bool m_previewShowFloors = true;
    private bool m_previewShowCeilings;
    private bool m_previewShowWalls = true;
    private bool m_previewShowSprites = true;
    private bool m_previewShowPlayer = true;
    private ImageSource? m_previewBackgroundImage;
    private System.Windows.Threading.DispatcherTimer? m_spriteValidationTimer;

    public ObservableCollection<EditorCellViewModel> Cells { get; } = [];
    public ObservableCollection<TextureAssetViewModel> Textures { get; } = [];
    public ObservableCollection<BlockPaletteEntryViewModel> Blocks { get; } = [];
    public ObservableCollection<SpriteDirectionViewModel> SpriteDirections { get; } = [];
    public ObservableCollection<SpriteAnimationViewModel> SpriteAnimations { get; } = [];
    public ObservableCollection<SpriteAnimationFrameViewModel> SpriteAnimationFrames { get; } = [];
    public ObservableCollection<SpriteLodRuleViewModel> SpriteLodRules { get; } = [];
    public ObservableCollection<WeaponAnimationViewModel> WeaponAnimations { get; } = [];
    public ObservableCollection<WeaponAnimationFrameViewModel> WeaponAnimationFrames { get; } = [];
    public ObservableCollection<WeaponLibraryItemViewModel> WeaponLibrary { get; } = [];
    public ObservableCollection<SpriteInstanceViewModel> SpriteInstances { get; } = [];
    public ObservableCollection<SpriteInstanceViewModel> SelectedCellSprites { get; } = [];
    public ObservableCollection<string> SpriteSetFiles { get; } = [];
    public ObservableCollection<string> ValidationMessages { get; } = [];
    public ObservableCollection<LayerConnectionOptionViewModel> SelectedCellLayerConnections { get; } = [];
    public ObservableCollection<TextureChoiceViewModel> TextureChoices { get; } = [];
    public ObservableCollection<string> LayerModes { get; } =
    [
        "Walls",
        "Floor",
        "Ceiling",
        "Sprites",
        "Player",
        "Goal",
        "Horizon",
        "Validation"
    ];
    public ObservableCollection<string> PaintTargets { get; } =
    [
        "Wall",
        "Floor",
        "Ceiling",
        "Transparent wall",
        "Upper wall"
    ];
    public ObservableCollection<string> CellEditScopes { get; } =
    [
        CellInstanceEditScope,
        BlockTemplateEditScope
    ];
    public IReadOnlyList<SpriteInstanceViewModel> KeySprites => SpriteInstances
        .Where(sprite => sprite.IsKey)
        .OrderBy(sprite => sprite.Name, StringComparer.OrdinalIgnoreCase)
        .ToList();
    public ObservableCollection<string> WallKinds { get; } =
    [
        "solid",
        "transparent"
    ];

    public ObservableCollection<WorldLayerDefinition> WorldLayers { get; } = [];

    public bool HasMultipleWorldLayers => WorldLayers.Count > 1;
    public string MapSelectionSummary
    {
        get
        {
            var count = CurrentMapSelection().Count;
            return count == 0
                ? "Selection: none"
                : count == 1
                    ? "Selection: 1 cell"
                    : $"Selection: {count} cells";
        }
    }

    private WorldLayerDefinition? m_selectedWorldLayer;
    private bool m_suspendWorldLayerSwitch;
    public WorldLayerDefinition? SelectedWorldLayer
    {
        get => m_selectedWorldLayer;
        set
        {
            if (ReferenceEquals(m_selectedWorldLayer, value)) {
                return;
            }

            var previous = m_selectedWorldLayer;
            m_selectedWorldLayer = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedWorldLayerDisplayName));

            if (!m_suspendWorldLayerSwitch
                && Document is not null
                && previous is not null
                && value is not null
                && !string.Equals(previous.Id, value.Id, StringComparison.OrdinalIgnoreCase)) {
                SwitchToWorldLayer(value);
            }
        }
    }

    public string SelectedWorldLayerDisplayName
    {
        get => SelectedWorldLayer?.Name ?? string.Empty;
        set
        {
            if (SelectedWorldLayer is null || string.Equals(SelectedWorldLayer.Name, value, StringComparison.Ordinal)) {
                return;
            }

            SelectedWorldLayer.Name = value;
            OnPropertyChanged();
            RefreshSelectedCellLayerConnections();
            RefreshElevatorTargetLabels();
        }
    }

    private void SwitchToWorldLayer(WorldLayerDefinition newLayer)
    {
        if (Document is null) {
            return;
        }

        PersistActiveLayerFromCurrentRows();
        Document.ActiveLayerId = newLayer.Id;
        ApplyWorldLayer(newLayer);
        RefreshValidation($"Switched to layer '{newLayer.Id}'.");
    }

    private void CloneSelectedWorldLayer()
    {
        if (Document is null || SelectedWorldLayer is null) {
            return;
        }

        PersistActiveLayerFromCurrentRows();
        var source = SelectedWorldLayer;
        var clone = CloneWorldLayer(source);
        clone.Id = AllocateWorldLayerId(source.Id);
        clone.Name = string.IsNullOrWhiteSpace(source.Name)
            ? $"{source.Id} copy"
            : $"{source.Name} copy";

        Document.Layers.Add(clone);
        WorldLayers.Add(clone);
        OnPropertyChanged(nameof(HasMultipleWorldLayers));
        OnPropertyChanged(nameof(WorldLayerIdOptions));
        CloneSelectedWorldLayerCommand.RaiseCanExecuteChanged();
        DeleteSelectedWorldLayerCommand.RaiseCanExecuteChanged();

        SelectedWorldLayer = clone;
        RefreshElevatorTargetLabels();
        RefreshValidation($"Cloned layer '{source.Id}' to '{clone.Id}'.");
    }

    private void RenameSelectedWorldLayer()
    {
        if (Document is null || SelectedWorldLayer is null) {
            return;
        }

        var currentId = SelectedWorldLayer.Id;
        var newId = Interaction.InputBox(
            "Enter the new layer id. References from elevators and layer transitions will be updated.",
            "Rename world layer",
            currentId);
        if (string.IsNullOrWhiteSpace(newId)
            || string.Equals(currentId, newId.Trim(), StringComparison.Ordinal)) {
            return;
        }

        if (!TryRenameSelectedWorldLayer(newId, out var message)) {
            MessageBox.Show(
                message,
                "Rename layer failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void DeleteSelectedWorldLayer()
    {
        if (Document is null || SelectedWorldLayer is null) {
            return;
        }

        if (Document.Layers.Count <= 1) {
            MessageBox.Show(
                "The last world layer cannot be deleted.",
                "Delete layer",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var layerId = SelectedWorldLayer.Id;
        var result = MessageBox.Show(
            $"Delete layer '{layerId}'?\n\nA backup of the current world/project will be written before the layer is removed.",
            "Delete world layer",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (result != MessageBoxResult.Yes) {
            return;
        }

        try {
            var backupPath = SaveDestructiveOperationBackup($"delete-layer-{SanitizeFileName(layerId)}");
            if (!TryDeleteWorldLayer(layerId, out var message)) {
                MessageBox.Show(
                    message,
                    "Delete layer failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            ValidationMessages.Add($"Backup before deleting layer '{layerId}': {backupPath}");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException) {
            MessageBox.Show(
                $"The layer was not deleted because the backup failed:\n{error.Message}",
                "Layer backup failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    internal bool TryRenameSelectedWorldLayer(string requestedId, out string message)
    {
        message = string.Empty;
        if (Document is null || SelectedWorldLayer is null) {
            message = "No world layer is selected.";
            return false;
        }

        var normalizedId = requestedId.Trim();
        if (!IsValidWorldLayerId(normalizedId)) {
            message = "Layer id cannot be empty and cannot contain whitespace or control characters.";
            return false;
        }

        var layer = SelectedWorldLayer;
        var oldId = layer.Id;
        if (string.Equals(oldId, normalizedId, StringComparison.Ordinal)) {
            message = $"Layer is already named '{normalizedId}'.";
            return true;
        }

        if (Document.Layers.Any(candidate =>
            !ReferenceEquals(candidate, layer)
            && string.Equals(candidate.Id, normalizedId, StringComparison.OrdinalIgnoreCase))) {
            message = $"A layer named '{normalizedId}' already exists.";
            return false;
        }

        PersistActiveLayerFromCurrentRows();
        var oldName = layer.Name;
        layer.Id = normalizedId;
        if (string.IsNullOrWhiteSpace(oldName)
            || string.Equals(oldName, oldId, StringComparison.OrdinalIgnoreCase)) {
            layer.Name = normalizedId;
        }

        if (string.Equals(Document.ActiveLayerId, oldId, StringComparison.OrdinalIgnoreCase)) {
            Document.ActiveLayerId = normalizedId;
        }

        foreach (var transition in Document.LayerTransitions) {
            if (string.Equals(transition.FromLayer, oldId, StringComparison.OrdinalIgnoreCase)) {
                transition.FromLayer = normalizedId;
            }

            if (string.Equals(transition.ToLayer, oldId, StringComparison.OrdinalIgnoreCase)) {
                transition.ToLayer = normalizedId;
            }
        }

        if (Document.GameGoal is not null
            && string.Equals(Document.GameGoal.Layer, oldId, StringComparison.OrdinalIgnoreCase)) {
            Document.GameGoal.Layer = normalizedId;
        }

        RefreshWorldLayerCollectionSelection(layer);
        ApplyWorldLayer(layer);
        JsonPanel.RefreshFromModel();
        RefreshElevatorTargetLabels();
        RefreshValidation($"Renamed layer '{oldId}' to '{normalizedId}'.");
        message = $"Renamed layer '{oldId}' to '{normalizedId}'.";
        return true;
    }

    internal bool TryDeleteWorldLayer(string layerId, out string message)
    {
        message = string.Empty;
        if (Document is null) {
            message = "No world is loaded.";
            return false;
        }

        if (Document.Layers.Count <= 1) {
            message = "The last world layer cannot be deleted.";
            return false;
        }

        var index = Document.Layers.FindIndex(layer =>
            string.Equals(layer.Id, layerId, StringComparison.OrdinalIgnoreCase));
        if (index < 0) {
            message = $"Layer '{layerId}' was not found.";
            return false;
        }

        PersistActiveLayerFromCurrentRows();
        var removed = Document.Layers[index];
        var replacement = Document.Layers
            .Where(layer => !ReferenceEquals(layer, removed))
            .ElementAtOrDefault(Math.Min(index, Document.Layers.Count - 2))
            ?? Document.Layers.First(layer => !ReferenceEquals(layer, removed));

        Document.Layers.RemoveAt(index);
        Document.LayerTransitions.RemoveAll(transition =>
            string.Equals(transition.FromLayer, removed.Id, StringComparison.OrdinalIgnoreCase)
            || string.Equals(transition.ToLayer, removed.Id, StringComparison.OrdinalIgnoreCase));
        if (Document.GameGoal is not null
            && string.Equals(Document.GameGoal.Layer, removed.Id, StringComparison.OrdinalIgnoreCase)) {
            Document.GameGoal = null;
        }

        if (string.Equals(Document.ActiveLayerId, removed.Id, StringComparison.OrdinalIgnoreCase)
            || !Document.Layers.Any(layer =>
                string.Equals(layer.Id, Document.ActiveLayerId, StringComparison.OrdinalIgnoreCase))) {
            Document.ActiveLayerId = replacement.Id;
        }

        RefreshWorldLayerCollectionSelection(replacement);
        ApplyWorldLayer(replacement);
        JsonPanel.RefreshFromModel();
        RefreshElevatorTargetLabels();
        RefreshValidation($"Deleted layer '{removed.Id}'.");
        message = $"Deleted layer '{removed.Id}'.";
        return true;
    }

    private static bool IsValidWorldLayerId(string value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && !value.Any(character => char.IsWhiteSpace(character) || char.IsControl(character));
    }

    private void RefreshWorldLayerCollectionSelection(WorldLayerDefinition? selectedLayer)
    {
        if (Document is null) {
            return;
        }

        m_suspendWorldLayerSwitch = true;
        try {
            WorldLayers.Clear();
            foreach (var layer in Document.Layers) {
                WorldLayers.Add(layer);
            }

            SelectedWorldLayer = selectedLayer is not null && Document.Layers.Contains(selectedLayer)
                ? selectedLayer
                : WorldLayers.FirstOrDefault(layer =>
                    string.Equals(layer.Id, Document.ActiveLayerId, StringComparison.OrdinalIgnoreCase))
                    ?? WorldLayers.FirstOrDefault();
        }
        finally {
            m_suspendWorldLayerSwitch = false;
        }

        OnPropertyChanged(nameof(WorldLayers));
        OnPropertyChanged(nameof(HasMultipleWorldLayers));
        OnPropertyChanged(nameof(WorldLayerIdOptions));
        OnPropertyChanged(nameof(SelectedCellTargetLayer));
        RenameSelectedWorldLayerCommand.RaiseCanExecuteChanged();
        DeleteSelectedWorldLayerCommand.RaiseCanExecuteChanged();
        CloneSelectedWorldLayerCommand.RaiseCanExecuteChanged();
    }

    private string SaveDestructiveOperationBackup(string operationName)
    {
        if (Document is null) {
            throw new InvalidOperationException("No map document is loaded.");
        }

        PersistActiveLayerFromCurrentRows();
        var timestamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var backupDirectory = Path.Combine(CurrentWorldDirectory(), "editor_backups");
        Directory.CreateDirectory(backupDirectory);

        var baseName = string.IsNullOrWhiteSpace(Document.SourcePath)
            ? "unsaved_world"
            : Path.GetFileNameWithoutExtension(Document.SourcePath);
        var worldBackupPath = Path.Combine(
            backupDirectory,
            $"{SanitizeFileName(baseName)}.{operationName}.{timestamp}.world.json");

        var worldName = string.IsNullOrWhiteSpace(Document.SourcePath)
            ? "backup"
            : Path.GetFileNameWithoutExtension(Document.SourcePath);
        var worldJson = WorldJsonDocumentService.Serialize(
            LegacyWorldConverter.FromEditorMap(Document, worldName));
        File.WriteAllText(worldBackupPath, worldJson);

        if (!string.IsNullOrWhiteSpace(m_projectPath)) {
            var projectBackupPath = Path.Combine(
                backupDirectory,
                $"{SanitizeFileName(Path.GetFileNameWithoutExtension(m_projectPath))}.{operationName}.{timestamp}.project.json");
            var projectDirectory =
                Path.GetDirectoryName(Path.GetFullPath(projectBackupPath)) ?? backupDirectory;
            var worldRelative = string.IsNullOrWhiteSpace(Document.SourcePath)
                ? string.Empty
                : Path.GetRelativePath(projectDirectory, Document.SourcePath).Replace('\\', '/');
            var project = EditorProjectDocumentService.FromMapDocument(Document, worldRelative);
            project.ProjectName = Path.GetFileNameWithoutExtension(m_projectPath);
            EditorProjectDocumentService.Save(project, projectBackupPath);
        }

        return worldBackupPath;
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(character =>
            invalid.Contains(character) ? '_' : character).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "backup" : sanitized;
    }

    private string AllocateWorldLayerId(string sourceId)
    {
        if (Document is null) {
            return "level_copy";
        }

        var normalized = string.IsNullOrWhiteSpace(sourceId)
            ? "level"
            : sourceId.Trim();
        var baseId = $"{normalized}_copy";
        if (!Document.Layers.Any(layer =>
            string.Equals(layer.Id, baseId, StringComparison.OrdinalIgnoreCase))) {
            return baseId;
        }

        for (var index = 2; ; ++index) {
            var candidate = $"{baseId}_{index}";
            if (!Document.Layers.Any(layer =>
                string.Equals(layer.Id, candidate, StringComparison.OrdinalIgnoreCase))) {
                return candidate;
            }
        }
    }

    private void PersistActiveLayerFromCurrentRows()
    {
        if (Document is null || string.IsNullOrWhiteSpace(Document.ActiveLayerId)) {
            return;
        }

        var activeLayer = Document.Layers.FirstOrDefault(layer =>
            string.Equals(layer.Id, Document.ActiveLayerId, StringComparison.OrdinalIgnoreCase));
        if (activeLayer is null) {
            return;
        }

        activeLayer.Cells = Document.Rows
            .Select(row => row
                .Select(cell => string.IsNullOrWhiteSpace(cell.BlockId)
                    ? WorldJsonDocumentService.EmptyBlockId
                    : cell.BlockId)
                .ToList())
            .ToList();
        activeLayer.Grid = new WorldGridDefinition {
            Columns = Document.ColumnCount,
            Rows = Document.RowCount,
            CellWidth = Document.CellWidth,
            CellDepth = Document.CellHeight,
            DefaultWallHeight = Document.CellHeight > 0 ? Document.CellHeight : 512
        };
        activeLayer.PlayerStart = ClonePlayerStart(Document.PlayerStart);
        activeLayer.DefaultHorizonImage = Document.DefaultHorizonImage;
        activeLayer.BackgroundMusic = CloneBackgroundMusic(Document.BackgroundMusic);

        // Hand the active layer's own sprites back to the layer and drop them from
        // the working set; global (top-level) sprites stay loaded across layers.
        var ownedSprites = Document.SpriteInstances
            .Where(Document.ActiveLayerSprites.Contains)
            .ToList();
        activeLayer.SpriteInstances = ownedSprites;
        foreach (var sprite in ownedSprites) {
            Document.SpriteInstances.Remove(sprite);
        }
        Document.ActiveLayerSprites.Clear();
    }

    private void LoadActiveLayerSprites(WorldLayerDefinition layer)
    {
        if (Document is null) {
            return;
        }

        // Merge the layer's own sprites into the working set (shown alongside the
        // global top-level sprites) and flag them as layer-owned. The working set
        // is the single source of truth until the next layer switch or save.
        foreach (var sprite in layer.SpriteInstances) {
            if (!Document.SpriteInstances.Contains(sprite)) {
                Document.SpriteInstances.Add(sprite);
            }

            Document.ActiveLayerSprites.Add(sprite);
        }

        layer.SpriteInstances = [];

        foreach (var viewModel in SpriteInstances) {
            viewModel.PropertyChanged -= OnSpriteInstanceChanged;
        }

        SpriteInstances.Clear();
        foreach (var sprite in Document.SpriteInstances) {
            SpriteInstances.Add(CreateSpriteInstanceViewModel(sprite));
        }

        SelectedSprite = null;
        UpdateSpriteSummary();
    }

    private void ApplyWorldLayer(WorldLayerDefinition layer)
    {
        if (Document is null) {
            return;
        }

        var grid = layer.Grid ?? new WorldGridDefinition {
            Columns = layer.Cells.FirstOrDefault()?.Count ?? Document.ColumnCount,
            Rows = layer.Cells.Count,
            CellWidth = Document.CellWidth,
            CellDepth = Document.CellHeight,
            DefaultWallHeight = Document.CellHeight > 0 ? Document.CellHeight : 512
        };

        Document.CellWidth = grid.CellWidth > 0 ? grid.CellWidth : Document.CellWidth;
        Document.CellHeight = grid.CellDepth > 0 ? grid.CellDepth : Document.CellHeight;
        Document.DefaultHorizonImage = layer.DefaultHorizonImage;
        Document.BackgroundMusic = CloneBackgroundMusic(layer.BackgroundMusic);
        if (layer.PlayerStart is not null) {
            Document.PlayerStart = ClonePlayerStart(layer.PlayerStart);
            PlayerStart = new PlayerStartViewModel(Document.PlayerStart);
        }

        foreach (var cell in Cells) {
            cell.ContentChanged -= OnCellContentChanged;
        }

        Cells.Clear();
        Document.Rows.Clear();
        var texturePreviews = BuildTexturePreviewMap();
        var rows = grid.Rows > 0 ? grid.Rows : layer.Cells.Count;
        var columns = grid.Columns > 0 ? grid.Columns : (layer.Cells.FirstOrDefault()?.Count ?? 0);
        for (var row = 0; row < rows; ++row) {
            var mapRow = new List<EditorMapCell>();
            for (var column = 0; column < columns; ++column) {
                var blockId = row < layer.Cells.Count && column < layer.Cells[row].Count
                    ? layer.Cells[row][column]
                    : WorldJsonDocumentService.EmptyBlockId;
                var mapCell = CreateEditorMapCellFromBlockId(row, column, blockId);
                mapRow.Add(mapCell);
                var cellViewModel = new EditorCellViewModel(mapCell, texturePreviews, m_spriteMapPreviews) {
                    SelectedLayer = SelectedLayer
                };
                cellViewModel.ContentChanged += OnCellContentChanged;
                Cells.Add(cellViewModel);
            }

            Document.Rows.Add(mapRow);
        }

        LoadActiveLayerSprites(layer);
        RefreshSpriteCellMembership();
        SelectedCell = Cells.FirstOrDefault();
        RefreshPlayerCellMarkers();
        RefreshGameGoalMarkers();
        RefreshSelectedCellSprites();
        RefreshElevatorTargetLabels();
        NotifySelectedCellEditorChanged();

        MapSummary = $"{Document.ColumnCount} x {Document.RowCount} cells";
        OnPropertyChanged(nameof(MapSummary));
        OnPropertyChanged(nameof(WorldBackgroundImage));
        OnPropertyChanged(nameof(WorldBackgroundSummary));
        NotifyBackgroundMusicChanged();
    }

    private Dictionary<byte, ImageSource?> BuildTexturePreviewMap()
    {
        // Refill the shared preview map in place so cell view models that already
        // hold a reference to it pick up the current textures (including any added
        // to the library after load).
        m_texturePreviews.Clear();
        foreach (var texture in Textures) {
            m_texturePreviews[texture.Asset.Key] = texture.Preview;
        }

        return m_texturePreviews;
    }

    private EditorMapCell CreateEditorMapCellFromBlockId(int row, int column, string? blockId)
    {
        var normalizedBlockId = string.IsNullOrWhiteSpace(blockId)
            ? WorldJsonDocumentService.EmptyBlockId
            : blockId;
        var block = Document is not null
            && Document.Blocks.TryGetValue(normalizedBlockId, out var found)
                ? found
                : new WorldBlockDefinition();
        return new EditorMapCell(row, column, FieldsFromBlock(block).Encode()) {
            BlockId = normalizedBlockId,
            HorizonImage = block.HorizonImage
        };
    }

    private void RefreshSpriteCellMembership()
    {
        if (Document is null) {
            return;
        }

        foreach (var row in Document.Rows) {
            foreach (var cell in row) {
                cell.Sprites.Clear();
            }
        }

        foreach (var sprite in Document.SpriteInstances) {
            var row = (int)Math.Floor(sprite.YCell);
            var column = (int)Math.Floor(sprite.XCell);
            Document.CellAt(row, column)?.Sprites.Add(sprite);
        }

        foreach (var cell in Cells) {
            cell.NotifySpriteCollectionChanged();
        }
    }

    public EditorMapDocument? Document { get; private set; }

    /// <summary>Backing view model for the dockable JSON editor panel.</summary>
    public JsonEditorPanelViewModel JsonPanel { get; }

    private enum JsonSelectionKind
    {
        Cell,
        Sprite,
        Block
    }

    private JsonSelectionKind m_lastSelectionKind = JsonSelectionKind.Cell;

    public string Title => "nuRCADE Editor";
    private int m_selectedInspectorTabIndex;
    public int SelectedInspectorTabIndex
    {
        get => m_selectedInspectorTabIndex;
        set
        {
            if (m_selectedInspectorTabIndex == value) {
                return;
            }

            m_selectedInspectorTabIndex = value;
            OnPropertyChanged();
        }
    }

    public string MapSummary { get; private set; } = "No map loaded";
    public string TextureSummary { get; private set; } = "No textures loaded";
    public string BlockSummary { get; private set; } = "No block palette loaded";
    public string SpriteSummary { get; private set; } = "No sprite instances";
    public string SpriteMetadataSummary { get; private set; } = "No sprite metadata loaded";
    public string SpriteTransparentColorSummary { get; private set; } = "Transparent color: n/a";
    public string SpriteLodSelectionSummary { get; private set; } = "No sprite LOD selected";
    public string WorldBackgroundImage
    {
        get => Document?.DefaultHorizonImage ?? string.Empty;
        set
        {
            if (Document is null) {
                return;
            }

            var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (string.Equals(Document.DefaultHorizonImage, normalized, StringComparison.Ordinal)) {
                return;
            }

            Document.DefaultHorizonImage = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(WorldBackgroundSummary));
            RefreshPreviewBackground();
            RefreshValidation("Map validation completed.");
        }
    }
    public string WorldBackgroundSummary =>
        string.IsNullOrWhiteSpace(WorldBackgroundImage)
            ? "Using texture 0xff or the engine fallback sky."
            : $"Default horizon: {WorldBackgroundImage}";
    public string LevelBackgroundMusicFile
    {
        get => Document?.BackgroundMusic?.File ?? string.Empty;
        set
        {
            if (Document is null || LevelBackgroundMusicFile == value) {
                return;
            }

            var normalized = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
            if (string.IsNullOrWhiteSpace(normalized)) {
                Document.BackgroundMusic = null;
            }
            else {
                EnsureBackgroundMusic().File = normalized;
            }

            NotifyBackgroundMusicChanged();
            RefreshValidation("Map validation completed.");
        }
    }
    public bool LevelBackgroundMusicEnabled
    {
        get => Document?.BackgroundMusic?.Enabled ?? false;
        set
        {
            if (Document is null || LevelBackgroundMusicEnabled == value) {
                return;
            }

            EnsureBackgroundMusic().Enabled = value;
            NotifyBackgroundMusicChanged();
            RefreshValidation("Map validation completed.");
        }
    }
    public bool LevelBackgroundMusicLoop
    {
        get => Document?.BackgroundMusic?.Loop ?? true;
        set
        {
            if (Document is null || LevelBackgroundMusicLoop == value) {
                return;
            }

            EnsureBackgroundMusic().Loop = value;
            NotifyBackgroundMusicChanged();
            RefreshValidation("Map validation completed.");
        }
    }
    public double LevelBackgroundMusicVolumePercent
    {
        get => Document?.BackgroundMusic?.VolumePercent ?? 80;
        set
        {
            if (Document is null) {
                return;
            }

            var clamped = (int)Math.Round(Math.Clamp(value, 0.0, 100.0));
            if (Document.BackgroundMusic is not null
                && Document.BackgroundMusic.VolumePercent == clamped) {
                return;
            }

            EnsureBackgroundMusic().VolumePercent = clamped;
            NotifyBackgroundMusicChanged();
            RefreshValidation("Map validation completed.");
        }
    }
    public string LevelBackgroundMusicVolumeLabel =>
        $"{LevelBackgroundMusicVolumePercent:0}%";
    public string LevelBackgroundMusicSummary
    {
        get
        {
            if (Document?.BackgroundMusic is null
                || string.IsNullOrWhiteSpace(Document.BackgroundMusic.File)) {
                return "No level background music.";
            }

            var state = Document.BackgroundMusic.Enabled ? "enabled" : "disabled";
            var loop = Document.BackgroundMusic.Loop ? "looped" : "one-shot";
            return $"{Document.BackgroundMusic.File} ({state}, {loop}, {Document.BackgroundMusic.VolumePercent}% volume)";
        }
    }
    public ImageSource? PreviewBackgroundImage => m_previewBackgroundImage;
    public string PreviewHudHealthText =>
        Document is null
            ? "0 / 0"
            : $"{Document.PlayerStats.Health:0} / {Document.PlayerStats.MaxHealth:0}";
    public double PreviewHudHealthPercent =>
        Document is null || Document.PlayerStats.MaxHealth <= 0.0
            ? 0.0
            : Math.Clamp(Document.PlayerStats.Health / Document.PlayerStats.MaxHealth * 100.0, 0.0, 100.0);
    public string PreviewHudWeaponText
    {
        get
        {
            if (Document?.PlayerWeapon is null || string.IsNullOrWhiteSpace(Document.PlayerWeapon.File)) {
                return "Unarmed";
            }

            return !string.IsNullOrWhiteSpace(m_weaponMetadata?.Weapon)
                ? m_weaponMetadata.Weapon
                : Path.GetFileNameWithoutExtension(Document.PlayerWeapon.File);
        }
    }
    public string Preview3DViewMode => m_preview3DViewMode;
    public Model3DGroup Preview3DModel { get; private set; } = new();
    public IReadOnlyDictionary<Model3D, WorldPreview3DHitTarget> Preview3DHitTargets { get; private set; } =
        new Dictionary<Model3D, WorldPreview3DHitTarget>();
    public PerspectiveCamera Preview3DCamera { get; } = new() {
        FieldOfView = 50,
        Position = new Point3D(4.0, 5.0, -8.0),
        LookDirection = new Vector3D(0.0, -4.0, 8.0),
        UpDirection = new Vector3D(0.0, 1.0, 0.0)
    };
    public PerspectiveCamera SelectedCellPreview3DCamera { get; } = new() {
        FieldOfView = 48,
        Position = new Point3D(1.8, 1.35, -2.1),
        LookDirection = new Vector3D(-1.3, -0.75, 2.6),
        UpDirection = new Vector3D(0.0, 1.0, 0.0)
    };
    public PerspectiveCamera SelectedBlockPreview3DCamera { get; } = new() {
        FieldOfView = 48,
        Position = new Point3D(1.8, 1.35, -2.1),
        LookDirection = new Vector3D(-1.3, -0.75, 2.6),
        UpDirection = new Vector3D(0.0, 1.0, 0.0)
    };
    public string Preview3DSummary { get; private set; } = "No map loaded";
    public Model3DGroup SelectedCellPreview3DModel { get; private set; } = new();
    public Model3DGroup SelectedBlockPreview3DModel { get; private set; } = new();
    public Dictionary<Model3D, WorldPreview3DHitTarget> SelectedCellPreview3DHitTargets { get; private set; } = new();
    public Dictionary<Model3D, WorldPreview3DHitTarget> SelectedBlockPreview3DHitTargets { get; private set; } = new();
    public string SelectedCellBlockId => SelectedCell?.Cell.BlockId ?? string.Empty;
    public string SelectedCellBlockSummary => SelectedCell is null
        ? "No selected cell"
        : string.IsNullOrWhiteSpace(SelectedCell.Cell.BlockId)
            ? "Legacy packed cell"
            : $"Block {SelectedCell.Cell.BlockId}";
    public int SelectedCellBlockReferenceCount =>
        Document is null || SelectedCell is null || string.IsNullOrWhiteSpace(SelectedCell.Cell.BlockId)
            ? 0
            : CountBlockReferences(SelectedCell.Cell.BlockId);
    public bool IsSelectedCellBlockUnique =>
        SelectedCellBlockReferenceCount <= 1;
    public string SelectedCellInstanceSummary =>
        SelectedCell is null
            ? "Select a cell to edit its instance."
            : string.IsNullOrWhiteSpace(SelectedCell.Cell.BlockId)
                ? "Cell edits apply to this legacy packed cell only."
                : IsCellTemplateEditScopeSelected
                    ? $"Edits update shared template {SelectedCell.Cell.BlockId}; {SelectedCellBlockReferenceCount} cell(s) use it."
                    : IsSelectedCellBlockUnique
                        ? $"Edits update this selected cell. Block {SelectedCell.Cell.BlockId} has one reference."
                        : $"Edits create/update a private copy for this cell. Template {SelectedCell.Cell.BlockId} is used by {SelectedCellBlockReferenceCount} cells.";
    public string SelectedCellFloorTextureKey
    {
        get => SelectedCellBlock?.Floor?.Texture ?? string.Empty;
        set
        {
            if (SelectedCellFloorTextureKey == value) {
                return;
            }

            EditSelectedCellBlock(block => SetSurfaceTexture(block, isFloor: true, value));
        }
    }
    public string SelectedCellCeilingTextureKey
    {
        get => SelectedCellBlock?.Ceiling?.Texture ?? string.Empty;
        set
        {
            if (SelectedCellCeilingTextureKey == value) {
                return;
            }

            EditSelectedCellBlock(block => SetSurfaceTexture(block, isFloor: false, value));
        }
    }
    public string SelectedCellLowerWallTextureKey
    {
        get => LowerWallSpan(SelectedCellBlock)?.Texture ?? string.Empty;
        set
        {
            if (SelectedCellLowerWallTextureKey == value) {
                return;
            }

            EditSelectedCellBlock(block => SetWallTexture(block, WallSlot.Lower, value));
        }
    }
    public string SelectedCellUpperWallTextureKey
    {
        get => UpperWallSpan(SelectedCellBlock)?.Texture ?? string.Empty;
        set
        {
            if (SelectedCellUpperWallTextureKey == value) {
                return;
            }

            EditSelectedCellBlock(block => SetWallTexture(block, WallSlot.Upper, value));
        }
    }
    public string SelectedCellTransparentWallTextureKey
    {
        get => TransparentWallSpan(SelectedCellBlock)?.Texture ?? string.Empty;
        set
        {
            if (SelectedCellTransparentWallTextureKey == value) {
                return;
            }

            EditSelectedCellBlock(block => SetWallTexture(block, WallSlot.Transparent, value));
        }
    }
    public IReadOnlyList<string> SelectedCellDoorKeyOptions { get; } =
        [string.Empty, "green", "blue", "red"];
    public string SelectedCellDoorSummary => DoorSummary(SelectedCellBlock?.Door);
    public bool SelectedCellDoorEnabled
    {
        get => SelectedCellBlock?.Door?.Enabled ?? false;
        set
        {
            if (SelectedCellDoorEnabled == value) {
                return;
            }

            EditSelectedCellBlock(block => EnsureDoor(block).Enabled = value);
        }
    }
    public bool SelectedCellDoorBlocksWhenClosed
    {
        get => SelectedCellBlock?.Door?.BlocksWhenClosed ?? true;
        set
        {
            if (SelectedCellDoorBlocksWhenClosed == value) {
                return;
            }

            EditSelectedCellBlock(block => EnsureDoor(block).BlocksWhenClosed = value);
        }
    }
    public string SelectedCellDoorRequiredKey
    {
        get => SelectedCellBlock?.Door?.RequiredKey ?? string.Empty;
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (string.Equals(SelectedCellBlock?.Door?.RequiredKey, normalized, StringComparison.OrdinalIgnoreCase)) {
                return;
            }

            EditSelectedCellBlock(block => EnsureDoor(block).RequiredKey = normalized);
        }
    }
    public double SelectedCellDoorTriggerDistanceCells
    {
        get => SelectedCellBlock?.Door?.TriggerDistanceCells ?? 1.25;
        set
        {
            if (Math.Abs(SelectedCellDoorTriggerDistanceCells - value) < 1e-9) {
                return;
            }

            EditSelectedCellBlock(block => EnsureDoor(block).TriggerDistanceCells = value);
        }
    }
    public double SelectedCellDoorOpenTimeSeconds
    {
        get => SelectedCellBlock?.Door?.OpenTimeSeconds ?? 0.45;
        set
        {
            if (Math.Abs(SelectedCellDoorOpenTimeSeconds - value) < 1e-9) {
                return;
            }

            EditSelectedCellBlock(block => EnsureDoor(block).OpenTimeSeconds = value);
        }
    }
    public double SelectedCellDoorCloseDelaySeconds
    {
        get => SelectedCellBlock?.Door?.CloseDelaySeconds ?? 1.0;
        set
        {
            if (Math.Abs(SelectedCellDoorCloseDelaySeconds - value) < 1e-9) {
                return;
            }

            EditSelectedCellBlock(block => EnsureDoor(block).CloseDelaySeconds = value);
        }
    }
    public string SelectedCellDoorGreenOverlayTexture
    {
        get => DoorOverlayTexture(SelectedCellBlock, "green");
        set => SetSelectedCellDoorOverlayTexture("green", value);
    }
    public string SelectedCellDoorBlueOverlayTexture
    {
        get => DoorOverlayTexture(SelectedCellBlock, "blue");
        set => SetSelectedCellDoorOverlayTexture("blue", value);
    }
    public string SelectedCellDoorRedOverlayTexture
    {
        get => DoorOverlayTexture(SelectedCellBlock, "red");
        set => SetSelectedCellDoorOverlayTexture("red", value);
    }
    public string SelectedCellDoorFramesText
    {
        get
        {
            var door = SelectedCellBlock?.Door;
            return door is null ? string.Empty : string.Join(", ", door.Frames);
        }
    }
    public bool PreviewShowGrid
    {
        get => m_previewShowGrid;
        set => SetPreview3DLayerVisibility(ref m_previewShowGrid, value);
    }
    public bool PreviewShowFloors
    {
        get => m_previewShowFloors;
        set => SetPreview3DLayerVisibility(ref m_previewShowFloors, value);
    }
    public bool PreviewShowCeilings
    {
        get => m_previewShowCeilings;
        set => SetPreview3DLayerVisibility(ref m_previewShowCeilings, value);
    }
    public bool PreviewShowWalls
    {
        get => m_previewShowWalls;
        set => SetPreview3DLayerVisibility(ref m_previewShowWalls, value);
    }
    public bool PreviewShowSprites
    {
        get => m_previewShowSprites;
        set => SetPreview3DLayerVisibility(ref m_previewShowSprites, value);
    }
    public bool PreviewShowPlayer
    {
        get => m_previewShowPlayer;
        set => SetPreview3DLayerVisibility(ref m_previewShowPlayer, value);
    }
    public string SpriteClipboardSummary => m_copiedSprite is null
        ? "Pending operation: none"
        : m_isSpriteCutPending
            ? $"Pending operation: cut {m_copiedSprite.Name}"
            : $"Pending operation: copy {m_copiedSprite.Name}";
    public System.Windows.Media.ImageSource? SelectedSpritePreview =>
        SelectedSprite is not null
        && m_spriteMapPreviews.TryGetValue(SelectedSprite.SpriteSet, out var preview)
            ? preview
            : null;
    private string m_selectedLayer = "Walls";
    public string SelectedLayer
    {
        get => m_selectedLayer;
        set
        {
            if (m_selectedLayer == value) {
                return;
            }

            m_selectedLayer = value;
            foreach (var cell in Cells) {
                cell.SelectedLayer = value;
            }

            if (IsSpriteLayerSelected) {
                SelectFirstSpriteFromSelectedCell();
            }

            SelectedPaintTarget = value switch
            {
                "Floor" => "Floor",
                "Ceiling" => "Ceiling",
                "Walls" => "Wall",
                _ => SelectedPaintTarget
            };

            OnPropertyChanged();
            OnPropertyChanged(nameof(IsSpriteLayerSelected));
            OnPropertyChanged(nameof(IsCellEditingLayerSelected));
            OnPropertyChanged(nameof(IsWallLayerSelected));
            OnPropertyChanged(nameof(IsFloorLayerSelected));
            OnPropertyChanged(nameof(IsCeilingLayerSelected));
            OnPropertyChanged(nameof(IsGoalLayerSelected));
            OnPropertyChanged(nameof(SelectedPaintTarget));
            CopyCellCommand.RaiseCanExecuteChanged();
            PasteCellCommand.RaiseCanExecuteChanged();
            CopySpriteCommand.RaiseCanExecuteChanged();
            CutSpriteCommand.RaiseCanExecuteChanged();
            PasteSpriteCommand.RaiseCanExecuteChanged();
        }
    }
    public string SelectedPaintTarget { get; set; } = "Wall";
    public bool IsPaintModeEnabled { get; set; }
    private string m_selectedCellEditScope = CellInstanceEditScope;
    public string SelectedCellEditScope
    {
        get => m_selectedCellEditScope;
        set
        {
            if (m_selectedCellEditScope == value) {
                return;
            }

            m_selectedCellEditScope = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsCellTemplateEditScopeSelected));
            OnPropertyChanged(nameof(SelectedCellInstanceSummary));
            MakeSelectedCellUniqueBlockCommand.RaiseCanExecuteChanged();
            EditSelectedCellUniqueBlockCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsCellTemplateEditScopeSelected =>
        string.Equals(SelectedCellEditScope, BlockTemplateEditScope, StringComparison.Ordinal);
    public RelayCommand OpenWorldJsonCommand { get; }
    public RelayCommand SaveWorldJsonAsCommand { get; }
    public RelayCommand SaveAllOpenJsonFilesCommand { get; }
    public RelayCommand BrowseWorldBackgroundCommand { get; }
    public RelayCommand ClearWorldBackgroundCommand { get; }
    public RelayCommand BrowseLevelBackgroundMusicCommand { get; }
    public RelayCommand ClearLevelBackgroundMusicCommand { get; }
    public RelayCommand CopyCellCommand { get; }
    public RelayCommand PasteCellCommand { get; }
    public RelayCommand UndoCommand { get; }
    public RelayCommand RedoCommand { get; }
    public RelayCommand CloneSelectedWorldLayerCommand { get; }
    public RelayCommand RenameSelectedWorldLayerCommand { get; }
    public RelayCommand DeleteSelectedWorldLayerCommand { get; }
    public RelayCommand CloneSelectedBlockCommand { get; }
    public RelayCommand RemoveDuplicateBlocksCommand { get; }
    public RelayCommand RemoveUnusedBlocksCommand { get; }
    public RelayCommand OpenSelectedCellBlockCommand { get; }
    public RelayCommand MakeSelectedCellUniqueBlockCommand { get; }
    public RelayCommand EditSelectedCellUniqueBlockCommand { get; }
    public RelayCommand PaintSelectedCellCommand { get; }
    public RelayCommand ClearSelectedCellWallsCommand { get; }
    public RelayCommand ClearSelectedCellSurfacesCommand { get; }
    public RelayCommand ValidateCommand { get; }
    public RelayCommand OpenSpriteMetadataCommand { get; }
    public RelayCommand AddSpriteToSelectedCellCommand { get; }
    public RelayCommand AddItemToSelectedCellCommand { get; }
    public RelayCommand RemoveSelectedSpriteCommand { get; }
    public RelayCommand CopySpriteCommand { get; }
    public RelayCommand CutSpriteCommand { get; }
    public RelayCommand PasteSpriteCommand { get; }
    public RelayCommand CancelSpriteClipboardCommand { get; }
    public RelayCommand PlacePlayerAtSelectedCellCommand { get; }
    public RelayCommand SetSelectedCellAsGameGoalCommand { get; }
    public RelayCommand ClearGameGoalCommand { get; }
    public RelayCommand OpenProjectCommand { get; }
    public RelayCommand SaveProjectAsCommand { get; }
    public RelayCommand SaveSpriteMetadataAsCommand { get; }
    public RelayCommand ReloadWeaponMetadataCommand { get; }
    public RelayCommand SaveWeaponMetadataCommand { get; }
    public RelayCommand SaveWeaponMetadataAsCommand { get; }
    public RelayCommand RefreshWeaponLibraryCommand { get; }
    public RelayCommand UseSelectedWeaponAsPlayerCommand { get; }
    public RelayCommand OpenWeaponJsonCommand { get; }
    public RelayCommand CloneWeaponMetadataCommand { get; }
    public RelayCommand RemovePlayerWeaponCommand { get; }
    public RelayCommand AddWeaponAnimationCommand { get; }
    public RelayCommand DuplicateWeaponAnimationCommand { get; }
    public RelayCommand RemoveWeaponAnimationCommand { get; }
    public RelayCommand AddWeaponAnimationFrameCommand { get; }
    public RelayCommand DuplicateWeaponAnimationFrameCommand { get; }
    public RelayCommand RemoveWeaponAnimationFrameCommand { get; }
    public RelayCommand AddSpriteAnimationCommand { get; }
    public RelayCommand DuplicateSpriteAnimationCommand { get; }
    public RelayCommand RemoveSpriteAnimationCommand { get; }
    public RelayCommand AddSpriteAnimationFrameCommand { get; }
    public RelayCommand DuplicateSpriteAnimationFrameCommand { get; }
    public RelayCommand RemoveSpriteAnimationFrameCommand { get; }
    public RelayCommand PlayAnimationCommand { get; }
    public RelayCommand PauseAnimationCommand { get; }
    public RelayCommand StopAnimationCommand { get; }
    public RelayCommand StepAnimationForwardCommand { get; }
    public RelayCommand StepAnimationBackwardCommand { get; }
    public SpriteAnimationPlaybackController AnimationPlayback { get; } = new();
    public RelayCommand PlayWeaponAnimationCommand { get; }
    public RelayCommand PauseWeaponAnimationCommand { get; }
    public RelayCommand StopWeaponAnimationCommand { get; }
    public RelayCommand StepWeaponAnimationForwardCommand { get; }
    public RelayCommand StepWeaponAnimationBackwardCommand { get; }
    public WeaponAnimationPlaybackController WeaponAnimationPlayback { get; } = new();
    public RelayCommand ZoomInCommand { get; }
    public RelayCommand ZoomOutCommand { get; }
    public RelayCommand ResetZoomCommand { get; }
    public RelayCommand PreviewAngledCameraCommand { get; }
    public RelayCommand PreviewTopCameraCommand { get; }
    public RelayCommand PreviewPlayerCameraCommand { get; }
    public RelayCommand PreviewRotateLeftCommand { get; }
    public RelayCommand PreviewRotateRightCommand { get; }
    public RelayCommand PreviewZoomInCommand { get; }
    public RelayCommand PreviewZoomOutCommand { get; }
    public RelayCommand PreviewFitAllCommand { get; }
    public RelayCommand PreviewMoveForwardCommand { get; }
    public RelayCommand PreviewMoveBackwardCommand { get; }
    public RelayCommand PreviewStrafeLeftCommand { get; }
    public RelayCommand PreviewStrafeRightCommand { get; }
    public RelayCommand InspectorPreviewRotateLeftCommand { get; }
    public RelayCommand InspectorPreviewRotateRightCommand { get; }
    public RelayCommand InspectorPreviewZoomInCommand { get; }
    public RelayCommand InspectorPreviewZoomOutCommand { get; }
    public RelayCommand InspectorPreviewShiftLeftCommand { get; }
    public RelayCommand InspectorPreviewShiftRightCommand { get; }
    public RelayCommand InspectorPreviewShiftUpCommand { get; }
    public RelayCommand InspectorPreviewShiftDownCommand { get; }
    public RelayCommand InspectorPreviewFitCommand { get; }
    public RelayCommand ExportCommand { get; }
    public RelayCommand TestIn3DCommand { get; }
    public RelayCommand AboutCommand { get; }

    private double m_mapZoomScale = 1.0;
    public double MapZoomScale
    {
        get => m_mapZoomScale;
        set
        {
            var clamped = Math.Clamp(value, 0.5, 2.5);
            if (Math.Abs(m_mapZoomScale - clamped) < 0.001) {
                return;
            }

            m_mapZoomScale = clamped;
            OnPropertyChanged();
            OnPropertyChanged(nameof(MapZoomText));
            ZoomInCommand.RaiseCanExecuteChanged();
            ZoomOutCommand.RaiseCanExecuteChanged();
        }
    }

    public string MapZoomText => $"{MapZoomScale:P0}";

    public bool HasGameGoal => Document?.GameGoal is not null;

    public string GameGoalSummary => Document?.GameGoal is { } goal
        ? $"{goal.Layer}: cell {goal.Column}, {goal.Row}"
        : "No final cell configured";

    public string GameGoalRequiredKey
    {
        get => Document?.GameGoal?.RequiredKey ?? string.Empty;
        set
        {
            if (Document?.GameGoal is not { } current) {
                return;
            }

            var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (string.Equals(current.RequiredKey, normalized, StringComparison.Ordinal)) {
                return;
            }

            var before = CloneGameGoal(current)!;
            var after = CloneGameGoal(current)!;
            after.RequiredKey = normalized;
            ApplyGameGoal(after);
            if (!m_isApplyingHistory) {
                RecordUndoAction(new GameGoalUndoAction(this, before, after));
            }

            RefreshValidation("Updated the final-cell key requirement.");
        }
    }

    private PlayerStartViewModel? m_playerStart;
    public PlayerStartViewModel? PlayerStart
    {
        get => m_playerStart;
        private set
        {
            if (m_playerStart == value) {
                return;
            }

            if (m_playerStart is not null) {
                m_playerStart.PropertyChanged -= OnPlayerStartChanged;
                m_playerStart.StartChanged -= OnPlayerStartValueChanged;
            }

            m_playerStart = value;

            if (m_playerStart is not null) {
                m_playerStart.PropertyChanged += OnPlayerStartChanged;
                m_playerStart.StartChanged += OnPlayerStartValueChanged;
            }

            OnPropertyChanged();
        }
    }

    public string PlayerWeaponFile
    {
        get => Document?.PlayerWeapon?.File ?? string.Empty;
        set
        {
            if (Document is null || PlayerWeaponFile == value) {
                return;
            }

            EnsurePlayerWeapon().File = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PlayerWeaponSummary));
            OnPropertyChanged(nameof(PreviewHudWeaponText));
            LoadPlayerWeaponMetadata(addValidationMessage: false);
            RefreshWeaponLibrarySelection(ResolvePlayerWeaponMetadataPath());
            RemovePlayerWeaponCommand.RaiseCanExecuteChanged();
        }
    }

    public bool PlayerWeaponVisible
    {
        get => Document?.PlayerWeapon?.Visible ?? false;
        set
        {
            if (Document is null || PlayerWeaponVisible == value) {
                return;
            }

            EnsurePlayerWeapon().Visible = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PlayerWeaponSummary));
            OnPropertyChanged(nameof(PreviewHudWeaponText));
        }
    }

    public double PlayerWeaponScreenHeightPercent
    {
        get
        {
            var fraction = Document?.PlayerWeapon?.ScreenHeightFraction ?? 0.0;
            if (fraction <= 0.0) {
                fraction = 0.45;
            }

            return Math.Round(fraction * 100.0, 1);
        }
        set
        {
            if (Document is null) {
                return;
            }

            var clamped = Math.Clamp(value, 5.0, 125.0) / 100.0;
            var weapon = EnsurePlayerWeapon();
            if (Math.Abs(weapon.ScreenHeightFraction - clamped) < 0.0001) {
                return;
            }

            weapon.ScreenHeightFraction = clamped;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PlayerWeaponSummary));
            OnPropertyChanged(nameof(PreviewHudWeaponText));
        }
    }

    public string PlayerWeaponSummary => Document?.PlayerWeapon is null
        || string.IsNullOrWhiteSpace(Document.PlayerWeapon.File)
            ? "No player weapon"
            : $"{Document.PlayerWeapon.File} ({PlayerWeaponScreenHeightPercent:0.#}% screen height)";

    public double PlayerMaxHealth
    {
        get => Document?.PlayerStats.MaxHealth ?? 0.0;
        set
        {
            if (Document is null) {
                return;
            }

            var clamped = Math.Max(1.0, value);
            if (Math.Abs(Document.PlayerStats.MaxHealth - clamped) < 0.001) {
                return;
            }

            Document.PlayerStats.MaxHealth = clamped;
            if (Document.PlayerStats.Health > clamped) {
                Document.PlayerStats.Health = clamped;
                OnPropertyChanged(nameof(PlayerHealth));
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(PreviewHudHealthText));
            OnPropertyChanged(nameof(PreviewHudHealthPercent));
            RefreshValidation("Map validation completed.");
        }
    }

    public double PlayerHealth
    {
        get => Document?.PlayerStats.Health ?? 0.0;
        set
        {
            if (Document is null) {
                return;
            }

            var clamped = Math.Clamp(value, 0.0, Document.PlayerStats.MaxHealth);
            if (Math.Abs(Document.PlayerStats.Health - clamped) < 0.001) {
                return;
            }

            Document.PlayerStats.Health = clamped;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PreviewHudHealthText));
            OnPropertyChanged(nameof(PreviewHudHealthPercent));
            RefreshValidation("Map validation completed.");
        }
    }

    // Progressive player-turn feel (degrees/second), persisted in the world JSON
    // "playerTurn" object and consumed by the engine.
    public double PlayerTurnBaseDegreesPerSecond
    {
        get => Document?.PlayerTurn.BaseDegreesPerSecond ?? 0.0;
        set => SetPlayerTurnValue(value, isBase: true);
    }

    public double PlayerTurnMaxDegreesPerSecond
    {
        get => Document?.PlayerTurn.MaxDegreesPerSecond ?? 0.0;
        set => SetPlayerTurnValue(value, isBase: false, isMax: true);
    }

    public double PlayerTurnAccelerationDegreesPerSecondSquared
    {
        get => Document?.PlayerTurn.AccelerationDegreesPerSecondSquared ?? 0.0;
        set => SetPlayerTurnValue(value, isBase: false, isMax: false);
    }

    private void SetPlayerTurnValue(double value, bool isBase, bool isMax = false)
    {
        if (Document is null) {
            return;
        }

        var turn = Document.PlayerTurn;
        var clamped = Math.Max(1.0, value);

        if (isBase) {
            if (Math.Abs(turn.BaseDegreesPerSecond - clamped) < 0.001) {
                return;
            }

            turn.BaseDegreesPerSecond = clamped;
            if (turn.MaxDegreesPerSecond < clamped) {
                turn.MaxDegreesPerSecond = clamped;
                OnPropertyChanged(nameof(PlayerTurnMaxDegreesPerSecond));
            }

            OnPropertyChanged(nameof(PlayerTurnBaseDegreesPerSecond));
        }
        else if (isMax) {
            var floor = Math.Max(clamped, turn.BaseDegreesPerSecond);
            if (Math.Abs(turn.MaxDegreesPerSecond - floor) < 0.001) {
                return;
            }

            turn.MaxDegreesPerSecond = floor;
            OnPropertyChanged(nameof(PlayerTurnMaxDegreesPerSecond));
        }
        else {
            if (Math.Abs(turn.AccelerationDegreesPerSecondSquared - clamped) < 0.001) {
                return;
            }

            turn.AccelerationDegreesPerSecondSquared = clamped;
            OnPropertyChanged(nameof(PlayerTurnAccelerationDegreesPerSecondSquared));
        }

        RefreshValidation("Map validation completed.");
    }

    private void ScheduleSpriteValidation()
    {
        m_spriteValidationTimer ??= new System.Windows.Threading.DispatcherTimer(
            System.Windows.Threading.DispatcherPriority.Background) {
            Interval = TimeSpan.FromMilliseconds(300)
        };
        m_spriteValidationTimer.Stop();
        m_spriteValidationTimer.Tick -= OnSpriteValidationTimerTick;
        m_spriteValidationTimer.Tick += OnSpriteValidationTimerTick;
        m_spriteValidationTimer.Start();
    }

    private void OnSpriteValidationTimerTick(object? sender, EventArgs args)
    {
        m_spriteValidationTimer?.Stop();
        RefreshValidation("Map validation completed.", refreshPreview: false);
    }

    public string WeaponMetadataSummary { get; private set; } = "No weapon metadata loaded";

    public string WeaponName
    {
        get => m_weaponMetadata?.Weapon ?? string.Empty;
        set
        {
            if (m_weaponMetadata is null || m_weaponMetadata.Weapon == value) {
                return;
            }

            m_weaponMetadata.Weapon = value;
            OnPropertyChanged();
            NotifyWeaponMetadataChanged();
        }
    }

    public string WeaponFormat
    {
        get => m_weaponMetadata?.Format ?? string.Empty;
        set
        {
            if (m_weaponMetadata is null || m_weaponMetadata.Format == value) {
                return;
            }

            m_weaponMetadata.Format = value;
            OnPropertyChanged();
            NotifyWeaponMetadataChanged();
        }
    }

    public int WeaponFrameWidth
    {
        get => m_weaponMetadata?.FrameWidth ?? 0;
        set
        {
            if (m_weaponMetadata is null) {
                return;
            }

            var clamped = Math.Max(1, value);
            if (m_weaponMetadata.FrameWidth == clamped) {
                return;
            }

            m_weaponMetadata.FrameWidth = clamped;
            OnPropertyChanged();
            NotifyWeaponMetadataChanged();
        }
    }

    public int WeaponFrameHeight
    {
        get => m_weaponMetadata?.FrameHeight ?? 0;
        set
        {
            if (m_weaponMetadata is null) {
                return;
            }

            var clamped = Math.Max(1, value);
            if (m_weaponMetadata.FrameHeight == clamped) {
                return;
            }

            m_weaponMetadata.FrameHeight = clamped;
            OnPropertyChanged();
            NotifyWeaponMetadataChanged();
        }
    }

    public double WeaponMetadataScreenHeightPercent
    {
        get => Math.Round((m_weaponMetadata?.ScreenHeightFraction ?? 0.0) * 100.0, 1);
        set
        {
            if (m_weaponMetadata is null) {
                return;
            }

            var clamped = Math.Clamp(value, 5.0, 125.0) / 100.0;
            if (Math.Abs(m_weaponMetadata.ScreenHeightFraction - clamped) < 0.0001) {
                return;
            }

            m_weaponMetadata.ScreenHeightFraction = clamped;
            OnPropertyChanged();
            NotifyWeaponMetadataChanged();
        }
    }

    public double WeaponDamage
    {
        get => m_weaponMetadata?.Damage ?? 0.0;
        set
        {
            if (m_weaponMetadata is null) {
                return;
            }

            var clamped = Math.Max(0.0, value);
            if (Math.Abs(m_weaponMetadata.Damage - clamped) < 0.001) {
                return;
            }

            m_weaponMetadata.Damage = clamped;
            OnPropertyChanged();
            NotifyWeaponMetadataChanged();
        }
    }

    public double WeaponRangeCells
    {
        get => m_weaponMetadata?.RangeCells ?? 0.0;
        set
        {
            if (m_weaponMetadata is null) {
                return;
            }

            var clamped = Math.Max(0.0, value);
            if (Math.Abs(m_weaponMetadata.RangeCells - clamped) < 0.001) {
                return;
            }

            m_weaponMetadata.RangeCells = clamped;
            OnPropertyChanged();
            NotifyWeaponMetadataChanged();
        }
    }

    public string WeaponFireSound
    {
        get => m_weaponMetadata?.Sounds?.Fire ?? string.Empty;
        set
        {
            if (m_weaponMetadata is null) {
                return;
            }

            var sounds = m_weaponMetadata.Sounds ??= new WeaponSoundMetadata();
            if (sounds.Fire == value) {
                return;
            }

            sounds.Fire = value;
            OnPropertyChanged();
            NotifyWeaponMetadataChanged();
        }
    }

    public int WeaponMagazineSize
    {
        get => m_weaponMetadata?.Ammo?.MagazineSize ?? 0;
        set
        {
            if (m_weaponMetadata is null) {
                return;
            }

            var ammo = m_weaponMetadata.Ammo ??= new WeaponAmmoMetadata();
            var clamped = Math.Max(0, value);
            if (ammo.MagazineSize == clamped) {
                return;
            }

            ammo.MagazineSize = clamped;
            OnPropertyChanged();
            NotifyWeaponMetadataChanged();
        }
    }

    public int WeaponMaxAmmo
    {
        get => m_weaponMetadata?.Ammo?.MaxAmmo ?? 0;
        set
        {
            if (m_weaponMetadata is null) {
                return;
            }

            var ammo = m_weaponMetadata.Ammo ??= new WeaponAmmoMetadata();
            var clamped = Math.Max(0, value);
            if (ammo.MaxAmmo == clamped) {
                return;
            }

            ammo.MaxAmmo = clamped;
            OnPropertyChanged();
            NotifyWeaponMetadataChanged();
        }
    }

    public int WeaponInitialAmmo
    {
        get => m_weaponMetadata?.Ammo?.InitialAmmo ?? -1;
        set
        {
            if (m_weaponMetadata is null) {
                return;
            }

            var ammo = m_weaponMetadata.Ammo ??= new WeaponAmmoMetadata();
            var clamped = Math.Max(-1, value);
            if (ammo.InitialAmmo == clamped) {
                return;
            }

            ammo.InitialAmmo = clamped;
            OnPropertyChanged();
            NotifyWeaponMetadataChanged();
        }
    }

    public double WeaponAnchorX
    {
        get => m_weaponMetadata?.Anchor.X ?? 0.0;
        set => SetWeaponPointValue(m_weaponMetadata?.Anchor, isX: true, value);
    }

    public double WeaponAnchorY
    {
        get => m_weaponMetadata?.Anchor.Y ?? 0.0;
        set => SetWeaponPointValue(m_weaponMetadata?.Anchor, isX: false, value);
    }

    public double WeaponBaseOffsetX
    {
        get => m_weaponMetadata?.BaseOffset.X ?? 0.0;
        set => SetWeaponPointValue(m_weaponMetadata?.BaseOffset, isX: true, value);
    }

    public double WeaponBaseOffsetY
    {
        get => m_weaponMetadata?.BaseOffset.Y ?? 0.0;
        set => SetWeaponPointValue(m_weaponMetadata?.BaseOffset, isX: false, value);
    }

    public double WeaponBobAmplitudeX
    {
        get => m_weaponMetadata?.Bob.AmplitudeX ?? 0.0;
        set
        {
            if (m_weaponMetadata is null || Math.Abs(m_weaponMetadata.Bob.AmplitudeX - value) < 0.001) {
                return;
            }

            m_weaponMetadata.Bob.AmplitudeX = Math.Max(0.0, value);
            OnPropertyChanged();
            NotifyWeaponMetadataChanged();
        }
    }

    public bool WeaponBobEnabled
    {
        get => m_weaponMetadata?.Bob.Enabled ?? false;
        set
        {
            if (m_weaponMetadata is null || m_weaponMetadata.Bob.Enabled == value) {
                return;
            }

            m_weaponMetadata.Bob.Enabled = value;
            OnPropertyChanged();
            NotifyWeaponMetadataChanged();
        }
    }

    public double WeaponBobAmountPercent
    {
        get => Math.Round((m_weaponMetadata?.Bob.Amount ?? 0.0) * 100.0, 1);
        set
        {
            if (m_weaponMetadata is null) {
                return;
            }

            var clamped = Math.Clamp(value, 0.0, 200.0) / 100.0;
            if (Math.Abs(m_weaponMetadata.Bob.Amount - clamped) < 0.0001) {
                return;
            }

            m_weaponMetadata.Bob.Amount = clamped;
            OnPropertyChanged();
            NotifyWeaponMetadataChanged();
        }
    }

    public double WeaponBobAmplitudeY
    {
        get => m_weaponMetadata?.Bob.AmplitudeY ?? 0.0;
        set
        {
            if (m_weaponMetadata is null || Math.Abs(m_weaponMetadata.Bob.AmplitudeY - value) < 0.001) {
                return;
            }

            m_weaponMetadata.Bob.AmplitudeY = Math.Max(0.0, value);
            OnPropertyChanged();
            NotifyWeaponMetadataChanged();
        }
    }

    public double WeaponBobFrequencyHz
    {
        get => m_weaponMetadata?.Bob.FrequencyHz ?? 0.0;
        set
        {
            if (m_weaponMetadata is null || Math.Abs(m_weaponMetadata.Bob.FrequencyHz - value) < 0.001) {
                return;
            }

            m_weaponMetadata.Bob.FrequencyHz = Math.Max(0.0, value);
            OnPropertyChanged();
            NotifyWeaponMetadataChanged();
        }
    }

    private WeaponAnimationViewModel? m_selectedWeaponAnimation;
    public WeaponAnimationViewModel? SelectedWeaponAnimation
    {
        get => m_selectedWeaponAnimation;
        set
        {
            if (m_selectedWeaponAnimation == value) {
                return;
            }

            if (m_selectedWeaponAnimation is not null) {
                m_selectedWeaponAnimation.PropertyChanged -= OnSelectedWeaponAnimationPropertyChanged;
            }

            m_selectedWeaponAnimation = value;
            if (m_selectedWeaponAnimation is not null) {
                m_selectedWeaponAnimation.PropertyChanged += OnSelectedWeaponAnimationPropertyChanged;
            }

            RefreshWeaponAnimationFrames();
            RefreshWeaponAnimationPlayback();
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedWeaponAnimationSummary));
            RaiseWeaponAnimationCanExecuteChanged();
        }
    }

    private WeaponAnimationFrameViewModel? m_selectedWeaponAnimationFrame;
    public WeaponAnimationFrameViewModel? SelectedWeaponAnimationFrame
    {
        get => m_selectedWeaponAnimationFrame;
        set
        {
            if (m_selectedWeaponAnimationFrame == value) {
                return;
            }

            if (m_selectedWeaponAnimationFrame is not null) {
                m_selectedWeaponAnimationFrame.PropertyChanged -= OnSelectedWeaponAnimationFramePropertyChanged;
            }

            m_selectedWeaponAnimationFrame = value;
            if (m_selectedWeaponAnimationFrame is not null) {
                m_selectedWeaponAnimationFrame.PropertyChanged += OnSelectedWeaponAnimationFramePropertyChanged;
                WeaponAnimationPlayback.SelectFrame(m_selectedWeaponAnimationFrame.Index);
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedWeaponAnimationSummary));
            RaiseWeaponAnimationCanExecuteChanged();
        }
    }

    public string SelectedWeaponAnimationSummary =>
        SelectedWeaponAnimation is null
            ? "No weapon animation selected"
            : $"{SelectedWeaponAnimation.Name}, {SelectedWeaponAnimation.Summary}";

    private EditorCellViewModel? m_selectedCell;
    public EditorCellViewModel? SelectedCell
    {
        get => m_selectedCell;
        set
        {
            if (m_selectedCell == value) {
                return;
            }

            m_selectedCell = value;
            if (value is null) {
                m_selectedMapCells = [];
            }
            else if (m_selectedMapCells.Count == 0
                || !m_selectedMapCells.Contains(value)) {
                m_selectedMapCells = [value];
            }
            m_lastSelectionKind = JsonSelectionKind.Cell;
            OnPropertyChanged();
            OnPropertyChanged(nameof(MapSelectionSummary));
            CopyCellCommand.RaiseCanExecuteChanged();
            PasteCellCommand.RaiseCanExecuteChanged();
            PasteSpriteCommand.RaiseCanExecuteChanged();
            PaintSelectedCellCommand.RaiseCanExecuteChanged();
            ClearSelectedCellWallsCommand.RaiseCanExecuteChanged();
            ClearSelectedCellSurfacesCommand.RaiseCanExecuteChanged();
            AddSpriteToSelectedCellCommand.RaiseCanExecuteChanged();
            AddItemToSelectedCellCommand.RaiseCanExecuteChanged();
            PlacePlayerAtSelectedCellCommand.RaiseCanExecuteChanged();
            SetSelectedCellAsGameGoalCommand.RaiseCanExecuteChanged();
            OpenSelectedCellBlockCommand.RaiseCanExecuteChanged();
            MakeSelectedCellUniqueBlockCommand.RaiseCanExecuteChanged();
            EditSelectedCellUniqueBlockCommand.RaiseCanExecuteChanged();
            RefreshSelectedCellSprites();
            if (IsSpriteLayerSelected) {
                SelectFirstSpriteFromSelectedCell();
            }

            if (IsPaintModeEnabled) {
                PaintSelectedCell();
            }

            NotifySelectedCellEditorChanged();
            SchedulePreview3DRefresh();
            UpdateJsonHighlight();
        }
    }

    public void SetSelectedMapCells(IEnumerable<EditorCellViewModel> cells)
    {
        m_selectedMapCells = cells
            .Where(cell => cell is not null)
            .Distinct()
            .OrderBy(cell => cell.Row)
            .ThenBy(cell => cell.Column)
            .ToList();
        OnPropertyChanged(nameof(MapSelectionSummary));
        CopyCellCommand.RaiseCanExecuteChanged();
        PasteCellCommand.RaiseCanExecuteChanged();
    }

    private IReadOnlyList<EditorCellViewModel> CurrentMapSelection()
    {
        if (m_selectedMapCells.Count > 0) {
            return m_selectedMapCells;
        }

        return SelectedCell is null ? [] : [SelectedCell];
    }

    private TextureAssetViewModel? m_selectedTexture;
    public TextureAssetViewModel? SelectedTexture
    {
        get => m_selectedTexture;
        set
        {
            if (m_selectedTexture == value) {
                return;
            }

            m_selectedTexture = value;
            OnPropertyChanged();
            PaintSelectedCellCommand.RaiseCanExecuteChanged();
        }
    }

    private BlockPaletteEntryViewModel? m_selectedBlock;
    public BlockPaletteEntryViewModel? SelectedBlock
    {
        get => m_selectedBlock;
        set
        {
            if (m_selectedBlock == value) {
                return;
            }

            if (m_selectedBlock is not null) {
                m_selectedBlock.PropertyChanged -= OnSelectedBlockChanged;
            }

            m_selectedBlock = value;
            if (m_selectedBlock is not null) {
                m_selectedBlock.PropertyChanged += OnSelectedBlockChanged;
            }

            m_lastSelectionKind = JsonSelectionKind.Block;
            OnPropertyChanged();
            CloneSelectedBlockCommand.RaiseCanExecuteChanged();
            RefreshSelectedBlockPreview3D();
            UpdateJsonHighlight();
        }
    }

    private SpriteInstanceViewModel? m_selectedSprite;
    public SpriteInstanceViewModel? SelectedSprite
    {
        get => m_selectedSprite;
        set
        {
            if (m_isRefreshingSelectedCellSprites && value is null) {
                return;
            }

            if (m_selectedSprite == value) {
                return;
            }

            m_selectedSprite = value;
            SelectCellForSelectedSprite();
            SyncSpriteMetadataWithSelectedSprite();
            m_lastSelectionKind = JsonSelectionKind.Sprite;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedSpritePreview));
            RemoveSelectedSpriteCommand.RaiseCanExecuteChanged();
            CopySpriteCommand.RaiseCanExecuteChanged();
            CutSpriteCommand.RaiseCanExecuteChanged();
            CopyCellCommand.RaiseCanExecuteChanged();
            SchedulePreview3DRefresh();
            UpdateJsonHighlight();
        }
    }

    private SpriteDirectionViewModel? m_selectedSpriteDirection;
    public SpriteDirectionViewModel? SelectedSpriteDirection
    {
        get => m_selectedSpriteDirection;
        set
        {
            if (m_selectedSpriteDirection == value) {
                return;
            }

            if (m_selectedSpriteDirection is not null) {
                m_selectedSpriteDirection.SetPreviewSize(56);
            }

            m_selectedSpriteDirection = value;
            if (m_selectedSpriteDirection is not null) {
                m_selectedSpriteDirection.SetPreviewSize(220);
            }

            RefreshAnimationPlayback();
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedSpriteDirectionPreview));
            OnPropertyChanged(nameof(SelectedSpriteDirectionSummary));
        }
    }

    public System.Windows.Media.ImageSource? SelectedSpriteDirectionPreview =>
        SelectedSpriteDirection?.Preview;

    public string SelectedSpriteDirectionSummary =>
        SelectedSpriteDirection is null
            ? "Select a direction to preview"
            : $"{SelectedSpriteDirection.Name} - {SelectedSpriteDirection.SelectedResolution}";

    private SpriteAnimationViewModel? m_selectedSpriteAnimation;
    public SpriteAnimationViewModel? SelectedSpriteAnimation
    {
        get => m_selectedSpriteAnimation;
        set
        {
            if (m_selectedSpriteAnimation == value) {
                return;
            }

            if (m_selectedSpriteAnimation is not null) {
                m_selectedSpriteAnimation.PropertyChanged -= OnSelectedSpriteAnimationPropertyChanged;
            }

            m_selectedSpriteAnimation = value;
            if (m_selectedSpriteAnimation is not null) {
                m_selectedSpriteAnimation.PropertyChanged += OnSelectedSpriteAnimationPropertyChanged;
            }

            RefreshSpriteAnimationFrames();
            RefreshAnimationPlayback();
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedSpriteAnimationSummary));
            RaiseSpriteAnimationCanExecuteChanged();
        }
    }

    private SpriteAnimationFrameViewModel? m_selectedSpriteAnimationFrame;
    public SpriteAnimationFrameViewModel? SelectedSpriteAnimationFrame
    {
        get => m_selectedSpriteAnimationFrame;
        set
        {
            if (m_selectedSpriteAnimationFrame == value) {
                return;
            }

            m_selectedSpriteAnimationFrame = value;
            RefreshSpriteDirectionsFromSelectedAnimationFrame();
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedSpriteAnimationSummary));
            RaiseSpriteAnimationCanExecuteChanged();
        }
    }

    public string SelectedSpriteAnimationSummary =>
        SelectedSpriteAnimation is null
            ? "No animation selected"
            : $"{SelectedSpriteAnimation.Name}, {SelectedSpriteAnimation.Summary}";

    private string? m_selectedSpriteSetFile;
    public string? SelectedSpriteSetFile
    {
        get => m_selectedSpriteSetFile;
        set
        {
            if (m_selectedSpriteSetFile == value) {
                return;
            }

            m_selectedSpriteSetFile = value;
            OnPropertyChanged();
            LoadSelectedSpriteSetMetadata();
        }
    }

    private WeaponLibraryItemViewModel? m_selectedWeaponLibraryItem;
    public WeaponLibraryItemViewModel? SelectedWeaponLibraryItem
    {
        get => m_selectedWeaponLibraryItem;
        set
        {
            if (m_selectedWeaponLibraryItem == value) {
                return;
            }

            m_selectedWeaponLibraryItem = value;
            OnPropertyChanged();
            UseSelectedWeaponAsPlayerCommand.RaiseCanExecuteChanged();
            OpenWeaponJsonCommand.RaiseCanExecuteChanged();

            if (!m_isSelectingWeaponLibraryItem && value is not null) {
                LoadWeaponMetadataFrom(value.AbsolutePath, addValidationMessage: true);
            }
        }
    }

    private SpriteMetadataDocument? m_spriteMetadata;
    private string m_spriteMetadataPath = string.Empty;
    private string m_spriteMetadataDirectory = Environment.CurrentDirectory;
    private string m_loadedSpriteSetName = string.Empty;
    private WeaponMetadataDocument? m_weaponMetadata;
    private string m_weaponMetadataPath = string.Empty;
    private string m_weaponMetadataDirectory = Environment.CurrentDirectory;
    private bool m_isSelectingWeaponLibraryItem;
    private string m_projectPath = string.Empty;
    private double m_spritePreviewDistance = 3.0;
    public double SpritePreviewDistance
    {
        get => m_spritePreviewDistance;
        set
        {
            if (Math.Abs(m_spritePreviewDistance - value) < 0.001) {
                return;
            }

            m_spritePreviewDistance = value;
            UpdateSpriteLodSelection();
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public MainWindowViewModel(bool loadDefaultWorld)
    {
        JsonPanel = new JsonEditorPanelViewModel(this);
        OpenWorldJsonCommand = new RelayCommand(_ => OpenWorldJson());
        SaveWorldJsonAsCommand = new RelayCommand(_ => SaveWorldJsonAs(), _ => Document is not null);
        SaveAllOpenJsonFilesCommand = new RelayCommand(
            _ => SaveAllOpenJsonFiles(),
            _ => Document is not null || m_spriteMetadata is not null || m_weaponMetadata is not null);
        BrowseWorldBackgroundCommand = new RelayCommand(_ => BrowseWorldBackground(), _ => Document is not null);
        ClearWorldBackgroundCommand = new RelayCommand(
            _ => WorldBackgroundImage = string.Empty,
            _ => Document is not null && !string.IsNullOrWhiteSpace(WorldBackgroundImage));
        BrowseLevelBackgroundMusicCommand = new RelayCommand(
            _ => BrowseLevelBackgroundMusic(),
            _ => Document is not null);
        ClearLevelBackgroundMusicCommand = new RelayCommand(
            _ => ClearLevelBackgroundMusic(),
            _ => Document is not null && !string.IsNullOrWhiteSpace(LevelBackgroundMusicFile));
        CopyCellCommand = new RelayCommand(_ => CopySelectedCell(), _ => CanCopyMapSelection());
        PasteCellCommand = new RelayCommand(
            _ => PasteToSelectedCell(),
            _ => CanPasteToMapSelection());
        UndoCommand = new RelayCommand(_ => Undo(), _ => m_undoStack.Count > 0);
        RedoCommand = new RelayCommand(_ => Redo(), _ => m_redoStack.Count > 0);
        CloneSelectedWorldLayerCommand = new RelayCommand(
            _ => CloneSelectedWorldLayer(),
            _ => Document is not null && SelectedWorldLayer is not null);
        RenameSelectedWorldLayerCommand = new RelayCommand(
            _ => RenameSelectedWorldLayer(),
            _ => Document is not null && SelectedWorldLayer is not null);
        DeleteSelectedWorldLayerCommand = new RelayCommand(
            _ => DeleteSelectedWorldLayer(),
            _ => Document is not null && SelectedWorldLayer is not null && Document.Layers.Count > 1);
        CloneSelectedBlockCommand = new RelayCommand(
            _ => CloneSelectedBlock(),
            _ => Document is not null && SelectedBlock is not null);
        RemoveDuplicateBlocksCommand = new RelayCommand(
            _ => RemoveDuplicateBlocks(),
            _ => Document is not null && Document.Blocks.Count > 1);
        RemoveUnusedBlocksCommand = new RelayCommand(
            _ => RemoveUnusedBlocks(),
            _ => Document is not null && Document.Blocks.Count > 0);
        OpenSelectedCellBlockCommand = new RelayCommand(
            _ => OpenSelectedCellBlock(),
            _ => CanOpenSelectedCellBlock());
        MakeSelectedCellUniqueBlockCommand = new RelayCommand(
            _ => MakeSelectedCellUniqueBlock(),
            _ => CanMakeSelectedCellUniqueBlock());
        EditSelectedCellUniqueBlockCommand = new RelayCommand(
            _ => EditSelectedCellUniqueBlock(),
            _ => CanMakeSelectedCellUniqueBlock());
        PaintSelectedCellCommand = new RelayCommand(
            _ => PaintSelectedCell(),
            _ => SelectedCell is not null && SelectedTexture is not null);
        ClearSelectedCellWallsCommand = new RelayCommand(
            _ => ClearSelectedCellWalls(),
            _ => SelectedCell is not null);
        ClearSelectedCellSurfacesCommand = new RelayCommand(
            _ => ClearSelectedCellSurfaces(),
            _ => SelectedCell is not null);
        ApplySelectedTextureToFaceCommand = new RelayCommand(
            _ => ApplySelectedTextureToFace(),
            _ => SelectedCell is not null
                && SelectedTexture is not null
                && HasSelectedCellFace);
        ValidateCommand = new RelayCommand(_ => RefreshValidation("Map validation completed."), _ => Document is not null);
        OpenSpriteMetadataCommand = new RelayCommand(_ => OpenSpriteMetadata());
        AddSpriteToSelectedCellCommand = new RelayCommand(
            _ => AddSpriteToSelectedCell(),
            _ => Document is not null
                && SelectedCell is not null
                && !string.IsNullOrWhiteSpace(m_loadedSpriteSetName));
        AddItemToSelectedCellCommand = new RelayCommand(
            _ => AddItemToSelectedCell(),
            _ => Document is not null
                && SelectedCell is not null
                && !string.IsNullOrWhiteSpace(m_loadedSpriteSetName));
        RemoveSelectedSpriteCommand = new RelayCommand(
            _ => RemoveSelectedSprite(),
            _ => Document is not null && SelectedSprite is not null);
        CopySpriteCommand = new RelayCommand(
            _ => CopySelectedSprite(),
            _ => SelectedSprite is not null);
        CutSpriteCommand = new RelayCommand(
            _ => CutSelectedSprite(),
            _ => SelectedSprite is not null);
        PasteSpriteCommand = new RelayCommand(
            _ => PasteSpriteToSelectedCell(),
            _ => Document is not null && SelectedCell is not null && m_copiedSprite is not null);
        CancelSpriteClipboardCommand = new RelayCommand(
            _ => CancelSpriteClipboard(),
            _ => m_copiedSprite is not null);
        PlacePlayerAtSelectedCellCommand = new RelayCommand(
            _ => PlacePlayerAtSelectedCell(),
            _ => Document is not null && SelectedCell is not null);
        SetSelectedCellAsGameGoalCommand = new RelayCommand(
            _ => SetSelectedCellAsGameGoal(),
            _ => Document is not null
                && SelectedCell is not null
                && !string.IsNullOrWhiteSpace(Document.ActiveLayerId));
        ClearGameGoalCommand = new RelayCommand(
            _ => ClearGameGoal(),
            _ => Document?.GameGoal is not null);
        OpenProjectCommand = new RelayCommand(_ => OpenProject());
        SaveProjectAsCommand = new RelayCommand(_ => SaveProjectAs(), _ => Document is not null);
        SaveSpriteMetadataAsCommand = new RelayCommand(_ => SaveSpriteMetadataAs(), _ => m_spriteMetadata is not null);
        ReloadWeaponMetadataCommand = new RelayCommand(
            _ => ReloadWeaponMetadata(),
            _ => !string.IsNullOrWhiteSpace(m_weaponMetadataPath));
        SaveWeaponMetadataCommand = new RelayCommand(
            _ => SaveWeaponMetadata(),
            _ => m_weaponMetadata is not null && !string.IsNullOrWhiteSpace(m_weaponMetadataPath));
        SaveWeaponMetadataAsCommand = new RelayCommand(
            _ => SaveWeaponMetadataAs(),
            _ => m_weaponMetadata is not null);
        RefreshWeaponLibraryCommand = new RelayCommand(
            _ => RefreshWeaponLibrary(),
            _ => Document is not null);
        UseSelectedWeaponAsPlayerCommand = new RelayCommand(
            _ => UseSelectedWeaponAsPlayer(),
            _ => Document is not null && SelectedWeaponLibraryItem is not null);
        OpenWeaponJsonCommand = new RelayCommand(
            _ => OpenWeaponJson(),
            _ => !string.IsNullOrWhiteSpace(m_weaponMetadataPath) && File.Exists(m_weaponMetadataPath));
        CloneWeaponMetadataCommand = new RelayCommand(
            _ => CloneWeaponMetadata(),
            _ => m_weaponMetadata is not null && !string.IsNullOrWhiteSpace(m_weaponMetadataPath));
        RemovePlayerWeaponCommand = new RelayCommand(
            _ => RemovePlayerWeapon(),
            _ => Document?.PlayerWeapon is not null && !string.IsNullOrWhiteSpace(Document.PlayerWeapon.File));
        AddWeaponAnimationCommand = new RelayCommand(
            _ => AddWeaponAnimation(),
            _ => m_weaponMetadata is not null);
        DuplicateWeaponAnimationCommand = new RelayCommand(
            _ => DuplicateWeaponAnimation(),
            _ => m_weaponMetadata is not null && SelectedWeaponAnimation is not null);
        RemoveWeaponAnimationCommand = new RelayCommand(
            _ => RemoveWeaponAnimation(),
            _ => CanRemoveSelectedWeaponAnimation());
        AddWeaponAnimationFrameCommand = new RelayCommand(
            _ => AddWeaponAnimationFrame(),
            _ => SelectedWeaponAnimation is not null);
        DuplicateWeaponAnimationFrameCommand = new RelayCommand(
            _ => DuplicateWeaponAnimationFrame(),
            _ => SelectedWeaponAnimation is not null && SelectedWeaponAnimationFrame is not null);
        RemoveWeaponAnimationFrameCommand = new RelayCommand(
            _ => RemoveWeaponAnimationFrame(),
            _ => CanRemoveSelectedWeaponAnimationFrame());
        AddSpriteAnimationCommand = new RelayCommand(
            _ => AddSpriteAnimation(),
            _ => m_spriteMetadata is not null);
        DuplicateSpriteAnimationCommand = new RelayCommand(
            _ => DuplicateSpriteAnimation(),
            _ => m_spriteMetadata is not null && SelectedSpriteAnimation is not null);
        RemoveSpriteAnimationCommand = new RelayCommand(
            _ => RemoveSpriteAnimation(),
            _ => CanRemoveSelectedSpriteAnimation());
        AddSpriteAnimationFrameCommand = new RelayCommand(
            _ => AddSpriteAnimationFrame(),
            _ => SelectedSpriteAnimation is not null);
        DuplicateSpriteAnimationFrameCommand = new RelayCommand(
            _ => DuplicateSpriteAnimationFrame(),
            _ => SelectedSpriteAnimation is not null && SelectedSpriteAnimationFrame is not null);
        RemoveSpriteAnimationFrameCommand = new RelayCommand(
            _ => RemoveSpriteAnimationFrame(),
            _ => CanRemoveSelectedSpriteAnimationFrame());
        PlayAnimationCommand = new RelayCommand(
            _ => AnimationPlayback.Play(),
            _ => AnimationPlayback.CanPlay);
        PauseAnimationCommand = new RelayCommand(
            _ => AnimationPlayback.Pause(),
            _ => AnimationPlayback.CanPause);
        StopAnimationCommand = new RelayCommand(
            _ => AnimationPlayback.Stop(),
            _ => AnimationPlayback.CanStop);
        StepAnimationForwardCommand = new RelayCommand(
            _ => AnimationPlayback.StepForward(),
            _ => AnimationPlayback.CanStep);
        StepAnimationBackwardCommand = new RelayCommand(
            _ => AnimationPlayback.StepBackward(),
            _ => AnimationPlayback.CanStep);
        AnimationPlayback.PropertyChanged += OnAnimationPlaybackPropertyChanged;
        PlayWeaponAnimationCommand = new RelayCommand(
            _ => WeaponAnimationPlayback.Play(),
            _ => WeaponAnimationPlayback.CanPlay);
        PauseWeaponAnimationCommand = new RelayCommand(
            _ => WeaponAnimationPlayback.Pause(),
            _ => WeaponAnimationPlayback.CanPause);
        StopWeaponAnimationCommand = new RelayCommand(
            _ => WeaponAnimationPlayback.Stop(),
            _ => WeaponAnimationPlayback.CanStop);
        StepWeaponAnimationForwardCommand = new RelayCommand(
            _ => WeaponAnimationPlayback.StepForward(),
            _ => WeaponAnimationPlayback.CanStep);
        StepWeaponAnimationBackwardCommand = new RelayCommand(
            _ => WeaponAnimationPlayback.StepBackward(),
            _ => WeaponAnimationPlayback.CanStep);
        WeaponAnimationPlayback.PropertyChanged += OnWeaponAnimationPlaybackPropertyChanged;
        ZoomInCommand = new RelayCommand(_ => MapZoomScale += 0.1, _ => MapZoomScale < 2.5);
        ZoomOutCommand = new RelayCommand(_ => MapZoomScale -= 0.1, _ => MapZoomScale > 0.5);
        ResetZoomCommand = new RelayCommand(_ => MapZoomScale = 1.0);
        PreviewAngledCameraCommand = new RelayCommand(_ => SetPreview3DViewMode("Angled"));
        PreviewTopCameraCommand = new RelayCommand(_ => SetPreview3DViewMode("Top"));
        PreviewPlayerCameraCommand = new RelayCommand(_ => SetPreview3DViewMode("Perspective"));
        PreviewRotateLeftCommand = new RelayCommand(_ => RotatePreview3D(-15.0));
        PreviewRotateRightCommand = new RelayCommand(_ => RotatePreview3D(15.0));
        PreviewZoomInCommand = new RelayCommand(_ => ZoomPreview3D(0.82));
        PreviewZoomOutCommand = new RelayCommand(_ => ZoomPreview3D(1.22));
        PreviewFitAllCommand = new RelayCommand(_ => FitPreview3DToWorld());
        PreviewMoveForwardCommand = new RelayCommand(_ => MovePreview3D(forward: 0.45, strafe: 0.0));
        PreviewMoveBackwardCommand = new RelayCommand(_ => MovePreview3D(forward: -0.45, strafe: 0.0));
        PreviewStrafeLeftCommand = new RelayCommand(_ => MovePreview3D(forward: 0.0, strafe: -0.45));
        PreviewStrafeRightCommand = new RelayCommand(_ => MovePreview3D(forward: 0.0, strafe: 0.45));
        InspectorPreviewRotateLeftCommand = new RelayCommand(parameter => AdjustInspectorPreview(parameter, rotateDegrees: -12.0));
        InspectorPreviewRotateRightCommand = new RelayCommand(parameter => AdjustInspectorPreview(parameter, rotateDegrees: 12.0));
        InspectorPreviewZoomInCommand = new RelayCommand(parameter => AdjustInspectorPreview(parameter, zoomFactor: 0.84));
        InspectorPreviewZoomOutCommand = new RelayCommand(parameter => AdjustInspectorPreview(parameter, zoomFactor: 1.18));
        InspectorPreviewShiftLeftCommand = new RelayCommand(parameter => AdjustInspectorPreview(parameter, shiftX: -0.18));
        InspectorPreviewShiftRightCommand = new RelayCommand(parameter => AdjustInspectorPreview(parameter, shiftX: 0.18));
        InspectorPreviewShiftUpCommand = new RelayCommand(parameter => AdjustInspectorPreview(parameter, shiftZ: 0.18));
        InspectorPreviewShiftDownCommand = new RelayCommand(parameter => AdjustInspectorPreview(parameter, shiftZ: -0.18));
        InspectorPreviewFitCommand = new RelayCommand(parameter => AdjustInspectorPreview(parameter, fit: true));
        ExportCommand = new RelayCommand(_ => ExportScene(), _ => Document is not null);
        TestIn3DCommand = new RelayCommand(_ => TestIn3D(), _ => Document is not null);
        AboutCommand = new RelayCommand(_ => ShowAbout());

        if (loadDefaultWorld) {
            LoadDefaultWorld();
        }
    }

    public MainWindowViewModel()
        : this(loadDefaultWorld: true)
    {
    }

    public void SaveWorldJsonTo(string path)
    {
        if (Document is null) {
            throw new InvalidOperationException("No map document is loaded.");
        }

        var world = LegacyWorldConverter.FromEditorMap(
            Document,
            Path.GetFileNameWithoutExtension(path));
        WorldJsonDocumentService.Save(world, path);
        Document.SourcePath = Path.GetFullPath(path);
        m_assetBasePath = Document.SourcePath;
        CaptureSavedWorldSnapshot();
        ValidationMessages.Add($"Saved world JSON to {path}.");
    }

    /// <summary>
    /// True when the open world has edits that have not been written to disk.
    /// Determined by comparing the current world against the snapshot captured at
    /// the last load or save, so it reflects any change (cells, sprites, settings).
    /// </summary>
    public bool HasUnsavedChanges =>
        Document is not null
        && !string.Equals(m_savedWorldSnapshot, CurrentWorldSnapshot(), StringComparison.Ordinal);

    /// <summary>
    /// Saves the open world for an exit prompt. Writes to the current path when one
    /// is known, otherwise prompts for a destination. Returns false only when the
    /// user cancels the Save As dialog (so the caller can abort closing).
    /// </summary>
    public bool SaveWorldForExit()
    {
        if (Document is null) {
            return true;
        }

        if (string.IsNullOrWhiteSpace(Document.SourcePath)
            || !Document.SourcePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) {
            return SaveWorldJsonAs();
        }

        try {
            SaveWorldJsonTo(Document.SourcePath);
            return true;
        }
        catch (Exception error) {
            MessageBox.Show(
                error.Message,
                "Save world JSON failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return false;
        }
    }

    private void CaptureSavedWorldSnapshot()
    {
        m_savedWorldSnapshot = CurrentWorldSnapshot();
    }

    private string? CurrentWorldSnapshot()
    {
        if (Document is null) {
            return null;
        }

        // A fixed name keeps the world Name out of the comparison; both the saved
        // baseline and the live snapshot run through this same path.
        return WorldJsonDocumentService.Serialize(
            LegacyWorldConverter.FromEditorMap(Document, "snapshot"));
    }

    private void BrowseWorldBackground()
    {
        if (Document is null) {
            return;
        }

        var worldDirectory = CurrentWorldDirectory();
        var dialog = new OpenFileDialog {
            Title = "Select default horizon image",
            Filter = "Image files (*.png;*.bmp)|*.png;*.bmp|PNG files (*.png)|*.png|BMP files (*.bmp)|*.bmp|All files (*.*)|*.*",
            CheckFileExists = true,
            InitialDirectory = Directory.Exists(worldDirectory)
                ? worldDirectory
                : Environment.CurrentDirectory
        };

        if (dialog.ShowDialog() != true) {
            return;
        }

        WorldBackgroundImage = Path.GetRelativePath(worldDirectory, dialog.FileName).Replace('\\', '/');
    }

    private void BrowseLevelBackgroundMusic()
    {
        if (Document is null) {
            return;
        }

        var worldDirectory = CurrentWorldDirectory();
        var dialog = new OpenFileDialog {
            Title = "Select level background music",
            Filter = "Audio files (*.ogg;*.oga;*.mp3;*.wav;*.wma;*.mid;*.midi)|*.ogg;*.oga;*.mp3;*.wav;*.wma;*.mid;*.midi|Ogg Vorbis files (*.ogg;*.oga)|*.ogg;*.oga|MP3 files (*.mp3)|*.mp3|All files (*.*)|*.*",
            CheckFileExists = true,
            InitialDirectory = Directory.Exists(worldDirectory)
                ? worldDirectory
                : Environment.CurrentDirectory
        };

        if (dialog.ShowDialog() != true) {
            return;
        }

        LevelBackgroundMusicFile = Path.GetRelativePath(worldDirectory, dialog.FileName).Replace('\\', '/');
        LevelBackgroundMusicEnabled = true;
        LevelBackgroundMusicLoop = true;
    }

    private void ClearLevelBackgroundMusic()
    {
        if (Document is null) {
            return;
        }

        Document.BackgroundMusic = null;
        NotifyBackgroundMusicChanged();
        RefreshValidation("Map validation completed.");
    }

    private WorldBackgroundMusic EnsureBackgroundMusic()
    {
        if (Document is null) {
            throw new InvalidOperationException("No map document is loaded.");
        }

        Document.BackgroundMusic ??= new WorldBackgroundMusic();
        return Document.BackgroundMusic;
    }

    private void NotifyBackgroundMusicChanged()
    {
        OnPropertyChanged(nameof(LevelBackgroundMusicFile));
        OnPropertyChanged(nameof(LevelBackgroundMusicEnabled));
        OnPropertyChanged(nameof(LevelBackgroundMusicLoop));
        OnPropertyChanged(nameof(LevelBackgroundMusicVolumePercent));
        OnPropertyChanged(nameof(LevelBackgroundMusicVolumeLabel));
        OnPropertyChanged(nameof(LevelBackgroundMusicSummary));
        ClearLevelBackgroundMusicCommand.RaiseCanExecuteChanged();
    }

    private void RefreshPreviewBackground()
    {
        m_previewBackgroundImage = LoadPreviewImage(ResolveWorldBackgroundPath(), decodePixelWidth: 1280);
        OnPropertyChanged(nameof(PreviewBackgroundImage));
        OnPropertyChanged(nameof(WorldBackgroundSummary));
        ClearWorldBackgroundCommand.RaiseCanExecuteChanged();
    }

    private string? ResolveWorldBackgroundPath()
    {
        if (Document is null) {
            return null;
        }

        var image = Document.DefaultHorizonImage;
        if (string.IsNullOrWhiteSpace(image)
            && Document.TextureMap.TryGetValue(0xff, out var textureSky)) {
            image = textureSky;
        }

        if (string.IsNullOrWhiteSpace(image)) {
            return null;
        }

        var worldDirectory = CurrentWorldDirectory();
        var relativePath = ResolveImageRelativePath(worldDirectory, image);
        var fullPath = Path.GetFullPath(Path.Combine(worldDirectory, relativePath));
        return File.Exists(fullPath) ? fullPath : null;
    }

    private string CurrentWorldDirectory()
    {
        if (Document?.SourcePath is { Length: > 0 } sourcePath) {
            return Path.GetDirectoryName(Path.GetFullPath(sourcePath)) ?? Environment.CurrentDirectory;
        }

        if (File.Exists(m_assetBasePath)) {
            return Path.GetDirectoryName(Path.GetFullPath(m_assetBasePath)) ?? Environment.CurrentDirectory;
        }

        return Path.GetFullPath(m_assetBasePath);
    }

    private static ImageSource? LoadPreviewImage(string? path, int decodePixelWidth)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) {
            return null;
        }

        try {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = decodePixelWidth;
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (IOException) {
            return null;
        }
        catch (NotSupportedException) {
            return null;
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

    private void SaveAllOpenJsonFiles()
    {
        try {
            var saved = 0;
            if (Document is not null) {
                var worldSaved = false;
                if (string.IsNullOrWhiteSpace(Document.SourcePath)
                    || !Document.SourcePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) {
                    worldSaved = SaveWorldJsonAs();
                }
                else {
                    SaveWorldJsonTo(Document.SourcePath);
                    worldSaved = true;
                }

                if (worldSaved) {
                    ++saved;
                }
            }

            if (!string.IsNullOrWhiteSpace(m_projectPath)) {
                SaveProjectTo(m_projectPath);
                ++saved;
            }

            if (m_spriteMetadata is not null && !string.IsNullOrWhiteSpace(m_spriteMetadataPath)) {
                SaveSpriteMetadataTo(m_spriteMetadataPath);
                ++saved;
            }

            if (m_weaponMetadata is not null && !string.IsNullOrWhiteSpace(m_weaponMetadataPath)) {
                SaveWeaponMetadataTo(m_weaponMetadataPath);
                ++saved;
            }

            ValidationMessages.Add(saved == 0
                ? "No open JSON files had a known save path."
                : $"Saved {saved} open JSON file(s).");
        }
        catch (Exception error) {
            MessageBox.Show(
                error.Message,
                "Save all JSON files failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    public void LoadWorldJsonFrom(string path)
    {
        var loaded = WorldJsonDocumentService.Load(path);
        if (!loaded.Success || loaded.Document is null) {
            ValidationMessages.Clear();
            foreach (var error in loaded.Errors) {
                ValidationMessages.Add(error);
            }

            return;
        }

        ApplyWorldDocument(loaded.Document, path, $"Loaded world JSON from {path}.");
    }

    private void ApplyWorldDocument(WorldDocument world, string path, string message)
    {
        PopulateDocument(LegacyWorldConverter.ToEditorMap(world, path), path, message);
    }

    // ----- JSON editor panel support -------------------------------------------------

    private static readonly System.Text.Json.JsonSerializerOptions SpriteWriteOptions = new() {
        WriteIndented = true,
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly System.Text.Json.JsonSerializerOptions SpriteReadOptions = new() {
        PropertyNameCaseInsensitive = true
    };

    public bool HasDocument => Document is not null;

    public string WorldSourcePath => Document?.SourcePath ?? string.Empty;

    public bool SelectionJsonAvailable => SelectedSprite is not null;

    public string SelectionJsonLabel => SelectedSprite?.Name is { Length: > 0 } name
        ? name
        : "sprite";

    /// <summary>Serializes the current world to the same JSON written to disk.</summary>
    public string BuildWorldJson()
    {
        return Document is null
            ? string.Empty
            : WorldJsonDocumentService.Serialize(LegacyWorldConverter.FromEditorMap(Document, "edit"));
    }

    /// <summary>Parses, validates, and (if valid) loads whole-world JSON back into the model.</summary>
    public bool TryApplyWorldJson(string json, out IReadOnlyList<string> errors)
    {
        var ok = WorldJsonDocumentService.TryParseAndValidate(json, out var world, out var parsed);
        errors = parsed;
        if (!ok || world is null) {
            return false;
        }

        ApplyWorldDocument(world, WorldSourcePath, "Applied edited world JSON.");
        return true;
    }

    /// <summary>Serializes the selected sprite as JSON for the selection-scope editor.</summary>
    public string BuildSelectionJson()
    {
        return SelectedSprite is null
            ? string.Empty
            : System.Text.Json.JsonSerializer.Serialize(SelectedSprite.Sprite, SpriteWriteOptions);
    }

    /// <summary>Applies edited sprite JSON onto the selected sprite model.</summary>
    public bool TryApplySelectionJson(string json, out IReadOnlyList<string> errors)
    {
        var problems = new List<string>();
        errors = problems;
        if (SelectedSprite is null) {
            problems.Add("No sprite selected.");
            return false;
        }

        EditorSpriteInstance? parsed;
        try {
            parsed = System.Text.Json.JsonSerializer.Deserialize<EditorSpriteInstance>(json, SpriteReadOptions);
        }
        catch (System.Text.Json.JsonException error) {
            problems.Add($"Invalid sprite JSON: {error.Message}");
            return false;
        }

        if (parsed is null) {
            problems.Add("Sprite JSON is empty.");
            return false;
        }

        CopySpriteFields(parsed, SelectedSprite.Sprite);
        SelectedSprite.Refresh();
        RefreshSelectedCellSprites();
        UpdateSpriteSummary();
        SchedulePreview3DRefresh();
        return true;
    }

    public IReadOnlyList<JsonPathSegment>? CellJsonPath(EditorCellViewModel? cell)
    {
        if (Document is null || cell is null) {
            return null;
        }

        return [
            JsonPathSegment.Property("cells"),
            JsonPathSegment.Element(cell.Row),
            JsonPathSegment.Element(cell.Column)
        ];
    }

    public IReadOnlyList<JsonPathSegment>? SpriteJsonPath(EditorSpriteInstance? sprite)
    {
        if (Document is null || sprite is null) {
            return null;
        }

        if (Document.Layers.Count > 0 && Document.ActiveLayerSprites.Contains(sprite)) {
            var layerIndex = ActiveLayerIndexOrNull() ?? 0;
            var index = Document.SpriteInstances
                .Where(Document.ActiveLayerSprites.Contains)
                .ToList()
                .IndexOf(sprite);
            if (index < 0) {
                return null;
            }

            return [
                JsonPathSegment.Property("layers"),
                JsonPathSegment.Element(layerIndex),
                JsonPathSegment.Property("spriteInstances"),
                JsonPathSegment.Element(index)
            ];
        }

        var globalIndex = Document.SpriteInstances
            .Where(candidate => !Document.ActiveLayerSprites.Contains(candidate))
            .ToList()
            .IndexOf(sprite);
        if (globalIndex < 0) {
            return null;
        }

        return [
            JsonPathSegment.Property("spriteInstances"),
            JsonPathSegment.Element(globalIndex)
        ];
    }

    public IReadOnlyList<JsonPathSegment>? BlockJsonPath(string? blockId)
    {
        if (Document is null || string.IsNullOrWhiteSpace(blockId)) {
            return null;
        }

        return [
            JsonPathSegment.Property("blocks"),
            JsonPathSegment.Property(blockId)
        ];
    }

    private void UpdateJsonHighlight()
    {
        IReadOnlyList<JsonPathSegment>? path = m_lastSelectionKind switch {
            JsonSelectionKind.Sprite => SelectedSprite is null ? null : SpriteJsonPath(SelectedSprite.Sprite),
            JsonSelectionKind.Block => SelectedBlock is null ? null : BlockJsonPath(SelectedBlock.Id),
            _ => CellJsonPath(SelectedCell)
        };
        JsonPanel.OnSelectionChanged(path);
    }

    private int? ActiveLayerIndexOrNull()
    {
        if (Document is null || Document.Layers.Count == 0) {
            return null;
        }

        var index = Document.Layers.FindIndex(
            layer => string.Equals(layer.Id, Document.ActiveLayerId, StringComparison.OrdinalIgnoreCase));
        return index >= 0 ? index : 0;
    }

    private static void CopySpriteFields(EditorSpriteInstance from, EditorSpriteInstance to)
    {
        to.Name = from.Name;
        to.SpriteSet = from.SpriteSet;
        to.XCell = from.XCell;
        to.YCell = from.YCell;
        to.FacingDegrees = from.FacingDegrees;
        to.ScaleCells = from.ScaleCells;
        to.VerticalOffsetCells = from.VerticalOffsetCells;
        to.CollisionRadiusCells = from.CollisionRadiusCells;
        to.Visible = from.Visible;
        to.PassThroughWalls = from.PassThroughWalls;
        to.ChasePlayer = from.ChasePlayer;
        to.SpeedCellsPerSecond = from.SpeedCellsPerSecond;
        to.DetectionRadiusCells = from.DetectionRadiusCells;
        to.PatrolRadiusCells = from.PatrolRadiusCells;
        to.EngagementHysteresisCells = from.EngagementHysteresisCells;
        to.PatrolCircuit = from.PatrolCircuit;
        to.StoppingDistanceCells = from.StoppingDistanceCells;
        to.MaxHealth = from.MaxHealth;
        to.Health = from.Health;
        to.AttackDamage = from.AttackDamage;
        to.RangedAttack = from.RangedAttack;
        to.AttackRangeCells = from.AttackRangeCells;
        to.AttackCooldownSeconds = from.AttackCooldownSeconds;
        to.AttackFovDegrees = from.AttackFovDegrees;
        to.AttackBurstShots = from.AttackBurstShots;
        to.AttackBurstPauseSeconds = from.AttackBurstPauseSeconds;
        to.PickupHealth = from.PickupHealth;
        to.UnlocksMap = from.UnlocksMap;
        to.SavePoint = from.SavePoint;
        to.PickupWeapon = from.PickupWeapon;
        to.Explosive = from.Explosive;
        to.ExplosiveHitPoints = from.ExplosiveHitPoints;
        to.ExplosionRadiusCells = from.ExplosionRadiusCells;
        to.ExplosionDamage = from.ExplosionDamage;
        to.ExplosionScaleCells = from.ExplosionScaleCells;
        to.ExplosionSpriteSet = from.ExplosionSpriteSet;
        to.DestroyedSpriteSet = from.DestroyedSpriteSet;
        to.DestroyedScaleCells = from.DestroyedScaleCells;
    }

    private void PopulateDocument(EditorMapDocument document, string assetBasePath, string successMessage)
    {
        Document = document;
        m_assetBasePath = assetBasePath;
        m_projectPath = string.Empty;
        m_undoStack.Clear();
        m_redoStack.Clear();
            m_copiedCellSelection = null;
            m_selectedMapCells = [];
            Cells.Clear();
            Textures.Clear();
            TextureChoices.Clear();
            Blocks.Clear();
            SpriteSetFiles.Clear();
            SpriteInstances.Clear();
            ValidationMessages.Clear();

            m_suspendWorldLayerSwitch = true;
            WorldLayers.Clear();
            foreach (var layer in Document.Layers) {
                WorldLayers.Add(layer);
            }
            var activeLayer = WorldLayers.FirstOrDefault(layer =>
                string.Equals(layer.Id, Document.ActiveLayerId, StringComparison.OrdinalIgnoreCase))
                ?? WorldLayers.FirstOrDefault();
            SelectedWorldLayer = activeLayer;
            m_suspendWorldLayerSwitch = false;
            OnPropertyChanged(nameof(HasMultipleWorldLayers));
            OnPropertyChanged(nameof(WorldLayerIdOptions));
            m_spriteMapPreviews.Clear();
            m_spriteSetFilesByName.Clear();
            m_weaponMetadata = null;
            m_weaponMetadataPath = string.Empty;
            m_weaponMetadataDirectory = Environment.CurrentDirectory;
            m_spriteMetadata = null;
            m_spriteMetadataPath = string.Empty;
            m_spriteMetadataDirectory = Environment.CurrentDirectory;
            SelectedWeaponAnimation = null;
            WeaponAnimations.Clear();
            WeaponAnimationFrames.Clear();
            WeaponMetadataSummary = "No weapon metadata loaded";
            m_copiedSprite = null;
            m_isSpriteCutPending = false;
            NotifySpriteClipboardChanged();
            PlayerStart = new PlayerStartViewModel(Document.PlayerStart);

        m_texturePreviews.Clear();
        var texturePreviews = m_texturePreviews;
        foreach (var texture in TexturePaletteBuilder.Build(Document, assetBasePath)) {
            var textureViewModel = new TextureAssetViewModel(texture);
            Textures.Add(textureViewModel);
            texturePreviews[texture.Key] = textureViewModel.Preview;
        }
        RefreshTextureChoices();
        RefreshBlockPalette();

        MapSummary = $"{Document.ColumnCount} x {Document.RowCount} cells";
        TextureSummary = $"{Document.TextureMap.Count} texture mappings";
        BlockSummary = $"{Document.Blocks.Count} block(s) in palette";
        SpriteSummary = $"{Document.SpriteInstances.Count} sprite instances";

        OnPropertyChanged(nameof(Document));
        OnPropertyChanged(nameof(MapSummary));
        OnPropertyChanged(nameof(TextureSummary));
        OnPropertyChanged(nameof(BlockSummary));
        OnPropertyChanged(nameof(SpriteSummary));
        OnPropertyChanged(nameof(SpriteMetadataSummary));
        OnPropertyChanged(nameof(SpriteTransparentColorSummary));
        OnPropertyChanged(nameof(WorldBackgroundImage));
        OnPropertyChanged(nameof(WorldBackgroundSummary));
        OnPropertyChanged(nameof(LevelBackgroundMusicFile));
        OnPropertyChanged(nameof(LevelBackgroundMusicEnabled));
        OnPropertyChanged(nameof(LevelBackgroundMusicLoop));
        OnPropertyChanged(nameof(LevelBackgroundMusicVolumePercent));
        OnPropertyChanged(nameof(LevelBackgroundMusicVolumeLabel));
        OnPropertyChanged(nameof(LevelBackgroundMusicSummary));
        OnPropertyChanged(nameof(PlayerWeaponFile));
        OnPropertyChanged(nameof(PlayerWeaponVisible));
        OnPropertyChanged(nameof(PlayerWeaponScreenHeightPercent));
        OnPropertyChanged(nameof(PlayerWeaponSummary));
        OnPropertyChanged(nameof(PreviewHudWeaponText));
        OnPropertyChanged(nameof(PlayerMaxHealth));
        OnPropertyChanged(nameof(PlayerHealth));
        OnPropertyChanged(nameof(PlayerTurnBaseDegreesPerSecond));
        OnPropertyChanged(nameof(PlayerTurnMaxDegreesPerSecond));
        OnPropertyChanged(nameof(PlayerTurnAccelerationDegreesPerSecondSquared));
        OnPropertyChanged(nameof(PreviewHudHealthText));
        OnPropertyChanged(nameof(PreviewHudHealthPercent));
        NotifyWeaponMetadataPropertiesChanged();

        RefreshPreviewBackground();

        RefreshSpriteMapPreviews(assetBasePath);
        RefreshWeaponLibrary();
        LoadPlayerWeaponMetadata(addValidationMessage: false);

        foreach (var row in Document.Rows) {
            foreach (var cell in row) {
                var cellViewModel = new EditorCellViewModel(cell, texturePreviews, m_spriteMapPreviews) {
                    SelectedLayer = SelectedLayer
                };
                cellViewModel.ContentChanged += OnCellContentChanged;
                Cells.Add(cellViewModel);
            }
        }

        RefreshElevatorTargetLabels();

        foreach (var sprite in Document.SpriteInstances) {
            SpriteInstances.Add(CreateSpriteInstanceViewModel(sprite));
        }

        foreach (var spriteSet in Document.SpriteSetFiles) {
            SpriteSetFiles.Add(spriteSet);
        }

        SelectedCell = Cells.FirstOrDefault();
        SelectedTexture = Textures.FirstOrDefault();
        SelectedBlock = Blocks.FirstOrDefault();
        SelectedSprite = SpriteInstances.FirstOrDefault();
        if (SelectedSprite is not null) {
            SyncSpriteMetadataWithSelectedSprite();
        }
        else {
            SelectedSpriteSetFile = SpriteSetFiles.FirstOrDefault();
        }
        ResetPreview3DNavigation();
        RefreshPlayerCellMarkers();
        RefreshGameGoalMarkers();
        RefreshValidation(successMessage);
        SaveWorldJsonAsCommand.RaiseCanExecuteChanged();
        SaveAllOpenJsonFilesCommand.RaiseCanExecuteChanged();
        BrowseWorldBackgroundCommand.RaiseCanExecuteChanged();
        ClearWorldBackgroundCommand.RaiseCanExecuteChanged();
        BrowseLevelBackgroundMusicCommand.RaiseCanExecuteChanged();
        ClearLevelBackgroundMusicCommand.RaiseCanExecuteChanged();
        CopyCellCommand.RaiseCanExecuteChanged();
        PasteCellCommand.RaiseCanExecuteChanged();
        CloneSelectedWorldLayerCommand.RaiseCanExecuteChanged();
        RenameSelectedWorldLayerCommand.RaiseCanExecuteChanged();
        DeleteSelectedWorldLayerCommand.RaiseCanExecuteChanged();
        RaiseHistoryCanExecuteChanged();
        PaintSelectedCellCommand.RaiseCanExecuteChanged();
        ValidateCommand.RaiseCanExecuteChanged();
        AddSpriteToSelectedCellCommand.RaiseCanExecuteChanged();
        RemoveSelectedSpriteCommand.RaiseCanExecuteChanged();
        CopySpriteCommand.RaiseCanExecuteChanged();
        CutSpriteCommand.RaiseCanExecuteChanged();
        PasteSpriteCommand.RaiseCanExecuteChanged();
        CancelSpriteClipboardCommand.RaiseCanExecuteChanged();
        PlacePlayerAtSelectedCellCommand.RaiseCanExecuteChanged();
        SetSelectedCellAsGameGoalCommand.RaiseCanExecuteChanged();
        ClearGameGoalCommand.RaiseCanExecuteChanged();
        CloneSelectedBlockCommand.RaiseCanExecuteChanged();
        RemoveDuplicateBlocksCommand.RaiseCanExecuteChanged();
        RemoveUnusedBlocksCommand.RaiseCanExecuteChanged();
        OpenSelectedCellBlockCommand.RaiseCanExecuteChanged();
        MakeSelectedCellUniqueBlockCommand.RaiseCanExecuteChanged();
        EditSelectedCellUniqueBlockCommand.RaiseCanExecuteChanged();
        SaveProjectAsCommand.RaiseCanExecuteChanged();
        RefreshWeaponLibraryCommand.RaiseCanExecuteChanged();
        UseSelectedWeaponAsPlayerCommand.RaiseCanExecuteChanged();
        OpenWeaponJsonCommand.RaiseCanExecuteChanged();
        CloneWeaponMetadataCommand.RaiseCanExecuteChanged();
        RemovePlayerWeaponCommand.RaiseCanExecuteChanged();
        ExportCommand.RaiseCanExecuteChanged();
        TestIn3DCommand.RaiseCanExecuteChanged();
        CaptureSavedWorldSnapshot();
    }

    private WorldPlayerWeapon EnsurePlayerWeapon()
    {
        if (Document is null) {
            throw new InvalidOperationException("No map document is loaded.");
        }

        Document.PlayerWeapon ??= new WorldPlayerWeapon {
            Visible = true,
            Unlocked = true,
            ScreenHeightFraction = 0.45
        };

        return Document.PlayerWeapon;
    }

    public string WeaponMetadataPath => string.IsNullOrWhiteSpace(m_weaponMetadataPath)
        ? "No metadata path"
        : m_weaponMetadataPath;

    private void ReloadWeaponMetadata()
    {
        if (string.IsNullOrWhiteSpace(m_weaponMetadataPath)) {
            LoadPlayerWeaponMetadata(addValidationMessage: true);
            return;
        }

        LoadWeaponMetadataFrom(m_weaponMetadataPath, addValidationMessage: true);
    }

    private void LoadPlayerWeaponMetadata(bool addValidationMessage)
    {
        var metadataPath = ResolvePlayerWeaponMetadataPath();
        if (metadataPath is null) {
            ClearWeaponMetadata();
            if (addValidationMessage) {
                ValidationMessages.Add("No player weapon metadata file is available.");
            }

            return;
        }

        LoadWeaponMetadataFrom(metadataPath, addValidationMessage);
        RefreshWeaponLibrarySelection(metadataPath);
    }

    private void LoadWeaponMetadataFrom(string metadataPath, bool addValidationMessage)
    {
        ClearWeaponMetadata();

        var result = WeaponMetadataLoader.Load(metadataPath);
        m_weaponMetadata = result.Document;
        m_weaponMetadataPath = metadataPath;
        m_weaponMetadataDirectory =
            Path.GetDirectoryName(Path.GetFullPath(metadataPath)) ?? Environment.CurrentDirectory;

        if (result.Document is not null) {
            foreach (var animation in result.Document.Animations) {
                WeaponAnimations.Add(new WeaponAnimationViewModel(animation));
            }

            SelectedWeaponAnimation = WeaponAnimations.FirstOrDefault(
                    animation => string.Equals(animation.Name, "idle", StringComparison.OrdinalIgnoreCase))
                ?? WeaponAnimations.FirstOrDefault();
        }

        if (!result.Success || result.Document is null) {
            WeaponMetadataSummary = "Weapon metadata has errors";
            foreach (var error in result.Errors) {
                ValidationMessages.Add(error);
            }
        }
        else {
            WeaponMetadataSummary =
                $"{result.Document.Weapon} ({result.Document.Format}), {result.Document.Animations.Count} animation(s)";
            if (addValidationMessage) {
                ValidationMessages.Add($"Loaded weapon metadata from {metadataPath}.");
            }
        }

        NotifyWeaponMetadataPropertiesChanged();
        RefreshWeaponLibrarySelection(metadataPath);
    }

    private void ClearWeaponMetadata()
    {
        SelectedWeaponAnimation = null;
        SelectedWeaponAnimationFrame = null;
        WeaponAnimations.Clear();
        WeaponAnimationFrames.Clear();
        m_weaponMetadata = null;
        m_weaponMetadataPath = string.Empty;
        m_weaponMetadataDirectory = Environment.CurrentDirectory;
        WeaponMetadataSummary = "No weapon metadata loaded";
        WeaponAnimationPlayback.Configure(null, m_weaponMetadataDirectory);
        NotifyWeaponMetadataPropertiesChanged();
    }

    private string? ResolvePlayerWeaponMetadataPath()
    {
        if (Document?.PlayerWeapon is null || string.IsNullOrWhiteSpace(Document.PlayerWeapon.File)) {
            return null;
        }

        if (Path.IsPathRooted(Document.PlayerWeapon.File)) {
            return File.Exists(Document.PlayerWeapon.File)
                ? Path.GetFullPath(Document.PlayerWeapon.File)
                : null;
        }

        var baseDirectory = !string.IsNullOrWhiteSpace(Document.SourcePath)
            ? Path.GetDirectoryName(Path.GetFullPath(Document.SourcePath)) ?? Environment.CurrentDirectory
            : File.Exists(m_assetBasePath)
                ? Path.GetDirectoryName(Path.GetFullPath(m_assetBasePath)) ?? Environment.CurrentDirectory
                : Path.GetFullPath(m_assetBasePath);
        var candidate = Path.GetFullPath(Path.Combine(baseDirectory, Document.PlayerWeapon.File));
        return File.Exists(candidate) ? candidate : null;
    }

    private void RefreshWeaponLibrary()
    {
        var previousPath = SelectedWeaponLibraryItem?.AbsolutePath;
        var activePath = ResolvePlayerWeaponMetadataPath();
        var selectedPath = !string.IsNullOrWhiteSpace(previousPath)
            ? previousPath
            : activePath;

        WeaponLibrary.Clear();
        if (Document is null) {
            SelectedWeaponLibraryItem = null;
            return;
        }

        foreach (var path in DiscoverWeaponMetadataFiles()) {
            var absolutePath = Path.GetFullPath(path);
            WeaponLibrary.Add(new WeaponLibraryItemViewModel(
                absolutePath,
                ToWorldRelative(absolutePath),
                ReadWeaponName(absolutePath)));
        }

        if (activePath is not null
            && WeaponLibrary.All(item => !PathsEqual(item.AbsolutePath, activePath))) {
            var absolutePath = Path.GetFullPath(activePath);
            WeaponLibrary.Add(new WeaponLibraryItemViewModel(
                absolutePath,
                ToWorldRelative(absolutePath),
                ReadWeaponName(absolutePath)));
        }

        RefreshWeaponLibrarySelection(selectedPath ?? activePath);
        RefreshWeaponLibraryCommand.RaiseCanExecuteChanged();
        UseSelectedWeaponAsPlayerCommand.RaiseCanExecuteChanged();
        OpenWeaponJsonCommand.RaiseCanExecuteChanged();
    }

    private IEnumerable<string> DiscoverWeaponMetadataFiles()
    {
        var worldDirectory = CurrentWorldDirectory();
        var roots = new List<string>();
        var weaponsDirectory = Path.Combine(worldDirectory, "weapons");
        if (Directory.Exists(weaponsDirectory)) {
            roots.Add(weaponsDirectory);
        }

        if (!string.IsNullOrWhiteSpace(m_weaponMetadataPath)
            && !string.IsNullOrWhiteSpace(m_weaponMetadataDirectory)
            && Directory.Exists(m_weaponMetadataDirectory)
            && !roots.Any(root => PathsEqual(root, m_weaponMetadataDirectory))) {
            roots.Add(m_weaponMetadataDirectory);
        }

        return roots
            .SelectMany(root => Directory.EnumerateFiles(root, "*.weapon.json", SearchOption.AllDirectories))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => ToWorldRelative(path), StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string ReadWeaponName(string metadataPath)
    {
        try {
            using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(metadataPath));
            return json.RootElement.TryGetProperty("weapon", out var weapon)
                && weapon.ValueKind == System.Text.Json.JsonValueKind.String
                ? weapon.GetString() ?? string.Empty
                : string.Empty;
        }
        catch (IOException) {
            return string.Empty;
        }
        catch (System.Text.Json.JsonException) {
            return string.Empty;
        }
    }

    private void RefreshWeaponLibrarySelection(string? metadataPath)
    {
        m_isSelectingWeaponLibraryItem = true;
        try {
            SelectedWeaponLibraryItem = string.IsNullOrWhiteSpace(metadataPath)
                ? null
                : WeaponLibrary.FirstOrDefault(item => PathsEqual(item.AbsolutePath, metadataPath));
        }
        finally {
            m_isSelectingWeaponLibraryItem = false;
        }
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            StringComparison.OrdinalIgnoreCase);
    }

    private void UseSelectedWeaponAsPlayer()
    {
        if (Document is null || SelectedWeaponLibraryItem is null) {
            return;
        }

        var weapon = EnsurePlayerWeapon();
        weapon.File = SelectedWeaponLibraryItem.RelativePath;
        if (weapon.ScreenHeightFraction <= 0.0) {
            weapon.ScreenHeightFraction = m_weaponMetadata?.ScreenHeightFraction > 0.0
                ? m_weaponMetadata.ScreenHeightFraction
                : 0.45;
        }

        OnPropertyChanged(nameof(PlayerWeaponFile));
        OnPropertyChanged(nameof(PlayerWeaponVisible));
        OnPropertyChanged(nameof(PlayerWeaponScreenHeightPercent));
        OnPropertyChanged(nameof(PlayerWeaponSummary));
        OnPropertyChanged(nameof(PreviewHudWeaponText));
        RemovePlayerWeaponCommand.RaiseCanExecuteChanged();
        LoadWeaponMetadataFrom(SelectedWeaponLibraryItem.AbsolutePath, addValidationMessage: false);
        RefreshValidation("Map validation completed.");
    }

    private void OpenWeaponJson()
    {
        if (string.IsNullOrWhiteSpace(m_weaponMetadataPath) || !File.Exists(m_weaponMetadataPath)) {
            return;
        }

        try {
            Process.Start(new ProcessStartInfo {
                FileName = m_weaponMetadataPath,
                UseShellExecute = true
            });
        }
        catch (Exception error) when (error is InvalidOperationException or IOException or System.ComponentModel.Win32Exception) {
            MessageBox.Show(
                error.Message,
                "Open weapon JSON failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void CloneWeaponMetadata()
    {
        if (m_weaponMetadata is null || string.IsNullOrWhiteSpace(m_weaponMetadataPath)) {
            return;
        }

        var dialog = new SaveFileDialog {
            Title = "Clone weapon metadata",
            Filter = "Weapon metadata (*.weapon.json)|*.weapon.json|JSON files (*.json)|*.json|All files (*.*)|*.*",
            InitialDirectory = Directory.Exists(m_weaponMetadataDirectory)
                ? m_weaponMetadataDirectory
                : CurrentWorldDirectory(),
            FileName = $"{Path.GetFileNameWithoutExtension(m_weaponMetadataPath)}_copy.weapon.json",
            AddExtension = true,
            OverwritePrompt = true
        };

        if (dialog.ShowDialog() != true) {
            return;
        }

        try {
            var clone = CloneWeaponMetadataDocument(m_weaponMetadata);
            clone.Weapon = Path.GetFileNameWithoutExtension(dialog.FileName)
                .Replace(".weapon", string.Empty, StringComparison.OrdinalIgnoreCase);
            WeaponMetadataWriter.Save(clone, dialog.FileName);
            ValidationMessages.Add($"Cloned weapon metadata to {dialog.FileName}.");
            RefreshWeaponLibrary();
            var item = WeaponLibrary.FirstOrDefault(candidate => PathsEqual(candidate.AbsolutePath, dialog.FileName));
            if (item is not null) {
                SelectedWeaponLibraryItem = item;
                UseSelectedWeaponAsPlayer();
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException) {
            MessageBox.Show(
                error.Message,
                "Clone weapon metadata failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void RemovePlayerWeapon()
    {
        if (Document?.PlayerWeapon is null) {
            return;
        }

        Document.PlayerWeapon.File = string.Empty;
        ClearWeaponMetadata();
        RefreshWeaponLibrarySelection(null);
        OnPropertyChanged(nameof(PlayerWeaponFile));
        OnPropertyChanged(nameof(PlayerWeaponSummary));
        OnPropertyChanged(nameof(PreviewHudWeaponText));
        RemovePlayerWeaponCommand.RaiseCanExecuteChanged();
        ValidationMessages.Add("Removed the player weapon reference. Weapon metadata files were left on disk.");
        RefreshValidation("Map validation completed.");
    }

    private static WeaponMetadataDocument CloneWeaponMetadataDocument(WeaponMetadataDocument source)
    {
        return new WeaponMetadataDocument {
            Weapon = source.Weapon,
            Format = source.Format,
            FrameWidth = source.FrameWidth,
            FrameHeight = source.FrameHeight,
            ScreenHeightFraction = source.ScreenHeightFraction,
            Damage = source.Damage,
            RangeCells = source.RangeCells,
            Sounds = source.Sounds is null
                ? null
                : new WeaponSoundMetadata {
                    Fire = source.Sounds.Fire
                },
            Ammo = source.Ammo is null
                ? null
                : new WeaponAmmoMetadata {
                    MagazineSize = source.Ammo.MagazineSize,
                    MaxAmmo = source.Ammo.MaxAmmo,
                    InitialAmmo = source.Ammo.InitialAmmo
                },
            Anchor = new WeaponPointMetadata {
                X = source.Anchor.X,
                Y = source.Anchor.Y
            },
            BaseOffset = new WeaponPointMetadata {
                X = source.BaseOffset.X,
                Y = source.BaseOffset.Y
            },
            Bob = new WeaponBobMetadata {
                Enabled = source.Bob.Enabled,
                Amount = source.Bob.Amount,
                AmplitudeX = source.Bob.AmplitudeX,
                AmplitudeY = source.Bob.AmplitudeY,
                FrequencyHz = source.Bob.FrequencyHz
            },
            Animations = source.Animations.Select(CloneWeaponAnimation).ToList()
        };
    }

    private void SaveWeaponMetadata()
    {
        if (m_weaponMetadata is null || string.IsNullOrWhiteSpace(m_weaponMetadataPath)) {
            return;
        }

        try {
            SaveWeaponMetadataTo(m_weaponMetadataPath);
        }
        catch (Exception error) {
            MessageBox.Show(
                error.Message,
                "Save weapon metadata failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void SaveWeaponMetadataAs()
    {
        if (m_weaponMetadata is null) {
            return;
        }

        var dialog = new SaveFileDialog {
            Filter = "Weapon metadata (*.weapon.json)|*.weapon.json|JSON files (*.json)|*.json|All files (*.*)|*.*",
            InitialDirectory = Directory.Exists(m_weaponMetadataDirectory)
                ? m_weaponMetadataDirectory
                : Environment.CurrentDirectory,
            FileName = string.IsNullOrWhiteSpace(m_weaponMetadataPath)
                ? "weapon.weapon.json"
                : Path.GetFileName(m_weaponMetadataPath)
        };

        if (dialog.ShowDialog() != true) {
            return;
        }

        try {
            SaveWeaponMetadataTo(dialog.FileName);
        }
        catch (Exception error) {
            MessageBox.Show(
                error.Message,
                "Save weapon metadata failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void SaveWeaponMetadataTo(string path)
    {
        if (m_weaponMetadata is null) {
            throw new InvalidOperationException("No weapon metadata is loaded.");
        }

        WeaponMetadataWriter.Save(m_weaponMetadata, path);
        m_weaponMetadataPath = Path.GetFullPath(path);
        m_weaponMetadataDirectory =
            Path.GetDirectoryName(m_weaponMetadataPath) ?? Environment.CurrentDirectory;
        ValidationMessages.Add($"Saved weapon metadata to {m_weaponMetadataPath}.");
        NotifyWeaponMetadataChanged();
        RefreshWeaponLibrary();
        RefreshWeaponLibrarySelection(m_weaponMetadataPath);
    }

    private void AddWeaponAnimation()
    {
        if (m_weaponMetadata is null) {
            return;
        }

        var source = SelectedWeaponAnimation?.Animation
            ?? m_weaponMetadata.Animations.FirstOrDefault();
        var animation = new WeaponAnimationMetadata {
            Name = UniqueWeaponAnimationName("animation"),
            FrameDurationMs = source is not null && source.FrameDurationMs > 0.0
                ? source.FrameDurationMs
                : 100.0,
            Loop = true
        };
        animation.Files.Add(SelectedWeaponAnimationFrame?.File
            ?? source?.Files.FirstOrDefault()
            ?? string.Empty);

        m_weaponMetadata.Animations.Add(animation);
        var viewModel = new WeaponAnimationViewModel(animation);
        WeaponAnimations.Add(viewModel);
        SelectedWeaponAnimation = viewModel;
        ValidationMessages.Add($"Added weapon animation {animation.Name}.");
        NotifyWeaponAnimationCollectionChanged();
    }

    private void DuplicateWeaponAnimation()
    {
        if (m_weaponMetadata is null || SelectedWeaponAnimation is null) {
            return;
        }

        var source = SelectedWeaponAnimation.Animation;
        var animation = CloneWeaponAnimation(source);
        animation.Name = UniqueWeaponAnimationName($"{source.Name}_copy");
        m_weaponMetadata.Animations.Add(animation);

        var viewModel = new WeaponAnimationViewModel(animation);
        WeaponAnimations.Add(viewModel);
        SelectedWeaponAnimation = viewModel;
        ValidationMessages.Add($"Duplicated weapon animation {source.Name}.");
        NotifyWeaponAnimationCollectionChanged();
    }

    private void RemoveWeaponAnimation()
    {
        if (!CanRemoveSelectedWeaponAnimation()
            || m_weaponMetadata is null
            || SelectedWeaponAnimation is null) {
            return;
        }

        var removed = SelectedWeaponAnimation;
        m_weaponMetadata.Animations.Remove(removed.Animation);
        WeaponAnimations.Remove(removed);
        SelectedWeaponAnimation = WeaponAnimations.FirstOrDefault(
                animation => string.Equals(animation.Name, "idle", StringComparison.OrdinalIgnoreCase))
            ?? WeaponAnimations.FirstOrDefault();
        ValidationMessages.Add($"Removed weapon animation {removed.Name}.");
        NotifyWeaponAnimationCollectionChanged();
    }

    private bool CanRemoveSelectedWeaponAnimation()
    {
        return m_weaponMetadata is not null
            && SelectedWeaponAnimation is not null
            && !string.Equals(SelectedWeaponAnimation.Name, "idle", StringComparison.OrdinalIgnoreCase);
    }

    private void AddWeaponAnimationFrame()
    {
        if (SelectedWeaponAnimation is null) {
            return;
        }

        SelectedWeaponAnimation.Animation.Files.Add(
            SelectedWeaponAnimationFrame?.File
            ?? SelectedWeaponAnimation.Animation.Files.FirstOrDefault()
            ?? string.Empty);
        RefreshWeaponAnimationFrames();
        SelectedWeaponAnimationFrame = WeaponAnimationFrames.LastOrDefault();
        SelectedWeaponAnimation.Refresh();
        ValidationMessages.Add($"Added frame to {SelectedWeaponAnimation.Name}.");
        NotifyWeaponAnimationCollectionChanged();
    }

    private void DuplicateWeaponAnimationFrame()
    {
        if (SelectedWeaponAnimation is null || SelectedWeaponAnimationFrame is null) {
            return;
        }

        var insertAt = SelectedWeaponAnimationFrame.Index + 1;
        SelectedWeaponAnimation.Animation.Files.Insert(insertAt, SelectedWeaponAnimationFrame.File);
        RefreshWeaponAnimationFrames();
        SelectedWeaponAnimationFrame = WeaponAnimationFrames.ElementAtOrDefault(insertAt);
        SelectedWeaponAnimation.Refresh();
        ValidationMessages.Add($"Duplicated frame in {SelectedWeaponAnimation.Name}.");
        NotifyWeaponAnimationCollectionChanged();
    }

    private void RemoveWeaponAnimationFrame()
    {
        if (!CanRemoveSelectedWeaponAnimationFrame()
            || SelectedWeaponAnimation is null
            || SelectedWeaponAnimationFrame is null) {
            return;
        }

        var removeAt = SelectedWeaponAnimationFrame.Index;
        SelectedWeaponAnimation.Animation.Files.RemoveAt(removeAt);
        RefreshWeaponAnimationFrames();
        SelectedWeaponAnimationFrame = WeaponAnimationFrames.ElementAtOrDefault(
                Math.Min(removeAt, WeaponAnimationFrames.Count - 1))
            ?? WeaponAnimationFrames.FirstOrDefault();
        SelectedWeaponAnimation.Refresh();
        ValidationMessages.Add($"Removed frame from {SelectedWeaponAnimation.Name}.");
        NotifyWeaponAnimationCollectionChanged();
    }

    private bool CanRemoveSelectedWeaponAnimationFrame()
    {
        return SelectedWeaponAnimation is not null
            && SelectedWeaponAnimationFrame is not null
            && SelectedWeaponAnimation.Animation.Files.Count > 1;
    }

    private string UniqueWeaponAnimationName(string baseName)
    {
        if (m_weaponMetadata is null) {
            return baseName;
        }

        var normalizedBase = string.IsNullOrWhiteSpace(baseName)
            ? "animation"
            : baseName.Trim();
        var existingNames = m_weaponMetadata.Animations
            .Select(animation => animation.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!existingNames.Contains(normalizedBase)) {
            return normalizedBase;
        }

        for (var index = 2; ; ++index) {
            var candidate = $"{normalizedBase}_{index}";
            if (!existingNames.Contains(candidate)) {
                return candidate;
            }
        }
    }

    private static WeaponAnimationMetadata CloneWeaponAnimation(WeaponAnimationMetadata source)
    {
        return new WeaponAnimationMetadata {
            Name = source.Name,
            FrameDurationMs = source.FrameDurationMs,
            Loop = source.Loop,
            Files = source.Files.ToList()
        };
    }

    private void RefreshWeaponAnimationFrames()
    {
        SelectedWeaponAnimationFrame = null;
        WeaponAnimationFrames.Clear();
        if (SelectedWeaponAnimation is null) {
            return;
        }

        for (var index = 0; index < SelectedWeaponAnimation.Animation.Files.Count; ++index) {
            WeaponAnimationFrames.Add(new WeaponAnimationFrameViewModel(
                index,
                SelectedWeaponAnimation.Animation));
        }

        SelectedWeaponAnimationFrame = WeaponAnimationFrames.FirstOrDefault();
    }

    private void RefreshWeaponAnimationPlayback()
    {
        WeaponAnimationPlayback.Configure(
            SelectedWeaponAnimation?.Animation,
            m_weaponMetadataDirectory);
        RaiseWeaponAnimationPlaybackCanExecuteChanged();
    }

    private void OnWeaponAnimationPlaybackPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        RaiseWeaponAnimationPlaybackCanExecuteChanged();
    }

    private void OnSelectedWeaponAnimationPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(WeaponAnimationViewModel.FrameDurationMs)
            or nameof(WeaponAnimationViewModel.Loop)
            or nameof(WeaponAnimationViewModel.Summary)) {
            RefreshWeaponAnimationPlayback();
            NotifyWeaponAnimationCollectionChanged();
        }
    }

    private void OnSelectedWeaponAnimationFramePropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        RefreshWeaponAnimationPlayback();
        SelectedWeaponAnimation?.Refresh();
        OnPropertyChanged(nameof(SelectedWeaponAnimationSummary));
        SaveWeaponMetadataCommand.RaiseCanExecuteChanged();
        SaveWeaponMetadataAsCommand.RaiseCanExecuteChanged();
        SaveAllOpenJsonFilesCommand.RaiseCanExecuteChanged();
    }

    private void NotifyWeaponAnimationCollectionChanged()
    {
        if (m_weaponMetadata is not null) {
            WeaponMetadataSummary =
                $"{m_weaponMetadata.Weapon} ({m_weaponMetadata.Format}), {m_weaponMetadata.Animations.Count} animation(s)";
        }

        OnPropertyChanged(nameof(WeaponMetadataSummary));
        OnPropertyChanged(nameof(SelectedWeaponAnimationSummary));
        RaiseWeaponAnimationCanExecuteChanged();
        RefreshWeaponAnimationPlayback();
    }

    private void RaiseWeaponAnimationCanExecuteChanged()
    {
        ReloadWeaponMetadataCommand.RaiseCanExecuteChanged();
        SaveWeaponMetadataCommand.RaiseCanExecuteChanged();
        SaveWeaponMetadataAsCommand.RaiseCanExecuteChanged();
        RefreshWeaponLibraryCommand.RaiseCanExecuteChanged();
        UseSelectedWeaponAsPlayerCommand.RaiseCanExecuteChanged();
        OpenWeaponJsonCommand.RaiseCanExecuteChanged();
        CloneWeaponMetadataCommand.RaiseCanExecuteChanged();
        RemovePlayerWeaponCommand.RaiseCanExecuteChanged();
        AddWeaponAnimationCommand.RaiseCanExecuteChanged();
        DuplicateWeaponAnimationCommand.RaiseCanExecuteChanged();
        RemoveWeaponAnimationCommand.RaiseCanExecuteChanged();
        AddWeaponAnimationFrameCommand.RaiseCanExecuteChanged();
        DuplicateWeaponAnimationFrameCommand.RaiseCanExecuteChanged();
        RemoveWeaponAnimationFrameCommand.RaiseCanExecuteChanged();
        RaiseWeaponAnimationPlaybackCanExecuteChanged();
    }

    private void RaiseWeaponAnimationPlaybackCanExecuteChanged()
    {
        PlayWeaponAnimationCommand.RaiseCanExecuteChanged();
        PauseWeaponAnimationCommand.RaiseCanExecuteChanged();
        StopWeaponAnimationCommand.RaiseCanExecuteChanged();
        StepWeaponAnimationForwardCommand.RaiseCanExecuteChanged();
        StepWeaponAnimationBackwardCommand.RaiseCanExecuteChanged();
    }

    private void SetWeaponPointValue(
        WeaponPointMetadata? point,
        bool isX,
        double value,
        [CallerMemberName] string? propertyName = null)
    {
        if (point is null) {
            return;
        }

        var current = isX ? point.X : point.Y;
        if (Math.Abs(current - value) < 0.001) {
            return;
        }

        if (isX) {
            point.X = value;
        }
        else {
            point.Y = value;
        }

        OnPropertyChanged(propertyName);
        NotifyWeaponMetadataChanged();
    }

    private void NotifyWeaponMetadataChanged()
    {
        if (m_weaponMetadata is not null) {
            WeaponMetadataSummary =
                $"{m_weaponMetadata.Weapon} ({m_weaponMetadata.Format}), {m_weaponMetadata.Animations.Count} animation(s)";
        }

        OnPropertyChanged(nameof(WeaponMetadataSummary));
        OnPropertyChanged(nameof(WeaponMetadataPath));
        OnPropertyChanged(nameof(PreviewHudWeaponText));
        SaveWeaponMetadataCommand.RaiseCanExecuteChanged();
        SaveWeaponMetadataAsCommand.RaiseCanExecuteChanged();
    }

    private void NotifyWeaponMetadataPropertiesChanged()
    {
        OnPropertyChanged(nameof(WeaponMetadataSummary));
        OnPropertyChanged(nameof(WeaponMetadataPath));
        OnPropertyChanged(nameof(PreviewHudWeaponText));
        OnPropertyChanged(nameof(WeaponName));
        OnPropertyChanged(nameof(WeaponFormat));
        OnPropertyChanged(nameof(WeaponFrameWidth));
        OnPropertyChanged(nameof(WeaponFrameHeight));
        OnPropertyChanged(nameof(WeaponMetadataScreenHeightPercent));
        OnPropertyChanged(nameof(WeaponDamage));
        OnPropertyChanged(nameof(WeaponRangeCells));
        OnPropertyChanged(nameof(WeaponFireSound));
        OnPropertyChanged(nameof(WeaponMagazineSize));
        OnPropertyChanged(nameof(WeaponMaxAmmo));
        OnPropertyChanged(nameof(WeaponInitialAmmo));
        OnPropertyChanged(nameof(WeaponAnchorX));
        OnPropertyChanged(nameof(WeaponAnchorY));
        OnPropertyChanged(nameof(WeaponBaseOffsetX));
        OnPropertyChanged(nameof(WeaponBaseOffsetY));
        OnPropertyChanged(nameof(WeaponBobEnabled));
        OnPropertyChanged(nameof(WeaponBobAmountPercent));
        OnPropertyChanged(nameof(WeaponBobAmplitudeX));
        OnPropertyChanged(nameof(WeaponBobAmplitudeY));
        OnPropertyChanged(nameof(WeaponBobFrequencyHz));
        RaiseWeaponAnimationCanExecuteChanged();
        SaveAllOpenJsonFilesCommand.RaiseCanExecuteChanged();
    }

    public void OpenProjectFrom(string projectPath)
    {
        var loaded = EditorProjectDocumentService.Load(projectPath);
        if (!loaded.Success || loaded.Document is null) {
            ValidationMessages.Clear();
            foreach (var error in loaded.Errors) {
                ValidationMessages.Add(error);
            }

            return;
        }

        var projectDirectory =
            Path.GetDirectoryName(Path.GetFullPath(projectPath)) ?? Environment.CurrentDirectory;
        var worldPath = Path.GetFullPath(Path.Combine(projectDirectory, loaded.Document.WorldFile));
        if (!worldPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) {
            ValidationMessages.Clear();
            ValidationMessages.Add(
                $"Project references unsupported legacy world file '{loaded.Document.WorldFile}'. Use a .world.json file.");
            return;
        }

        LoadWorldJsonFrom(worldPath);
        if (Document is null) {
            return;
        }

        m_projectPath = Path.GetFullPath(projectPath);

        if (loaded.Document.PlayerStart is not null) {
            Document.PlayerStart = loaded.Document.PlayerStart;
            PlayerStart = new PlayerStartViewModel(Document.PlayerStart);
            RefreshPlayerCellMarkers();
        }

        Document.PlayerStats = new WorldCombatStats {
            MaxHealth = loaded.Document.PlayerStats.MaxHealth,
            Health = loaded.Document.PlayerStats.Health
        };
        OnPropertyChanged(nameof(PlayerMaxHealth));
        OnPropertyChanged(nameof(PlayerHealth));

        if (loaded.Document.PlayerWeapon is not null) {
            Document.PlayerWeapon = new WorldPlayerWeapon {
                File = loaded.Document.PlayerWeapon.File,
                Visible = loaded.Document.PlayerWeapon.Visible,
                Unlocked = loaded.Document.PlayerWeapon.Unlocked,
                ScreenHeightFraction = loaded.Document.PlayerWeapon.ScreenHeightFraction
            };
            OnPropertyChanged(nameof(PlayerWeaponFile));
            OnPropertyChanged(nameof(PlayerWeaponVisible));
            OnPropertyChanged(nameof(PlayerWeaponScreenHeightPercent));
            OnPropertyChanged(nameof(PlayerWeaponSummary));
            LoadPlayerWeaponMetadata(addValidationMessage: false);
        }

        foreach (var existing in Document.SpriteInstances.ToList()) {
            Document.RemoveSpriteInstance(existing);
        }

        SpriteInstances.Clear();

        foreach (var sprite in loaded.Document.SpriteInstances) {
            Document.SpriteInstances.Add(sprite);
            var cell = Document.CellAt((int)Math.Floor(sprite.YCell), (int)Math.Floor(sprite.XCell));
            cell?.Sprites.Add(sprite);
            SpriteInstances.Add(CreateSpriteInstanceViewModel(sprite));
        }

        foreach (var cell in Cells) {
            cell.NotifySpriteCollectionChanged();
        }

        Document.SpriteSetFiles.Clear();
        SpriteSetFiles.Clear();
        foreach (var spriteSet in loaded.Document.SpriteSets) {
            var spriteSetPath = Path.GetFullPath(Path.Combine(projectDirectory, spriteSet));
            if (File.Exists(spriteSetPath)) {
                Document.SpriteSetFiles.Add(spriteSet);
                SpriteSetFiles.Add(spriteSet);
            }
            else {
                ValidationMessages.Add($"Sprite set not found: {spriteSetPath}");
            }
        }

        RefreshSpriteMapPreviews(projectPath);
        SelectedSprite = SpriteInstances.FirstOrDefault();
        SelectedSpriteSetFile = SpriteSetFiles.FirstOrDefault();
        UpdateSpriteSummary();

        // The project overrides applied above are the freshly-loaded baseline, not
        // user edits, so reset the unsaved-changes snapshot to this state.
        CaptureSavedWorldSnapshot();
        RefreshValidation($"Loaded project from {projectPath}.");
    }

    public void SaveProjectTo(string projectPath)
    {
        if (Document is null) {
            throw new InvalidOperationException("No map document is loaded.");
        }

        var projectDirectory =
            Path.GetDirectoryName(Path.GetFullPath(projectPath)) ?? Environment.CurrentDirectory;
        var worldRelative = string.IsNullOrWhiteSpace(Document.SourcePath)
            ? string.Empty
            : Path.GetRelativePath(projectDirectory, Document.SourcePath).Replace('\\', '/');

        var project = EditorProjectDocumentService.FromMapDocument(Document, worldRelative);
        project.ProjectName = Path.GetFileNameWithoutExtension(projectPath);
        EditorProjectDocumentService.Save(project, projectPath);
        m_projectPath = Path.GetFullPath(projectPath);
        CaptureSavedWorldSnapshot();
        ValidationMessages.Add($"Saved project to {projectPath}.");
    }

    private void PaintSelectedCell()
    {
        if (SelectedCell is null || SelectedTexture is null) {
            return;
        }

        var textureId = TextureKey(SelectedTexture.Asset.Key);
        switch (SelectedPaintTarget) {
            case "Wall":
                SelectedCellLowerWallTextureKey = textureId;
                break;
            case "Floor":
                SelectedCellFloorTextureKey = textureId;
                break;
            case "Ceiling":
                SelectedCellCeilingTextureKey = textureId;
                break;
            case "Transparent wall":
                SelectedCellTransparentWallTextureKey = textureId;
                break;
            case "Upper wall":
                SelectedCellUpperWallTextureKey = textureId;
                break;
        }
    }

    private void ClearSelectedCellWalls()
    {
        EditSelectedCellBlock(block => block.Walls.Clear());
    }

    private void ClearSelectedCellSurfaces()
    {
        EditSelectedCellBlock(block => {
            block.Floor = null;
            block.Ceiling = null;
        });
    }

    private void CopySelectedCell()
    {
        var selectedCells = CurrentMapSelection();
        if (selectedCells.Count == 0) {
            return;
        }

        var minRow = selectedCells.Min(cell => cell.Row);
        var minColumn = selectedCells.Min(cell => cell.Column);
        var maxRow = selectedCells.Max(cell => cell.Row);
        var maxColumn = selectedCells.Max(cell => cell.Column);
        var entries = selectedCells
            .Select(cell => new CellClipboardEntry(
                cell.Row - minRow,
                cell.Column - minColumn,
                CloneContent(EditorCellContent.Capture(cell.Cell))))
            .ToList();

        m_copiedCellSelection = new CellSelectionClipboard(
            maxRow - minRow + 1,
            maxColumn - minColumn + 1,
            entries);
        PasteCellCommand.RaiseCanExecuteChanged();
        ValidationMessages.Add(entries.Count == 1
            ? $"Copied cell {selectedCells[0].Coordinates}."
            : $"Copied {entries.Count} cells ({m_copiedCellSelection.Columns} x {m_copiedCellSelection.Rows}).");
    }

    private void PasteToSelectedCell()
    {
        if (SelectedCell is null || m_copiedCellSelection is null) {
            return;
        }

        var changes = new List<CellContentChange>();
        foreach (var entry in m_copiedCellSelection.Entries) {
            var target = CellAt(
                SelectedCell.Row + entry.RowOffset,
                SelectedCell.Column + entry.ColumnOffset);
            if (target is null) {
                continue;
            }

            var before = EditorCellContent.Capture(target.Cell);
            var after = CloneContent(entry.Content);
            if (ContentEquals(before, after)) {
                continue;
            }

            ApplyCellContent(target, after);
            changes.Add(new CellContentChange(target, before, after));
        }

        if (changes.Count == 0) {
            return;
        }

        RecordUndoAction(changes.Count == 1
            ? new CellContentUndoAction(
                this,
                changes[0].Cell,
                changes[0].Before,
                changes[0].After)
            : new MultiCellContentUndoAction(this, changes));
        RefreshValidation(changes.Count == 1
            ? $"Pasted cell content into {changes[0].Cell.Coordinates}."
            : $"Pasted {changes.Count} cells starting at {SelectedCell.Coordinates}.");
    }

    private bool CanCopyMapSelection()
    {
        return CurrentMapSelection().Count > 0;
    }

    private bool CanPasteToMapSelection()
    {
        return SelectedCell is not null && m_copiedCellSelection is not null;
    }

    private void Undo()
    {
        if (m_undoStack.Count == 0) {
            return;
        }

        var action = m_undoStack.Pop();
        m_isApplyingHistory = true;
        try {
            action.Undo();
        }
        finally {
            m_isApplyingHistory = false;
        }

        m_redoStack.Push(action);
        RaiseHistoryCanExecuteChanged();
        RefreshValidation("Undo completed.");
    }

    private void Redo()
    {
        if (m_redoStack.Count == 0) {
            return;
        }

        var action = m_redoStack.Pop();
        m_isApplyingHistory = true;
        try {
            action.Redo();
        }
        finally {
            m_isApplyingHistory = false;
        }

        m_undoStack.Push(action);
        RaiseHistoryCanExecuteChanged();
        RefreshValidation("Redo completed.");
    }

    private void OnCellContentChanged(object? sender, CellContentChangedEventArgs args)
    {
        if (m_isApplyingHistory || ContentEquals(args.Before, args.After)) {
            return;
        }

        RecordUndoAction(new CellContentUndoAction(this, args.Cell, args.Before, args.After));
        if (args.Cell == SelectedCell) {
            NotifySelectedCellEditorChanged();
        }

        RefreshValidation("Map validation completed.");
        MaybePromptElevatorSync(args.Cell, args.Before, args.After);
        RefreshElevatorTargetLabels();
    }

    private void RefreshElevatorTargetLabels()
    {
        if (Document is null) {
            foreach (var cell in Cells) {
                cell.TargetLayerLabel = null;
            }
            return;
        }

        var activeLayerId = Document.ActiveLayerId ?? string.Empty;
        var targetsByCell = new Dictionary<(int row, int col), List<string>>();
        foreach (var transition in Document.LayerTransitions) {
            if (!string.Equals(transition.FromLayer, activeLayerId, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            var trigger = transition.Trigger;
            if (trigger?.Row is not int row || trigger.Column is not int col) {
                continue;
            }

            if (!targetsByCell.TryGetValue((row, col), out var targets)) {
                targets = [];
                targetsByCell[(row, col)] = targets;
            }

            if (!targets.Contains(transition.ToLayer, StringComparer.OrdinalIgnoreCase)) {
                targets.Add(transition.ToLayer);
            }
        }

        foreach (var cell in Cells) {
            cell.TargetLayerLabel =
                targetsByCell.TryGetValue((cell.Row, cell.Column), out var targets)
                    ? string.Join(", ", targets)
                    : null;
        }
    }

    private const string ElevatorBlockIdLowercase = "e1";

    private void MaybePromptElevatorSync(
        EditorCellViewModel cell,
        EditorCellContent before,
        EditorCellContent after)
    {
        if (Document is null || Document.Layers.Count < 2) {
            return;
        }

        var beforeIsElevator = string.Equals(
            before.BlockId, ElevatorBlockIdLowercase, StringComparison.OrdinalIgnoreCase);
        var afterIsElevator = string.Equals(
            after.BlockId, ElevatorBlockIdLowercase, StringComparison.OrdinalIgnoreCase);
        if (!beforeIsElevator && !afterIsElevator) {
            return;
        }

        var activeLayerId = Document.ActiveLayerId;
        if (string.IsNullOrWhiteSpace(activeLayerId)) {
            return;
        }

        var row = cell.Row;
        var column = cell.Column;
        var outOfSync = new List<WorldLayerDefinition>();
        foreach (var layer in Document.Layers) {
            if (string.Equals(layer.Id, activeLayerId, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            if (row < 0 || row >= layer.Cells.Count) {
                continue;
            }

            if (column < 0 || column >= layer.Cells[row].Count) {
                continue;
            }

            var otherIsElevator = string.Equals(
                layer.Cells[row][column], ElevatorBlockIdLowercase, StringComparison.OrdinalIgnoreCase);
            if (otherIsElevator != afterIsElevator) {
                outOfSync.Add(layer);
            }
        }

        if (outOfSync.Count == 0) {
            return;
        }

        var layerNames = string.Join(", ", outOfSync.Select(layer => layer.Id));
        var message = afterIsElevator
            ? $"Hai aggiunto un ascensore alla cella (row {row}, col {column}).\n"
              + $"Vuoi aggiungerlo anche al layer {layerNames} nella stessa posizione "
              + "per mantenere i livelli allineati?"
            : $"Hai rimosso l'ascensore dalla cella (row {row}, col {column}).\n"
              + $"Vuoi rimuoverlo anche dal layer {layerNames}?";

        var result = MessageBox.Show(
            message,
            "Sincronizza ascensori tra layer",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) {
            return;
        }

        var replacement = afterIsElevator
            ? ElevatorBlockIdLowercase
            : ChooseReplacementBlockId(outOfSync[0], row, column, before.BlockId);

        foreach (var layer in outOfSync) {
            if (row < 0 || row >= layer.Cells.Count) {
                continue;
            }

            if (column < 0 || column >= layer.Cells[row].Count) {
                continue;
            }

            layer.Cells[row][column] = replacement;
        }

        ValidationMessages.Add(afterIsElevator
            ? $"Elevator added to layer(s) {layerNames} at (row {row}, col {column})."
            : $"Elevator removed from layer(s) {layerNames} at (row {row}, col {column}); replaced with '{replacement}'.");
    }

    private static string ChooseReplacementBlockId(
        WorldLayerDefinition layer, int row, int column, string fallbackBlockId)
    {
        // Try the cell to the north (row-1) of the other layer as a hint of what
        // the surrounding block looks like; otherwise fall back to the block the
        // active layer used to replace the elevator.
        if (row - 1 >= 0
            && row - 1 < layer.Cells.Count
            && column >= 0
            && column < layer.Cells[row - 1].Count) {
            var north = layer.Cells[row - 1][column];
            if (!string.Equals(north, ElevatorBlockIdLowercase, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(north)) {
                return north;
            }
        }

        return string.IsNullOrWhiteSpace(fallbackBlockId)
            ? WorldJsonDocumentService.EmptyBlockId
            : fallbackBlockId;
    }

    private bool m_selectedBlockRefreshScheduled;
    private bool m_preview3DRefreshScheduled;

    private void OnSelectedBlockChanged(object? sender, PropertyChangedEventArgs args)
    {
        ScheduleSelectedBlockRefresh();
    }

    private void ScheduleSelectedBlockRefresh()
    {
        if (m_selectedBlockRefreshScheduled) {
            return;
        }

        m_selectedBlockRefreshScheduled = true;
        void Refresh()
        {
            m_selectedBlockRefreshScheduled = false;
            RefreshSelectedBlockPreview3D();
            if (SelectedBlock is not null
                && SelectedCell is not null
                && string.Equals(
                    SelectedCell.Cell.BlockId,
                    SelectedBlock.Id,
                    StringComparison.OrdinalIgnoreCase)) {
                NotifySelectedCellEditorChanged();
            }

            SchedulePreview3DRefresh();
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null) {
            Refresh();
            return;
        }

        dispatcher.BeginInvoke(
            new Action(Refresh),
            System.Windows.Threading.DispatcherPriority.Background);
    }

    private void SchedulePreview3DRefresh()
    {
        if (m_preview3DRefreshScheduled) {
            return;
        }

        m_preview3DRefreshScheduled = true;
        void Refresh()
        {
            m_preview3DRefreshScheduled = false;
            RefreshPreview3D();
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null) {
            Refresh();
            return;
        }

        dispatcher.BeginInvoke(
            new Action(Refresh),
            System.Windows.Threading.DispatcherPriority.Background);
    }

    private void RecordUndoAction(IEditorUndoAction action)
    {
        if (m_isApplyingHistory) {
            return;
        }

        m_undoStack.Push(action);
        m_redoStack.Clear();
        RaiseHistoryCanExecuteChanged();
    }

    private void RaiseHistoryCanExecuteChanged()
    {
        UndoCommand.RaiseCanExecuteChanged();
        RedoCommand.RaiseCanExecuteChanged();
    }

    private EditorCellViewModel? CellAt(int row, int column)
    {
        if (Document is null
            || row < 0
            || column < 0
            || row >= Document.RowCount
            || column >= Document.ColumnCount) {
            return null;
        }

        var index = row * Document.ColumnCount + column;
        return index >= 0 && index < Cells.Count
            ? Cells[index]
            : null;
    }

    private void ApplyCellContent(EditorCellViewModel cell, EditorCellContent content)
    {
        cell.ApplyContent(CloneContent(content));
        if (cell == SelectedCell) {
            NotifySelectedCellEditorChanged();
        }

        RefreshPreview3D();
    }

    internal void ApplyCellContentForHistory(EditorCellViewModel cell, EditorCellContent content)
    {
        ApplyCellContent(cell, content);
    }

    internal void ApplyCellBlockEditForHistory(
        EditorCellViewModel cell,
        EditorCellContent content,
        string blockId,
        WorldBlockDefinition block)
    {
        if (Document is not null && !string.IsNullOrWhiteSpace(blockId)) {
            Document.Blocks[blockId] = CloneBlock(block);
            RefreshBlockPalette();
            SelectedBlock = Blocks.FirstOrDefault(item =>
                string.Equals(item.Id, content.BlockId, StringComparison.OrdinalIgnoreCase));
            BlockSummary = $"{Document.Blocks.Count} block(s) in palette";
            OnPropertyChanged(nameof(BlockSummary));
        }

        ApplyCellContent(cell, content);
    }

    internal void ApplyBlockTemplateForHistory(string blockId, WorldBlockDefinition block)
    {
        if (Document is null || string.IsNullOrWhiteSpace(blockId)) {
            return;
        }

        Document.Blocks[blockId] = CloneBlock(block);
        SyncActiveCellsFromBlock(blockId);
        RefreshBlockPalette();
        SelectedBlock = Blocks.FirstOrDefault(item =>
            string.Equals(item.Id, blockId, StringComparison.OrdinalIgnoreCase));
        BlockSummary = $"{Document.Blocks.Count} block(s) in palette";
        OnPropertyChanged(nameof(BlockSummary));
        NotifySelectedCellEditorChanged();
    }

    private static EditorCellContent CloneContent(EditorCellContent content)
    {
        return new EditorCellContent {
            Fields = content.Fields,
            BlockId = content.BlockId,
            HorizonImage = content.HorizonImage
        };
    }

    private static bool ContentEquals(EditorCellContent lhs, EditorCellContent rhs)
    {
        return lhs.Fields == rhs.Fields
            && lhs.BlockId == rhs.BlockId
            && lhs.HorizonImage == rhs.HorizonImage;
    }

    private void AddSpriteToSelectedCell() => AddSpriteToSelectedCell(forceItem: null);

    private void AddItemToSelectedCell() => AddSpriteToSelectedCell(forceItem: true);

    private void AddSpriteToSelectedCell(bool? forceItem)
    {
        if (Document is null || SelectedCell is null || string.IsNullOrWhiteSpace(m_loadedSpriteSetName)) {
            return;
        }

        var isItem = forceItem
            ?? EditorSpriteClassifier.IsItemSpriteSet(m_loadedSpriteSetName, SelectedSpriteSetFile);

        var sprite = new EditorSpriteInstance {
            Name = $"{m_loadedSpriteSetName}_{Document.SpriteInstances.Count + 1}",
            SpriteSet = m_loadedSpriteSetName,
            XCell = SelectedCell.Column + 0.5,
            YCell = SelectedCell.Row + 0.5,
            Visible = true
        };
        EditorSpriteClassifier.ApplyPlacementDefaults(sprite, isItem);

        if (!Document.AddSpriteInstance(sprite, SelectedCell.Row, SelectedCell.Column)) {
            return;
        }

        // When editing a layered world, new sprites belong to the active layer
        // (saved into that layer) rather than the shared top-level sprite set.
        if (Document.Layers.Count > 0) {
            Document.ActiveLayerSprites.Add(sprite);
        }

        SelectedCell.NotifySpriteCollectionChanged();
        var spriteViewModel = CreateSpriteInstanceViewModel(sprite);
        SpriteInstances.Add(spriteViewModel);
        SelectedSprite = spriteViewModel;
        RefreshSelectedCellSprites();
        UpdateSpriteSummary();
    }

    public void SelectSprite(EditorSpriteInstance sprite)
    {
        SelectedSprite = SpriteInstances.FirstOrDefault(viewModel => ReferenceEquals(viewModel.Sprite, sprite));
    }

    public bool IsSpriteLayerSelected =>
        string.Equals(SelectedLayer, "Sprites", StringComparison.Ordinal);

    public bool IsCellEditingLayerSelected =>
        IsWallLayerSelected || IsFloorLayerSelected || IsCeilingLayerSelected;

    public bool IsWallLayerSelected =>
        string.Equals(SelectedLayer, "Walls", StringComparison.Ordinal);

    public bool IsFloorLayerSelected =>
        string.Equals(SelectedLayer, "Floor", StringComparison.Ordinal);

    public bool IsCeilingLayerSelected =>
        string.Equals(SelectedLayer, "Ceiling", StringComparison.Ordinal);

    public bool IsGoalLayerSelected =>
        string.Equals(SelectedLayer, "Goal", StringComparison.Ordinal);

    private void SelectFirstSpriteFromSelectedCell()
    {
        if (SelectedCell is null) {
            SelectedSprite = null;
            return;
        }

        if (SelectedSprite is not null && SelectedCell.Cell.Sprites.Contains(SelectedSprite.Sprite)) {
            return;
        }

        if (SelectedCell?.Cell.Sprites.FirstOrDefault() is { } sprite) {
            SelectSprite(sprite);
        }
        else {
            SelectedSprite = null;
        }
    }

    private void SelectCellForSelectedSprite()
    {
        if (Document is null || SelectedSprite is null) {
            return;
        }

        var row = (int)Math.Floor(SelectedSprite.YCell);
        var column = (int)Math.Floor(SelectedSprite.XCell);
        if (SelectedCell is not null && SelectedCell.Row == row && SelectedCell.Column == column) {
            RefreshSelectedCellSprites();
            return;
        }

        var targetCell = Cells.FirstOrDefault(cell => cell.Row == row && cell.Column == column);
        if (targetCell is not null) {
            SelectedCell = targetCell;
        }
    }

    private void SyncSpriteMetadataWithSelectedSprite()
    {
        if (SelectedSprite is null) {
            return;
        }

        var spriteSetFile = FindSpriteSetFileForSpriteSet(SelectedSprite.SpriteSet);
        if (string.IsNullOrWhiteSpace(spriteSetFile)
            || string.Equals(SelectedSpriteSetFile, spriteSetFile, StringComparison.OrdinalIgnoreCase)) {
            return;
        }

        SelectedSpriteSetFile = spriteSetFile;
    }

    private string? FindSpriteSetFileForSpriteSet(string spriteSet)
    {
        if (string.IsNullOrWhiteSpace(spriteSet)) {
            return null;
        }

        if (m_spriteSetFilesByName.TryGetValue(spriteSet, out var spriteSetFile)) {
            return spriteSetFile;
        }

        foreach (var candidate in SpriteSetFiles) {
            var metadataPath = ResolveSpriteMetadataPath(candidate, m_assetBasePath);
            if (metadataPath is null) {
                continue;
            }

            var result = SpriteMetadataLoader.Load(metadataPath);
            if (result.Document is null) {
                continue;
            }

            m_spriteSetFilesByName[result.Document.SpriteSet] = candidate;
            if (string.Equals(result.Document.SpriteSet, spriteSet, StringComparison.OrdinalIgnoreCase)) {
                return candidate;
            }
        }

        return null;
    }

    private void RefreshSelectedCellSprites()
    {
        var selectedSprite = SelectedSprite;
        m_isRefreshingSelectedCellSprites = true;
        try {
            SelectedCellSprites.Clear();
            if (SelectedCell is not null) {
                foreach (var sprite in SelectedCell.Cell.Sprites) {
                    var viewModel = SpriteInstances.FirstOrDefault(item =>
                        ReferenceEquals(item.Sprite, sprite));
                    if (viewModel is not null) {
                        SelectedCellSprites.Add(viewModel);
                    }
                }
            }
        }
        finally {
            m_isRefreshingSelectedCellSprites = false;
        }

        if (selectedSprite is not null && SelectedCellSprites.Contains(selectedSprite)) {
            OnPropertyChanged(nameof(SelectedSprite));
        }
    }

    public void MoveSelectedSpriteToCell(EditorCellViewModel targetCell)
    {
        if (SelectedSprite is null) {
            return;
        }

        MoveSpriteToCell(SelectedSprite, targetCell);
    }

    public void MoveSpriteToCell(EditorSpriteInstance sprite, EditorCellViewModel targetCell)
    {
        var viewModel = SpriteInstances.FirstOrDefault(item => ReferenceEquals(item.Sprite, sprite));
        if (viewModel is not null) {
            MoveSpriteToCell(viewModel, targetCell);
        }
    }

    private void CopySelectedSprite()
    {
        if (SelectedSprite is null) {
            return;
        }

        m_copiedSprite = CloneSprite(SelectedSprite.Sprite);
        m_isSpriteCutPending = false;
        NotifySpriteClipboardChanged();
        ValidationMessages.Add($"Copied sprite {SelectedSprite.Name}.");
    }

    private void CutSelectedSprite()
    {
        if (SelectedSprite is null) {
            return;
        }

        m_copiedSprite = SelectedSprite.Sprite;
        m_isSpriteCutPending = true;
        NotifySpriteClipboardChanged();
        ValidationMessages.Add($"Cut sprite {SelectedSprite.Name}; select a cell and paste it.");
    }

    private void CancelSpriteClipboard()
    {
        if (m_copiedSprite is null) {
            return;
        }

        m_copiedSprite = null;
        m_isSpriteCutPending = false;
        NotifySpriteClipboardChanged();
        ValidationMessages.Add("Cancelled pending sprite operation.");
    }

    private void PasteSpriteToSelectedCell()
    {
        if (SelectedCell is null) {
            return;
        }

        PasteSpriteToCell(SelectedCell);
    }

    private void PasteSpriteToCell(EditorCellViewModel targetCell)
    {
        if (Document is null || m_copiedSprite is null) {
            return;
        }

        if (m_isSpriteCutPending) {
            var viewModel = SpriteInstances.FirstOrDefault(
                sprite => ReferenceEquals(sprite.Sprite, m_copiedSprite));
            if (viewModel is not null) {
                MoveSpriteToCell(viewModel, targetCell);
                m_copiedSprite = null;
                m_isSpriteCutPending = false;
                NotifySpriteClipboardChanged();
                return;
            }

            // A layer switch hands layer-owned sprites back to their layer and
            // rebuilds the working set. Recover the cut object from that source
            // layer, then attach it to the currently active layer.
            foreach (var layer in Document.Layers) {
                layer.SpriteInstances.Remove(m_copiedSprite);
            }

            m_copiedSprite.XCell = targetCell.Column + 0.5;
            m_copiedSprite.YCell = targetCell.Row + 0.5;
            if (!Document.AddSpriteInstance(m_copiedSprite, targetCell.Row, targetCell.Column)) {
                return;
            }

            if (Document.Layers.Count > 0) {
                Document.ActiveLayerSprites.Add(m_copiedSprite);
            }

            var movedViewModel = CreateSpriteInstanceViewModel(m_copiedSprite);
            SpriteInstances.Add(movedViewModel);
            SelectedSprite = movedViewModel;
            targetCell.NotifySpriteCollectionChanged();
            m_copiedSprite = null;
            m_isSpriteCutPending = false;
            NotifySpriteClipboardChanged();
            RefreshSelectedCellSprites();
            UpdateSpriteSummary();
            RefreshValidation($"Moved sprite {movedViewModel.Name} to layer {Document.ActiveLayerId}.");
            return;
        }

        var clone = CloneSprite(m_copiedSprite);
        clone.Name = AllocateSpriteName(clone.Name);
        clone.XCell = targetCell.Column + 0.5;
        clone.YCell = targetCell.Row + 0.5;
        if (!Document.AddSpriteInstance(clone, targetCell.Row, targetCell.Column)) {
            return;
        }

        // The paste targets the active layer's map, so the clone belongs there.
        if (Document.Layers.Count > 0) {
            Document.ActiveLayerSprites.Add(clone);
        }

        targetCell.NotifySpriteCollectionChanged();
        var cloneViewModel = CreateSpriteInstanceViewModel(clone);
        SpriteInstances.Add(cloneViewModel);
        SelectedSprite = cloneViewModel;
        RefreshSelectedCellSprites();
        UpdateSpriteSummary();
        RefreshValidation($"Pasted sprite {clone.Name} into {targetCell.Coordinates}.");
    }

    private void MoveSpriteToCell(SpriteInstanceViewModel sprite, EditorCellViewModel targetCell)
    {
        if (Document is null) {
            return;
        }

        sprite.XCell = targetCell.Column + 0.5;
        sprite.YCell = targetCell.Row + 0.5;
        Document.RelocateSpriteInstance(sprite.Sprite);
        foreach (var cell in Cells) {
            cell.NotifySpriteCollectionChanged();
        }

        SelectedCell = targetCell;
        SelectedSprite = sprite;
        RefreshSelectedCellSprites();
        RefreshValidation($"Moved sprite {sprite.Name} to {targetCell.Coordinates}.");
    }

    private void PlacePlayerAtSelectedCell()
    {
        if (Document is null || SelectedCell is null || PlayerStart is null) {
            return;
        }

        MovePlayerToCell(SelectedCell);
    }

    public void MovePlayerToCell(EditorCellViewModel targetCell)
    {
        if (Document is null || PlayerStart is null) {
            return;
        }

        var before = ClonePlayerStart(Document.PlayerStart);
        var after = new WorldPlayerStart {
            XCell = targetCell.Column + 0.5,
            YCell = targetCell.Row + 0.5,
            FacingDegrees = Document.PlayerStart.FacingDegrees
        };
        if (PlayerStartEquals(before, after)) {
            return;
        }

        m_isApplyingHistory = true;
        try {
            ApplyPlayerStart(after);
        }
        finally {
            m_isApplyingHistory = false;
        }

        RecordUndoAction(new PlayerStartUndoAction(this, before, after));
        RefreshPlayerCellMarkers();
        RefreshValidation($"Moved player start to {targetCell.Coordinates}.");
    }

    private void OnPlayerStartChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (Document is null || PlayerStart is null) {
            return;
        }

        if (args.PropertyName is nameof(PlayerStartViewModel.XCell)
            or nameof(PlayerStartViewModel.YCell)) {
            RefreshPlayerCellMarkers();
        }

        RefreshValidation("Map validation completed.");
    }

    private void OnPlayerStartValueChanged(object? sender, PlayerStartChangedEventArgs args)
    {
        if (m_isApplyingHistory || PlayerStartEquals(args.Before, args.After)) {
            return;
        }

        RecordUndoAction(new PlayerStartUndoAction(this, args.Before, args.After));
    }

    private void RefreshPlayerCellMarkers()
    {
        if (Document is null) {
            return;
        }

        var playerRow = (int)Math.Floor(Document.PlayerStart.YCell);
        var playerColumn = (int)Math.Floor(Document.PlayerStart.XCell);
        foreach (var cell in Cells) {
            cell.HasPlayerStart = cell.Row == playerRow && cell.Column == playerColumn;
        }
    }

    private void SetSelectedCellAsGameGoal()
    {
        if (Document is null
            || SelectedCell is null
            || string.IsNullOrWhiteSpace(Document.ActiveLayerId)) {
            return;
        }

        var before = CloneGameGoal(Document.GameGoal);
        var after = new WorldGameGoal {
            Layer = Document.ActiveLayerId,
            Row = SelectedCell.Row,
            Column = SelectedCell.Column,
            RequiredKey = Document.GameGoal?.RequiredKey
        };
        if (GameGoalsEqual(before, after)) {
            return;
        }

        ApplyGameGoal(after);
        RecordUndoAction(new GameGoalUndoAction(this, before, after));
        RefreshValidation($"Set {SelectedCell.Coordinates} as the final cell.");
    }

    private void ClearGameGoal()
    {
        if (Document?.GameGoal is null) {
            return;
        }

        var before = CloneGameGoal(Document.GameGoal);
        ApplyGameGoal(null);
        RecordUndoAction(new GameGoalUndoAction(this, before, null));
        RefreshValidation("Removed the final-cell goal.");
    }

    private void RefreshGameGoalMarkers()
    {
        if (Document is null) {
            return;
        }

        var goal = Document.GameGoal;
        var activeLayerId = Document.ActiveLayerId;
        foreach (var cell in Cells) {
            cell.HasGameGoal = goal is not null
                && string.Equals(goal.Layer, activeLayerId, StringComparison.OrdinalIgnoreCase)
                && cell.Row == goal.Row
                && cell.Column == goal.Column;
        }
    }

    private void ApplyGameGoal(WorldGameGoal? goal)
    {
        if (Document is null) {
            return;
        }

        Document.GameGoal = CloneGameGoal(goal);
        RefreshGameGoalMarkers();
        OnPropertyChanged(nameof(HasGameGoal));
        OnPropertyChanged(nameof(GameGoalSummary));
        OnPropertyChanged(nameof(GameGoalRequiredKey));
        ClearGameGoalCommand.RaiseCanExecuteChanged();
        JsonPanel.RefreshFromModel();
    }

    internal void ApplyGameGoalForHistory(WorldGameGoal? goal)
    {
        m_isApplyingHistory = true;
        try {
            ApplyGameGoal(goal);
            RefreshValidation("Restored the final-cell goal.");
        }
        finally {
            m_isApplyingHistory = false;
        }
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

    private static bool GameGoalsEqual(WorldGameGoal? lhs, WorldGameGoal? rhs)
    {
        if (lhs is null || rhs is null) {
            return lhs is null && rhs is null;
        }

        return string.Equals(lhs.Layer, rhs.Layer, StringComparison.Ordinal)
            && lhs.Row == rhs.Row
            && lhs.Column == rhs.Column
            && string.Equals(lhs.RequiredKey, rhs.RequiredKey, StringComparison.Ordinal);
    }

    private void ApplyPlayerStart(WorldPlayerStart playerStart)
    {
        if (PlayerStart is null) {
            return;
        }

        PlayerStart.XCell = playerStart.XCell;
        PlayerStart.YCell = playerStart.YCell;
        PlayerStart.FacingDegrees = playerStart.FacingDegrees;
        RefreshPlayerCellMarkers();
    }

    internal void ApplyPlayerStartForHistory(WorldPlayerStart playerStart)
    {
        ApplyPlayerStart(ClonePlayerStart(playerStart));
    }

    private static WorldPlayerStart ClonePlayerStart(WorldPlayerStart playerStart)
    {
        return new WorldPlayerStart {
            XCell = playerStart.XCell,
            YCell = playerStart.YCell,
            FacingDegrees = playerStart.FacingDegrees
        };
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

    private WorldLayerDefinition CloneWorldLayer(WorldLayerDefinition layer)
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
            SpriteInstances = layer.SpriteInstances.Select(CloneSprite).ToList()
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

    private static bool PlayerStartEquals(WorldPlayerStart lhs, WorldPlayerStart rhs)
    {
        return Math.Abs(lhs.XCell - rhs.XCell) < 0.001
            && Math.Abs(lhs.YCell - rhs.YCell) < 0.001
            && Math.Abs(lhs.FacingDegrees - rhs.FacingDegrees) < 0.001;
    }

    private EditorSpriteInstance CloneSprite(EditorSpriteInstance sprite)
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

    private string AllocateSpriteName(string baseName)
    {
        if (Document is null) {
            return baseName;
        }

        var normalized = string.IsNullOrWhiteSpace(baseName) ? "sprite" : baseName;
        if (Document.SpriteInstances.All(sprite =>
            !string.Equals(sprite.Name, normalized, StringComparison.OrdinalIgnoreCase))) {
            return normalized;
        }

        for (var index = 2; ; ++index) {
            var candidate = $"{normalized}_{index}";
            if (Document.SpriteInstances.All(sprite =>
                !string.Equals(sprite.Name, candidate, StringComparison.OrdinalIgnoreCase))) {
                return candidate;
            }
        }
    }

    private void RemoveSelectedSprite()
    {
        if (Document is null || SelectedSprite is null) {
            return;
        }

        var sprite = SelectedSprite.Sprite;
        if (!Document.RemoveSpriteInstance(sprite)) {
            return;
        }

        SpriteInstances.Remove(SelectedSprite);
        SelectedSprite = SpriteInstances.FirstOrDefault();
        foreach (var cell in Cells) {
            cell.NotifySpriteCollectionChanged();
        }

        RefreshSelectedCellSprites();
        UpdateSpriteSummary();
    }

    private SpriteInstanceViewModel CreateSpriteInstanceViewModel(EditorSpriteInstance sprite)
    {
        var viewModel = new SpriteInstanceViewModel(sprite);
        viewModel.PropertyChanged += OnSpriteInstanceChanged;
        return viewModel;
    }

    private void OnSpriteInstanceChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (Document is null || sender is not SpriteInstanceViewModel sprite) {
            return;
        }

        if (args.PropertyName is nameof(SpriteInstanceViewModel.XCell)
            or nameof(SpriteInstanceViewModel.YCell)) {
            Document.RelocateSpriteInstance(sprite.Sprite);
            foreach (var cell in Cells) {
                cell.NotifySpriteCollectionChanged();
            }
            SelectCellForSelectedSprite();
            SchedulePreview3DRefresh();
        }
        else if (args.PropertyName is nameof(SpriteInstanceViewModel.Visible)) {
            NotifySpriteMapPreviewsChanged();
            SchedulePreview3DRefresh();
        }
        else if (args.PropertyName is nameof(SpriteInstanceViewModel.ScaleCells)) {
            foreach (var cell in Cells) {
                cell.NotifySpriteCollectionChanged();
            }
            SchedulePreview3DRefresh();
        }
        else if (args.PropertyName is nameof(SpriteInstanceViewModel.SpriteSet)) {
            foreach (var cell in Cells) {
                cell.NotifySpriteCollectionChanged();
            }
            OnPropertyChanged(nameof(SelectedSpritePreview));
            OnPropertyChanged(nameof(KeySprites));
            SchedulePreview3DRefresh();
        }
        else if (args.PropertyName is nameof(SpriteInstanceViewModel.FacingDegrees)) {
            SchedulePreview3DRefresh();
        }

        ScheduleSpriteValidation();
    }

    private void UpdateSpriteSummary()
    {
        if (Document is null) {
            SpriteSummary = "No sprite instances";
        }
        else {
            SpriteSummary = $"{Document.SpriteInstances.Count} sprite instances";
        }

        OnPropertyChanged(nameof(SpriteSummary));
        OnPropertyChanged(nameof(KeySprites));
    }


    private void LoadDefaultWorld()
    {
        var worldPath = FindRepoFile("res", "worlds", "demo_embedded", "demo.world.json");
        if (worldPath is null) {
            ValidationMessages.Add("Could not locate the demo world package.");
            return;
        }

        LoadWorldJsonFrom(worldPath);
    }

    private void RefreshBlockPalette()
    {
        Blocks.Clear();
        if (Document is null) {
            return;
        }

        if (Document.Blocks.Count == 0) {
            var derived = LegacyWorldConverter.FromEditorMap(Document, Document.SourcePath ?? string.Empty);
            foreach (var entry in derived.Blocks) {
                Document.Blocks[entry.Key] = entry.Value;
            }
        }

        var texturePreviews = new Dictionary<string, System.Windows.Media.ImageSource?>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var texture in Textures) {
            var key = texture.Asset.Key.ToString("x2", CultureInfo.InvariantCulture);
            texturePreviews[key] = texture.Preview;
        }

        foreach (var entry in Document.Blocks.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)) {
            Blocks.Add(new BlockPaletteEntryViewModel(entry.Key, entry.Value, texturePreviews));
        }
    }

    private void RefreshTextureChoices()
    {
        TextureChoices.Clear();
        TextureChoices.Add(new TextureChoiceViewModel(string.Empty, "(none)", null));
        foreach (var texture in Textures.OrderBy(texture => texture.Asset.Key)) {
            var key = TextureKey(texture.Asset.Key);
            TextureChoices.Add(new TextureChoiceViewModel(
                key,
                $"{key} - {texture.Name}",
                texture.Preview));
        }
    }

    public static bool IsSupportedTextureFile(string path) =>
        TextureImporter.IsSupportedTextureFile(path);

    /// <summary>
    /// The directory the world's <c>textures</c> folder lives under (next to the
    /// existing texture resources). Used by the import progress dialog.
    /// </summary>
    public string GetTextureWorldDirectory() => CurrentWorldDirectory();

    /// <summary>
    /// Adds one or more image files to the texture library synchronously. The import
    /// progress dialog uses the finer-grained <see cref="RegisterImportedTexture"/>
    /// and <see cref="FinalizeTextureImport"/> hooks instead.
    /// </summary>
    public void AddTexturesFromFiles(IReadOnlyList<string> paths)
    {
        if (Document is null || paths.Count == 0) {
            return;
        }

        var worldDirectory = CurrentWorldDirectory();
        var added = 0;
        byte? lastKey = null;
        foreach (var path in paths) {
            var result = RegisterImportedTexture(TextureImporter.CopyToWorld(path, worldDirectory));
            if (result.Status == TextureImportStatus.Added) {
                ++added;
                lastKey = result.Key;
            }
        }

        FinalizeTextureImport(lastKey, added);
    }

    /// <summary>
    /// Turns a copied image into a palette entry: reuses the existing key when the
    /// image is already in the library, otherwise allocates the next free key. Must
    /// run on the UI thread; call <see cref="FinalizeTextureImport"/> once afterwards.
    /// </summary>
    public TextureImportResult RegisterImportedTexture(TextureCopyOutcome outcome)
    {
        if (Document is null || !outcome.Success || outcome.RelativePath is null) {
            return new TextureImportResult {
                FileName = outcome.FileName,
                Status = TextureImportStatus.Failed,
                Message = outcome.Message ?? "No world is loaded."
            };
        }

        foreach (var existing in Document.TextureMap) {
            if (string.Equals(existing.Value, outcome.RelativePath, StringComparison.OrdinalIgnoreCase)) {
                return new TextureImportResult {
                    FileName = outcome.FileName,
                    Status = TextureImportStatus.ReusedExisting,
                    Key = existing.Key,
                    RelativePath = outcome.RelativePath,
                    DestinationPath = outcome.DestinationPath
                };
            }
        }

        var key = AllocateTextureKey();
        if (key is null) {
            return new TextureImportResult {
                FileName = outcome.FileName,
                Status = TextureImportStatus.Failed,
                Message = "Texture palette is full (255 textures maximum)."
            };
        }

        Document.TextureMap[key.Value] = outcome.RelativePath;
        return new TextureImportResult {
            FileName = outcome.FileName,
            Status = TextureImportStatus.Added,
            Key = key.Value,
            RelativePath = outcome.RelativePath,
            DestinationPath = outcome.DestinationPath
        };
    }

    /// <summary>
    /// Rebuilds the palette and selects the last added texture after a batch import.
    /// </summary>
    public void FinalizeTextureImport(byte? lastAddedKey, int addedCount)
    {
        if (Document is null || addedCount == 0) {
            return;
        }

        RebuildTexturePalette();
        if (lastAddedKey is byte key) {
            SelectedTexture = Textures.FirstOrDefault(texture => texture.Asset.Key == key)
                ?? SelectedTexture;
        }

        TextureSummary = $"{Document.TextureMap.Count} texture mappings";
        OnPropertyChanged(nameof(TextureSummary));
        RefreshValidation($"Added {addedCount} texture(s) to the library.");
    }

    private byte? AllocateTextureKey()
    {
        // Keys 0x01..0xfe are usable; 0x00 means "no texture" and 0xff is reserved
        // for the transparent/sky texture.
        for (var key = 1; key < 0xff; ++key) {
            if (!Document!.TextureMap.ContainsKey((byte)key)) {
                return (byte)key;
            }
        }

        return null;
    }

    private void RebuildTexturePalette()
    {
        if (Document is null) {
            return;
        }

        var selectedKey = SelectedTexture?.Asset.Key;
        Textures.Clear();
        m_texturePreviews.Clear();
        foreach (var texture in TexturePaletteBuilder.Build(Document, m_assetBasePath)) {
            var textureViewModel = new TextureAssetViewModel(texture);
            Textures.Add(textureViewModel);
            m_texturePreviews[texture.Key] = textureViewModel.Preview;
        }

        RefreshTextureChoices();
        RefreshBlockPalette();

        if (selectedKey is byte key) {
            SelectedTexture = Textures.FirstOrDefault(texture => texture.Asset.Key == key);
        }
    }

    private void AddTextureFromDialog()
    {
        if (Document is null) {
            return;
        }

        var dialog = new OpenFileDialog {
            Title = "Add textures to the library",
            Filter = "Image files (*.png;*.bmp)|*.png;*.bmp|PNG files (*.png)|*.png|BMP files (*.bmp)|*.bmp|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = true
        };

        if (dialog.ShowDialog() == true) {
            AddTexturesFromFiles(dialog.FileNames);
        }
    }

    private WorldBlockDefinition? SelectedCellBlock
    {
        get
        {
            if (Document is null || SelectedCell is null) {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(SelectedCell.Cell.BlockId)
                && Document.Blocks.TryGetValue(SelectedCell.Cell.BlockId, out var block)) {
                return block;
            }

            return BlockFromCellFields(SelectedCell.Cell.Fields);
        }
    }

    private void EditSelectedCellBlock(Action<WorldBlockDefinition> edit)
    {
        if (Document is null || SelectedCell is null) {
            return;
        }

        if (IsCellTemplateEditScopeSelected
            && !string.IsNullOrWhiteSpace(SelectedCell.Cell.BlockId)
            && Document.Blocks.TryGetValue(SelectedCell.Cell.BlockId, out var sharedBlock)) {
            EditSelectedCellSharedTemplate(sharedBlock, edit);
            return;
        }

        var before = EditorCellContent.Capture(SelectedCell.Cell);
        var block = EnsureSelectedCellOwnsEditableBlock();
        if (block is null) {
            return;
        }

        var editedBlockId = SelectedCell.Cell.BlockId;
        var blockBefore = CloneBlock(block);
        edit(block);
        SelectedCell.Cell.Fields = FieldsFromBlock(block);
        SelectedCell.Cell.HorizonImage = block.HorizonImage;
        var after = EditorCellContent.Capture(SelectedCell.Cell);
        var blockAfter = CloneBlock(block);
        ApplyCellContent(SelectedCell, after);
        if (!ContentEquals(before, after)
            || !string.Equals(
                LegacyWorldConverter.BlockSignature(blockBefore),
                LegacyWorldConverter.BlockSignature(blockAfter),
                StringComparison.Ordinal)) {
            RecordUndoAction(new CellBlockEditUndoAction(
                this,
                SelectedCell,
                before,
                after,
                editedBlockId,
                blockBefore,
                blockAfter));
        }

        RefreshBlockPalette();
        SelectedBlock = Blocks.FirstOrDefault(item => item.Id == SelectedCell.Cell.BlockId);
        BlockSummary = $"{Document.Blocks.Count} block(s) in palette";
        OnPropertyChanged(nameof(BlockSummary));
        RefreshValidation($"Updated cell {SelectedCell.Coordinates} using block {SelectedCell.Cell.BlockId}.");
    }

    private void EditSelectedCellSharedTemplate(
        WorldBlockDefinition block,
        Action<WorldBlockDefinition> edit)
    {
        if (Document is null || SelectedCell is null || string.IsNullOrWhiteSpace(SelectedCell.Cell.BlockId)) {
            return;
        }

        var blockId = SelectedCell.Cell.BlockId;
        var blockBefore = CloneBlock(block);
        edit(block);
        var blockAfter = CloneBlock(block);
        if (string.Equals(
                LegacyWorldConverter.BlockSignature(blockBefore),
                LegacyWorldConverter.BlockSignature(blockAfter),
                StringComparison.Ordinal)) {
            return;
        }

        SyncActiveCellsFromBlock(blockId);
        RecordUndoAction(new BlockTemplateEditUndoAction(
            this,
            blockId,
            blockBefore,
            blockAfter));

        RefreshBlockPalette();
        SelectedBlock = Blocks.FirstOrDefault(item => item.Id == blockId);
        BlockSummary = $"{Document.Blocks.Count} block(s) in palette";
        OnPropertyChanged(nameof(BlockSummary));
        NotifySelectedCellEditorChanged();
        RefreshValidation($"Updated shared template {blockId} from cell {SelectedCell.Coordinates}.");
    }

    private WorldBlockDefinition? EnsureSelectedCellOwnsEditableBlock()
    {
        if (Document is null || SelectedCell is null) {
            return null;
        }

        var cell = SelectedCell.Cell;
        if (!string.IsNullOrWhiteSpace(cell.BlockId)
            && Document.Blocks.TryGetValue(cell.BlockId, out var existingBlock)
            && CountBlockReferences(cell.BlockId) <= 1) {
            return existingBlock;
        }

        var source = SelectedCellBlock ?? new WorldBlockDefinition();
        var newId = AllocateBlockId(Document.Blocks);
        var clone = CloneBlock(source);
        clone.Name = $"{(string.IsNullOrWhiteSpace(source.Name) ? "cell_block" : source.Name)}_{cell.Column}_{cell.Row}";
        Document.Blocks[newId] = clone;
        cell.BlockId = newId;
        cell.Fields = FieldsFromBlock(clone);
        return clone;
    }

    private void NotifySelectedCellEditorChanged()
    {
        OnPropertyChanged(nameof(SelectedCellBlockId));
        OnPropertyChanged(nameof(SelectedCellBlockSummary));
        OnPropertyChanged(nameof(SelectedCellBlockReferenceCount));
        OnPropertyChanged(nameof(IsSelectedCellBlockUnique));
        OnPropertyChanged(nameof(IsCellTemplateEditScopeSelected));
        OnPropertyChanged(nameof(SelectedCellInstanceSummary));
        OnPropertyChanged(nameof(SelectedCellFloorTextureKey));
        OnPropertyChanged(nameof(SelectedCellCeilingTextureKey));
        OnPropertyChanged(nameof(SelectedCellLowerWallTextureKey));
        OnPropertyChanged(nameof(SelectedCellUpperWallTextureKey));
        OnPropertyChanged(nameof(SelectedCellTransparentWallTextureKey));
        OnPropertyChanged(nameof(SelectedCellDoorSummary));
        OnPropertyChanged(nameof(SelectedCellDoorEnabled));
        OnPropertyChanged(nameof(SelectedCellDoorBlocksWhenClosed));
        OnPropertyChanged(nameof(SelectedCellDoorRequiredKey));
        OnPropertyChanged(nameof(SelectedCellDoorTriggerDistanceCells));
        OnPropertyChanged(nameof(SelectedCellDoorOpenTimeSeconds));
        OnPropertyChanged(nameof(SelectedCellDoorCloseDelaySeconds));
        OnPropertyChanged(nameof(SelectedCellDoorGreenOverlayTexture));
        OnPropertyChanged(nameof(SelectedCellDoorBlueOverlayTexture));
        OnPropertyChanged(nameof(SelectedCellDoorRedOverlayTexture));
        OnPropertyChanged(nameof(SelectedCellDoorFramesText));
        OnPropertyChanged(nameof(SelectedCellIsElevator));
        OnPropertyChanged(nameof(SelectedCellTargetLayer));
        OnPropertyChanged(nameof(WorldLayerIdOptions));
        RefreshSelectedCellLayerConnections();
        OnPropertyChanged(nameof(SelectedCellFaceTextureKey));
        ApplySelectedTextureToFaceCommand?.RaiseCanExecuteChanged();
        OpenSelectedCellBlockCommand.RaiseCanExecuteChanged();
        MakeSelectedCellUniqueBlockCommand.RaiseCanExecuteChanged();
        EditSelectedCellUniqueBlockCommand.RaiseCanExecuteChanged();
        RefreshSelectedCellPreview3D();
    }

    public bool SelectedCellIsElevator =>
        Document is not null
        && SelectedCell is not null
        && (string.Equals(
                SelectedCell.Cell.BlockId,
                ElevatorBlockIdLowercase,
                StringComparison.OrdinalIgnoreCase)
            || Document.LayerTransitions.Any(transition =>
                string.Equals(
                    transition.FromLayer,
                    Document.ActiveLayerId,
                    StringComparison.OrdinalIgnoreCase)
                && transition.Trigger?.Row == SelectedCell.Row
                && transition.Trigger?.Column == SelectedCell.Column));

    private void RefreshSelectedCellLayerConnections()
    {
        SelectedCellLayerConnections.Clear();
        if (Document is null || SelectedCell is null || !SelectedCellIsElevator) {
            return;
        }

        var fromLayer = Document.ActiveLayerId ?? string.Empty;
        foreach (var layer in Document.Layers) {
            if (string.IsNullOrWhiteSpace(layer.Id)
                || string.Equals(layer.Id, fromLayer, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            var isConnected = Document.LayerTransitions.Any(transition =>
                string.Equals(transition.FromLayer, fromLayer, StringComparison.OrdinalIgnoreCase)
                && string.Equals(transition.ToLayer, layer.Id, StringComparison.OrdinalIgnoreCase)
                && transition.Trigger?.Row == SelectedCell.Row
                && transition.Trigger?.Column == SelectedCell.Column);
            SelectedCellLayerConnections.Add(new LayerConnectionOptionViewModel(
                layer.Id,
                string.IsNullOrWhiteSpace(layer.Name) ? layer.Id : $"{layer.Name} ({layer.Id})",
                isConnected,
                SetSelectedCellLayerConnection));
        }
    }

    private void SetSelectedCellLayerConnection(string targetLayer, bool isConnected)
    {
        if (Document is null || SelectedCell is null) {
            return;
        }

        var fromLayer = Document.ActiveLayerId ?? string.Empty;
        var matching = Document.LayerTransitions.Where(transition =>
            string.Equals(transition.FromLayer, fromLayer, StringComparison.OrdinalIgnoreCase)
            && string.Equals(transition.ToLayer, targetLayer, StringComparison.OrdinalIgnoreCase)
            && transition.Trigger?.Row == SelectedCell.Row
            && transition.Trigger?.Column == SelectedCell.Column).ToList();
        if (!isConnected) {
            foreach (var transition in matching) {
                Document.LayerTransitions.Remove(transition);
            }
        }
        else if (matching.Count == 0) {
            Document.LayerTransitions.Add(new WorldLayerTransition {
                FromLayer = fromLayer,
                ToLayer = targetLayer,
                RequiredKey = Document.GameGoal is { } goal
                    && string.Equals(goal.Layer, targetLayer, StringComparison.OrdinalIgnoreCase)
                        ? goal.RequiredKey
                        : null,
                Trigger = new WorldLayerTransitionTrigger {
                    BlockId = SelectedCell.Cell.BlockId,
                    Row = SelectedCell.Row,
                    Column = SelectedCell.Column
                },
                WaitSeconds = 1.5,
                TargetPlayerStart = new WorldPlayerStart {
                    XCell = SelectedCell.Column + 0.5,
                    YCell = SelectedCell.Row + 0.5,
                    FacingDegrees = 0
                }
            });
        }

        RefreshElevatorTargetLabels();
        UpdateJsonHighlight();
    }

    public IEnumerable<string> WorldLayerIdOptions
    {
        get
        {
            if (Document is null) {
                yield break;
            }

            foreach (var layer in Document.Layers) {
                if (!string.IsNullOrWhiteSpace(layer.Id)) {
                    yield return layer.Id;
                }
            }
        }
    }

    public string? SelectedCellTargetLayer
    {
        get
        {
            if (Document is null || SelectedCell is null) {
                return null;
            }

            var fromLayer = Document.ActiveLayerId;
            if (string.IsNullOrWhiteSpace(fromLayer)) {
                return null;
            }

            var existing = Document.LayerTransitions.FirstOrDefault(t =>
                string.Equals(t.FromLayer, fromLayer, StringComparison.OrdinalIgnoreCase)
                && t.Trigger?.Row == SelectedCell.Row
                && t.Trigger?.Column == SelectedCell.Column);
            return existing?.ToLayer;
        }
        set
        {
            if (Document is null || SelectedCell is null) {
                return;
            }

            var fromLayer = Document.ActiveLayerId;
            if (string.IsNullOrWhiteSpace(fromLayer)) {
                return;
            }

            var existing = Document.LayerTransitions.FirstOrDefault(t =>
                string.Equals(t.FromLayer, fromLayer, StringComparison.OrdinalIgnoreCase)
                && t.Trigger?.Row == SelectedCell.Row
                && t.Trigger?.Column == SelectedCell.Column);

            if (string.IsNullOrWhiteSpace(value)) {
                if (existing is not null) {
                    Document.LayerTransitions.Remove(existing);
                }
            }
            else {
                if (existing is null) {
                    existing = new WorldLayerTransition {
                        FromLayer = fromLayer,
                        Trigger = new WorldLayerTransitionTrigger {
                            BlockId = ElevatorBlockIdLowercase,
                            Row = SelectedCell.Row,
                            Column = SelectedCell.Column
                        },
                        WaitSeconds = 1.5
                    };
                    Document.LayerTransitions.Add(existing);
                }

                existing.ToLayer = value;
                existing.TargetPlayerStart = new WorldPlayerStart {
                    XCell = SelectedCell.Column + 0.5,
                    YCell = SelectedCell.Row + 0.5,
                    FacingDegrees = 0
                };
            }

            OnPropertyChanged();
            RefreshElevatorTargetLabels();
        }
    }

    private void AdjustInspectorPreview(
        object? parameter,
        double rotateDegrees = 0.0,
        double zoomFactor = 1.0,
        double shiftX = 0.0,
        double shiftZ = 0.0,
        bool fit = false)
    {
        var isBlockPreview = string.Equals(parameter?.ToString(), "Block", StringComparison.OrdinalIgnoreCase);
        var state = isBlockPreview
            ? m_selectedBlockPreviewCameraState
            : m_selectedCellPreviewCameraState;

        if (fit) {
            state.Reset();
        }
        else {
            state.YawDegrees = NormalizeDegrees(state.YawDegrees + rotateDegrees);
            state.Zoom = Math.Clamp(state.Zoom * zoomFactor, 0.38, 3.5);
            state.ShiftX = Math.Clamp(state.ShiftX + shiftX, -1.0, 1.0);
            state.ShiftZ = Math.Clamp(state.ShiftZ + shiftZ, -1.0, 1.0);
        }

        if (isBlockPreview) {
            RefreshSelectedBlockPreview3D();
        }
        else {
            RefreshSelectedCellPreview3D();
        }
    }

    private static void UpdateInspectorPreviewCamera(PerspectiveCamera camera, InspectorPreviewCameraState state)
    {
        var yaw = state.YawDegrees * Math.PI / 180.0;
        var pitch = Math.Clamp(state.PitchDegrees, 8.0, 70.0) * Math.PI / 180.0;
        var distance = 3.0 * state.Zoom;
        var horizontalDistance = Math.Cos(pitch) * distance;
        var targetX = 0.5 + state.ShiftX;
        var targetY = 0.7;
        var targetZ = 0.5 + state.ShiftZ;

        camera.Position = new Point3D(
            targetX + Math.Sin(yaw) * horizontalDistance,
            targetY + Math.Sin(pitch) * distance,
            targetZ - Math.Cos(yaw) * horizontalDistance);
        camera.LookDirection = new Vector3D(
            targetX - camera.Position.X,
            targetY - camera.Position.Y,
            targetZ - camera.Position.Z);
        camera.UpDirection = new Vector3D(0.0, 1.0, 0.0);
        camera.FieldOfView = 48;
    }

    private void RefreshSelectedCellPreview3D()
    {
        UpdateInspectorPreviewCamera(SelectedCellPreview3DCamera, m_selectedCellPreviewCameraState);
        if (Document is null || SelectedCell is null) {
            SelectedCellPreview3DModel = new Model3DGroup();
            SelectedCellPreview3DHitTargets = new();
        }
        else {
            var previewDocument = new EditorMapDocument {
                SourcePath = Document.SourcePath,
                CellWidth = Document.CellWidth,
                CellHeight = Document.CellHeight,
                DefaultHorizonImage = Document.DefaultHorizonImage
            };
            foreach (var texture in Document.TextureMap) {
                previewDocument.TextureMap[texture.Key] = texture.Value;
            }

            foreach (var block in Document.Blocks) {
                previewDocument.Blocks[block.Key] = block.Value;
            }

            var previewCell = new EditorMapCell(0, 0, SelectedCell.Cell.PackedValue) {
                BlockId = SelectedCell.Cell.BlockId,
                HorizonImage = SelectedCell.Cell.HorizonImage
            };
            foreach (var sprite in SelectedCell.Cell.Sprites) {
                previewCell.Sprites.Add(sprite);
            }

            previewDocument.Rows.Add([previewCell]);
            var scene = WorldPreview3DBuilder.Build(
                previewDocument,
                m_assetBasePath,
                m_spriteMapPreviews,
                SelectedCellPreview3DCamera.LookDirection,
                new WorldPreview3DLayers {
                    ShowGrid = true,
                    ShowFloors = true,
                    ShowCeilings = true,
                    ShowWalls = true,
                    ShowSprites = true,
                    ShowPlayer = false
                },
                0,
                0);
            SelectedCellPreview3DModel = scene.Model;
            SelectedCellPreview3DHitTargets = scene.HitTargets.ToDictionary(kv => kv.Key, kv => kv.Value);
        }

        OnPropertyChanged(nameof(SelectedCellPreview3DModel));
        OnPropertyChanged(nameof(SelectedCellPreview3DCamera));
    }

    private void RefreshSelectedBlockPreview3D()
    {
        UpdateInspectorPreviewCamera(SelectedBlockPreview3DCamera, m_selectedBlockPreviewCameraState);
        if (Document is null || SelectedBlock is null) {
            SelectedBlockPreview3DModel = new Model3DGroup();
            SelectedBlockPreview3DHitTargets = new();
        }
        else {
            var (model, targets) = BuildOneBlockPreviewModel(SelectedBlock.Id, SelectedBlock.Block);
            SelectedBlockPreview3DModel = model;
            SelectedBlockPreview3DHitTargets = targets;
        }

        OnPropertyChanged(nameof(SelectedBlockPreview3DModel));
        OnPropertyChanged(nameof(SelectedBlockPreview3DCamera));
    }

    private (Model3DGroup model, Dictionary<Model3D, WorldPreview3DHitTarget> targets) BuildOneBlockPreviewModel(
        string blockId, WorldBlockDefinition block)
    {
        if (Document is null) {
            return (new Model3DGroup(), new Dictionary<Model3D, WorldPreview3DHitTarget>());
        }

        var previewDocument = new EditorMapDocument {
            SourcePath = Document.SourcePath,
            CellWidth = Document.CellWidth,
            CellHeight = Document.CellHeight,
            DefaultHorizonImage = Document.DefaultHorizonImage
        };
        foreach (var texture in Document.TextureMap) {
            previewDocument.TextureMap[texture.Key] = texture.Value;
        }

        previewDocument.Blocks[blockId] = block;
        previewDocument.Rows.Add([
            new EditorMapCell(0, 0, 0) {
                BlockId = blockId,
                Fields = FieldsFromBlock(block),
                HorizonImage = block.HorizonImage
            }
        ]);

        var scene = WorldPreview3DBuilder.Build(
            previewDocument,
            m_assetBasePath,
            m_spriteMapPreviews,
            SelectedBlockPreview3DCamera.LookDirection,
            new WorldPreview3DLayers {
                ShowGrid = true,
                ShowFloors = true,
                ShowCeilings = true,
                ShowWalls = true,
                ShowSprites = false,
                ShowPlayer = false
            },
            0,
            0);
        var targets = scene.HitTargets.ToDictionary(kv => kv.Key, kv => kv.Value);
        return (scene.Model, targets);
    }

    private static void SetSurfaceTexture(WorldBlockDefinition block, bool isFloor, string textureKey)
    {
        if (string.IsNullOrWhiteSpace(textureKey)) {
            if (isFloor) {
                block.Floor = null;
            }
            else {
                block.Ceiling = null;
            }

            return;
        }

        var surface = isFloor
            ? block.Floor ??= new WorldSurface { Height = 0 }
            : block.Ceiling ??= new WorldSurface { Height = 512 };
        surface.Texture = textureKey;
    }

    private static WorldDoorDefinition EnsureDoor(WorldBlockDefinition block)
    {
        block.Door ??= new WorldDoorDefinition();
        return block.Door;
    }

    private static string DoorSummary(WorldDoorDefinition? door)
    {
        if (door is null) {
            return "Door metadata: none";
        }

        var state = door.Enabled ? "enabled" : "disabled";
        var key = string.IsNullOrWhiteSpace(door.RequiredKey)
            ? "no key"
            : $"key {door.RequiredKey}";
        var overlayCount = door.LockedOverlays?.Count ?? 0;
        return $"Door metadata: {state}, {door.Frames.Count} frame(s), {key}, {overlayCount} lock overlay(s)";
    }

    private static string DoorOverlayTexture(WorldBlockDefinition? block, string key)
    {
        if (block?.Door?.LockedOverlays is null) {
            return string.Empty;
        }

        return block.Door.LockedOverlays.TryGetValue(key, out var texture)
            ? texture
            : string.Empty;
    }

    private void SetSelectedCellDoorOverlayTexture(string key, string? textureKey)
    {
        var normalized = string.IsNullOrWhiteSpace(textureKey) ? string.Empty : textureKey.Trim();
        if (string.Equals(
                DoorOverlayTexture(SelectedCellBlock, key),
                normalized,
                StringComparison.OrdinalIgnoreCase)) {
            return;
        }

        EditSelectedCellBlock(block => {
            var door = EnsureDoor(block);
            if (string.IsNullOrWhiteSpace(normalized)) {
                door.LockedOverlays?.Remove(key);
                if (door.LockedOverlays is { Count: 0 }) {
                    door.LockedOverlays = null;
                }

                return;
            }

            door.LockedOverlays ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            door.LockedOverlays[key] = normalized;
        });
    }

    private void SetWallTexture(WorldBlockDefinition block, WallSlot slot, string textureKey)
    {
        var existing = slot switch
        {
            WallSlot.Lower => LowerWallSpan(block),
            WallSlot.Upper => UpperWallSpan(block),
            WallSlot.Transparent => TransparentWallSpan(block),
            _ => null
        };

        if (string.IsNullOrWhiteSpace(textureKey)) {
            if (existing is not null) {
                block.Walls.Remove(existing);
            }

            return;
        }

        var wall = existing ?? CreateWallSpan(slot);
        wall.Texture = textureKey;
        if (existing is null) {
            block.Walls.Add(wall);
        }
    }

    private WorldWallSpan CreateWallSpan(WallSlot slot)
    {
        var height = Document?.CellHeight ?? 512;
        return slot switch
        {
            WallSlot.Upper => new WorldWallSpan {
                Kind = "solid",
                Bottom = height,
                Top = height * 2,
                Collision = true
            },
            WallSlot.Transparent => new WorldWallSpan {
                Kind = "transparent",
                Bottom = 0,
                Top = height,
                Collision = false
            },
            _ => new WorldWallSpan {
                Kind = "solid",
                Bottom = 0,
                Top = height,
                Collision = true
            }
        };
    }

    private MapCellFields FieldsFromBlock(WorldBlockDefinition block)
    {
        var fields = new MapCellFields(
            SolidWallTexture: 0,
            CeilingTexture: TextureByte(block.Ceiling?.Texture),
            FloorTexture: TextureByte(block.Floor?.Texture),
            TransparentWallTexture: 0,
            UpperWallTexture: 0);
        var height = Document?.CellHeight ?? 512;
        foreach (var wall in block.Walls) {
            var texture = TextureByte(PrimaryWallTexture(wall));
            if (texture == 0) {
                continue;
            }

            if (IsTransparentWall(wall)) {
                fields = fields with { TransparentWallTexture = texture };
            }
            else if (wall.Bottom >= height) {
                fields = fields with { UpperWallTexture = texture };
            }
            else if (fields.SolidWallTexture == 0) {
                fields = fields with { SolidWallTexture = texture };
            }
        }

        return fields;
    }

    private WorldBlockDefinition BlockFromCellFields(MapCellFields fields)
    {
        var block = new WorldBlockDefinition();
        var height = Document?.CellHeight ?? 512;
        if (fields.FloorTexture != 0) {
            block.Floor = new WorldSurface { Texture = TextureKey(fields.FloorTexture), Height = 0 };
        }

        if (fields.CeilingTexture != 0) {
            block.Ceiling = new WorldSurface { Texture = TextureKey(fields.CeilingTexture), Height = height };
        }

        if (fields.SolidWallTexture != 0) {
            block.Walls.Add(new WorldWallSpan {
                Kind = "solid",
                Texture = TextureKey(fields.SolidWallTexture),
                Bottom = 0,
                Top = height,
                Collision = true
            });
        }

        if (fields.UpperWallTexture != 0) {
            block.Walls.Add(new WorldWallSpan {
                Kind = "solid",
                Texture = TextureKey(fields.UpperWallTexture),
                Bottom = height,
                Top = height * 2,
                Collision = true
            });
        }

        if (fields.TransparentWallTexture != 0) {
            block.Walls.Add(new WorldWallSpan {
                Kind = "transparent",
                Texture = TextureKey(fields.TransparentWallTexture),
                Bottom = 0,
                Top = height,
                Collision = false
            });
        }

        return block;
    }

    private static WorldWallSpan? LowerWallSpan(WorldBlockDefinition? block)
    {
        return block?.Walls.FirstOrDefault(wall => !IsTransparentWall(wall) && wall.Bottom <= 0);
    }

    private static WorldWallSpan? UpperWallSpan(WorldBlockDefinition? block)
    {
        return block?.Walls.FirstOrDefault(wall => !IsTransparentWall(wall) && wall.Bottom > 0);
    }

    private static WorldWallSpan? TransparentWallSpan(WorldBlockDefinition? block)
    {
        return block?.Walls.FirstOrDefault(IsTransparentWall);
    }

    private static bool IsTransparentWall(WorldWallSpan wall)
    {
        return string.Equals(wall.Kind, "transparent", StringComparison.OrdinalIgnoreCase);
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

    private static byte TextureByte(string? textureKey)
    {
        return byte.TryParse(textureKey, System.Globalization.NumberStyles.HexNumber, null, out var key)
            ? key
            : (byte)0;
    }

    private static string TextureKey(byte key)
    {
        return key.ToString("x2");
    }

    private void CloneSelectedBlock()
    {
        if (Document is null || SelectedBlock is null) {
            return;
        }

        var sourceId = SelectedBlock.Id;
        var newId = AllocateBlockId(Document.Blocks);
        var clone = CloneBlock(SelectedBlock.Block);
        clone.Name = $"{SelectedBlock.Name}_copy";
        Document.Blocks[newId] = clone;
        RefreshBlockPalette();
        SelectedBlock = Blocks.FirstOrDefault(block => block.Id == newId);
        BlockSummary = $"{Document.Blocks.Count} block(s) in palette";
        OnPropertyChanged(nameof(BlockSummary));
        RemoveDuplicateBlocksCommand.RaiseCanExecuteChanged();
        RemoveUnusedBlocksCommand.RaiseCanExecuteChanged();
        RefreshValidation($"Cloned block {newId} from {sourceId}.");
    }

    private void RemoveDuplicateBlocks()
    {
        if (Document is null || Document.Blocks.Count <= 1) {
            return;
        }

        // Group blocks by their visual/behavioural signature (name is ignored).
        // The lowest id of each group is kept; the rest are remapped onto it.
        var canonicalBySignature = new Dictionary<string, string>(StringComparer.Ordinal);
        var remap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in Document.Blocks.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)) {
            var signature = LegacyWorldConverter.BlockSignature(entry.Value);
            if (canonicalBySignature.TryGetValue(signature, out var canonical)) {
                remap[entry.Key] = canonical;
            }
            else {
                canonicalBySignature[signature] = entry.Key;
            }
        }

        if (remap.Count == 0) {
            RefreshValidation("No duplicate blocks found in the palette.");
            return;
        }

        RemapBlockReferences(remap);
        foreach (var duplicateId in remap.Keys) {
            Document.Blocks.Remove(duplicateId);
        }

        RefreshBlockPalette();
        SelectedBlock = Blocks.FirstOrDefault(block =>
                SelectedCell is not null
                && string.Equals(block.Id, SelectedCell.Cell.BlockId, StringComparison.OrdinalIgnoreCase))
            ?? Blocks.FirstOrDefault();

        BlockSummary = $"{Document.Blocks.Count} block(s) in palette";
        OnPropertyChanged(nameof(BlockSummary));
        OnPropertyChanged(nameof(SelectedCellBlockId));
        NotifySelectedCellEditorChanged();
        RemoveDuplicateBlocksCommand.RaiseCanExecuteChanged();
        RemoveUnusedBlocksCommand.RaiseCanExecuteChanged();
        RefreshValidation(
            $"Removed {remap.Count} duplicate block(s); palette now has {Document.Blocks.Count} unique block(s).");
    }

    private void RemoveUnusedBlocks()
    {
        if (Document is null || Document.Blocks.Count == 0) {
            return;
        }

        var referencedBlockIds = ReferencedBlockIds();
        var unusedBlockIds = Document.Blocks.Keys
            .Where(blockId => !referencedBlockIds.Contains(blockId))
            .OrderBy(blockId => blockId, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (unusedBlockIds.Count == 0) {
            RefreshValidation("No unused blocks found in the palette.");
            return;
        }

        var previouslySelectedBlockId = SelectedBlock?.Id;
        foreach (var blockId in unusedBlockIds) {
            Document.Blocks.Remove(blockId);
        }

        RefreshBlockPalette();
        SelectedBlock = !string.IsNullOrWhiteSpace(previouslySelectedBlockId)
            ? Blocks.FirstOrDefault(block =>
                string.Equals(block.Id, previouslySelectedBlockId, StringComparison.OrdinalIgnoreCase))
            : null;
        SelectedBlock ??= Blocks.FirstOrDefault(block =>
                SelectedCell is not null
                && string.Equals(block.Id, SelectedCell.Cell.BlockId, StringComparison.OrdinalIgnoreCase))
            ?? Blocks.FirstOrDefault();

        BlockSummary = $"{Document.Blocks.Count} block(s) in palette";
        OnPropertyChanged(nameof(BlockSummary));
        NotifySelectedCellEditorChanged();
        RemoveDuplicateBlocksCommand.RaiseCanExecuteChanged();
        RemoveUnusedBlocksCommand.RaiseCanExecuteChanged();
        RefreshValidation(
            $"Removed {unusedBlockIds.Count} unused block(s); palette now has {Document.Blocks.Count} block(s).");
    }

    private HashSet<string> ReferencedBlockIds()
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (Document is null) {
            return result;
        }

        foreach (var row in Document.Rows) {
            foreach (var cell in row) {
                AddReferencedBlockId(result, cell.BlockId);
            }
        }

        foreach (var layer in Document.Layers) {
            if (!string.IsNullOrWhiteSpace(Document.ActiveLayerId)
                && string.Equals(layer.Id, Document.ActiveLayerId, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            foreach (var row in layer.Cells) {
                foreach (var blockId in row) {
                    AddReferencedBlockId(result, blockId);
                }
            }
        }

        foreach (var transition in Document.LayerTransitions) {
            AddReferencedBlockId(result, transition.TriggerBlockId);
            AddReferencedBlockId(result, transition.Trigger?.BlockId);
        }

        return result;
    }

    private static void AddReferencedBlockId(HashSet<string> blockIds, string? blockId)
    {
        if (string.IsNullOrWhiteSpace(blockId)) {
            return;
        }

        blockIds.Add(blockId.Trim());
    }

    private void RemapBlockReferences(IReadOnlyDictionary<string, string> remap)
    {
        if (Document is null) {
            return;
        }

        // Active layer's live cells.
        foreach (var row in Document.Rows) {
            foreach (var cell in row) {
                if (remap.TryGetValue(cell.BlockId, out var canonical)) {
                    cell.BlockId = canonical;
                }
            }
        }

        // Every layer's stored cell grid (block ids as strings).
        foreach (var layer in Document.Layers) {
            foreach (var row in layer.Cells) {
                for (var column = 0; column < row.Count; ++column) {
                    if (remap.TryGetValue(row[column], out var canonical)) {
                        row[column] = canonical;
                    }
                }
            }
        }

        // Layer transition triggers reference block ids too.
        foreach (var transition in Document.LayerTransitions) {
            if (!string.IsNullOrEmpty(transition.TriggerBlockId)
                && remap.TryGetValue(transition.TriggerBlockId, out var canonicalTrigger)) {
                transition.TriggerBlockId = canonicalTrigger;
            }

            if (transition.Trigger is not null
                && !string.IsNullOrEmpty(transition.Trigger.BlockId)
                && remap.TryGetValue(transition.Trigger.BlockId, out var canonicalNested)) {
                transition.Trigger.BlockId = canonicalNested;
            }
        }
    }

    private int CountBlockReferences(string blockId)
    {
        if (Document is null || string.IsNullOrWhiteSpace(blockId)) {
            return 0;
        }

        var count = 0;
        foreach (var row in Document.Rows) {
            count += row.Count(cell => string.Equals(cell.BlockId, blockId, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var layer in Document.Layers) {
            if (string.Equals(layer.Id, Document.ActiveLayerId, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            foreach (var row in layer.Cells) {
                count += row.Count(cell => string.Equals(cell, blockId, StringComparison.OrdinalIgnoreCase));
            }
        }

        return count;
    }

    private void SyncActiveCellsFromBlock(string blockId)
    {
        if (Document is null || string.IsNullOrWhiteSpace(blockId)) {
            return;
        }

        if (!Document.Blocks.TryGetValue(blockId, out var block)) {
            return;
        }

        var fields = FieldsFromBlock(block);
        foreach (var cell in Cells.Where(cell =>
                     string.Equals(cell.Cell.BlockId, blockId, StringComparison.OrdinalIgnoreCase))) {
            var content = EditorCellContent.Capture(cell.Cell);
            content.Fields = fields;
            content.HorizonImage = block.HorizonImage;
            cell.ApplyContent(content);
        }

        RefreshSelectedCellSprites();
        RefreshPreview3D();
    }

    private bool CanOpenSelectedCellBlock()
    {
        return Document is not null
            && !string.IsNullOrWhiteSpace(SelectedCellBlockId)
            && Blocks.Any(block => string.Equals(block.Id, SelectedCellBlockId, StringComparison.OrdinalIgnoreCase));
    }

    private void OpenSelectedCellBlock()
    {
        if (!CanOpenSelectedCellBlock()) {
            return;
        }

        SelectedBlock = Blocks.First(block =>
            string.Equals(block.Id, SelectedCellBlockId, StringComparison.OrdinalIgnoreCase));
        SelectedInspectorTabIndex = BlockInspectorTabIndex;
    }

    private bool CanMakeSelectedCellUniqueBlock()
    {
        return Document is not null && SelectedCell is not null;
    }

    private void MakeSelectedCellUniqueBlock()
    {
        MakeSelectedCellUniqueBlock(openEditor: false);
    }

    private void MakeSelectedCellUniqueBlock(bool openEditor)
    {
        if (Document is null || SelectedCell is null) {
            return;
        }

        var before = EditorCellContent.Capture(SelectedCell.Cell);
        var source = SelectedCellBlock ?? new WorldBlockDefinition();
        var newId = AllocateBlockId(Document.Blocks);
        var clone = CloneBlock(source);
        clone.Name = $"{(string.IsNullOrWhiteSpace(source.Name) ? "cell_block" : source.Name)}_{SelectedCell.Column}_{SelectedCell.Row}";
        Document.Blocks[newId] = clone;

        var after = CloneContent(before);
        after.BlockId = newId;
        after.Fields = FieldsFromBlock(clone);
        after.HorizonImage = clone.HorizonImage;
        ApplyCellContent(SelectedCell, after);

        if (!ContentEquals(before, after)) {
            RecordUndoAction(new CellContentUndoAction(this, SelectedCell, before, after));
        }

        RefreshBlockPalette();
        SelectedBlock = Blocks.FirstOrDefault(block => string.Equals(block.Id, newId, StringComparison.OrdinalIgnoreCase));
        if (openEditor) {
            SelectedInspectorTabIndex = BlockInspectorTabIndex;
        }

        BlockSummary = $"{Document.Blocks.Count} block(s) in palette";
        OnPropertyChanged(nameof(BlockSummary));
        NotifySelectedCellEditorChanged();
        RemoveDuplicateBlocksCommand.RaiseCanExecuteChanged();
        RemoveUnusedBlocksCommand.RaiseCanExecuteChanged();
        RefreshValidation($"Created unique block {newId} for cell {SelectedCell.Coordinates}.");
    }

    private void EditSelectedCellUniqueBlock()
    {
        if (Document is null || SelectedCell is null) {
            return;
        }

        if (string.IsNullOrWhiteSpace(SelectedCell.Cell.BlockId)
            || !Document.Blocks.ContainsKey(SelectedCell.Cell.BlockId)
            || !IsSelectedCellBlockUnique) {
            MakeSelectedCellUniqueBlock(openEditor: true);
            return;
        }

        OpenSelectedCellBlock();
    }

    private static string AllocateBlockId(IReadOnlyDictionary<string, WorldBlockDefinition> blocks)
    {
        for (var id = 0; id <= 0xff; ++id) {
            var key = id.ToString("x2");
            if (!blocks.ContainsKey(key)) {
                return key;
            }
        }

        throw new InvalidOperationException("World block palette is full.");
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
            clone.Door = new WorldDoorDefinition {
                Enabled = block.Door.Enabled,
                BlocksWhenClosed = block.Door.BlocksWhenClosed,
                RequiredKey = block.Door.RequiredKey,
                TriggerDistanceCells = block.Door.TriggerDistanceCells,
                OpenTimeSeconds = block.Door.OpenTimeSeconds,
                CloseDelaySeconds = block.Door.CloseDelaySeconds,
                OpenSound = block.Door.OpenSound,
                OpenSoundVolumePercent = block.Door.OpenSoundVolumePercent,
                Frames = [..block.Door.Frames],
                LockedOverlays = block.Door.LockedOverlays is null
                    ? null
                    : new Dictionary<string, string>(block.Door.LockedOverlays, StringComparer.OrdinalIgnoreCase)
            };
        }

        if (block.Animations is not null) {
            clone.Animations = block.Animations.Select(animation => new WorldBlockAnimationDefinition {
                Name = animation.Name,
                Target = animation.Target,
                WallIndex = animation.WallIndex,
                Face = animation.Face,
                FrameDurationMs = animation.FrameDurationMs,
                Loop = animation.Loop,
                Frames = [..animation.Frames]
            }).ToList();
        }

        foreach (var wall in block.Walls) {
            clone.Walls.Add(new WorldWallSpan {
                Kind = wall.Kind,
                Texture = wall.Texture,
                FaceTextures = wall.FaceTextures is null
                    ? null
                    : new Dictionary<string, string>(
                        wall.FaceTextures,
                        StringComparer.OrdinalIgnoreCase),
                FacesEnabled = wall.FacesEnabled is null
                    ? null
                    : new Dictionary<string, bool>(
                        wall.FacesEnabled,
                        StringComparer.OrdinalIgnoreCase),
                InteriorTexture = wall.InteriorTexture,
                Bottom = wall.Bottom,
                Top = wall.Top,
                Collision = wall.Collision
            });
        }

        return clone;
    }

    private void RefreshValidation(string successMessage, bool refreshPreview = true)
    {
        if (Document is null) {
            return;
        }

        ValidationMessages.Clear();
        foreach (var message in EditorValidation.Validate(Document, Document.SourcePath)) {
            ValidationMessages.Add(message);
        }

        if (ValidationMessages.Count == 0) {
            ValidationMessages.Add(successMessage);
        }

        if (refreshPreview) {
            RefreshPreview3D();
        }
    }

    private void RefreshPreview3D()
    {
        UpdatePreview3DCamera();
        var scene = WorldPreview3DBuilder.Build(
            Document,
            m_assetBasePath,
            m_spriteMapPreviews,
            Preview3DCamera.LookDirection,
            new WorldPreview3DLayers {
                ShowGrid = PreviewShowGrid,
                ShowFloors = PreviewShowFloors,
                ShowCeilings = PreviewShowCeilings,
                ShowWalls = PreviewShowWalls,
                ShowSprites = PreviewShowSprites,
                ShowPlayer = PreviewShowPlayer
            },
            SelectedCell?.Row,
            SelectedCell?.Column,
            SelectedSprite?.Sprite);
        Preview3DModel = scene.Model;
        Preview3DSummary = scene.Summary;
        Preview3DHitTargets = scene.HitTargets;
        OnPropertyChanged(nameof(Preview3DModel));
        OnPropertyChanged(nameof(Preview3DSummary));
        OnPropertyChanged(nameof(Preview3DHitTargets));
    }

    private void SetPreview3DLayerVisibility(ref bool field, bool value, [CallerMemberName] string? propertyName = null)
    {
        if (field == value) {
            return;
        }

        field = value;
        OnPropertyChanged(propertyName);
        RefreshPreview3D();
    }

    public void SelectPreview3DTarget(Model3D model)
    {
        if (Document is null || !Preview3DHitTargets.TryGetValue(model, out var target)) {
            return;
        }

        var targetCell = Cells.FirstOrDefault(
            cell => cell.Row == target.Row && cell.Column == target.Column);

        if (targetCell is not null) {
            SelectedCell = targetCell;
        }

        if (target.Kind == WorldPreview3DHitKind.Sprite && target.Sprite is not null) {
            SelectedLayer = "Sprites";
            SelectSprite(target.Sprite);
        }

        SelectedCellFace = target.Face;
        SelectedCellFaceSpanIndex = target.WallSpanIndex;
    }

    public void SelectBlockPreview3DFace(Model3D model)
    {
        if (!SelectedBlockPreview3DHitTargets.TryGetValue(model, out var target)) {
            return;
        }

        SelectedCellFace = target.Face;
        SelectedCellFaceSpanIndex = target.WallSpanIndex;
    }

    public void SelectCellPreview3DFace(Model3D model)
    {
        if (!SelectedCellPreview3DHitTargets.TryGetValue(model, out var target)) {
            return;
        }

        SelectedCellFace = target.Face;
        SelectedCellFaceSpanIndex = target.WallSpanIndex;
    }

    private int m_selectedCellFaceSpanIndex = -1;
    public int SelectedCellFaceSpanIndex
    {
        get => m_selectedCellFaceSpanIndex;
        set
        {
            if (m_selectedCellFaceSpanIndex == value) {
                return;
            }

            m_selectedCellFaceSpanIndex = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedCellFaceSpanLabel));
        }
    }

    public string SelectedCellFaceSpanLabel =>
        m_selectedCellFaceSpanIndex >= 0 ? $"Wall span #{m_selectedCellFaceSpanIndex}" : string.Empty;

    public void DragInspectorPreview(bool isBlockPreview, double deltaX, double deltaY)
    {
        var state = isBlockPreview ? m_selectedBlockPreviewCameraState : m_selectedCellPreviewCameraState;
        state.YawDegrees = NormalizeDegrees(state.YawDegrees + deltaX * 0.40);
        state.PitchDegrees = Math.Clamp(state.PitchDegrees - deltaY * 0.32, 8.0, 70.0);
        if (isBlockPreview) {
            RefreshSelectedBlockPreview3D();
        }
        else {
            RefreshSelectedCellPreview3D();
        }
    }

    private string? m_selectedCellFace;
    public string? SelectedCellFace
    {
        get => m_selectedCellFace;
        set
        {
            if (string.Equals(m_selectedCellFace, value, StringComparison.OrdinalIgnoreCase)) {
                return;
            }

            m_selectedCellFace = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedCellFaceLabel));
            OnPropertyChanged(nameof(HasSelectedCellFace));
            OnPropertyChanged(nameof(SelectedCellFaceTextureKey));
            ApplySelectedTextureToFaceCommand?.RaiseCanExecuteChanged();
        }
    }

    public bool HasSelectedCellFace => !string.IsNullOrEmpty(m_selectedCellFace);
    public string SelectedCellFaceLabel => m_selectedCellFace switch {
        "floor" => "Floor",
        "ceiling" => "Ceiling",
        "north" => "North wall",
        "east" => "East wall",
        "south" => "South wall",
        "west" => "West wall",
        _ => "(none)"
    };

    public string SelectedCellFaceTextureKey
    {
        get
        {
            if (SelectedCell is null || string.IsNullOrEmpty(m_selectedCellFace)) {
                return string.Empty;
            }

            return m_selectedCellFace switch {
                "floor" => SelectedCellFloorTextureKey,
                "ceiling" => SelectedCellCeilingTextureKey,
                "north" or "east" or "south" or "west" => SelectedCellLowerWallTextureKey,
                _ => string.Empty
            };
        }
        set
        {
            if (SelectedCell is null || string.IsNullOrEmpty(m_selectedCellFace)) {
                return;
            }

            switch (m_selectedCellFace) {
                case "floor": SelectedCellFloorTextureKey = value; break;
                case "ceiling": SelectedCellCeilingTextureKey = value; break;
                case "north" or "east" or "south" or "west":
                    SelectedCellLowerWallTextureKey = value;
                    break;
            }
            OnPropertyChanged();
        }
    }

    public RelayCommand? ApplySelectedTextureToFaceCommand { get; private set; }

    private void ApplySelectedTextureToFace()
    {
        if (SelectedCell is null || SelectedTexture is null || string.IsNullOrEmpty(m_selectedCellFace)) {
            return;
        }

        var key = SelectedTexture.Asset.Key.ToString("x2", CultureInfo.InvariantCulture);
        SelectedCellFaceTextureKey = key;
    }

    public void DragPreview3D(double deltaX, double deltaY)
    {
        if (Math.Abs(deltaX) < 0.01 && Math.Abs(deltaY) < 0.01) {
            return;
        }

        if (m_preview3DViewMode == "Perspective") {
            m_previewPerspectiveYawDegrees =
                NormalizeDegrees(m_previewPerspectiveYawDegrees + deltaX * 0.22);
            m_previewPerspectivePitchDegrees =
                Math.Clamp(m_previewPerspectivePitchDegrees - deltaY * 0.18, -35.0, 35.0);
            RefreshPreview3D();
            return;
        }

        m_previewOrbitYawDegrees = NormalizeDegrees(m_previewOrbitYawDegrees + deltaX * 0.30);
        if (m_preview3DViewMode == "Angled") {
            m_previewOrbitPitchDegrees =
                Math.Clamp(m_previewOrbitPitchDegrees - deltaY * 0.22, 12.0, 75.0);
        }

        RefreshPreview3D();
    }

    private void SetPreview3DViewMode(string mode)
    {
        if (m_preview3DViewMode == mode) {
            return;
        }

        if (mode == "Perspective" && m_preview3DViewMode != "Perspective") {
            InitializePerspectiveCameraFromPlayer();
        }

        m_preview3DViewMode = mode;
        RefreshPreview3D();
        OnPropertyChanged(nameof(Preview3DViewMode));
    }

    private void RotatePreview3D(double degrees)
    {
        if (m_preview3DViewMode != "Angled") {
            TurnPreviewPerspective(degrees);
            return;
        }

        m_previewOrbitYawDegrees = NormalizeDegrees(m_previewOrbitYawDegrees + degrees);
        RefreshPreview3D();
    }

    private void ZoomPreview3D(double factor)
    {
        if (m_preview3DViewMode == "Perspective") {
            Preview3DCamera.FieldOfView = Math.Clamp(Preview3DCamera.FieldOfView * factor, 28.0, 82.0);
            OnPropertyChanged(nameof(Preview3DCamera));
            return;
        }

        m_previewOrbitZoom = Math.Clamp(m_previewOrbitZoom * factor, 0.25, 4.0);
        RefreshPreview3D();
    }

    private void FitPreview3DToWorld()
    {
        m_previewOrbitYawDegrees = 0.0;
        m_previewOrbitPitchDegrees = 32.0;
        m_previewOrbitZoom = 1.0;
        m_previewOrbitPanX = 0.0;
        m_previewOrbitPanZ = 0.0;
        m_preview3DViewMode = "Angled";
        RefreshPreview3D();
        OnPropertyChanged(nameof(Preview3DViewMode));
    }

    private void MovePreview3D(double forward, double strafe)
    {
        if (m_preview3DViewMode == "Angled") {
            PanPreviewAngledView(forward, strafe);
            return;
        }

        MovePreviewPerspective(forward, strafe);
    }

    private void PanPreviewAngledView(double forward, double strafe)
    {
        var yaw = m_previewOrbitYawDegrees * Math.PI / 180.0;
        var forwardX = -Math.Sin(yaw);
        var forwardZ = Math.Cos(yaw);
        var rightX = Math.Cos(yaw);
        var rightZ = Math.Sin(yaw);

        m_previewOrbitPanX += forwardX * forward + rightX * strafe;
        m_previewOrbitPanZ += forwardZ * forward + rightZ * strafe;
        ClampPreviewOrbitPanToWorld();
        RefreshPreview3D();
    }

    private void MovePreviewPerspective(double forward, double strafe)
    {
        if (m_preview3DViewMode != "Perspective") {
            SetPreview3DViewMode("Perspective");
        }

        var yaw = m_previewPerspectiveYawDegrees * Math.PI / 180.0;
        var forwardX = Math.Cos(yaw);
        var forwardZ = Math.Sin(yaw);
        var rightX = -Math.Sin(yaw);
        var rightZ = Math.Cos(yaw);

        m_previewPerspectiveX += forwardX * forward + rightX * strafe;
        m_previewPerspectiveZ += forwardZ * forward + rightZ * strafe;
        ClampPerspectiveCameraToWorld();
        RefreshPreview3D();
    }

    private void TurnPreviewPerspective(double degrees)
    {
        if (m_preview3DViewMode != "Perspective") {
            SetPreview3DViewMode("Perspective");
        }

        m_previewPerspectiveYawDegrees = NormalizeDegrees(m_previewPerspectiveYawDegrees + degrees);
        RefreshPreview3D();
    }

    private void UpdatePreview3DCamera()
    {
        if (Document is null || Document.RowCount == 0 || Document.ColumnCount == 0) {
            Preview3DCamera.Position = new Point3D(4.0, 5.0, -8.0);
            Preview3DCamera.LookDirection = new Vector3D(0.0, -4.0, 8.0);
            Preview3DCamera.UpDirection = new Vector3D(0.0, 1.0, 0.0);
            Preview3DCamera.FieldOfView = 50;
            return;
        }

        var centerX = Document.ColumnCount * 0.5;
        var centerZ = Document.RowCount * 0.5;
        var dimension = Math.Max(Document.ColumnCount, Document.RowCount);
        if (m_preview3DViewMode == "Top") {
            var height = Math.Max(8.0, dimension * 1.25) * m_previewOrbitZoom;
            var yaw = m_previewOrbitYawDegrees * Math.PI / 180.0;
            Preview3DCamera.Position = new Point3D(centerX, height, centerZ + 0.001);
            Preview3DCamera.LookDirection = new Vector3D(0.0, -height, -0.001);
            Preview3DCamera.UpDirection = new Vector3D(Math.Sin(yaw), 0.0, -Math.Cos(yaw));
            Preview3DCamera.FieldOfView = 50;
            return;
        }

        if (m_preview3DViewMode == "Perspective") {
            ClampPerspectiveCameraToWorld();
            var yaw = m_previewPerspectiveYawDegrees * Math.PI / 180.0;
            var pitch = Math.Clamp(m_previewPerspectivePitchDegrees, -35.0, 35.0) * Math.PI / 180.0;
            var lookScale = Math.Max(4.0, dimension * 0.3);
            var horizontalScale = Math.Cos(pitch) * lookScale;
            Preview3DCamera.Position = new Point3D(
                m_previewPerspectiveX,
                m_previewPerspectiveY,
                m_previewPerspectiveZ);
            Preview3DCamera.LookDirection = new Vector3D(
                Math.Cos(yaw) * horizontalScale,
                Math.Sin(pitch) * lookScale,
                Math.Sin(yaw) * horizontalScale);
            Preview3DCamera.UpDirection = new Vector3D(0.0, 1.0, 0.0);
            if (Preview3DCamera.FieldOfView < 28.0 || Preview3DCamera.FieldOfView > 82.0) {
                Preview3DCamera.FieldOfView = 62;
            }
            return;
        }

        var yawRadians = m_previewOrbitYawDegrees * Math.PI / 180.0;
        var pitchRadians = Math.Clamp(m_previewOrbitPitchDegrees, 12.0, 75.0) * Math.PI / 180.0;
        var distance = Math.Max(6.0, dimension * 1.15) * m_previewOrbitZoom;
        var elevation = Math.Sin(pitchRadians) * distance;
        var horizontalDistance = Math.Cos(pitchRadians) * distance;
        ClampPreviewOrbitPanToWorld();
        var targetX = centerX + m_previewOrbitPanX;
        var targetZ = centerZ + m_previewOrbitPanZ;
        var targetHeight = 0.55;

        Preview3DCamera.Position = new Point3D(
            targetX + Math.Sin(yawRadians) * horizontalDistance,
            elevation,
            targetZ - Math.Cos(yawRadians) * horizontalDistance);
        Preview3DCamera.LookDirection = new Vector3D(
            targetX - Preview3DCamera.Position.X,
            targetHeight - elevation,
            targetZ - Preview3DCamera.Position.Z);
        Preview3DCamera.UpDirection = new Vector3D(0.0, 1.0, 0.0);
        Preview3DCamera.FieldOfView = 50;
    }

    private void ResetPreview3DNavigation()
    {
        m_previewOrbitYawDegrees = 0.0;
        m_previewOrbitPitchDegrees = 32.0;
        m_previewOrbitZoom = 1.0;
        m_previewOrbitPanX = 0.0;
        m_previewOrbitPanZ = 0.0;
        InitializePerspectiveCameraFromPlayer();
    }

    private void InitializePerspectiveCameraFromPlayer()
    {
        if (Document is null) {
            return;
        }

        m_previewPerspectiveX = Document.PlayerStart.XCell;
        m_previewPerspectiveY = 0.62;
        m_previewPerspectiveZ = Document.PlayerStart.YCell;
        m_previewPerspectiveYawDegrees = Document.PlayerStart.FacingDegrees;
        m_previewPerspectivePitchDegrees = 0.0;
        ClampPerspectiveCameraToWorld();
    }

    private void ClampPerspectiveCameraToWorld()
    {
        if (Document is null || Document.ColumnCount == 0 || Document.RowCount == 0) {
            return;
        }

        m_previewPerspectiveX = Math.Clamp(m_previewPerspectiveX, 0.15, Math.Max(0.15, Document.ColumnCount - 0.15));
        m_previewPerspectiveY = Math.Clamp(m_previewPerspectiveY, 0.2, 2.5);
        m_previewPerspectiveZ = Math.Clamp(m_previewPerspectiveZ, 0.15, Math.Max(0.15, Document.RowCount - 0.15));
    }

    private void ClampPreviewOrbitPanToWorld()
    {
        if (Document is null || Document.ColumnCount == 0 || Document.RowCount == 0) {
            return;
        }

        var centerX = Document.ColumnCount * 0.5;
        var centerZ = Document.RowCount * 0.5;
        var targetX = Math.Clamp(centerX + m_previewOrbitPanX, 0.0, Document.ColumnCount);
        var targetZ = Math.Clamp(centerZ + m_previewOrbitPanZ, 0.0, Document.RowCount);
        m_previewOrbitPanX = targetX - centerX;
        m_previewOrbitPanZ = targetZ - centerZ;
    }

    private static double NormalizeDegrees(double degrees)
    {
        degrees %= 360.0;
        return degrees < 0 ? degrees + 360.0 : degrees;
    }

    private void OpenWorldJson()
    {
        var dialog = new OpenFileDialog {
            Title = "Open nuRCADE world JSON",
            Filter = "nuRCADE world JSON|*.world.json;*.json|All files|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog() != true) {
            return;
        }

        try {
            LoadWorldJsonFrom(dialog.FileName);
        }
        catch (Exception error) {
            MessageBox.Show(
                error.Message,
                "Open world JSON failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void OpenSpriteMetadata()
    {
        var dialog = new OpenFileDialog {
            Title = "Open nuRCADE sprite metadata",
            Filter = "Sprite metadata JSON|*.json|All files|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog() != true) {
            return;
        }

        LoadSpriteMetadataFrom(dialog.FileName, registerInDocument: true);
    }

    public void LoadSpriteMetadataFrom(string metadataPath, bool registerInDocument)
    {
        var result = SpriteMetadataLoader.Load(metadataPath);
        SelectedSpriteDirection = null;
        SpriteDirections.Clear();
        SelectedSpriteAnimation = null;
        SpriteAnimations.Clear();
        SpriteAnimationFrames.Clear();
        SpriteLodRules.Clear();
        m_spriteMetadata = result.Document;

        if (!result.Success || result.Document is null) {
            SpriteMetadataSummary = "Sprite metadata has errors";
            SpriteTransparentColorSummary = "Transparent color: n/a";
            SpriteLodSelectionSummary = "No sprite LOD selected";
            m_loadedSpriteSetName = string.Empty;
            m_spriteMetadataPath = string.Empty;
            foreach (var error in result.Errors) {
                ValidationMessages.Add(error);
            }
        }
        else {
            m_loadedSpriteSetName = result.Document.SpriteSet;
            m_spriteMetadataPath = Path.GetFullPath(metadataPath);
            SpriteMetadataSummary =
                $"{result.Document.SpriteSet} ({result.Document.Format}), {result.Document.Animations.Count} animation(s)";
            SpriteTransparentColorSummary =
                $"Transparent color: RGB({string.Join(", ", result.Document.TransparentColor)})";
            var metadataDirectory =
                Path.GetDirectoryName(Path.GetFullPath(metadataPath)) ?? Environment.CurrentDirectory;
            m_spriteMetadataDirectory = metadataDirectory;

            foreach (var animation in result.Document.Animations) {
                SpriteAnimations.Add(new SpriteAnimationViewModel(animation));
            }

            SelectedSpriteAnimation = SpriteAnimations.FirstOrDefault(
                    animation => string.Equals(animation.Name, "idle", StringComparison.OrdinalIgnoreCase))
                ?? SpriteAnimations.FirstOrDefault();

            foreach (var rule in result.Document.Lod.OrderBy(rule => rule.MaxDistance)) {
                SpriteLodRules.Add(new SpriteLodRuleViewModel(rule));
            }

            RegisterSpriteMapPreview(result.Document, metadataPath);
            UpdateSpriteLodSelection();
            ValidationMessages.Add($"Loaded sprite metadata from {metadataPath}.");

            if (registerInDocument && Document is not null) {
                var relative = ToWorldRelative(metadataPath);
                if (!Document.SpriteSetFiles.Contains(relative, StringComparer.OrdinalIgnoreCase)) {
                    Document.SpriteSetFiles.Add(relative);
                }
                if (!SpriteSetFiles.Contains(relative, StringComparer.OrdinalIgnoreCase)) {
                    SpriteSetFiles.Add(relative);
                }
            }
        }

        OnPropertyChanged(nameof(SpriteMetadataSummary));
        OnPropertyChanged(nameof(SpriteTransparentColorSummary));
        OnPropertyChanged(nameof(SelectedSpriteAnimationSummary));
        AddSpriteToSelectedCellCommand.RaiseCanExecuteChanged();
        AddItemToSelectedCellCommand.RaiseCanExecuteChanged();
        SaveSpriteMetadataAsCommand.RaiseCanExecuteChanged();
        SaveAllOpenJsonFilesCommand.RaiseCanExecuteChanged();
        RaiseSpriteAnimationCanExecuteChanged();
    }

    private void LoadSelectedSpriteSetMetadata()
    {
        if (Document is null || string.IsNullOrWhiteSpace(SelectedSpriteSetFile)) {
            return;
        }

        var worldDirectory = string.IsNullOrWhiteSpace(Document.SourcePath)
            ? Environment.CurrentDirectory
            : Path.GetDirectoryName(Path.GetFullPath(Document.SourcePath)) ?? Environment.CurrentDirectory;
        var metadataPath = Path.IsPathRooted(SelectedSpriteSetFile)
            ? SelectedSpriteSetFile
            : Path.GetFullPath(Path.Combine(worldDirectory, SelectedSpriteSetFile));
        if (!File.Exists(metadataPath)) {
            ValidationMessages.Add($"Sprite set not found: {metadataPath}");
            return;
        }

        LoadSpriteMetadataFrom(metadataPath, registerInDocument: false);
    }

    private void SaveSpriteMetadataAs()
    {
        if (m_spriteMetadata is null) {
            return;
        }

        var dialog = new SaveFileDialog {
            Title = "Save nuRCADE sprite metadata",
            Filter = "Sprite metadata JSON|*.sprite.json;*.json|All files|*.*",
            FileName = string.IsNullOrWhiteSpace(m_spriteMetadata.SpriteSet)
                ? "sprite.sprite.json"
                : $"{m_spriteMetadata.SpriteSet}.sprite.json",
            AddExtension = true,
            OverwritePrompt = true
        };

        if (dialog.ShowDialog() != true) {
            return;
        }

        try {
            SaveSpriteMetadataTo(dialog.FileName);
        }
        catch (Exception error) {
            MessageBox.Show(
                error.Message,
                "Save sprite metadata failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void SaveSpriteMetadataTo(string path)
    {
        if (m_spriteMetadata is null) {
            return;
        }

        SpriteMetadataWriter.Save(m_spriteMetadata, path);
        m_spriteMetadataPath = Path.GetFullPath(path);
        ValidationMessages.Add($"Saved sprite metadata to {path}.");
        SaveAllOpenJsonFilesCommand.RaiseCanExecuteChanged();
    }

    private void AddSpriteAnimation()
    {
        if (m_spriteMetadata is null) {
            return;
        }

        var source = SelectedSpriteAnimation?.Animation
            ?? m_spriteMetadata.Animations.FirstOrDefault();
        var animation = new SpriteAnimationMetadata {
            Name = UniqueSpriteAnimationName("animation"),
            FrameDurationMs = source is not null && source.FrameDurationMs > 0.0
                ? source.FrameDurationMs
                : 160.0,
            Loop = true
        };

        var directions = FirstAnimationDirections(source) ?? m_spriteMetadata.Directions;
        if (directions.Count > 0) {
            var frame = new SpriteAnimationFrameMetadata {
                Directions = CloneSpriteDirections(directions)
            };
            animation.Frames.Add(frame);
            animation.Directions.AddRange(CloneSpriteDirections(frame.Directions));
        }

        m_spriteMetadata.Animations.Add(animation);

        var viewModel = new SpriteAnimationViewModel(animation);
        SpriteAnimations.Add(viewModel);
        SelectedSpriteAnimation = viewModel;
        ValidationMessages.Add($"Added sprite animation {animation.Name}.");
        NotifySpriteAnimationCollectionChanged();
    }

    private void DuplicateSpriteAnimation()
    {
        if (m_spriteMetadata is null || SelectedSpriteAnimation is null) {
            return;
        }

        var source = SelectedSpriteAnimation.Animation;
        var animation = CloneSpriteAnimation(source);
        animation.Name = UniqueSpriteAnimationName($"{source.Name}_copy");
        m_spriteMetadata.Animations.Add(animation);

        var viewModel = new SpriteAnimationViewModel(animation);
        SpriteAnimations.Add(viewModel);
        SelectedSpriteAnimation = viewModel;
        ValidationMessages.Add($"Duplicated sprite animation {source.Name}.");
        NotifySpriteAnimationCollectionChanged();
    }

    private void RemoveSpriteAnimation()
    {
        if (!CanRemoveSelectedSpriteAnimation() || m_spriteMetadata is null || SelectedSpriteAnimation is null) {
            return;
        }

        var removed = SelectedSpriteAnimation;
        m_spriteMetadata.Animations.Remove(removed.Animation);
        SpriteAnimations.Remove(removed);
        SelectedSpriteAnimation = SpriteAnimations.FirstOrDefault(
                animation => string.Equals(animation.Name, "idle", StringComparison.OrdinalIgnoreCase))
            ?? SpriteAnimations.FirstOrDefault();
        ValidationMessages.Add($"Removed sprite animation {removed.Name}.");
        NotifySpriteAnimationCollectionChanged();
    }

    private bool CanRemoveSelectedSpriteAnimation()
    {
        return m_spriteMetadata is not null
            && SelectedSpriteAnimation is not null
            && !string.Equals(SelectedSpriteAnimation.Name, "idle", StringComparison.OrdinalIgnoreCase);
    }

    private void AddSpriteAnimationFrame()
    {
        if (SelectedSpriteAnimation is null) {
            return;
        }

        var sourceDirections = SelectedSpriteAnimationFrame?.Frame.Directions
            ?? FirstAnimationDirections(SelectedSpriteAnimation.Animation);
        if (sourceDirections is null || sourceDirections.Count == 0) {
            return;
        }

        var frame = new SpriteAnimationFrameMetadata {
            Directions = CloneSpriteDirections(sourceDirections)
        };
        SelectedSpriteAnimation.Animation.Frames.Add(frame);
        SyncAnimationDirectionsWithFirstFrame(SelectedSpriteAnimation.Animation);
        RefreshSpriteAnimationFrames();
        SelectedSpriteAnimationFrame = SpriteAnimationFrames.LastOrDefault();
        SelectedSpriteAnimation.Refresh();
        ValidationMessages.Add($"Added frame to {SelectedSpriteAnimation.Name}.");
        NotifySpriteAnimationCollectionChanged();
    }

    private void DuplicateSpriteAnimationFrame()
    {
        if (SelectedSpriteAnimation is null || SelectedSpriteAnimationFrame is null) {
            return;
        }

        var frame = new SpriteAnimationFrameMetadata {
            Directions = CloneSpriteDirections(SelectedSpriteAnimationFrame.Frame.Directions)
        };
        var insertAt = SelectedSpriteAnimationFrame.Index + 1;
        SelectedSpriteAnimation.Animation.Frames.Insert(insertAt, frame);
        SyncAnimationDirectionsWithFirstFrame(SelectedSpriteAnimation.Animation);
        RefreshSpriteAnimationFrames();
        SelectedSpriteAnimationFrame = SpriteAnimationFrames.ElementAtOrDefault(insertAt);
        SelectedSpriteAnimation.Refresh();
        ValidationMessages.Add($"Duplicated frame in {SelectedSpriteAnimation.Name}.");
        NotifySpriteAnimationCollectionChanged();
    }

    private void RemoveSpriteAnimationFrame()
    {
        if (!CanRemoveSelectedSpriteAnimationFrame()
            || SelectedSpriteAnimation is null
            || SelectedSpriteAnimationFrame is null) {
            return;
        }

        var removeAt = SelectedSpriteAnimationFrame.Index;
        SelectedSpriteAnimation.Animation.Frames.RemoveAt(removeAt);
        SyncAnimationDirectionsWithFirstFrame(SelectedSpriteAnimation.Animation);
        RefreshSpriteAnimationFrames();
        SelectedSpriteAnimationFrame = SpriteAnimationFrames.ElementAtOrDefault(
                Math.Min(removeAt, SpriteAnimationFrames.Count - 1))
            ?? SpriteAnimationFrames.FirstOrDefault();
        SelectedSpriteAnimation.Refresh();
        ValidationMessages.Add($"Removed frame from {SelectedSpriteAnimation.Name}.");
        NotifySpriteAnimationCollectionChanged();
    }

    private bool CanRemoveSelectedSpriteAnimationFrame()
    {
        return SelectedSpriteAnimation is not null
            && SelectedSpriteAnimationFrame is not null
            && SelectedSpriteAnimation.Animation.Frames.Count > 1;
    }

    private string UniqueSpriteAnimationName(string baseName)
    {
        if (m_spriteMetadata is null) {
            return baseName;
        }

        var normalizedBase = string.IsNullOrWhiteSpace(baseName)
            ? "animation"
            : baseName.Trim();
        var existingNames = m_spriteMetadata.Animations
            .Select(animation => animation.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!existingNames.Contains(normalizedBase)) {
            return normalizedBase;
        }

        for (var index = 2; ; ++index) {
            var candidate = $"{normalizedBase}_{index}";
            if (!existingNames.Contains(candidate)) {
                return candidate;
            }
        }
    }

    private static SpriteAnimationMetadata CloneSpriteAnimation(SpriteAnimationMetadata source)
    {
        var clone = new SpriteAnimationMetadata {
            Name = source.Name,
            FrameDurationMs = source.FrameDurationMs,
            Loop = source.Loop,
            Directions = CloneSpriteDirections(source.Directions)
        };

        foreach (var frame in source.Frames) {
            clone.Frames.Add(new SpriteAnimationFrameMetadata {
                Directions = CloneSpriteDirections(frame.Directions)
            });
        }

        return clone;
    }

    private static List<SpriteDirectionMetadata>? FirstAnimationDirections(SpriteAnimationMetadata? animation)
    {
        if (animation is null) {
            return null;
        }

        if (animation.Frames.FirstOrDefault()?.Directions is { Count: > 0 } frameDirections) {
            return frameDirections;
        }

        return animation.Directions.Count > 0 ? animation.Directions : null;
    }

    private static void SyncAnimationDirectionsWithFirstFrame(SpriteAnimationMetadata animation)
    {
        animation.Directions.Clear();
        if (animation.Frames.FirstOrDefault()?.Directions is { Count: > 0 } directions) {
            animation.Directions.AddRange(CloneSpriteDirections(directions));
        }
    }

    private static List<SpriteDirectionMetadata> CloneSpriteDirections(
        IEnumerable<SpriteDirectionMetadata> directions)
    {
        return directions
            .Select(direction => new SpriteDirectionMetadata {
                Name = direction.Name,
                Angle = direction.Angle,
                Files = direction.Files.ToDictionary(
                    item => item.Key,
                    item => item.Value)
            })
            .ToList();
    }

    private void NotifySpriteAnimationCollectionChanged()
    {
        if (m_spriteMetadata is not null) {
            SpriteMetadataSummary =
                $"{m_spriteMetadata.SpriteSet} ({m_spriteMetadata.Format}), {m_spriteMetadata.Animations.Count} animation(s)";
        }

        OnPropertyChanged(nameof(SpriteMetadataSummary));
        OnPropertyChanged(nameof(SelectedSpriteAnimationSummary));
        RaiseSpriteAnimationCanExecuteChanged();
    }

    private void RaiseSpriteAnimationCanExecuteChanged()
    {
        AddSpriteAnimationCommand.RaiseCanExecuteChanged();
        DuplicateSpriteAnimationCommand.RaiseCanExecuteChanged();
        RemoveSpriteAnimationCommand.RaiseCanExecuteChanged();
        AddSpriteAnimationFrameCommand.RaiseCanExecuteChanged();
        DuplicateSpriteAnimationFrameCommand.RaiseCanExecuteChanged();
        RemoveSpriteAnimationFrameCommand.RaiseCanExecuteChanged();
        SaveSpriteMetadataAsCommand.RaiseCanExecuteChanged();
    }

    private string ToWorldRelative(string metadataPath)
    {
        var absolute = Path.GetFullPath(metadataPath);
        if (Document is null || string.IsNullOrWhiteSpace(Document.SourcePath)) {
            return absolute;
        }

        var worldDirectory =
            Path.GetDirectoryName(Path.GetFullPath(Document.SourcePath)) ?? Environment.CurrentDirectory;
        return Path.GetRelativePath(worldDirectory, absolute).Replace('\\', '/');
    }

    private void RefreshSpriteMapPreviews(string assetBasePath)
    {
        if (Document is null) {
            return;
        }

        m_spriteMapPreviews.Clear();
        m_spriteSetFilesByName.Clear();
        foreach (var spriteSet in Document.SpriteSetFiles) {
            var metadataPath = ResolveSpriteMetadataPath(spriteSet, assetBasePath);
            if (metadataPath is null) {
                continue;
            }

            var preview = SpriteMapPreviewLoader.Load(metadataPath);
            if (preview is not null) {
                m_spriteMapPreviews[preview.SpriteSet] = preview.Image;
                m_spriteSetFilesByName[preview.SpriteSet] = spriteSet;
            }
        }

        NotifySpriteMapPreviewsChanged();
    }

    private void RegisterSpriteMapPreview(SpriteMetadataDocument document, string metadataPath)
    {
        var preview = SpriteMapPreviewLoader.Load(document, metadataPath);
        m_spriteMapPreviews[preview.SpriteSet] = preview.Image;
        m_spriteSetFilesByName[preview.SpriteSet] = Document is null
            ? metadataPath
            : ToWorldRelative(metadataPath);
        NotifySpriteMapPreviewsChanged();
    }

    private void NotifySpriteMapPreviewsChanged()
    {
        foreach (var cell in Cells) {
            cell.NotifySpriteCollectionChanged();
        }

        OnPropertyChanged(nameof(SelectedSpritePreview));
        RefreshPreview3D();
    }

    private void NotifySpriteClipboardChanged()
    {
        OnPropertyChanged(nameof(SpriteClipboardSummary));
        PasteSpriteCommand.RaiseCanExecuteChanged();
        CancelSpriteClipboardCommand.RaiseCanExecuteChanged();
    }

    private static string? ResolveSpriteMetadataPath(string spriteSet, string assetBasePath)
    {
        if (string.IsNullOrWhiteSpace(spriteSet)) {
            return null;
        }

        if (Path.IsPathRooted(spriteSet)) {
            return File.Exists(spriteSet) ? spriteSet : null;
        }

        var baseDirectory = File.Exists(assetBasePath)
            ? Path.GetDirectoryName(Path.GetFullPath(assetBasePath)) ?? Environment.CurrentDirectory
            : Path.GetFullPath(assetBasePath);
        var candidate = Path.GetFullPath(Path.Combine(baseDirectory, spriteSet));
        return File.Exists(candidate) ? candidate : null;
    }

    private void UpdateSpriteLodSelection()
    {
        if (m_spriteMetadata is null) {
            SpriteLodSelectionSummary = "No sprite LOD selected";
        }
        else {
            var resolution = SpriteLodSelector.SelectResolution(m_spriteMetadata, SpritePreviewDistance);
            SpriteLodSelectionSummary =
                $"{resolution}px at {SpritePreviewDistance:0.##} cells";
            foreach (var direction in SpriteDirections) {
                direction.UpdateSelectedResolution(resolution);
            }
        }

        OnPropertyChanged(nameof(SpriteLodSelectionSummary));
        OnPropertyChanged(nameof(SelectedSpriteDirectionPreview));
        OnPropertyChanged(nameof(SelectedSpriteDirectionSummary));
        RefreshAnimationPlayback();
    }

    private void RefreshAnimationPlayback()
    {
        if (m_spriteMetadata is null || SelectedSpriteAnimation is null) {
            AnimationPlayback.Configure(
                animation: null,
                directionName: null,
                metadataDirectory: m_spriteMetadataDirectory,
                transparentColor: m_spriteMetadata?.TransparentColor ?? [0, 0, 0],
                resolution: 0);
            RaiseAnimationPlaybackCanExecuteChanged();
            return;
        }

        var resolution = SpriteLodSelector.SelectResolution(m_spriteMetadata, SpritePreviewDistance);
        AnimationPlayback.Configure(
            animation: SelectedSpriteAnimation.Animation,
            directionName: SelectedSpriteDirection?.Name,
            metadataDirectory: m_spriteMetadataDirectory,
            transparentColor: m_spriteMetadata.TransparentColor,
            resolution: resolution);
        RaiseAnimationPlaybackCanExecuteChanged();
    }

    private void OnAnimationPlaybackPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        RaiseAnimationPlaybackCanExecuteChanged();
    }

    private void OnSelectedSpriteAnimationPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(SpriteAnimationViewModel.FrameDurationMs)
            or nameof(SpriteAnimationViewModel.Loop)
            or nameof(SpriteAnimationViewModel.Summary)) {
            RefreshAnimationPlayback();
        }
    }

    private void RaiseAnimationPlaybackCanExecuteChanged()
    {
        PlayAnimationCommand.RaiseCanExecuteChanged();
        PauseAnimationCommand.RaiseCanExecuteChanged();
        StopAnimationCommand.RaiseCanExecuteChanged();
        StepAnimationForwardCommand.RaiseCanExecuteChanged();
        StepAnimationBackwardCommand.RaiseCanExecuteChanged();
    }

    private void RefreshSpriteAnimationFrames()
    {
        SelectedSpriteAnimationFrame = null;
        SpriteAnimationFrames.Clear();

        if (SelectedSpriteAnimation is null) {
            RefreshSpriteDirections([]);
            return;
        }

        for (var index = 0; index < SelectedSpriteAnimation.Animation.Frames.Count; ++index) {
            SpriteAnimationFrames.Add(new SpriteAnimationFrameViewModel(
                index,
                SelectedSpriteAnimation.Animation.Frames[index]));
        }

        SelectedSpriteAnimationFrame = SpriteAnimationFrames.FirstOrDefault();
        if (SelectedSpriteAnimationFrame is null) {
            RefreshSpriteDirections(SelectedSpriteAnimation.Animation.Directions);
        }
    }

    private void RefreshSpriteDirectionsFromSelectedAnimationFrame()
    {
        if (SelectedSpriteAnimationFrame is not null) {
            RefreshSpriteDirections(SelectedSpriteAnimationFrame.Frame.Directions);
        }
        else if (SelectedSpriteAnimation is not null) {
            RefreshSpriteDirections(SelectedSpriteAnimation.Animation.Directions);
        }
        else {
            RefreshSpriteDirections([]);
        }
    }

    private void RefreshSpriteDirections(IEnumerable<SpriteDirectionMetadata> directions)
    {
        SelectedSpriteDirection = null;
        SpriteDirections.Clear();
        if (m_spriteMetadata is null) {
            return;
        }

        foreach (var direction in directions) {
            SpriteDirections.Add(new SpriteDirectionViewModel(
                direction,
                m_spriteMetadataDirectory,
                m_spriteMetadata.TransparentColor));
        }

        SelectedSpriteDirection = SpriteDirections.FirstOrDefault();
        UpdateSpriteLodSelection();
    }

    private void OpenProject()
    {
        var dialog = new OpenFileDialog {
            Title = "Open nuRCADE project",
            Filter = "nuRCADE project|*.nurcadeproj.json;*.json|All files|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog() != true) {
            return;
        }

        try {
            OpenProjectFrom(dialog.FileName);
        }
        catch (Exception error) {
            MessageBox.Show(
                error.Message,
                "Open project failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void SaveProjectAs()
    {
        if (Document is null) {
            return;
        }

        var dialog = new SaveFileDialog {
            Title = "Save nuRCADE project",
            Filter = "nuRCADE project|*.nurcadeproj.json|All files|*.*",
            FileName = "project.nurcadeproj.json",
            AddExtension = true,
            OverwritePrompt = true
        };

        if (dialog.ShowDialog() != true) {
            return;
        }

        try {
            SaveProjectTo(dialog.FileName);
        }
        catch (Exception error) {
            MessageBox.Show(
                error.Message,
                "Save project failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void ExportScene()
    {
        if (Document is null) {
            return;
        }

        var enginePath = FindEngineExecutable();
        if (enginePath is null) {
            MessageBox.Show(
                "Could not locate the nuRCADE Player executable (nuRCADEPlayer.exe). Build the C++ player first, then try again.",
                "Export playable demo failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        var dialog = new SaveFileDialog {
            Title = "Export playable nuRCADE demo package",
            Filter = "nuRCADE project|*.nurcadeproj.json|All files|*.*",
            FileName = "playable_demo.nurcadeproj.json",
            AddExtension = true,
            OverwritePrompt = true
        };

        if (dialog.ShowDialog() != true) {
            return;
        }

        try {
            var result = EditorSceneExporter.Export(
                Document,
                dialog.FileName,
                new EditorSceneExportOptions {
                    EngineExecutablePath = enginePath
                });
            ValidationMessages.Add($"Exported scene project to {result.ProjectPath}.");
            ValidationMessages.Add($"Exported world map to {result.WorldPath}.");
            if (!string.IsNullOrWhiteSpace(result.EnginePath)) {
                ValidationMessages.Add($"Copied runtime to {result.EnginePath}.");
            }

            if (!string.IsNullOrWhiteSpace(result.RunScriptPath)) {
                ValidationMessages.Add($"Created run script at {result.RunScriptPath}.");
            }

            if (!string.IsNullOrWhiteSpace(result.EnginePath)) {
                var shortcuts = WindowsShortcutInstaller.InstallDemoShortcuts(
                    result.ProjectPath,
                    result.EnginePath,
                    Path.GetFileNameWithoutExtension(result.ProjectPath));
                ValidationMessages.Add($"Created Start menu shortcut at {shortcuts.StartMenuShortcutPath}.");
                ValidationMessages.Add($"Created desktop shortcut at {shortcuts.DesktopShortcutPath}.");
            }
        }
        catch (Exception error) {
            MessageBox.Show(
                error.Message,
                "Export playable demo failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void TestIn3D()
    {
        if (Document is null) {
            return;
        }

        var enginePath = FindEngineExecutable();
        if (enginePath is null) {
            MessageBox.Show(
                "Could not locate the nuRCADE Player executable (nuRCADEPlayer.exe). Build the C++ player first, then try again.",
                "Test in 3D failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        try {
            var previewDirectory = Path.Combine(
                Path.GetTempPath(),
                "NuRcade.Editor",
                "Preview");
            Directory.CreateDirectory(previewDirectory);

            var projectPath = Path.Combine(previewDirectory, "preview.nurcadeproj.json");
            var result = EditorSceneExporter.Export(Document, projectPath);

            var startInfo = new ProcessStartInfo {
                FileName = enginePath,
                WorkingDirectory = FindRepoRoot() ?? Path.GetDirectoryName(enginePath) ?? Environment.CurrentDirectory,
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add(result.ProjectPath);

            Process.Start(startInfo);
            ValidationMessages.Add($"Launched 3D preview using {result.ProjectPath}.");
        }
        catch (Exception error) {
            MessageBox.Show(
                error.Message,
                "Test in 3D failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private static void ShowAbout()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var name = assembly.GetName();
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        var version = string.IsNullOrWhiteSpace(informationalVersion)
            ? name.Version?.ToString(3) ?? "unknown"
            : informationalVersion;

        MessageBox.Show(
            "nuRCADE Editor\n"
            + $"Version {version}\n\n"
            + "Authoring tool for nuRCADE worlds, blocks, sprites, weapons, and playable previews.\n\n"
            + "Copyright (C) 2005 - 2018 Antonino Calderone\n"
            + "Licensed under the MIT License.\n\n"
            + $"Install path:\n{AppContext.BaseDirectory}",
            "About nuRCADE Editor",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private bool SaveWorldJsonAs()
    {
        if (Document is null) {
            return false;
        }

        var dialog = new SaveFileDialog {
            Title = "Save nuRCADE world JSON",
            Filter = "nuRCADE world JSON|*.world.json|All files|*.*",
            FileName = "world.world.json",
            AddExtension = true,
            OverwritePrompt = true
        };

        if (dialog.ShowDialog() != true) {
            return false;
        }

        try {
            SaveWorldJsonTo(dialog.FileName);
            return true;
        }
        catch (Exception error) {
            MessageBox.Show(
                error.Message,
                "Save world JSON failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return false;
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
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

    private static string? FindEngineExecutable()
    {
        return EngineExecutableLocator.Find(
            AppContext.BaseDirectory,
            Environment.ProcessPath,
            FindRepoFile);
    }

    private static string? FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null) {
            if (File.Exists(Path.Combine(directory.FullName, "CMakeLists.txt"))
                && Directory.Exists(Path.Combine(directory.FullName, "res"))) {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

}

internal interface IEditorUndoAction
{
    void Undo();
    void Redo();
}

internal sealed class CellSelectionClipboard
{
    public CellSelectionClipboard(
        int rows,
        int columns,
        IReadOnlyList<CellClipboardEntry> entries)
    {
        Rows = rows;
        Columns = columns;
        Entries = entries;
    }

    public int Rows { get; }
    public int Columns { get; }
    public IReadOnlyList<CellClipboardEntry> Entries { get; }
}

internal sealed class CellClipboardEntry
{
    public CellClipboardEntry(
        int rowOffset,
        int columnOffset,
        EditorCellContent content)
    {
        RowOffset = rowOffset;
        ColumnOffset = columnOffset;
        Content = content;
    }

    public int RowOffset { get; }
    public int ColumnOffset { get; }
    public EditorCellContent Content { get; }
}

internal sealed class CellContentChange
{
    public CellContentChange(
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

internal enum WallSlot
{
    Lower,
    Upper,
    Transparent
}

internal sealed class InspectorPreviewCameraState
{
    public double YawDegrees { get; set; } = 32.0;
    public double PitchDegrees { get; set; } = 24.0;
    public double Zoom { get; set; } = 1.0;
    public double ShiftX { get; set; }
    public double ShiftZ { get; set; }

    public void Reset()
    {
        YawDegrees = 32.0;
        PitchDegrees = 24.0;
        Zoom = 1.0;
        ShiftX = 0.0;
        ShiftZ = 0.0;
    }
}

internal sealed class CellContentUndoAction : IEditorUndoAction
{
    private readonly MainWindowViewModel m_owner;
    private readonly EditorCellViewModel m_cell;
    private readonly EditorCellContent m_before;
    private readonly EditorCellContent m_after;

    public CellContentUndoAction(
        MainWindowViewModel owner,
        EditorCellViewModel cell,
        EditorCellContent before,
        EditorCellContent after)
    {
        m_owner = owner;
        m_cell = cell;
        m_before = before;
        m_after = after;
    }

    public void Undo()
    {
        m_owner.ApplyCellContentForHistory(m_cell, m_before);
    }

    public void Redo()
    {
        m_owner.ApplyCellContentForHistory(m_cell, m_after);
    }
}

internal sealed class MultiCellContentUndoAction : IEditorUndoAction
{
    private readonly MainWindowViewModel m_owner;
    private readonly IReadOnlyList<CellContentChange> m_changes;

    public MultiCellContentUndoAction(
        MainWindowViewModel owner,
        IReadOnlyList<CellContentChange> changes)
    {
        m_owner = owner;
        m_changes = changes;
    }

    public void Undo()
    {
        foreach (var change in m_changes) {
            m_owner.ApplyCellContentForHistory(change.Cell, change.Before);
        }
    }

    public void Redo()
    {
        foreach (var change in m_changes) {
            m_owner.ApplyCellContentForHistory(change.Cell, change.After);
        }
    }
}

internal sealed class CellBlockEditUndoAction : IEditorUndoAction
{
    private readonly MainWindowViewModel m_owner;
    private readonly EditorCellViewModel m_cell;
    private readonly EditorCellContent m_beforeContent;
    private readonly EditorCellContent m_afterContent;
    private readonly string m_blockId;
    private readonly WorldBlockDefinition m_beforeBlock;
    private readonly WorldBlockDefinition m_afterBlock;

    public CellBlockEditUndoAction(
        MainWindowViewModel owner,
        EditorCellViewModel cell,
        EditorCellContent beforeContent,
        EditorCellContent afterContent,
        string blockId,
        WorldBlockDefinition beforeBlock,
        WorldBlockDefinition afterBlock)
    {
        m_owner = owner;
        m_cell = cell;
        m_beforeContent = beforeContent;
        m_afterContent = afterContent;
        m_blockId = blockId;
        m_beforeBlock = beforeBlock;
        m_afterBlock = afterBlock;
    }

    public void Undo()
    {
        m_owner.ApplyCellBlockEditForHistory(m_cell, m_beforeContent, m_blockId, m_beforeBlock);
    }

    public void Redo()
    {
        m_owner.ApplyCellBlockEditForHistory(m_cell, m_afterContent, m_blockId, m_afterBlock);
    }
}

internal sealed class BlockTemplateEditUndoAction : IEditorUndoAction
{
    private readonly MainWindowViewModel m_owner;
    private readonly string m_blockId;
    private readonly WorldBlockDefinition m_beforeBlock;
    private readonly WorldBlockDefinition m_afterBlock;

    public BlockTemplateEditUndoAction(
        MainWindowViewModel owner,
        string blockId,
        WorldBlockDefinition beforeBlock,
        WorldBlockDefinition afterBlock)
    {
        m_owner = owner;
        m_blockId = blockId;
        m_beforeBlock = beforeBlock;
        m_afterBlock = afterBlock;
    }

    public void Undo()
    {
        m_owner.ApplyBlockTemplateForHistory(m_blockId, m_beforeBlock);
    }

    public void Redo()
    {
        m_owner.ApplyBlockTemplateForHistory(m_blockId, m_afterBlock);
    }
}

internal sealed class PlayerStartUndoAction : IEditorUndoAction
{
    private readonly MainWindowViewModel m_owner;
    private readonly WorldPlayerStart m_before;
    private readonly WorldPlayerStart m_after;

    public PlayerStartUndoAction(
        MainWindowViewModel owner,
        WorldPlayerStart before,
        WorldPlayerStart after)
    {
        m_owner = owner;
        m_before = before;
        m_after = after;
    }

    public void Undo()
    {
        m_owner.ApplyPlayerStartForHistory(m_before);
    }

    public void Redo()
    {
        m_owner.ApplyPlayerStartForHistory(m_after);
    }
}

internal sealed class GameGoalUndoAction : IEditorUndoAction
{
    private readonly MainWindowViewModel m_owner;
    private readonly WorldGameGoal? m_before;
    private readonly WorldGameGoal? m_after;

    public GameGoalUndoAction(
        MainWindowViewModel owner,
        WorldGameGoal? before,
        WorldGameGoal? after)
    {
        m_owner = owner;
        m_before = before;
        m_after = after;
    }

    public void Undo()
    {
        m_owner.ApplyGameGoalForHistory(m_before);
    }

    public void Redo()
    {
        m_owner.ApplyGameGoalForHistory(m_after);
    }
}

public sealed class LayerConnectionOptionViewModel : INotifyPropertyChanged
{
    private readonly Action<string, bool> m_onChanged;
    private bool m_isConnected;

    public LayerConnectionOptionViewModel(
        string layerId,
        string label,
        bool isConnected,
        Action<string, bool> onChanged)
    {
        LayerId = layerId;
        Label = label;
        m_isConnected = isConnected;
        m_onChanged = onChanged;
    }

    public string LayerId { get; }
    public string Label { get; }

    public bool IsConnected
    {
        get => m_isConnected;
        set
        {
            if (m_isConnected == value) {
                return;
            }

            m_isConnected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsConnected)));
            m_onChanged(LayerId, value);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
