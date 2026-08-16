using System.ComponentModel;
using System.Runtime.CompilerServices;
using NuRcade.Editor.Core;

namespace NuRcade.Editor;

public sealed class SpriteAnimationViewModel : INotifyPropertyChanged
{
    public SpriteAnimationViewModel(SpriteAnimationMetadata animation)
    {
        Animation = animation;
    }

    public SpriteAnimationMetadata Animation { get; }
    public string Name => Animation.Name;
    public string Summary => $"{Animation.Frames.Count} frame(s), {Animation.FrameDurationMs:0.#} ms";

    public double FrameDurationMs
    {
        get => Animation.FrameDurationMs;
        set
        {
            if (Math.Abs(Animation.FrameDurationMs - value) < 0.001) {
                return;
            }

            Animation.FrameDurationMs = Math.Max(0.0, value);
            NotifyChanged();
        }
    }

    public bool Loop
    {
        get => Animation.Loop;
        set
        {
            if (Animation.Loop == value) {
                return;
            }

            Animation.Loop = value;
            NotifyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Refresh()
    {
        NotifyChanged(nameof(Summary));
    }

    private void NotifyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Summary)));
    }
}

public sealed class SpriteAnimationFrameViewModel
{
    public SpriteAnimationFrameViewModel(
        int index,
        SpriteAnimationFrameMetadata frame)
    {
        Index = index;
        Frame = frame;
    }

    public int Index { get; }
    public SpriteAnimationFrameMetadata Frame { get; }
    public string Name => $"Frame {Index + 1}";
    public string Summary => $"{Frame.Directions.Count} direction(s)";
}
