using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using WinRaycastEditor.Core;

namespace WinRaycastEditor;

public sealed class BlockPaletteEntryViewModel : INotifyPropertyChanged
{
    private readonly WorldBlockDefinition m_block;
    private readonly IReadOnlyDictionary<string, ImageSource?> m_texturePreviews;
    private BlockWallSpanViewModel? m_selectedWallSpan;
    private BlockAnimationViewModel? m_selectedAnimation;
    private bool m_blockChangedScheduled;

    public BlockPaletteEntryViewModel(
        string id,
        WorldBlockDefinition block,
        IReadOnlyDictionary<string, ImageSource?>? texturePreviews = null)
    {
        Id = id;
        m_block = block;
        m_texturePreviews = texturePreviews
            ?? new Dictionary<string, ImageSource?>(StringComparer.OrdinalIgnoreCase);
        foreach (var wall in block.Walls) {
            AddWallViewModel(wall);
        }

        foreach (var animation in block.Animations ?? Enumerable.Empty<WorldBlockAnimationDefinition>()) {
            AddAnimationViewModel(animation);
        }

        AddWallSpanCommand = new RelayCommand(_ => AddWallSpan());
        RemoveWallSpanCommand = new RelayCommand(
            _ => RemoveSelectedWallSpan(),
            _ => SelectedWallSpan is not null);
        AddAnimationCommand = new RelayCommand(_ => AddAnimation());
        RemoveAnimationCommand = new RelayCommand(
            _ => RemoveSelectedAnimation(),
            _ => SelectedAnimation is not null);
        AddDoorFrameCommand = new RelayCommand(_ => AddDoorFrame());
        RemoveDoorFrameCommand = new RelayCommand(
            _ => RemoveSelectedDoorFrame(),
            _ => SelectedDoorFrame is not null);
        MoveDoorFrameUpCommand = new RelayCommand(
            _ => MoveSelectedDoorFrame(-1),
            _ => SelectedDoorFrame is not null && DoorFrames.IndexOf(SelectedDoorFrame) > 0);
        MoveDoorFrameDownCommand = new RelayCommand(
            _ => MoveSelectedDoorFrame(+1),
            _ => SelectedDoorFrame is not null
                && DoorFrames.IndexOf(SelectedDoorFrame) >= 0
                && DoorFrames.IndexOf(SelectedDoorFrame) < DoorFrames.Count - 1);
        RebuildDoorFrames();
        RebuildStructureTree();
    }

    private void RebuildDoorFrames()
    {
        DoorFrames.Clear();
        if (m_block.Door is null) {
            SelectedDoorFrame = null;
            return;
        }

        for (var i = 0; i < m_block.Door.Frames.Count; ++i) {
            DoorFrames.Add(new BlockFrameViewModel(
                i, m_block.Door.Frames[i], TexturePreviewFor(m_block.Door.Frames[i])));
        }

        SelectedDoorFrame = DoorFrames.FirstOrDefault();
    }

    private void AddDoorFrame()
    {
        var door = EnsureDoor();
        var nextFrame = DoorFrames.LastOrDefault()?.TextureKey ?? string.Empty;
        door.Frames.Add(nextFrame);
        NotifyBlockChanged();
        SelectedDoorFrame = DoorFrames.LastOrDefault();
    }

    private void RemoveSelectedDoorFrame()
    {
        if (SelectedDoorFrame is null || m_block.Door is null) {
            return;
        }

        var index = SelectedDoorFrame.Index;
        if (index < 0 || index >= m_block.Door.Frames.Count) {
            return;
        }

        m_block.Door.Frames.RemoveAt(index);
        NotifyBlockChanged();
        SelectedDoorFrame = DoorFrames.ElementAtOrDefault(Math.Min(index, DoorFrames.Count - 1));
    }

    private void MoveSelectedDoorFrame(int delta)
    {
        if (SelectedDoorFrame is null || m_block.Door is null) {
            return;
        }

        var index = SelectedDoorFrame.Index;
        var newIndex = index + delta;
        if (newIndex < 0 || newIndex >= m_block.Door.Frames.Count) {
            return;
        }

        var frame = m_block.Door.Frames[index];
        m_block.Door.Frames.RemoveAt(index);
        m_block.Door.Frames.Insert(newIndex, frame);
        NotifyBlockChanged();
        SelectedDoorFrame = DoorFrames.ElementAtOrDefault(newIndex);
    }

