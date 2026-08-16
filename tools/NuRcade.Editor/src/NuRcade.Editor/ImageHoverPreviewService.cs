using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace NuRcade.Editor;

internal static class ImageHoverPreviewService
{
    private const double PreviewMaxSize = 460.0;
    private const double PreviewMinLongestSide = 260.0;
    private const int HoverDelayMilliseconds = 650;

    private static readonly DependencyProperty IsPreviewChromeProperty =
        DependencyProperty.RegisterAttached(
            "IsPreviewChrome",
            typeof(bool),
            typeof(ImageHoverPreviewService),
            new PropertyMetadata(false));

    private static bool s_initialized;
    private static DispatcherTimer? s_timer;
    private static Image? s_pendingImage;
    private static Image? s_activeImage;
    private static Popup? s_popup;

    public static void Initialize()
    {
        if (s_initialized) {
            return;
        }

        s_initialized = true;
        EventManager.RegisterClassHandler(
            typeof(Image),
            UIElement.MouseEnterEvent,
            new MouseEventHandler(OnImageMouseEnter),
            true);
        EventManager.RegisterClassHandler(
            typeof(Image),
            UIElement.MouseLeaveEvent,
            new MouseEventHandler(OnImageMouseLeave),
            true);
        EventManager.RegisterClassHandler(
            typeof(Image),
            UIElement.PreviewMouseDownEvent,
            new MouseButtonEventHandler(OnImageMouseDown),
            true);
    }

    private static bool GetIsPreviewChrome(DependencyObject target)
    {
        return (bool)target.GetValue(IsPreviewChromeProperty);
    }

    private static void SetIsPreviewChrome(DependencyObject target, bool value)
    {
        target.SetValue(IsPreviewChromeProperty, value);
    }

    private static void OnImageMouseEnter(object sender, MouseEventArgs args)
    {
        if (sender is not Image image
            || GetIsPreviewChrome(image)
            || image.Source is null
            || !image.IsVisible) {
            return;
        }

        EnsureTimer(image.Dispatcher);
        s_pendingImage = image;
        s_timer!.Stop();
        s_timer.Start();
    }

    private static void OnImageMouseLeave(object sender, MouseEventArgs args)
    {
        if (ReferenceEquals(sender, s_pendingImage)
            || ReferenceEquals(sender, s_activeImage)) {
            HidePreview();
        }
    }

    private static void OnImageMouseDown(object sender, MouseButtonEventArgs args)
    {
        HidePreview();
    }

