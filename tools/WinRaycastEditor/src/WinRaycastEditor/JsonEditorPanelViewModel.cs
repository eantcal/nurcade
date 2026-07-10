using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows.Threading;
using WinRaycastEditor.Core;

namespace WinRaycastEditor;

public enum JsonEditorScope
{
    WholeWorld,
    Selection
}

public sealed class JsonHighlightEventArgs : EventArgs
{
    private JsonHighlightEventArgs(bool hasRange, int start, int length)
    {
        HasRange = hasRange;
        Start = start;
        Length = length;
    }

    public bool HasRange { get; }
    public int Start { get; }
    public int Length { get; }

    public static JsonHighlightEventArgs Range(int start, int length) => new(true, start, length);
    public static JsonHighlightEventArgs Clear() => new(false, 0, 0);
}

/// <summary>
/// Backs the dockable JSON editor panel. Builds JSON from the editor model, validates and
/// backs up edits, applies valid JSON back to the model on demand, and drives the
/// highlight that points at the JSON fragment of the current selection.
/// </summary>
public sealed class JsonEditorPanelViewModel : INotifyPropertyChanged
{
    private readonly MainWindowViewModel m_owner;
    private readonly DispatcherTimer m_debounce;

    private bool m_isVisible;
    private JsonEditorScope m_scope = JsonEditorScope.WholeWorld;
    private string m_jsonText = string.Empty;
    private bool m_isDirty;
    private bool m_isValid = true;
    private bool m_canApply;
    private string m_statusMessage = "World JSON";
    private bool m_suppressDirty;
    private bool m_suspendSelectionSync;
    private IReadOnlyList<JsonPathSegment>? m_highlightPath;
    private string m_findText = string.Empty;
    private string m_replaceText = string.Empty;
    private bool m_findCaseSensitive;
    private int m_findStartIndex;
    private int? m_currentFindStart;
    private int m_currentFindLength;