    public void UpdateDoorFrameTexture(int index, string textureKey)
    {
        if (m_block.Door is null || index < 0 || index >= m_block.Door.Frames.Count) {
            return;
        }

        if (string.Equals(m_block.Door.Frames[index], textureKey, StringComparison.OrdinalIgnoreCase)) {
            return;
        }

        m_block.Door.Frames[index] = textureKey ?? string.Empty;
        NotifyBlockChanged();
    }

    private ImageSource? TexturePreviewFor(string? textureKey)
    {
        if (string.IsNullOrWhiteSpace(textureKey)) {
            return null;
        }

        return m_texturePreviews.TryGetValue(textureKey, out var preview) ? preview : null;
    }

    private BlockTreeNodeViewModel TextureNode(string label, string textureKey)
    {
        return new BlockTreeNodeViewModel(label, TexturePreviewFor(textureKey));
    }

    public string Id { get; }

    public string Name
    {
        get => string.IsNullOrEmpty(m_block.Name) ? Id : m_block.Name;
        set
        {
            if (m_block.Name == value) {
                return;
            }

            m_block.Name = value;
            NotifyBlockChanged();
        }
    }

    public string Summary => DescribeSummary(m_block);

    public ImageSource? PrimaryPreview
    {
        get
        {
            foreach (var wall in m_block.Walls) {
                var key = wall.Texture;
                if (!string.IsNullOrWhiteSpace(key)) {
                    var preview = TexturePreviewFor(key);
                    if (preview is not null) {
                        return preview;
                    }
                }
            }

            return TexturePreviewFor(m_block.Floor?.Texture)
                ?? TexturePreviewFor(m_block.Ceiling?.Texture);
        }
    }

    public bool HasPrimaryPreview => PrimaryPreview is not null;

    public string FloorTexture
    {
        get => m_block.Floor?.Texture ?? string.Empty;
        set
        {
            if (string.IsNullOrWhiteSpace(value)) {
                if (m_block.Floor is null) {
                    return;
                }

                m_block.Floor = null;
                NotifyBlockChanged();
                return;
            }

            EnsureFloor().Texture = value;
            NotifyBlockChanged();
        }
    }

    public int FloorHeight
    {
        get => m_block.Floor?.Height ?? 0;
        set
        {
            EnsureFloor().Height = value;
            NotifyBlockChanged();
        }
    }

    public string CeilingTexture
    {
        get => m_block.Ceiling?.Texture ?? string.Empty;
        set
        {
            if (string.IsNullOrWhiteSpace(value)) {
                if (m_block.Ceiling is null) {
                    return;
                }

                m_block.Ceiling = null;
                NotifyBlockChanged();
                return;
            }

            EnsureCeiling().Texture = value;
            NotifyBlockChanged();
        }
    }

    public int CeilingHeight
    {
        get => m_block.Ceiling?.Height ?? 0;
        set
        {
            EnsureCeiling().Height = value;
            NotifyBlockChanged();
        }
    }

