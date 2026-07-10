using System.Windows.Media;

namespace WinRaycastEditor;

public sealed class TextureChoiceViewModel
{
    public TextureChoiceViewModel(string key, string label, ImageSource? preview)
    {
        Key = key;
        Label = label;
        Preview = preview;
    }

    public string Key { get; }
    public string Label { get; }
    public ImageSource? Preview { get; }
}