    public JsonEditorPanelViewModel(MainWindowViewModel owner)
    {
        m_owner = owner;
        m_debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        m_debounce.Tick += OnDebounceTick;

        ApplyCommand = new RelayCommand(_ => Apply(), _ => IsDirty && IsValid && m_canApply);
        RevertCommand = new RelayCommand(_ => RefreshFromModel(), _ => IsDirty);
        FormatCommand = new RelayCommand(_ => Format(), _ => !string.IsNullOrWhiteSpace(JsonText));
        FindNextCommand = new RelayCommand(_ => FindNext(), _ => CanSearch);
        ReplaceCommand = new RelayCommand(_ => ReplaceCurrent(), _ => CanSearch);
        ReplaceAllCommand = new RelayCommand(_ => ReplaceAll(), _ => CanSearch);
        ToggleVisibilityCommand = new RelayCommand(_ => IsVisible = !IsVisible);
        ShowWholeWorldCommand = new RelayCommand(_ => Scope = JsonEditorScope.WholeWorld);
        ShowSelectionCommand = new RelayCommand(_ => Scope = JsonEditorScope.Selection);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised to ask the Scintilla host to highlight (or clear) a range.</summary>
    public event EventHandler<JsonHighlightEventArgs>? HighlightRequested;

    public RelayCommand ApplyCommand { get; }
    public RelayCommand RevertCommand { get; }
    public RelayCommand FormatCommand { get; }
    public RelayCommand FindNextCommand { get; }
    public RelayCommand ReplaceCommand { get; }
    public RelayCommand ReplaceAllCommand { get; }
    public RelayCommand ToggleVisibilityCommand { get; }
    public RelayCommand ShowWholeWorldCommand { get; }
    public RelayCommand ShowSelectionCommand { get; }

    public ObservableCollection<string> Errors { get; } = [];

    public bool IsVisible
    {
        get => m_isVisible;
        set
        {
            if (m_isVisible == value) {
                return;
            }

            m_isVisible = value;
            OnPropertyChanged();
            if (m_isVisible) {
                RefreshFromModel();
            }
            else {
                HighlightRequested?.Invoke(this, JsonHighlightEventArgs.Clear());
            }
        }
    }

    public JsonEditorScope Scope
    {
        get => m_scope;
        set
        {
            if (m_scope == value) {
                return;
            }

            m_scope = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsWholeWorldScope));
            OnPropertyChanged(nameof(IsSelectionScope));
            RefreshFromModel();
        }
    }

    public bool IsWholeWorldScope => Scope == JsonEditorScope.WholeWorld;
    public bool IsSelectionScope => Scope == JsonEditorScope.Selection;

    public string JsonText
    {
        get => m_jsonText;
        set
        {
            if (string.Equals(m_jsonText, value, StringComparison.Ordinal)) {
                return;
            }

            m_jsonText = value ?? string.Empty;
            OnPropertyChanged();
            ResetFindMatch();

            if (m_suppressDirty) {
                return;
            }

            IsDirty = true;
            m_debounce.Stop();
            m_debounce.Start();
        }
    }

    public bool IsDirty
    {
        get => m_isDirty;
        private set
        {
            if (m_isDirty == value) {
                return;
            }

            m_isDirty = value;
            OnPropertyChanged();
            RaiseCommandState();
        }
    }

    public bool IsValid
    {
        get => m_isValid;
        private set
        {
            if (m_isValid == value) {
                return;
            }

            m_isValid = value;
            OnPropertyChanged();
            RaiseCommandState();
        }
    }

    public string StatusMessage
    {
        get => m_statusMessage;
        private set
        {
            if (string.Equals(m_statusMessage, value, StringComparison.Ordinal)) {
                return;
            }

            m_statusMessage = value;
            OnPropertyChanged();
        }
    }

    public string FindText
    {
        get => m_findText;
        set
        {
            if (string.Equals(m_findText, value, StringComparison.Ordinal)) {
                return;
            }

            m_findText = value ?? string.Empty;
            ResetFindMatch();
            OnPropertyChanged();
            RaiseCommandState();
        }
    }

    public string ReplaceText
    {
        get => m_replaceText;
        set
        {
            if (string.Equals(m_replaceText, value, StringComparison.Ordinal)) {
                return;
            }

            m_replaceText = value ?? string.Empty;
            OnPropertyChanged();
        }
    }

    public bool FindCaseSensitive
    {
        get => m_findCaseSensitive;
        set
        {
            if (m_findCaseSensitive == value) {
                return;
            }

            m_findCaseSensitive = value;
            ResetFindMatch();
            OnPropertyChanged();
        }
    }

    private bool CanSearch =>
        !string.IsNullOrEmpty(FindText)
        && !string.IsNullOrEmpty(JsonText);

    /// <summary>
    /// Rebuilds the editor text from the current model and clears the dirty/validation state.
    /// Called when the panel opens, the scope changes, or an apply completes.
    /// </summary>
    public void RefreshFromModel()
    {
        if (!m_owner.HasDocument) {
            SetTextProgrammatically(string.Empty);
            m_canApply = false;
            IsValid = true;
            StatusMessage = "No world loaded";
            Errors.Clear();
            RaiseCommandState();
            return;
        }

        if (Scope == JsonEditorScope.WholeWorld) {
            SetTextProgrammatically(m_owner.BuildWorldJson());
            m_canApply = true;
            StatusMessage = "World JSON";
        }
        else if (m_owner.SelectionJsonAvailable) {
            SetTextProgrammatically(m_owner.BuildSelectionJson());
            m_canApply = true;
            StatusMessage = $"Selection: {m_owner.SelectionJsonLabel}";
        }
        else {
            SetTextProgrammatically(string.Empty);
            m_canApply = false;
            StatusMessage = "Select a sprite to edit it as JSON";
        }

        IsDirty = false;
        Validate();
        ApplyHighlight();
        RaiseCommandState();
    }

    /// <summary>
    /// Notified by the owner whenever the selected cell/sprite/block changes. Moves the
    /// highlight (whole-world scope) or refreshes the shown fragment (selection scope).
    /// </summary>
    public void OnSelectionChanged(IReadOnlyList<JsonPathSegment>? highlightPath)
    {
        m_highlightPath = highlightPath;
        if (!IsVisible || m_suspendSelectionSync) {
            return;
        }

        if (Scope == JsonEditorScope.Selection) {
            if (!IsDirty) {
                RefreshFromModel();
            }

            return;
        }

        ApplyHighlight();
    }

    private void Apply()
    {
        if (!IsDirty || !IsValid || !m_canApply) {
            return;
        }

        bool applied;
        IReadOnlyList<string> errors;
        m_suspendSelectionSync = true;
        try {
            applied = Scope == JsonEditorScope.WholeWorld
                ? m_owner.TryApplyWorldJson(JsonText, out errors)
                : m_owner.TryApplySelectionJson(JsonText, out errors);
        }
        finally {
            m_suspendSelectionSync = false;
        }

        if (!applied) {
            ShowErrors(errors, "Apply failed");
            return;
        }

        RefreshFromModel();
        StatusMessage = Scope == JsonEditorScope.WholeWorld
            ? "Applied world JSON"
            : "Applied sprite JSON";
    }

    private void Format()
    {
        if (string.IsNullOrWhiteSpace(JsonText)) {
            return;
        }

        try {
            using var document = System.Text.Json.JsonDocument.Parse(JsonText);
            var pretty = System.Text.Json.JsonSerializer.Serialize(
                document.RootElement,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            JsonText = pretty;
        }
        catch (System.Text.Json.JsonException) {
            // Leave the (invalid) text untouched; validation already reports the problem.
        }
    }

    private void FindNext()
    {
        if (!CanSearch) {
            return;
        }

        var start = m_currentFindStart is int current
            ? current + Math.Max(1, m_currentFindLength)
            : m_findStartIndex;
        if (start > JsonText.Length) {
            start = 0;
        }

        var index = FindMatch(start);
        if (index < 0 && start > 0) {
            index = FindMatch(0);
        }

        if (index < 0) {
            ResetFindMatch();
            HighlightRequested?.Invoke(this, JsonHighlightEventArgs.Clear());
            StatusMessage = $"Find: '{FindText}' not found";
            return;
        }

        SetCurrentFindMatch(index, FindText.Length);
        StatusMessage = $"Find: match at character {index + 1}";
    }

    private void ReplaceCurrent()
    {
        if (!CanSearch) {
            return;
        }

        if (!CurrentFindMatchIsValid()) {
            var searchStart = Math.Min(m_findStartIndex, JsonText.Length);
            var index = FindMatch(searchStart);
            if (index < 0 && searchStart > 0) {
                index = FindMatch(0);
            }

            if (index < 0) {
                ResetFindMatch();
                HighlightRequested?.Invoke(this, JsonHighlightEventArgs.Clear());
                StatusMessage = $"Replace: '{FindText}' not found";
                return;
            }

            SetCurrentFindMatch(index, FindText.Length);
        }

        var start = m_currentFindStart!.Value;
        var before = JsonText[..start];
        var after = JsonText[(start + m_currentFindLength)..];
        JsonText = before + ReplaceText + after;
        m_findStartIndex = start + ReplaceText.Length;
        m_currentFindStart = null;
        m_currentFindLength = 0;
        if (ReplaceText.Length > 0) {
            HighlightRequested?.Invoke(this, JsonHighlightEventArgs.Range(start, ReplaceText.Length));
        }
        else {
            HighlightRequested?.Invoke(this, JsonHighlightEventArgs.Clear());
        }

        StatusMessage = $"Replaced one match for '{FindText}'";
    }

    private void ReplaceAll()
    {
        if (!CanSearch) {
            return;
        }

        var comparison = FindComparison;
        var source = JsonText;
        var builder = new StringBuilder(source.Length);
        var searchStart = 0;
        var replacements = 0;
        while (searchStart <= source.Length) {
            var index = source.IndexOf(FindText, searchStart, comparison);
            if (index < 0) {
                builder.Append(source, searchStart, source.Length - searchStart);
                break;
            }

            builder.Append(source, searchStart, index - searchStart);
            builder.Append(ReplaceText);
            searchStart = index + FindText.Length;
            ++replacements;
        }

        if (replacements == 0) {
            ResetFindMatch();
            HighlightRequested?.Invoke(this, JsonHighlightEventArgs.Clear());
            StatusMessage = $"Replace all: '{FindText}' not found";
            return;
        }

        JsonText = builder.ToString();
        ResetFindMatch();
        HighlightRequested?.Invoke(this, JsonHighlightEventArgs.Clear());
        StatusMessage = $"Replaced {replacements} match(es) for '{FindText}'";
    }

    private int FindMatch(int start)
    {
        if (!CanSearch) {
            return -1;
        }

        start = Math.Clamp(start, 0, JsonText.Length);
        return JsonText.IndexOf(FindText, start, FindComparison);
    }

    private StringComparison FindComparison =>
        FindCaseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

    private void SetCurrentFindMatch(int start, int length)
    {
        m_currentFindStart = start;
        m_currentFindLength = length;
        m_findStartIndex = start + Math.Max(1, length);
        HighlightRequested?.Invoke(this, JsonHighlightEventArgs.Range(start, length));
    }

    private bool CurrentFindMatchIsValid()
    {
        if (m_currentFindStart is not int start
            || m_currentFindLength <= 0
            || start < 0
            || start + m_currentFindLength > JsonText.Length) {
            return false;
        }

        return string.Equals(
            JsonText.Substring(start, m_currentFindLength),
            FindText,
            FindComparison);
    }

    private void ResetFindMatch()
    {
        m_findStartIndex = 0;
        m_currentFindStart = null;
        m_currentFindLength = 0;
    }

    private void OnDebounceTick(object? sender, EventArgs e)
    {
        m_debounce.Stop();
        Validate();

        // Persist in-progress edits so nothing is lost while the JSON is being changed.
        // Only the whole-world scope mirrors the on-disk world file.
        if (IsDirty && Scope == JsonEditorScope.WholeWorld) {
            var sourcePath = m_owner.WorldSourcePath;
            if (!string.IsNullOrWhiteSpace(sourcePath)) {
                try {
                    JsonEditingBackupService.WriteBackup(sourcePath, JsonText);
                }
                catch (IOException) {
                    // A failed backup must not interrupt editing.
                }
            }
        }
    }

    private void Validate()
    {
        if (Scope == JsonEditorScope.WholeWorld) {
            var ok = WorldJsonDocumentService.TryParseAndValidate(JsonText, out _, out var errors);
            IsValid = ok;
            if (ok) {
                Errors.Clear();
                StatusMessage = IsDirty ? "Valid - press Apply" : "World JSON";
            }
            else {
                ShowErrors(errors, "Invalid JSON");
            }

            return;
        }

        // Selection scope: validate the sprite fragment.
        if (!m_owner.SelectionJsonAvailable) {
            IsValid = false;
            Errors.Clear();
            return;
        }

        try {
            using var _ = System.Text.Json.JsonDocument.Parse(JsonText);
            IsValid = true;
            Errors.Clear();
            StatusMessage = IsDirty ? "Valid - press Apply" : $"Selection: {m_owner.SelectionJsonLabel}";
        }
        catch (System.Text.Json.JsonException error) {
            IsValid = false;
            ShowErrors([$"Invalid sprite JSON: {error.Message}"], "Invalid JSON");
        }
    }

    private void ShowErrors(IReadOnlyList<string> errors, string status)
    {
        Errors.Clear();
        foreach (var error in errors) {
            Errors.Add(error);
        }

        StatusMessage = errors.Count > 0 ? $"{status}: {errors[0]}" : status;
    }

    private void ApplyHighlight()
    {
        if (!IsVisible || Scope != JsonEditorScope.WholeWorld || m_highlightPath is null) {
            HighlightRequested?.Invoke(this, JsonHighlightEventArgs.Clear());
            return;
        }

        if (JsonSpanLocator.TryLocate(JsonText, m_highlightPath, out var start, out var length)) {
            HighlightRequested?.Invoke(this, JsonHighlightEventArgs.Range(start, length));
        }
        else {
            HighlightRequested?.Invoke(this, JsonHighlightEventArgs.Clear());
        }
    }

    private void SetTextProgrammatically(string text)
    {
        m_suppressDirty = true;
        try {
            JsonText = text;
        }
        finally {
            m_suppressDirty = false;
        }
    }

    private void RaiseCommandState()
    {
        ApplyCommand.RaiseCanExecuteChanged();
        RevertCommand.RaiseCanExecuteChanged();
        FormatCommand.RaiseCanExecuteChanged();
        FindNextCommand.RaiseCanExecuteChanged();
        ReplaceCommand.RaiseCanExecuteChanged();
        ReplaceAllCommand.RaiseCanExecuteChanged();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