    public string HorizonImage
    {
        get => m_block.HorizonImage ?? string.Empty;
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? null : value;
            if (m_block.HorizonImage == normalized) {
                return;
            }

            m_block.HorizonImage = normalized;
            NotifyBlockChanged();
        }
    }

    public string FloorText => m_block.Floor is null
        ? "-"
        : $"{m_block.Floor.Texture} @ {m_block.Floor.Height}";

    public string CeilingText => m_block.Ceiling is null
        ? "-"
        : $"{m_block.Ceiling.Texture} @ {m_block.Ceiling.Height}";

    public string HorizonText => string.IsNullOrEmpty(m_block.HorizonImage)
        ? "-"
        : m_block.HorizonImage!;

    public bool DoorEnabled
    {
        get => m_block.Door?.Enabled ?? false;
        set
        {
            if (!value && m_block.Door is null) {
                return;
            }

            EnsureDoor().Enabled = value;
            NotifyBlockChanged();
        }
    }

    public bool DoorBlocksWhenClosed
    {
        get => m_block.Door?.BlocksWhenClosed ?? true;
        set
        {
            EnsureDoor().BlocksWhenClosed = value;
            NotifyBlockChanged();
        }
    }

    public IReadOnlyList<string> DoorKeyOptions { get; } =
        [string.Empty, "green", "blue", "red"];

    public string DoorSummary
    {
        get
        {
            if (m_block.Door is null) {
                return "Door metadata: none";
            }

            var state = m_block.Door.Enabled ? "enabled" : "disabled";
            var key = string.IsNullOrWhiteSpace(m_block.Door.RequiredKey)
                ? "no key"
                : $"key {m_block.Door.RequiredKey}";
            var overlayCount = m_block.Door.LockedOverlays?.Count ?? 0;
            return $"Door metadata: {state}, {m_block.Door.Frames.Count} frame(s), {key}, {overlayCount} lock overlay(s)";
        }
    }

    public string DoorRequiredKey
    {
        get => m_block.Door?.RequiredKey ?? string.Empty;
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (string.Equals(m_block.Door?.RequiredKey, normalized, StringComparison.OrdinalIgnoreCase)) {
                return;
            }

            EnsureDoor().RequiredKey = normalized;
            NotifyBlockChanged();
        }
    }

    public string DoorGreenOverlayTexture
    {
        get => DoorOverlayTexture("green");
        set => SetDoorOverlayTexture("green", value);
    }

    public string DoorBlueOverlayTexture
    {
        get => DoorOverlayTexture("blue");
        set => SetDoorOverlayTexture("blue", value);
    }

    public string DoorRedOverlayTexture
    {
        get => DoorOverlayTexture("red");
        set => SetDoorOverlayTexture("red", value);
    }

    public double DoorTriggerDistanceCells
    {
        get => m_block.Door?.TriggerDistanceCells ?? 1.25;
        set
        {
            EnsureDoor().TriggerDistanceCells = value;
            NotifyBlockChanged();
        }
    }

    public double DoorOpenTimeSeconds
    {
        get => m_block.Door?.OpenTimeSeconds ?? 0.45;
        set
        {
            EnsureDoor().OpenTimeSeconds = value;
            NotifyBlockChanged();
        }
    }

    public double DoorCloseDelaySeconds
    {
        get => m_block.Door?.CloseDelaySeconds ?? 1.0;
        set
        {
            EnsureDoor().CloseDelaySeconds = value;
            NotifyBlockChanged();
        }
    }

    public string DoorFramesText
    {
        get => m_block.Door is null
            ? string.Empty
            : string.Join(", ", m_block.Door.Frames);
        set
        {
            var frames = value
                .Split([',', ';', '\r', '\n', '\t', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
            var door = EnsureDoor();
            if (door.Frames.SequenceEqual(frames, StringComparer.OrdinalIgnoreCase)) {
                return;
            }

            door.Frames = frames;
            NotifyBlockChanged();
        }
    }

    public ObservableCollection<BlockWallSpanViewModel> Walls { get; } = [];
    public ObservableCollection<BlockAnimationViewModel> Animations { get; } = [];
    public ObservableCollection<BlockTreeNodeViewModel> StructureTree { get; } = [];
    public ObservableCollection<BlockFrameViewModel> DoorFrames { get; } = [];

    private BlockFrameViewModel? m_selectedDoorFrame;
    public BlockFrameViewModel? SelectedDoorFrame
    {
        get => m_selectedDoorFrame;
        set
        {
            if (m_selectedDoorFrame == value) {
                return;
            }

            m_selectedDoorFrame = value;
            OnPropertyChanged();
            MoveDoorFrameUpCommand.RaiseCanExecuteChanged();
            MoveDoorFrameDownCommand.RaiseCanExecuteChanged();
            RemoveDoorFrameCommand.RaiseCanExecuteChanged();
        }
    }

    public IReadOnlyList<string> AnimationTargets { get; } =
        ["block", "floor", "ceiling", "wall", "wallOverlay", "door"];

    public IReadOnlyList<string> AnimationFaces { get; } =
        ["all", "north", "east", "south", "west"];

    public BlockWallSpanViewModel? SelectedWallSpan
    {
        get => m_selectedWallSpan;
        set
        {
            if (m_selectedWallSpan == value) {
                return;
            }

            m_selectedWallSpan = value;
            OnPropertyChanged();
            RemoveWallSpanCommand.RaiseCanExecuteChanged();
        }
    }

    public BlockAnimationViewModel? SelectedAnimation
    {
        get => m_selectedAnimation;
        set
        {
            if (m_selectedAnimation == value) {
                return;
            }

            m_selectedAnimation = value;
            OnPropertyChanged();
            RemoveAnimationCommand.RaiseCanExecuteChanged();
        }
    }

    public string AnimationSummary => Animations.Count == 0
        ? "No block animations"
        : $"{Animations.Count} block animation(s)";

    public double PreviewHeight => IsDoubleHeightBlock() ? 340.0 : 170.0;

    public RelayCommand AddWallSpanCommand { get; }
    public RelayCommand RemoveWallSpanCommand { get; }
    public RelayCommand AddAnimationCommand { get; }
    public RelayCommand RemoveAnimationCommand { get; }
    public RelayCommand AddDoorFrameCommand { get; }
    public RelayCommand RemoveDoorFrameCommand { get; }
    public RelayCommand MoveDoorFrameUpCommand { get; }
    public RelayCommand MoveDoorFrameDownCommand { get; }

    public WorldBlockDefinition Block => m_block;

    public event PropertyChangedEventHandler? PropertyChanged;

    private static string DescribeSummary(WorldBlockDefinition block)
    {
        if (block.Walls.Count == 0
            && block.Floor is null
            && block.Ceiling is null
            && string.IsNullOrEmpty(block.HorizonImage)) {
            return "empty";
        }

        var maxTop = 0;
        var minBottom = int.MaxValue;
        foreach (var wall in block.Walls) {
            if (wall.Top > maxTop) {
                maxTop = wall.Top;
            }

            if (wall.Bottom < minBottom) {
                minBottom = wall.Bottom;
            }
        }

        if (block.Walls.Count == 0) {
            return "open";
        }

        if (minBottom == int.MaxValue) {
            minBottom = 0;
        }

        return $"{block.Walls.Count} span(s), {minBottom}..{maxTop}";
    }

    private WorldSurface EnsureFloor()
    {
        m_block.Floor ??= new WorldSurface();
        return m_block.Floor;
    }

    private WorldSurface EnsureCeiling()
    {
        m_block.Ceiling ??= new WorldSurface();
        return m_block.Ceiling;
    }

    private WorldDoorDefinition EnsureDoor()
    {
        m_block.Door ??= new WorldDoorDefinition();
        return m_block.Door;
    }

    private string DoorOverlayTexture(string key)
    {
        if (m_block.Door?.LockedOverlays is null) {
            return string.Empty;
        }

        return m_block.Door.LockedOverlays.TryGetValue(key, out var texture)
            ? texture
            : string.Empty;
    }

    private void SetDoorOverlayTexture(string key, string? textureKey)
    {
        var normalized = string.IsNullOrWhiteSpace(textureKey) ? string.Empty : textureKey.Trim();
        var current = DoorOverlayTexture(key);
        if (string.Equals(current, normalized, StringComparison.OrdinalIgnoreCase)) {
            return;
        }

        var door = EnsureDoor();
        if (string.IsNullOrEmpty(normalized)) {
            if (door.LockedOverlays is not null) {
                door.LockedOverlays.Remove(key);
                if (door.LockedOverlays.Count == 0) {
                    door.LockedOverlays = null;
                }
            }
        }
        else {
            door.LockedOverlays ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            door.LockedOverlays[key] = normalized;
        }

        NotifyBlockChanged();
    }

    private List<WorldBlockAnimationDefinition> EnsureAnimations()
    {
        m_block.Animations ??= [];
        return m_block.Animations;
    }

    private void AddWallViewModel(WorldWallSpan wall)
    {
        var wallViewModel = new BlockWallSpanViewModel(wall);
        wallViewModel.PropertyChanged += (_, _) => ScheduleBlockChanged();
        Walls.Add(wallViewModel);
    }

    private void AddAnimationViewModel(WorldBlockAnimationDefinition animation)
    {
        var animationViewModel = new BlockAnimationViewModel(animation);
        animationViewModel.PropertyChanged += (_, _) => ScheduleBlockChanged();
        Animations.Add(animationViewModel);
    }

    private void AddWallSpan()
    {
        var wall = new WorldWallSpan
        {
            Kind = "solid",
            Texture = string.Empty,
            Bottom = 0,
            Top = 512,
            Collision = true
        };

        m_block.Walls.Add(wall);
        AddWallViewModel(wall);
        SelectedWallSpan = Walls.Last();
        NotifyBlockChanged();
    }

    private void AddAnimation()
    {
        var animation = new WorldBlockAnimationDefinition {
            Name = $"animation_{Animations.Count + 1}",
            Target = Walls.Count == 0 ? "block" : "wall",
            WallIndex = Walls.Count == 0 ? null : 0,
            Face = "all",
            FrameDurationMs = 120.0,
            Loop = true
        };

        var seedFrame = SeedAnimationFrame();
        if (!string.IsNullOrWhiteSpace(seedFrame)) {
            animation.Frames.Add(seedFrame);
        }

        EnsureAnimations().Add(animation);
        AddAnimationViewModel(animation);
        SelectedAnimation = Animations.Last();
        NotifyBlockChanged();
    }

    private void RemoveSelectedAnimation()
    {
        if (SelectedAnimation is null || m_block.Animations is null) {
            return;
        }

        var selected = SelectedAnimation;
        var index = Animations.IndexOf(selected);
        if (index < 0) {
            return;
        }

        m_block.Animations.Remove(selected.Animation);
        if (m_block.Animations.Count == 0) {
            m_block.Animations = null;
        }

        Animations.RemoveAt(index);
        SelectedAnimation = Animations.Count == 0
            ? null
            : Animations[Math.Min(index, Animations.Count - 1)];
        NotifyBlockChanged();
    }

    private string SeedAnimationFrame()
    {
        if (m_block.Door?.Frames.FirstOrDefault(frame => !string.IsNullOrWhiteSpace(frame)) is { } doorFrame) {
            return doorFrame;
        }

        if (m_block.Walls.FirstOrDefault(wall => !string.IsNullOrWhiteSpace(wall.Texture))?.Texture is { } wallTexture) {
            return wallTexture;
        }

        if (!string.IsNullOrWhiteSpace(m_block.Floor?.Texture)) {
            return m_block.Floor.Texture;
        }

        return m_block.Ceiling?.Texture ?? string.Empty;
    }

    private void RebuildStructureTree()
    {
        StructureTree.Clear();
        var root = new BlockTreeNodeViewModel($"Block {Id}: {Name}");
        root.Children.Add(TextureNode($"Floor: {FloorText}", FloorTexture));
        root.Children.Add(TextureNode($"Ceiling: {CeilingText}", CeilingTexture));
        root.Children.Add(new BlockTreeNodeViewModel($"Horizon: {HorizonText}"));

        var wallsNode = new BlockTreeNodeViewModel($"Walls ({Walls.Count})");
        for (var index = 0; index < Walls.Count; ++index) {
            var wall = Walls[index];
            var wallNode = new BlockTreeNodeViewModel($"Wall {index}: {wall.Display}");
            wallNode.Children.Add(TextureNode($"default: {TextureText(wall.Texture)}", wall.Texture));
            wallNode.Children.Add(TextureNode($"north: {TextureText(wall.NorthTexture)}", wall.NorthTexture));
            wallNode.Children.Add(TextureNode($"east: {TextureText(wall.EastTexture)}", wall.EastTexture));
            wallNode.Children.Add(TextureNode($"south: {TextureText(wall.SouthTexture)}", wall.SouthTexture));
            wallNode.Children.Add(TextureNode($"west: {TextureText(wall.WestTexture)}", wall.WestTexture));
            wallsNode.Children.Add(wallNode);
        }

        root.Children.Add(wallsNode);

        var doorNode = new BlockTreeNodeViewModel(DoorSummary);
        if (!string.IsNullOrWhiteSpace(DoorRequiredKey)) {
            doorNode.Children.Add(new BlockTreeNodeViewModel($"required key: {DoorRequiredKey}"));
        }

        foreach (var frame in m_block.Door?.Frames ?? Enumerable.Empty<string>()) {
            doorNode.Children.Add(TextureNode($"frame: {frame}", frame));
        }

        foreach (var overlay in m_block.Door?.LockedOverlays ?? Enumerable.Empty<KeyValuePair<string, string>>()) {
            doorNode.Children.Add(TextureNode($"locked overlay {overlay.Key}: {overlay.Value}", overlay.Value));
        }

        root.Children.Add(doorNode);

        var animationsNode = new BlockTreeNodeViewModel($"Block animations ({Animations.Count})");
        foreach (var animation in Animations) {
            var animationNode = new BlockTreeNodeViewModel(animation.Display);
            foreach (var frame in animation.Animation.Frames) {
                animationNode.Children.Add(TextureNode($"frame: {frame}", frame));
            }

            animationsNode.Children.Add(animationNode);
        }

        root.Children.Add(animationsNode);
        StructureTree.Add(root);
    }

    private static string TextureText(string texture)
    {
        return string.IsNullOrWhiteSpace(texture) ? "-" : texture;
    }

    private void RemoveSelectedWallSpan()
    {
        if (SelectedWallSpan is null) {
            return;
        }

        var selected = SelectedWallSpan;
        var index = Walls.IndexOf(selected);
        if (index < 0) {
            return;
        }

        m_block.Walls.Remove(selected.Span);
        Walls.RemoveAt(index);
        SelectedWallSpan = Walls.Count == 0
            ? null
            : Walls[Math.Min(index, Walls.Count - 1)];
        NotifyBlockChanged();
    }

    private void NotifyBlockChanged()
    {
        RebuildStructureTree();
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(FloorTexture));
        OnPropertyChanged(nameof(FloorHeight));
        OnPropertyChanged(nameof(FloorText));
        OnPropertyChanged(nameof(CeilingTexture));
        OnPropertyChanged(nameof(CeilingHeight));
        OnPropertyChanged(nameof(CeilingText));
        OnPropertyChanged(nameof(HorizonImage));
        OnPropertyChanged(nameof(HorizonText));
        OnPropertyChanged(nameof(DoorEnabled));
        OnPropertyChanged(nameof(DoorBlocksWhenClosed));
        OnPropertyChanged(nameof(DoorSummary));
        OnPropertyChanged(nameof(DoorRequiredKey));
        OnPropertyChanged(nameof(DoorGreenOverlayTexture));
        OnPropertyChanged(nameof(DoorBlueOverlayTexture));
        OnPropertyChanged(nameof(DoorRedOverlayTexture));
        OnPropertyChanged(nameof(DoorTriggerDistanceCells));
        OnPropertyChanged(nameof(DoorOpenTimeSeconds));
        OnPropertyChanged(nameof(DoorCloseDelaySeconds));
        OnPropertyChanged(nameof(DoorFramesText));
        OnPropertyChanged(nameof(AnimationSummary));
        OnPropertyChanged(nameof(PreviewHeight));
        OnPropertyChanged(nameof(PrimaryPreview));
        OnPropertyChanged(nameof(HasPrimaryPreview));
        RebuildDoorFrames();
    }

    private void ScheduleBlockChanged()
    {
        if (m_blockChangedScheduled) {
            return;
        }

        m_blockChangedScheduled = true;
        void Notify()
        {
            m_blockChangedScheduled = false;
            NotifyBlockChanged();
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null) {
            Notify();
            return;
        }

        dispatcher.BeginInvoke(
            new Action(Notify),
            System.Windows.Threading.DispatcherPriority.Background);
    }

    private bool IsDoubleHeightBlock()
    {
        return m_block.Walls.Any(wall => wall.Top > 512 || wall.Bottom >= 512)
            || (m_block.Ceiling?.Height ?? 0) > 512;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class BlockWallSpanViewModel : INotifyPropertyChanged
{
    private readonly WorldWallSpan m_span;

    public BlockWallSpanViewModel(WorldWallSpan span)
    {
        m_span = span;
    }

    public string Kind
    {
        get => m_span.Kind;
        set
        {
            if (m_span.Kind == value) {
                return;
            }

            m_span.Kind = value;
            NotifyChangedWithDisplay();
        }
    }

    public string Texture
    {
        get => m_span.Texture;
        set
        {
            if (m_span.Texture == value) {
                return;
            }

            m_span.Texture = value;
            NotifyChangedWithDisplay();
        }
    }

    public string NorthTexture
    {
        get => FaceTexture("north");
        set => SetFaceTexture("north", value);
    }

    public string EastTexture
    {
        get => FaceTexture("east");
        set => SetFaceTexture("east", value);
    }

    public string SouthTexture
    {
        get => FaceTexture("south");
        set => SetFaceTexture("south", value);
    }

    public string WestTexture
    {
        get => FaceTexture("west");
        set => SetFaceTexture("west", value);
    }

    public string InteriorTexture
    {
        get => m_span.InteriorTexture ?? string.Empty;
        set
        {
            var normalized = (value ?? string.Empty).Trim();
            var current = m_span.InteriorTexture ?? string.Empty;
            if (string.Equals(normalized, current, StringComparison.OrdinalIgnoreCase)) {
                return;
            }

            m_span.InteriorTexture = string.IsNullOrEmpty(normalized) ? null : normalized;
            NotifyChanged();
        }
    }

    public bool NorthEnabled
    {
        get => FaceEnabled("north");
        set => SetFaceEnabled("north", value);
    }

    public bool EastEnabled
    {
        get => FaceEnabled("east");
        set => SetFaceEnabled("east", value);
    }

    public bool SouthEnabled
    {
        get => FaceEnabled("south");
        set => SetFaceEnabled("south", value);
    }

    public bool WestEnabled
    {
        get => FaceEnabled("west");
        set => SetFaceEnabled("west", value);
    }

    private bool FaceEnabled(string face)
    {
        return m_span.FacesEnabled is null
            || !m_span.FacesEnabled.TryGetValue(face, out var enabled)
            || enabled;
    }

    private void SetFaceEnabled(string face, bool value)
    {
        var current = FaceEnabled(face);
        if (current == value) {
            return;
        }

        if (value) {
            if (m_span.FacesEnabled is not null) {
                m_span.FacesEnabled.Remove(face);
                if (m_span.FacesEnabled.Count == 0) {
                    m_span.FacesEnabled = null;
                }
            }
        }
        else {
            m_span.FacesEnabled ??= new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            m_span.FacesEnabled[face] = false;
        }

        NotifyChanged(face switch
        {
            "north" => nameof(NorthEnabled),
            "east" => nameof(EastEnabled),
            "south" => nameof(SouthEnabled),
            "west" => nameof(WestEnabled),
            _ => nameof(Kind)
        });
    }

    public int Bottom
    {
        get => m_span.Bottom;
        set
        {
            if (m_span.Bottom == value) {
                return;
            }

            m_span.Bottom = value;
            NotifyChangedWithDisplay();
        }
    }

    public int Top
    {
        get => m_span.Top;
        set
        {
            if (m_span.Top == value) {
                return;
            }

            m_span.Top = value;
            NotifyChangedWithDisplay();
        }
    }

    public bool Collision
    {
        get => m_span.Collision;
        set
        {
            if (m_span.Collision == value) {
                return;
            }

            m_span.Collision = value;
            NotifyChanged();
            NotifyChanged(nameof(Passable));
        }
    }

    public bool Passable
    {
        get => m_span.Passable;
        set
        {
            if (m_span.Passable == value) {
                return;
            }

            m_span.Passable = value;
            NotifyChangedWithDisplay();
            NotifyChanged(nameof(Collision));
        }
    }

    public string Display =>
        $"[{m_span.Kind}] {m_span.Texture}  {m_span.Bottom}..{m_span.Top}"
        + (m_span.Passable ? " passable" : " blocking");

    public WorldWallSpan Span => m_span;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void NotifyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private void NotifyChangedWithDisplay([CallerMemberName] string? propertyName = null)
    {
        NotifyChanged(propertyName);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Display)));
    }

    private string FaceTexture(string face)
    {
        return m_span.FaceTextures is not null
            && m_span.FaceTextures.TryGetValue(face, out var texture)
            ? texture
            : string.Empty;
    }

    private void SetFaceTexture(string face, string value)
    {
        var normalized = value.Trim();
        var current = FaceTexture(face);
        if (string.Equals(current, normalized, StringComparison.OrdinalIgnoreCase)) {
            return;
        }

        if (string.IsNullOrWhiteSpace(normalized)) {
            m_span.FaceTextures?.Remove(face);
            if (m_span.FaceTextures is { Count: 0 }) {
                m_span.FaceTextures = null;
            }
        }
        else {
            m_span.FaceTextures ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            m_span.FaceTextures[face] = normalized;
        }

        NotifyChanged(face switch
        {
            "north" => nameof(NorthTexture),
            "east" => nameof(EastTexture),
            "south" => nameof(SouthTexture),
            "west" => nameof(WestTexture),
            _ => nameof(Texture)
        });
    }
}

