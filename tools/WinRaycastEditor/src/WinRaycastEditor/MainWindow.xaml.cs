using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using WinRaycastEditor.Core;

namespace WinRaycastEditor;

public partial class MainWindow : Window
{
    private const string SpriteDragFormat = "WinRaycastEditor.Sprite";
    private const string PlayerDragFormat = "WinRaycastEditor.Player";
    private Point m_dragStartPosition;
    private EditorSpriteInstance? m_dragSprite;
    private bool m_isDraggingPlayer;
    private Point m_worldPreviewMouseDownPosition;
    private Point m_worldPreviewLastMousePosition;
    private bool m_isRotatingWorldPreview;
    private bool m_worldPreviewDragMoved;
    private Viewport3D? m_activeInspectorPreview;
    private Point m_inspectorPreviewMouseDownPosition;
    private Point m_inspectorPreviewLastMousePosition;
    private bool m_inspectorPreviewDragMoved;

    // Splitter (5px) + JSON panel column (480px) from MainWindow.xaml. The window widens
    // by this much the first time the JSON editor opens so the map is not squeezed. When
    // the editor closes the window keeps its width, so the central map column (a star
    // column) reclaims the area the editor occupied.
    private const double JsonPanelTotalWidth = 485.0;
    private bool m_windowWidenedForJsonPanel;

    public MainWindow()
    {
        ImageHoverPreviewService.Initialize();
        InitializeComponent();
        var viewModel = new MainWindowViewModel();
        DataContext = viewModel;
        viewModel.JsonPanel.PropertyChanged += OnJsonPanelPropertyChanged;
        BindingOperations.SetBinding(
            WorldPreviewModelVisual,
            ModelVisual3D.ContentProperty,
            new Binding(nameof(MainWindowViewModel.Preview3DModel)) {
                Source = viewModel
            });
        BindingOperations.SetBinding(
            SelectedCellPreviewModelVisual,
            ModelVisual3D.ContentProperty,
            new Binding(nameof(MainWindowViewModel.SelectedCellPreview3DModel)) {
                Source = viewModel
            });
        BindingOperations.SetBinding(
            SelectedBlockPreviewModelVisual,
            ModelVisual3D.ContentProperty,
            new Binding(nameof(MainWindowViewModel.SelectedBlockPreview3DModel)) {
                Source = viewModel
            });
    }

