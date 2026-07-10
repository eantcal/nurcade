using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WinRaycastEditor.Core;

namespace WinRaycastEditor;

public sealed class WeaponAnimationPlaybackController : INotifyPropertyChanged
{
    private readonly DispatcherTimer m_timer;
    private List<ImageSource?> m_framePreviews = [];
    private WeaponAnimationMetadata? m_animation;
    private string m_metadataDirectory = Environment.CurrentDirectory;
    private int m_frameIndex;
    private bool m_isPlaying;

    public WeaponAnimationPlaybackController()
    {
        m_timer = new DispatcherTimer();
        m_timer.Tick += OnTimerTick;
    }

    public bool IsPlaying
    {
        get => m_isPlaying;
        private set
        {
            if (m_isPlaying == value) {
                return;
            }

            m_isPlaying = value;
            OnPropertyChanged();
        }
    }

    public ImageSource? CurrentPreview =>
        m_framePreviews.Count == 0
            ? null
            : m_framePreviews[Math.Clamp(m_frameIndex, 0, m_framePreviews.Count - 1)];

    public int FrameIndex
    {
        get => m_frameIndex;
        private set
        {
            var clamped = m_framePreviews.Count == 0
                ? 0
                : Math.Clamp(value, 0, m_framePreviews.Count - 1);
            if (m_frameIndex == clamped) {
                return;
            }

            m_frameIndex = clamped;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CurrentPreview));
            OnPropertyChanged(nameof(Summary));
        }
    }

    public int FrameCount => m_framePreviews.Count;

    public string Summary
    {
        get
        {
            if (m_animation is null || m_framePreviews.Count == 0) {
                return "No weapon animation loaded";
            }

            var loopText = m_animation.Loop ? "loop" : "once";
            var durationText = m_animation.FrameDurationMs > 0.0
                ? $"{m_animation.FrameDurationMs:0.#} ms/frame"
                : "-";
            return $"Frame {m_frameIndex + 1} / {m_framePreviews.Count} - {durationText} - {loopText}";
        }
    }

    public bool CanPlay => m_framePreviews.Count > 1 && !IsPlaying;
    public bool CanPause => IsPlaying;
    public bool CanStop => m_framePreviews.Count > 0 && (IsPlaying || m_frameIndex != 0);
    public bool CanStep => m_framePreviews.Count > 1;

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Configure(WeaponAnimationMetadata? animation, string metadataDirectory)
    {
        Pause();
        m_animation = animation;
        m_metadataDirectory = metadataDirectory;
        m_frameIndex = 0;
        RebuildPreviews();
        UpdateTimerInterval();
        OnPropertyChanged(nameof(FrameIndex));
        OnPropertyChanged(nameof(FrameCount));
        OnPropertyChanged(nameof(CurrentPreview));
        OnPropertyChanged(nameof(Summary));
    }

    public void RebuildPreviews()
    {
        m_framePreviews = BuildFramePreviews();
        if (m_frameIndex >= m_framePreviews.Count) {
            m_frameIndex = 0;
            OnPropertyChanged(nameof(FrameIndex));
        }

        OnPropertyChanged(nameof(CurrentPreview));
        OnPropertyChanged(nameof(FrameCount));
        OnPropertyChanged(nameof(Summary));
    }

    public void SelectFrame(int index)
    {
        Pause();
        FrameIndex = index;
        OnPropertyChanged(nameof(CanStop));
    }

    public void Play()
    {
        if (!CanPlay) {
            return;
        }

        UpdateTimerInterval();
        m_timer.Start();
        IsPlaying = true;
        OnPropertyChanged(nameof(CanPlay));
        OnPropertyChanged(nameof(CanPause));
        OnPropertyChanged(nameof(CanStop));
    }

    public void Pause()
    {
        if (!IsPlaying) {
            return;
        }

        m_timer.Stop();
        IsPlaying = false;
        OnPropertyChanged(nameof(CanPlay));
        OnPropertyChanged(nameof(CanPause));
        OnPropertyChanged(nameof(CanStop));
    }

    public void Stop()
    {
        m_timer.Stop();
        var wasPlaying = IsPlaying;
        IsPlaying = false;
        FrameIndex = 0;
        if (wasPlaying) {
            OnPropertyChanged(nameof(CanPlay));
            OnPropertyChanged(nameof(CanPause));
        }

        OnPropertyChanged(nameof(CanStop));
    }

    public void StepForward()
    {
        if (m_framePreviews.Count == 0) {
            return;
        }

        Pause();
        var next = m_frameIndex + 1;
        if (next >= m_framePreviews.Count) {
            next = m_animation?.Loop == true ? 0 : m_framePreviews.Count - 1;
        }

        FrameIndex = next;
        OnPropertyChanged(nameof(CanStop));
    }

    public void StepBackward()
    {
        if (m_framePreviews.Count == 0) {
            return;
        }

        Pause();
        var previous = m_frameIndex - 1;
        if (previous < 0) {
            previous = m_animation?.Loop == true ? m_framePreviews.Count - 1 : 0;
        }

        FrameIndex = previous;
        OnPropertyChanged(nameof(CanStop));
    }

    private void OnTimerTick(object? sender, EventArgs args)
    {
        if (m_framePreviews.Count == 0) {
            Pause();
            return;
        }

        var next = m_frameIndex + 1;
        if (next >= m_framePreviews.Count) {
            if (m_animation?.Loop == true) {
                next = 0;
            }
            else {
                FrameIndex = m_framePreviews.Count - 1;
                Pause();
                return;
            }
        }

        FrameIndex = next;
    }

    private void UpdateTimerInterval()
    {
        var milliseconds = m_animation?.FrameDurationMs ?? 0.0;
        if (milliseconds <= 0.0) {
            milliseconds = 100.0;
        }

        m_timer.Interval = TimeSpan.FromMilliseconds(milliseconds);
    }

    private List<ImageSource?> BuildFramePreviews()
    {
        if (m_animation is null) {
            return [];
        }

        return m_animation.Files
            .Select(LoadFramePreview)
            .ToList();
    }

    private ImageSource? LoadFramePreview(string relativePath)
    {
        var absolutePath = Path.GetFullPath(Path.Combine(m_metadataDirectory, relativePath));
        if (!File.Exists(absolutePath)) {
            return null;
        }

        try {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(absolutePath, UriKind.Absolute);
            bitmap.DecodePixelWidth = 360;
            bitmap.EndInit();
            bitmap.Freeze();
            return ApplyCheckerboard(bitmap);
        }
        catch {
            return null;
        }
    }

    private static ImageSource ApplyCheckerboard(BitmapSource source)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);

        for (var y = 0; y < converted.PixelHeight; ++y) {
            for (var x = 0; x < converted.PixelWidth; ++x) {
                var offset = y * stride + x * 4;
                var alpha = pixels[offset + 3];
                if (alpha == 255) {
                    continue;
                }

                var darkSquare = ((x / 8) + (y / 8)) % 2 == 0;
                var checker = darkSquare ? (byte)196 : (byte)232;
                pixels[offset] = (byte)((pixels[offset] * alpha + checker * (255 - alpha)) / 255);
                pixels[offset + 1] = (byte)((pixels[offset + 1] * alpha + checker * (255 - alpha)) / 255);
                pixels[offset + 2] = (byte)((pixels[offset + 2] * alpha + checker * (255 - alpha)) / 255);
                pixels[offset + 3] = 255;
            }
        }

        var preview = BitmapSource.Create(
            converted.PixelWidth,
            converted.PixelHeight,
            converted.DpiX,
            converted.DpiY,
            PixelFormats.Bgra32,
            null,
            pixels,
            stride);
        preview.Freeze();
        return preview;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