public sealed class BlockAnimationViewModel : INotifyPropertyChanged
{
    private readonly WorldBlockAnimationDefinition m_animation;

    public BlockAnimationViewModel(WorldBlockAnimationDefinition animation)
    {
        m_animation = animation;
    }

    public string Name
    {
        get => m_animation.Name;
        set
        {
            if (m_animation.Name == value) {
                return;
            }

            m_animation.Name = value;
            NotifyChanged();
        }
    }

    public string Target
    {
        get => m_animation.Target;
        set
        {
            if (m_animation.Target == value) {
                return;
            }

            m_animation.Target = value;
            if (!TargetsWall(value)) {
                m_animation.WallIndex = null;
                m_animation.Face = "all";
            }
            else {
                m_animation.WallIndex ??= 0;
            }

            NotifyChanged();
        }
    }

    public string WallIndexText
    {
        get => m_animation.WallIndex?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value)
                ? null
                : int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                    ? parsed
                    : m_animation.WallIndex;
            if (m_animation.WallIndex == normalized) {
                return;
            }

            m_animation.WallIndex = normalized;
            NotifyChanged();
        }
    }

    public string Face
    {
        get => m_animation.Face;
        set
        {
            if (m_animation.Face == value) {
                return;
            }

            m_animation.Face = value;
            NotifyChanged();
        }
    }

    public double FrameDurationMs
    {
        get => m_animation.FrameDurationMs;
        set
        {
            if (Math.Abs(m_animation.FrameDurationMs - value) < 0.001) {
                return;
            }

            m_animation.FrameDurationMs = value;
            NotifyChanged();
        }
    }

    public bool Loop
    {
        get => m_animation.Loop;
        set
        {
            if (m_animation.Loop == value) {
                return;
            }

            m_animation.Loop = value;
            NotifyChanged();
        }
    }

    public string FramesText
    {
        get => string.Join(", ", m_animation.Frames);
        set
        {
            var frames = value
                .Split([',', ';', '\r', '\n', '\t', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
            if (m_animation.Frames.SequenceEqual(frames, StringComparer.OrdinalIgnoreCase)) {
                return;
            }

            m_animation.Frames = frames;
            NotifyChanged();
        }
    }

    public string Display
    {
        get {
            var target = TargetsWall(m_animation.Target)
                ? $"{m_animation.Target}[{WallIndexText}].{m_animation.Face}"
                : m_animation.Target;
            return $"{NameOrFallback()} -> {target}, {m_animation.Frames.Count} frame(s)";
        }
    }

    public WorldBlockAnimationDefinition Animation => m_animation;

    public event PropertyChangedEventHandler? PropertyChanged;

    private string NameOrFallback()
    {
        return string.IsNullOrWhiteSpace(m_animation.Name) ? "(unnamed)" : m_animation.Name;
    }

    private static bool TargetsWall(string target)
    {
        return string.Equals(target, "wall", StringComparison.OrdinalIgnoreCase)
            || string.Equals(target, "wallOverlay", StringComparison.OrdinalIgnoreCase)
            || string.Equals(target, "overlay", StringComparison.OrdinalIgnoreCase);
    }

    private void NotifyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Display)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(WallIndexText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Face)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FramesText)));
    }
}

public sealed class BlockTreeNodeViewModel
{
    public BlockTreeNodeViewModel(string label, ImageSource? preview = null)
    {
        Label = label;
        Preview = preview;
    }

    public string Label { get; }
    public ImageSource? Preview { get; }
    public bool HasPreview => Preview is not null;
    public ObservableCollection<BlockTreeNodeViewModel> Children { get; } = [];
}

public sealed class BlockFrameViewModel
{
    public BlockFrameViewModel(int index, string textureKey, ImageSource? preview)
    {
        Index = index;
        TextureKey = textureKey ?? string.Empty;
        Preview = preview;
    }

    public int Index { get; }
    public string TextureKey { get; }
    public ImageSource? Preview { get; }
    public bool HasPreview => Preview is not null;
    public string Display => $"#{Index} - {(string.IsNullOrEmpty(TextureKey) ? "(empty)" : TextureKey)}";
}