    private void OnJsonPanelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(JsonEditorPanelViewModel.IsVisible)) {
            return;
        }

        if (DataContext is not MainWindowViewModel viewModel) {
            return;
        }

        // When maximized the grid simply reflows; only resize a normal-state window.
        if (WindowState != WindowState.Normal) {
            return;
        }

        if (viewModel.JsonPanel.IsVisible) {
            // Widen once so opening the editor adds space on the right instead of
            // shrinking the map; later opens reuse the width kept after a close.
            if (!m_windowWidenedForJsonPanel) {
                m_windowWidenedForJsonPanel = true;
                Width += JsonPanelTotalWidth;

                // Keep the widened window on screen.
                var workArea = SystemParameters.WorkArea;
                if (Left + Width > workArea.Right) {
                    Left = Math.Max(workArea.Left, workArea.Right - Width);
                }
            }
        }

        // On close the window width is intentionally left unchanged: the editor columns
        // collapse to zero and the central (star) map column expands to fill the gap.
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs args)
    {
        if (DataContext is not MainWindowViewModel viewModel || !viewModel.HasUnsavedChanges) {
            return;
        }

        var choice = MessageBox.Show(
            this,
            "You have unsaved changes. Do you want to save them before exiting?",
            "WinRaycast Editor",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning,
            MessageBoxResult.Yes);

        switch (choice) {
            case MessageBoxResult.Yes:
                // Abort the close if the save is cancelled or fails.
                if (!viewModel.SaveWorldForExit()) {
                    args.Cancel = true;
                }
                break;
            case MessageBoxResult.No:
                break;
            default:
                args.Cancel = true;
                break;
        }
    }

    private void TextureLibrary_DragOver(object sender, DragEventArgs args)
    {
        args.Effects = TextureDropPaths(args).Count > 0
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        args.Handled = true;
    }

    private void TextureLibrary_Drop(object sender, DragEventArgs args)
    {
        if (DataContext is not MainWindowViewModel viewModel) {
            return;
        }

        var paths = TextureDropPaths(args);
        if (paths.Count > 0) {
            ImportTextures(viewModel, paths);
            args.Handled = true;
        }
    }

    private void AddTexture_Click(object sender, RoutedEventArgs args)
    {
        if (DataContext is not MainWindowViewModel viewModel || viewModel.Document is null) {
            return;
        }

        var dialog = new Microsoft.Win32.OpenFileDialog {
            Title = "Add textures to the library",
            Filter = "Image files (*.png;*.bmp)|*.png;*.bmp|PNG files (*.png)|*.png|BMP files (*.bmp)|*.bmp|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = true
        };

        if (dialog.ShowDialog() == true && dialog.FileNames.Length > 0) {
            ImportTextures(viewModel, dialog.FileNames);
        }
    }

    private void ImportTextures(MainWindowViewModel viewModel, IReadOnlyList<string> paths)
    {
        if (viewModel.Document is null || paths.Count == 0) {
            return;
        }

        var progress = new TextureImportProgressWindow(viewModel, paths) {
            Owner = this
        };
        progress.ShowDialog();
    }

    private static IReadOnlyList<string> TextureDropPaths(DragEventArgs args)
    {
        if (!args.Data.GetDataPresent(DataFormats.FileDrop)) {
            return [];
        }

        return args.Data.GetData(DataFormats.FileDrop) is string[] files
            ? files.Where(MainWindowViewModel.IsSupportedTextureFile).ToList()
            : [];
    }

    private void SpriteDrag_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs args)
    {
        m_dragStartPosition = args.GetPosition(this);
        if (DataContext is not MainWindowViewModel viewModel) {
            return;
        }

        m_dragSprite = FindSpriteFromDataContext(sender, viewModel.SelectedLayer);
        if (m_dragSprite is not null) {
            viewModel.SelectSprite(m_dragSprite);
        }
    }

    private void SpriteDrag_MouseMove(object sender, MouseEventArgs args)
    {
        if (args.LeftButton != MouseButtonState.Pressed || m_dragSprite is null) {
            return;
        }

        var position = args.GetPosition(this);
        if (Math.Abs(position.X - m_dragStartPosition.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(position.Y - m_dragStartPosition.Y) < SystemParameters.MinimumVerticalDragDistance) {
            return;
        }

        DragDrop.DoDragDrop(
            sender as DependencyObject ?? this,
            new DataObject(SpriteDragFormat, m_dragSprite),
            DragDropEffects.Move);
        m_dragSprite = null;
    }

    private void MapCell_DragOver(object sender, DragEventArgs args)
    {
        var acceptsSprite = args.Data.GetDataPresent(SpriteDragFormat);
        var acceptsPlayer = args.Data.GetDataPresent(PlayerDragFormat);
        args.Effects = (acceptsSprite || acceptsPlayer)
            ? DragDropEffects.Move
            : DragDropEffects.None;
        args.Handled = true;
    }

    private void MapCell_Drop(object sender, DragEventArgs args)
    {
        if (DataContext is not MainWindowViewModel viewModel
            || sender is not FrameworkElement { DataContext: EditorCellViewModel targetCell }) {
            return;
        }

        if (args.Data.GetData(SpriteDragFormat) is EditorSpriteInstance sprite) {
            viewModel.SelectSprite(sprite);
            viewModel.MoveSelectedSpriteToCell(targetCell);
            args.Handled = true;
            return;
        }

        if (args.Data.GetDataPresent(PlayerDragFormat)) {
            viewModel.MovePlayerToCell(targetCell);
            args.Handled = true;
        }
    }

    private void MapCells_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (DataContext is not MainWindowViewModel viewModel
            || sender is not ListBox listBox) {
            return;
        }

        viewModel.SetSelectedMapCells(
            listBox.SelectedItems
                .OfType<EditorCellViewModel>()
                .ToList());
    }

    private void MapCell_MouseDoubleClick(object sender, MouseButtonEventArgs args)
    {
        if (args.ClickCount < 2) {
            return;
        }

        if (DataContext is not MainWindowViewModel viewModel
            || sender is not FrameworkElement { DataContext: EditorCellViewModel targetCell }) {
            return;
        }

        viewModel.SelectedCell = targetCell;
        if (viewModel.OpenSelectedCellBlockCommand.CanExecute(null)) {
            viewModel.OpenSelectedCellBlockCommand.Execute(null);
            args.Handled = true;
        }
    }

    private void PlayerDrag_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs args)
    {
        m_dragStartPosition = args.GetPosition(this);
        m_isDraggingPlayer = true;
    }

    private void PlayerDrag_MouseMove(object sender, MouseEventArgs args)
    {
        if (args.LeftButton != MouseButtonState.Pressed || !m_isDraggingPlayer) {
            return;
        }

        var position = args.GetPosition(this);
        if (Math.Abs(position.X - m_dragStartPosition.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(position.Y - m_dragStartPosition.Y) < SystemParameters.MinimumVerticalDragDistance) {
            return;
        }

        DragDrop.DoDragDrop(
            sender as DependencyObject ?? this,
            new DataObject(PlayerDragFormat, true),
            DragDropEffects.Move);
        m_isDraggingPlayer = false;
    }

    private bool m_isDraggingPlayer3D;

    private void WorldPreviewViewport_MouseLeftButtonDown(object sender, MouseButtonEventArgs args)
    {
        WorldPreviewViewport.Focus();
        m_worldPreviewMouseDownPosition = args.GetPosition(WorldPreviewViewport);
        m_worldPreviewLastMousePosition = m_worldPreviewMouseDownPosition;
        m_worldPreviewDragMoved = false;
        m_isDraggingPlayer3D = IsHitOnPlayerMarker(m_worldPreviewMouseDownPosition);
        m_isRotatingWorldPreview = !m_isDraggingPlayer3D;
        WorldPreviewViewport.CaptureMouse();
        args.Handled = true;
    }

    private void WorldPreviewViewport_MouseMove(object sender, MouseEventArgs args)
    {
        if (!m_isRotatingWorldPreview && !m_isDraggingPlayer3D) {
            return;
        }

        if (args.LeftButton != MouseButtonState.Pressed) {
            StopWorldPreviewMouseDrag();
            return;
        }

        var position = args.GetPosition(WorldPreviewViewport);
        var totalDelta = position - m_worldPreviewMouseDownPosition;
        if (!m_worldPreviewDragMoved
            && Math.Abs(totalDelta.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(totalDelta.Y) < SystemParameters.MinimumVerticalDragDistance) {
            return;
        }

        var delta = position - m_worldPreviewLastMousePosition;
        m_worldPreviewLastMousePosition = position;
        m_worldPreviewDragMoved = true;

        if (m_isRotatingWorldPreview && DataContext is MainWindowViewModel viewModel) {
            viewModel.DragPreview3D(delta.X, delta.Y);
        }

        args.Handled = true;
    }

    private void WorldPreviewViewport_MouseLeftButtonUp(object sender, MouseButtonEventArgs args)
    {
        if (!m_isRotatingWorldPreview && !m_isDraggingPlayer3D) {
            return;
        }

        var position = args.GetPosition(WorldPreviewViewport);
        var wasDraggingPlayer = m_isDraggingPlayer3D;
        var shouldSelect = !m_worldPreviewDragMoved && !wasDraggingPlayer;
        StopWorldPreviewMouseDrag();

        if (wasDraggingPlayer && m_worldPreviewDragMoved) {
            MovePlayerInPreview3DTo(position);
        }
        else if (shouldSelect) {
            SelectWorldPreviewTargetAt(position);
        }

        args.Handled = true;
    }

    private bool IsHitOnPlayerMarker(Point position)
    {
        if (DataContext is not MainWindowViewModel viewModel) {
            return false;
        }

        var hitPlayer = false;
        HitTestResultBehavior Check(HitTestResult result)
        {
            if (result is RayMeshGeometry3DHitTestResult meshHit
                && meshHit.ModelHit is not null
                && viewModel.Preview3DHitTargets.TryGetValue(meshHit.ModelHit, out var target)
                && target.Kind == WorldPreview3DHitKind.Player) {
                hitPlayer = true;
                return HitTestResultBehavior.Stop;
            }
            return HitTestResultBehavior.Continue;
        }

        VisualTreeHelper.HitTest(
            WorldPreviewViewport,
            filterCallback: null,
            resultCallback: Check,
            hitTestParameters: new PointHitTestParameters(position));
        return hitPlayer;
    }

    private void MovePlayerInPreview3DTo(Point position)
    {
        if (DataContext is not MainWindowViewModel viewModel) {
            return;
        }

        EditorCellViewModel? target = null;
        HitTestResultBehavior PickCell(HitTestResult result)
        {
            if (result is RayMeshGeometry3DHitTestResult meshHit
                && meshHit.ModelHit is not null
                && viewModel.Preview3DHitTargets.TryGetValue(meshHit.ModelHit, out var hit)
                && hit.Kind == WorldPreview3DHitKind.Cell) {
                target = viewModel.Cells.FirstOrDefault(
                    cell => cell.Row == hit.Row && cell.Column == hit.Column);
                return HitTestResultBehavior.Stop;
            }
            return HitTestResultBehavior.Continue;
        }

        VisualTreeHelper.HitTest(
            WorldPreviewViewport,
            filterCallback: null,
            resultCallback: PickCell,
            hitTestParameters: new PointHitTestParameters(position));

        if (target is not null) {
            viewModel.MovePlayerToCell(target);
        }
    }

    private void StopWorldPreviewMouseDrag()
    {
        if (WorldPreviewViewport.IsMouseCaptured) {
            WorldPreviewViewport.ReleaseMouseCapture();
        }

        m_isRotatingWorldPreview = false;
        m_worldPreviewDragMoved = false;
        m_isDraggingPlayer3D = false;
    }

    private void InspectorPreview_MouseLeftButtonDown(object sender, MouseButtonEventArgs args)
    {
        if (sender is not Viewport3D viewport) {
            return;
        }

        viewport.Focus();
        m_activeInspectorPreview = viewport;
        m_inspectorPreviewMouseDownPosition = args.GetPosition(viewport);
        m_inspectorPreviewLastMousePosition = m_inspectorPreviewMouseDownPosition;
        m_inspectorPreviewDragMoved = false;
        viewport.CaptureMouse();
        args.Handled = true;
    }

    private void InspectorPreview_MouseMove(object sender, MouseEventArgs args)
    {
        if (m_activeInspectorPreview is null || sender != m_activeInspectorPreview) {
            return;
        }

        if (args.LeftButton != MouseButtonState.Pressed) {
            StopInspectorPreviewDrag();
            return;
        }

        var position = args.GetPosition(m_activeInspectorPreview);
        var totalDelta = position - m_inspectorPreviewMouseDownPosition;
        if (!m_inspectorPreviewDragMoved
            && Math.Abs(totalDelta.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(totalDelta.Y) < SystemParameters.MinimumVerticalDragDistance) {
            return;
        }

        var delta = position - m_inspectorPreviewLastMousePosition;
        m_inspectorPreviewLastMousePosition = position;
        m_inspectorPreviewDragMoved = true;

        if (DataContext is MainWindowViewModel viewModel) {
            var isBlockPreview = ReferenceEquals(m_activeInspectorPreview, SelectedBlockPreviewViewport);
            viewModel.DragInspectorPreview(isBlockPreview, delta.X, delta.Y);
        }

        args.Handled = true;
    }

    private void InspectorPreview_MouseLeftButtonUp(object sender, MouseButtonEventArgs args)
    {
        if (m_activeInspectorPreview is null || sender != m_activeInspectorPreview) {
            return;
        }

        var viewport = m_activeInspectorPreview;
        var position = args.GetPosition(viewport);
        var shouldSelect = !m_inspectorPreviewDragMoved;
        StopInspectorPreviewDrag();

        if (shouldSelect) {
            SelectInspectorPreviewFaceAt(viewport, position);
        }

        args.Handled = true;
    }

    private void StopInspectorPreviewDrag()
    {
        if (m_activeInspectorPreview is not null && m_activeInspectorPreview.IsMouseCaptured) {
            m_activeInspectorPreview.ReleaseMouseCapture();
        }

        m_activeInspectorPreview = null;
        m_inspectorPreviewDragMoved = false;
    }

    private void SelectInspectorPreviewFaceAt(Viewport3D viewport, Point position)
    {
        if (DataContext is not MainWindowViewModel viewModel) {
            return;
        }

        var isBlockPreview = ReferenceEquals(viewport, SelectedBlockPreviewViewport);

        HitTestResultBehavior PickFace(HitTestResult result)
        {
            if (result is not RayMeshGeometry3DHitTestResult meshHit || meshHit.ModelHit is null) {
                return HitTestResultBehavior.Continue;
            }

            if (isBlockPreview) {
                viewModel.SelectBlockPreview3DFace(meshHit.ModelHit);
            }
            else {
                viewModel.SelectCellPreview3DFace(meshHit.ModelHit);
            }

            return HitTestResultBehavior.Stop;
        }

        VisualTreeHelper.HitTest(
            viewport,
            filterCallback: null,
            resultCallback: PickFace,
            hitTestParameters: new PointHitTestParameters(position));
    }

    private void SelectWorldPreviewTargetAt(Point position)
    {
        if (DataContext is not MainWindowViewModel viewModel) {
            return;
        }

        HitTestResultBehavior SelectFirstHit(HitTestResult result)
        {
            if (result is not RayMeshGeometry3DHitTestResult meshHit
                || meshHit.ModelHit is null) {
                return HitTestResultBehavior.Continue;
            }

            viewModel.SelectPreview3DTarget(meshHit.ModelHit);
            return HitTestResultBehavior.Stop;
        }

        VisualTreeHelper.HitTest(
            WorldPreviewViewport,
            filterCallback: null,
            resultCallback: SelectFirstHit,
            hitTestParameters: new PointHitTestParameters(position));
    }

    private void WorldPreviewViewport_MouseWheel(object sender, MouseWheelEventArgs args)
    {
        if (DataContext is not MainWindowViewModel viewModel) {
            return;
        }

        WorldPreviewViewport.Focus();
        if (args.Delta > 0) {
            viewModel.PreviewZoomInCommand.Execute(null);
        }
        else {
            viewModel.PreviewZoomOutCommand.Execute(null);
        }

        args.Handled = true;
    }

    private void WorldPreviewViewport_KeyDown(object sender, KeyEventArgs args)
    {
        if (DataContext is not MainWindowViewModel viewModel) {
            return;
        }

        switch (args.Key) {
            case Key.W:
            case Key.Up:
                viewModel.PreviewMoveForwardCommand.Execute(null);
                break;
            case Key.S:
            case Key.Down:
                viewModel.PreviewMoveBackwardCommand.Execute(null);
                break;
            case Key.A:
            case Key.Left:
                viewModel.PreviewStrafeLeftCommand.Execute(null);
                break;
            case Key.D:
            case Key.Right:
                viewModel.PreviewStrafeRightCommand.Execute(null);
                break;
            case Key.Q:
                viewModel.PreviewRotateLeftCommand.Execute(null);
                break;
            case Key.E:
                viewModel.PreviewRotateRightCommand.Execute(null);
                break;
            case Key.Add:
            case Key.OemPlus:
                viewModel.PreviewZoomInCommand.Execute(null);
                break;
            case Key.Subtract:
            case Key.OemMinus:
                viewModel.PreviewZoomOutCommand.Execute(null);
                break;
            case Key.Home:
                viewModel.PreviewFitAllCommand.Execute(null);
                break;
            default:
                return;
        }

        args.Handled = true;
    }

    private static EditorSpriteInstance? FindSpriteFromDataContext(object sender, string selectedLayer)
    {
        return sender switch
        {
            FrameworkElement { DataContext: SpriteInstanceViewModel sprite } => sprite.Sprite,
            FrameworkElement { DataContext: EditorCellViewModel cell }
                when string.Equals(selectedLayer, "Sprites", StringComparison.Ordinal) =>
                    cell.Cell.Sprites.FirstOrDefault(),
            _ => null
        };
    }
}
