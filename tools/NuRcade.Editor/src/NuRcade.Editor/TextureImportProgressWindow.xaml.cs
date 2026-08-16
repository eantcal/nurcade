using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

namespace NuRcade.Editor;

public partial class TextureImportProgressWindow : Window
{
    private readonly MainWindowViewModel m_viewModel;
    private readonly IReadOnlyList<string> m_paths;
    private bool m_finished;

    public TextureImportProgressWindow(MainWindowViewModel viewModel, IReadOnlyList<string> paths)
    {
        InitializeComponent();
        m_viewModel = viewModel;
        m_paths = paths;
        ResultsList.ItemsSource = Results;
        ImportProgress.Maximum = Math.Max(1, paths.Count);
        Loaded += OnLoaded;
    }

    public ObservableCollection<TextureImportResult> Results { get; } = [];

    private async void OnLoaded(object sender, RoutedEventArgs args)
    {
        var worldDirectory = m_viewModel.GetTextureWorldDirectory();
        var destination = Path.Combine(worldDirectory, "textures");

        var added = 0;
        byte? lastAddedKey = null;

        try {
            for (var index = 0; index < m_paths.Count; ++index) {
                var path = m_paths[index];
                StatusText.Text = $"Processing {index + 1} of {m_paths.Count}: {Path.GetFileName(path)}";

                // The file copy runs off the UI thread; the palette mutation happens here.
                var outcome = await Task.Run(() => TextureImporter.CopyToWorld(path, worldDirectory));
                var result = m_viewModel.RegisterImportedTexture(outcome);
                Results.Add(result);

                if (result.Status == TextureImportStatus.Added) {
                    ++added;
                    lastAddedKey = result.Key;
                }

                ImportProgress.Value = index + 1;
            }

            m_viewModel.FinalizeTextureImport(lastAddedKey, added);

            var skipped = m_paths.Count - added;
            SummaryText.Text = added > 0
                ? $"Added {added} texture(s){(skipped > 0 ? $" ({skipped} skipped/reused)" : string.Empty)} to:\n{destination}"
                : "No new textures were added.";
            StatusText.Text = "Completed.";
        }
        catch (Exception error) {
            SummaryText.Text = $"The import did not complete: {error.Message}";
            StatusText.Text = "Failed.";
        }
        finally {
            ImportProgress.Value = ImportProgress.Maximum;
            m_finished = true;
            OkButton.IsEnabled = true;
            OkButton.Focus();
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Don't let the window close while the import is still running.
        if (!m_finished) {
            e.Cancel = true;
            return;
        }

        base.OnClosing(e);
    }

    private void Ok_Click(object sender, RoutedEventArgs args)
    {
        Close();
    }
}
