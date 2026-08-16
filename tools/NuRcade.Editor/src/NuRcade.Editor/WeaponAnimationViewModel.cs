using System.ComponentModel;
using System.Runtime.CompilerServices;
using NuRcade.Editor.Core;

namespace NuRcade.Editor;

public sealed class WeaponAnimationViewModel : INotifyPropertyChanged
{
    public WeaponAnimationViewModel(WeaponAnimationMetadata animation)
    {
        Animation = animation;
    }

    public WeaponAnimationMetadata Animation { get; }
    public string Name => Animation.Name;
    public string Summary => $"{Animation.Files.Count} frame(s), {Animation.FrameDurationMs:0.#} ms";

    public double FrameDurationMs
    {
        get => Animation.FrameDurationMs;
        set
        {
            if (Math.Abs(Animation.FrameDurationMs - value) < 0.001) {
                return;
            }

            Animation.FrameDurationMs = Math.Max(0.0, value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(Summary));
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
            OnPropertyChanged();
            OnPropertyChanged(nameof(Summary));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Refresh()
    {
        OnPropertyChanged(nameof(Summary));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class WeaponAnimationFrameViewModel : INotifyPropertyChanged
{
    private readonly WeaponAnimationMetadata m_animation;

    public WeaponAnimationFrameViewModel(int index, WeaponAnimationMetadata animation)
    {
        Index = index;
        m_animation = animation;
    }

    public int Index { get; }
    public string Name => $"Frame {Index + 1}";

    public string File
    {
        get => Index >= 0 && Index < m_animation.Files.Count
            ? m_animation.Files[Index]
            : string.Empty;
        set
        {
            if (Index < 0 || Index >= m_animation.Files.Count || m_animation.Files[Index] == value) {
                return;
            }

            m_animation.Files[Index] = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Summary));
        }
    }

    public string Summary => string.IsNullOrWhiteSpace(File)
        ? "No image"
        : File;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
