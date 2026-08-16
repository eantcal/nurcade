using System.Windows;
using System.Windows.Controls;
using ScintillaNET;
using Color = System.Drawing.Color;
using SciStyle = ScintillaNET.Style;

namespace NuRcade.Editor;

/// <summary>
/// Hosts a ScintillaNET editor (WinForms) inside WPF, configured for JSON syntax
/// highlighting. Exposes a two-way bindable <see cref="Text"/> and reacts to a bound
/// <see cref="JsonEditorPanelViewModel"/>'s highlight requests by filling and scrolling
/// to the JSON fragment of the current selection.
/// </summary>
public partial class JsonScintillaHost : UserControl
{
    private const int HighlightIndicator = 8;

    private readonly Scintilla m_scintilla;
    private bool m_updatingText;
    private JsonEditorPanelViewModel? m_viewModel;

    public JsonScintillaHost()
    {
        InitializeComponent();
        m_scintilla = CreateScintilla();
        Host.Child = m_scintilla;
        m_scintilla.TextChanged += OnScintillaTextChanged;
        DataContextChanged += OnDataContextChanged;
        Unloaded += OnUnloaded;
    }

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text),
        typeof(string),
        typeof(JsonScintillaHost),
        new FrameworkPropertyMetadata(
            string.Empty,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnTextPropertyChanged));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    private static Scintilla CreateScintilla()
    {
        var scintilla = new Scintilla {
            WrapMode = WrapMode.None,
            IndentationGuides = IndentView.LookBoth,
            TabWidth = 2,
            UseTabs = false
        };

        scintilla.Styles[SciStyle.Default].Font = "Consolas";
        scintilla.Styles[SciStyle.Default].Size = 10;
        scintilla.StyleClearAll();

        scintilla.LexerName = "json";
        scintilla.Styles[SciStyle.Json.Default].ForeColor = Color.FromArgb(0x20, 0x20, 0x20);
        scintilla.Styles[SciStyle.Json.PropertyName].ForeColor = Color.FromArgb(0x05, 0x5b, 0xa6);
        scintilla.Styles[SciStyle.Json.PropertyName].Bold = true;
        scintilla.Styles[SciStyle.Json.String].ForeColor = Color.FromArgb(0xa3, 0x15, 0x15);
        scintilla.Styles[SciStyle.Json.StringEol].ForeColor = Color.FromArgb(0xa3, 0x15, 0x15);
        scintilla.Styles[SciStyle.Json.Number].ForeColor = Color.FromArgb(0x09, 0x80, 0x58);
        scintilla.Styles[SciStyle.Json.Keyword].ForeColor = Color.FromArgb(0x7a, 0x3e, 0x9d);
        scintilla.Styles[SciStyle.Json.Operator].ForeColor = Color.FromArgb(0x60, 0x60, 0x60);
        scintilla.Styles[SciStyle.Json.Error].ForeColor = Color.Red;
        scintilla.Styles[SciStyle.Json.LineComment].ForeColor = Color.FromArgb(0x60, 0x80, 0x60);
        scintilla.Styles[SciStyle.Json.BlockComment].ForeColor = Color.FromArgb(0x60, 0x80, 0x60);

        // Line-number margin.
        scintilla.Margins[0].Type = MarginType.Number;
        scintilla.Margins[0].Width = 40;
        for (var i = 1; i < scintilla.Margins.Count; ++i) {
            scintilla.Margins[i].Width = 0;
        }

        // Translucent box that marks the JSON fragment of the current selection.
        var highlight = scintilla.Indicators[HighlightIndicator];
        highlight.Style = IndicatorStyle.StraightBox;
        highlight.Under = true;
        highlight.ForeColor = Color.FromArgb(0x2f, 0x6f, 0xd6);
        highlight.OutlineAlpha = 110;
        highlight.Alpha = 50;

        return scintilla;
    }

    private static void OnTextPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var host = (JsonScintillaHost)d;
        if (host.m_updatingText) {
            return;
        }

        var text = e.NewValue as string ?? string.Empty;
        if (host.m_scintilla.Text == text) {
            return;
        }

        host.m_updatingText = true;
        try {
            host.m_scintilla.Text = text;
        }
        finally {
            host.m_updatingText = false;
        }
    }

    private void OnScintillaTextChanged(object? sender, EventArgs e)
    {
        if (m_updatingText) {
            return;
        }

        m_updatingText = true;
        try {
            Text = m_scintilla.Text;
        }
        finally {
            m_updatingText = false;
        }
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (m_viewModel is not null) {
            m_viewModel.HighlightRequested -= OnHighlightRequested;
        }

        m_viewModel = e.NewValue as JsonEditorPanelViewModel;
        if (m_viewModel is not null) {
            m_viewModel.HighlightRequested += OnHighlightRequested;
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (m_viewModel is not null) {
            m_viewModel.HighlightRequested -= OnHighlightRequested;
        }
    }

    private void OnHighlightRequested(object? sender, JsonHighlightEventArgs e)
    {
        if (!Dispatcher.CheckAccess()) {
            Dispatcher.Invoke(() => OnHighlightRequested(sender, e));
            return;
        }

        m_scintilla.IndicatorCurrent = HighlightIndicator;
        m_scintilla.IndicatorClearRange(0, m_scintilla.TextLength);
        if (!e.HasRange || e.Length <= 0) {
            return;
        }

        var start = Math.Max(0, Math.Min(e.Start, m_scintilla.TextLength));
        var end = Math.Min(e.Start + e.Length, m_scintilla.TextLength);
        if (end <= start) {
            return;
        }

        m_scintilla.IndicatorFillRange(start, end - start);
        m_scintilla.GotoPosition(start);
        m_scintilla.ScrollRange(start, end);
    }
}