    private static void EnsureTimer(Dispatcher dispatcher)
    {
        if (s_timer is not null && s_timer.Dispatcher == dispatcher) {
            return;
        }

        s_timer?.Stop();
        s_timer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(HoverDelayMilliseconds),
            DispatcherPriority.Background,
            (_, _) => {
                s_timer?.Stop();
                ShowPendingPreview();
            },
            dispatcher);
        s_timer.Stop();
    }

    private static void ShowPendingPreview()
    {
        var image = s_pendingImage;
        var source = image?.Source;
        if (image is null || source is null || !image.IsMouseOver || !image.IsVisible) {
            HidePreview();
            return;
        }

        var previewSource = ResolvePreviewSource(source);
        HidePreview();
        s_activeImage = image;
        s_popup = new Popup {
            PlacementTarget = image,
            Placement = PlacementMode.MousePoint,
            HorizontalOffset = 18,
            VerticalOffset = 18,
            AllowsTransparency = true,
            StaysOpen = true,
            PopupAnimation = PopupAnimation.Fade,
            Child = CreatePreviewContent(previewSource)
        };
        s_popup.Opened += (_, _) => s_popup?.Child?.Focus();
        s_popup.IsOpen = true;
    }

    private static UIElement CreatePreviewContent(ImageSource source)
    {
        var (width, height) = PreviewSizeFor(source);
        var previewImage = new Image {
            Source = source,
            Width = width,
            Height = height,
            Stretch = Stretch.Uniform,
            SnapsToDevicePixels = true,
            IsHitTestVisible = false
        };
        SetIsPreviewChrome(previewImage, true);
        RenderOptions.SetBitmapScalingMode(previewImage, BitmapScalingMode.HighQuality);

        var imageHost = new Border {
            Background = CreateCheckerBrush(),
            BorderBrush = new SolidColorBrush(Color.FromRgb(72, 76, 84)),
            BorderThickness = new Thickness(1),
            Child = previewImage
        };

        var text = new TextBlock {
            Text = DescribeSource(source),
            Foreground = Brushes.White,
            FontSize = 11,
            Margin = new Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = PreviewMaxSize
        };

        var stack = new StackPanel();
        stack.Children.Add(imageHost);
        stack.Children.Add(text);

        return new Border {
            Background = new SolidColorBrush(Color.FromRgb(31, 34, 39)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(146, 157, 180)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10),
            Effect = new DropShadowEffect {
                BlurRadius = 18,
                ShadowDepth = 4,
                Opacity = 0.35
            },
            Child = stack
        };
    }

    private static ImageSource ResolvePreviewSource(ImageSource source)
    {
        if (source is BitmapImage { UriSource: { } uri } && uri.IsAbsoluteUri) {
            try {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = uri;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch {
                return source;
            }
        }

        return source;
    }

    private static (double width, double height) PreviewSizeFor(ImageSource source)
    {
        var width = source.Width;
        var height = source.Height;
        if (source is BitmapSource bitmap && bitmap.PixelWidth > 0 && bitmap.PixelHeight > 0) {
            width = bitmap.PixelWidth;
            height = bitmap.PixelHeight;
        }

        if (width <= 0.0 || double.IsNaN(width) || double.IsInfinity(width)) {
            width = 256.0;
        }

        if (height <= 0.0 || double.IsNaN(height) || double.IsInfinity(height)) {
            height = 256.0;
        }

        var scale = Math.Min(PreviewMaxSize / width, PreviewMaxSize / height);
        var longest = Math.Max(width, height);
        if (longest * scale < PreviewMinLongestSide) {
            scale = PreviewMinLongestSide / longest;
        }

        scale = Math.Min(scale, Math.Min(PreviewMaxSize / width, PreviewMaxSize / height));
        return (Math.Max(1.0, width * scale), Math.Max(1.0, height * scale));
    }

    private static string DescribeSource(ImageSource source)
    {
        if (source is BitmapSource bitmap && bitmap.PixelWidth > 0 && bitmap.PixelHeight > 0) {
            return $"{bitmap.PixelWidth} x {bitmap.PixelHeight} px";
        }

        return $"{source.Width:0.#} x {source.Height:0.#}";
    }

    private static DrawingBrush CreateCheckerBrush()
    {
        const double size = 16.0;
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(
            new SolidColorBrush(Color.FromRgb(236, 238, 242)),
            null,
            new RectangleGeometry(new Rect(0, 0, size, size))));
        group.Children.Add(new GeometryDrawing(
            new SolidColorBrush(Color.FromRgb(203, 207, 216)),
            null,
            new RectangleGeometry(new Rect(0, 0, size / 2, size / 2))));
        group.Children.Add(new GeometryDrawing(
            new SolidColorBrush(Color.FromRgb(203, 207, 216)),
            null,
            new RectangleGeometry(new Rect(size / 2, size / 2, size / 2, size / 2))));

        return new DrawingBrush(group) {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, size, size),
            ViewportUnits = BrushMappingMode.Absolute
        };
    }

    private static void HidePreview()
    {
        s_timer?.Stop();
        s_pendingImage = null;
        s_activeImage = null;
        if (s_popup is not null) {
            s_popup.IsOpen = false;
            s_popup.Child = null;
            s_popup = null;
        }
    }
}
