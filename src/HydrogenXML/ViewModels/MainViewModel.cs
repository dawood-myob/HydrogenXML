using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using HydrogenXML.Core.Engine;
using HydrogenXML.Core.Models;
using HydrogenXML.Core.Workspace;
using HydrogenXML.Infrastructure;

namespace HydrogenXML.ViewModels;

public sealed class RecentFile
{
    public string Path { get; init; } = "";
    public string Name => System.IO.Path.GetFileName(Path);
}

public sealed class MessageRow
{
    public TransformMessage Message { get; init; } = null!;
    public string Text => Message.Text.Trim();
    public string Location => Message.Line > 0 ? $"{System.IO.Path.GetFileName(Message.File)}:{Message.Line}" : "";
    public string Icon => Message.Terminate ? "✖" : "✉";
}

public sealed class OutputChoice
{
    public string Name { get; init; } = "";
    public string? File { get; init; }
    public override string ToString() => Name;
}

public sealed class MainViewModel : ObservableObject
{
    private const int MaxRecent = 15;
    private const long MaxOutputDisplayBytes = 20L * 1024 * 1024;
    private const long OutputPreviewBytes = 1024 * 1024;
    private const int MaxLiveValidationChars = 3_000_000;

    private readonly WorkspaceService _workspaceService = new();
    private readonly XsltEngine _engine = new();
    private readonly XPathEvaluator _xpath = new();
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "HydrogenXML", Environment.ProcessId.ToString());
    private readonly Dictionary<DocumentViewModel, Debouncer> _validationTimers = new();
    private readonly Debouncer _autoTransformTimer = new();
    private readonly DispatcherTimer _autosaveTimer;
    private readonly DispatcherTimer _watchTimer;
    private readonly HashSet<string> _notifiedChanges = new(StringComparer.OrdinalIgnoreCase);
    private List<string> _lastBackups = new();

    private WorkspaceState _state = new();
    private string? _workspacePath;
    private DocumentViewModel? _selectedXsl;
    private DocumentViewModel? _selectedXml;
    private DocumentViewModel? _activeDocument;
    private string _status = "Ready";
    private string _transformInfo = "";
    private bool _isBusy;
    private bool _transforming;
    private bool _transformQueued;
    private bool _autoTransform;
    private int _autoDelayMs = 1000;
    private bool _prettyPrint = true;
    private string _theme = "Dark";
    private List<Diagnostic> _transformDiagnostics = new();
    private List<Diagnostic> _xpathDiagnostics = new();
    private string _xpathExpression = "";
    private string _xpathStatus = "";
    private string? _currentOutputFile;
    private bool _outputIsPreview;
    private OutputChoice? _selectedOutputChoice;
    private int _errorCount;
    private int _warningCount;
    private string? _lastXmlTitleForOutput;

    public MainViewModel()
    {
        Samples = new SampleStore(Path.Combine(_workspaceService.RootDirectory, "Samples"));
        Output = new DocumentViewModel(DocKind.Output) { IsReadOnly = true };

        SamplesView = CollectionViewSource.GetDefaultView(SampleItems);
        SamplesView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(SampleFile.Dataset)));
        TemplatesView = CollectionViewSource.GetDefaultView(TemplateItems);
        TemplatesView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(TemplateInfo.Category)));

        NewXslCommand = new RelayCommand(() => NewDocument(DocKind.Xsl));
        NewXmlCommand = new RelayCommand(() => NewDocument(DocKind.Xml));
        OpenXslCommand = new RelayCommand(() => OpenWithDialog(DocKind.Xsl));
        OpenXmlCommand = new RelayCommand(() => OpenWithDialog(DocKind.Xml));
        SaveCommand = new RelayCommand(() => SaveDocument(CurrentDocument), () => CurrentDocument is { IsReadOnly: false });
        SaveAsCommand = new RelayCommand(() => SaveDocumentAs(CurrentDocument), () => CurrentDocument is { IsReadOnly: false });
        SaveAllCommand = new RelayCommand(SaveAll);
        ReloadCommand = new RelayCommand(() => Reload(CurrentDocument), () => CurrentDocument?.FilePath != null);
        CloseDocumentCommand = new RelayCommand(p => CloseDocument(p as DocumentViewModel ?? CurrentDocument));
        TransformCommand = new AsyncCommand(() => TransformAsync(false));
        ValidateCommand = new AsyncCommand(() => ValidateCurrentAsync());
        ValidateXslCommand = new AsyncCommand(() => ValidateAsync(SelectedXsl, true));
        ValidateXmlCommand = new AsyncCommand(() => ValidateAsync(SelectedXml, true));
        ValidateOutputCommand = new AsyncCommand(() => ValidateAsync(Output, true));
        FormatCommand = new AsyncCommand(() => FormatAsync(CurrentDocument));
        FormatOutputCommand = new AsyncCommand(() => FormatAsync(Output));
        CopyOutputCommand = new RelayCommand(CopyOutput);
        SaveOutputCommand = new RelayCommand(SaveOutput);
        XPathRunCommand = new AsyncCommand(RunXPathAsync);
        ToggleThemeCommand = new RelayCommand(() => Theme = Theme == "Dark" ? "Light" : "Dark");
        OpenRecentCommand = new RelayCommand(OpenRecent);
        OpenSampleCommand = new RelayCommand(p => { if (p is SampleFile s) OpenFile(DocKind.Xml, s.Path); });
        NewFromTemplateCommand = new RelayCommand(p => { if (p is TemplateInfo t) NewFromTemplate(t); });
        SaveAsTemplateCommand = new RelayCommand(SaveAsTemplate, () => SelectedXsl != null);
        AddCurrentAsSampleCommand = new RelayCommand(AddCurrentAsSample, () => SelectedXml != null);
        AddSampleFilesCommand = new RelayCommand(AddSampleFiles);
        NewDatasetCommand = new RelayCommand(NewDataset);
        NewSampleCommand = new RelayCommand(NewSample);
        DeleteSampleCommand = new RelayCommand(DeleteSample);
        DeleteTemplateCommand = new RelayCommand(DeleteTemplate);
        RefreshLibraryCommand = new RelayCommand(RefreshLibrary);
        ClearDiagnosticsCommand = new RelayCommand(ClearDiagnostics);
        SaveWorkspaceCommand = new RelayCommand(() => SaveWorkspace());
        SaveWorkspaceAsCommand = new RelayCommand(SaveWorkspaceAs);
        OpenWorkspaceCommand = new RelayCommand(OpenWorkspaceWithDialog);
        NewWorkspaceCommand = new RelayCommand(NewWorkspace);
        OpenRecentWorkspaceCommand = new RelayCommand(p => { if (p is string s) OpenWorkspace(s); });

        _autosaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        _autosaveTimer.Tick += (_, _) => SaveWorkspace();
        _watchTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        _watchTimer.Tick += (_, _) => CheckExternalChanges();
    }

    #region Properties

    public SampleStore Samples { get; }
    public DocumentViewModel Output { get; }
    public ObservableCollection<DocumentViewModel> XslDocs { get; } = new();
    public ObservableCollection<DocumentViewModel> XmlDocs { get; } = new();
    public ObservableCollection<RecentFile> RecentXsl { get; } = new();
    public ObservableCollection<RecentFile> RecentXml { get; } = new();
    public ObservableCollection<string> RecentWorkspaces { get; } = new();
    public ObservableCollection<SampleFile> SampleItems { get; } = new();
    public ObservableCollection<TemplateInfo> TemplateItems { get; } = new();
    public ObservableCollection<DiagnosticRow> Diagnostics { get; } = new();
    public ObservableCollection<DiagnosticRow> ValidationRows { get; } = new();
    public ObservableCollection<MessageRow> Messages { get; } = new();
    public ObservableCollection<BookmarkItem> BookmarkItems { get; } = new();
    public ObservableCollection<XPathRow> XPathResults { get; } = new();
    public ObservableCollection<string> XPathHistory { get; } = new();
    public ObservableCollection<OutputChoice> OutputChoices { get; } = new();
    public ICollectionView SamplesView { get; }
    public ICollectionView TemplatesView { get; }
    public IReadOnlyList<SnippetInfo> Snippets => TemplateLibrary.Snippets;
    public IReadOnlyList<int> DelayChoices { get; } = new[] { 500, 750, 1000, 1500, 2000 };

    public WorkspaceState State => _state;
    public LayoutState Layout => _state.Layout;
    public string? WorkspacePath => _workspacePath;
    public string WorkspaceName => _state.Name;

    public DocumentViewModel? SelectedXsl
    {
        get => _selectedXsl;
        set
        {
            if (Set(ref _selectedXsl, value))
            {
                OnPropertyChanged(nameof(WindowTitle));
                if (value != null) ActiveDocument = value;
                if (AutoTransform && value != null) ScheduleAutoTransform();
            }
        }
    }

    public DocumentViewModel? SelectedXml
    {
        get => _selectedXml;
        set
        {
            if (Set(ref _selectedXml, value))
            {
                if (value != null) ActiveDocument = value;
                if (AutoTransform && value != null) ScheduleAutoTransform();
            }
        }
    }

    /// <summary>The editor that last had focus (used by Save, Format, Reload and Validate).</summary>
    public DocumentViewModel? ActiveDocument
    {
        get => _activeDocument;
        set
        {
            if (Set(ref _activeDocument, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    public DocumentViewModel? CurrentDocument => ActiveDocument ?? SelectedXsl;

    public string Status
    {
        get => _status;
        set => Set(ref _status, value);
    }

    public string TransformInfo
    {
        get => _transformInfo;
        private set => Set(ref _transformInfo, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => Set(ref _isBusy, value);
    }

    public bool AutoTransform
    {
        get => _autoTransform;
        set
        {
            if (Set(ref _autoTransform, value))
            {
                _state.Transform.AutoTransform = value;
                if (value) ScheduleAutoTransform();
                else _autoTransformTimer.Cancel();
            }
        }
    }

    public int AutoDelayMs
    {
        get => _autoDelayMs;
        set
        {
            if (Set(ref _autoDelayMs, value)) _state.Transform.AutoTransformDelayMs = value;
        }
    }

    public bool PrettyPrint
    {
        get => _prettyPrint;
        set
        {
            if (Set(ref _prettyPrint, value)) _state.Transform.PrettyPrintOutput = value;
        }
    }

    public string Theme
    {
        get => _theme;
        set
        {
            if (Set(ref _theme, value))
            {
                _state.Layout.Theme = value;
                App.ApplyTheme(value);
            }
        }
    }

    public string XPathExpression
    {
        get => _xpathExpression;
        set => Set(ref _xpathExpression, value);
    }

    public string XPathStatus
    {
        get => _xpathStatus;
        private set => Set(ref _xpathStatus, value);
    }

    public int ErrorCount
    {
        get => _errorCount;
        private set => Set(ref _errorCount, value);
    }

    public int WarningCount
    {
        get => _warningCount;
        private set => Set(ref _warningCount, value);
    }

    public bool OutputIsPreview => _outputIsPreview;

    public OutputChoice? SelectedOutputChoice
    {
        get => _selectedOutputChoice;
        set
        {
            if (Set(ref _selectedOutputChoice, value) && value != null)
                ShowOutputFile(value.File);
        }
    }

    public string WindowTitle =>
        $"HydrogenXML — {_state.Name}" + (SelectedXsl != null ? $" — {SelectedXsl.Title}" : "");

    public string DiagnosticsHeader => ErrorCount + WarningCount > 0
        ? $"Diagnostics ({ErrorCount} errors, {WarningCount} warnings)"
        : "Diagnostics";

    public string MessagesHeader => Messages.Count > 0 ? $"Messages ({Messages.Count})" : "Messages";

    public TransformSettings TransformSettings => _state.Transform;

    public event Action<int>? BottomTabRequested;
    public event Action? BookmarksRefreshed;

    public ICommand NewXslCommand { get; }
    public ICommand NewXmlCommand { get; }
    public ICommand OpenXslCommand { get; }
    public ICommand OpenXmlCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand SaveAsCommand { get; }
    public ICommand SaveAllCommand { get; }
    public ICommand ReloadCommand { get; }
    public ICommand CloseDocumentCommand { get; }
    public ICommand TransformCommand { get; }
    public ICommand ValidateCommand { get; }
    public ICommand ValidateXslCommand { get; }
    public ICommand ValidateXmlCommand { get; }
    public ICommand ValidateOutputCommand { get; }
    public ICommand FormatCommand { get; }
    public ICommand FormatOutputCommand { get; }
    public ICommand CopyOutputCommand { get; }
    public ICommand SaveOutputCommand { get; }
    public ICommand XPathRunCommand { get; }
    public ICommand ToggleThemeCommand { get; }
    public ICommand OpenRecentCommand { get; }
    public ICommand OpenSampleCommand { get; }
    public ICommand NewFromTemplateCommand { get; }
    public ICommand SaveAsTemplateCommand { get; }
    public ICommand AddCurrentAsSampleCommand { get; }
    public ICommand AddSampleFilesCommand { get; }
    public ICommand NewDatasetCommand { get; }
    public ICommand NewSampleCommand { get; }
    public ICommand DeleteSampleCommand { get; }
    public ICommand DeleteTemplateCommand { get; }
    public ICommand RefreshLibraryCommand { get; }
    public ICommand ClearDiagnosticsCommand { get; }
    public ICommand SaveWorkspaceCommand { get; }
    public ICommand SaveWorkspaceAsCommand { get; }
    public ICommand OpenWorkspaceCommand { get; }
    public ICommand NewWorkspaceCommand { get; }
    public ICommand OpenRecentWorkspaceCommand { get; }

    #endregion

    #region Startup / workspace

    public void Initialize()
    {
        Directory.CreateDirectory(_tempDir);
        SeedSamples();
        _workspacePath = _workspaceService.LoadLastWorkspacePath();
        ApplyState(_workspaceService.Load(_workspacePath));
        RefreshLibrary();
        _autosaveTimer.Start();
        _watchTimer.Start();
        Task.Run(XsltEngine.WarmUp);
    }

    private void SeedSamples()
    {
        try
        {
            if (Samples.GetSamples().Count > 0) return;
            foreach (var (dataset, name, content) in TemplateLibrary.SampleData)
                Samples.Add(dataset, name, content);
        }
        catch (IOException) { }
    }

    private void ApplyState(WorkspaceState state)
    {
        _state = state;
        _autoTransform = state.Transform.AutoTransform;
        _autoDelayMs = state.Transform.AutoTransformDelayMs;
        _prettyPrint = state.Transform.PrettyPrintOutput;
        _theme = state.Layout.Theme;
        App.ApplyTheme(_theme);
        OnPropertyChanged(nameof(AutoTransform));
        OnPropertyChanged(nameof(AutoDelayMs));
        OnPropertyChanged(nameof(PrettyPrint));
        OnPropertyChanged(nameof(Theme));
        OnPropertyChanged(nameof(Layout));
        OnPropertyChanged(nameof(WorkspaceName));

        FillRecent(RecentXsl, state.RecentXsl);
        FillRecent(RecentXml, state.RecentXml);
        RecentWorkspaces.Clear();
        foreach (var w in state.RecentWorkspaces.Where(File.Exists)) RecentWorkspaces.Add(w);

        _lastBackups = state.XslDocuments.Concat(state.XmlDocuments)
            .Where(d => d.BackupFile != null).Select(d => d.BackupFile!).ToList();

        RestoreDocs(state.XslDocuments, DocKind.Xsl, XslDocs, state.ActiveXslIndex, d => SelectedXsl = d);
        RestoreDocs(state.XmlDocuments, DocKind.Xml, XmlDocs, state.ActiveXmlIndex, d => SelectedXml = d);

        if (XslDocs.Count == 0)
        {
            // First run: start with something useful so the three-pane layout is immediately visible.
            var xsl = AddDocument(DocumentViewModel.CreateNew(DocKind.Xsl,
                TemplateLibrary.BuiltIn.First(t => t.Name == "Get_Workers to flat XML").Content));
            SelectedXsl = xsl;
        }

        if (XmlDocs.Count == 0)
        {
            var sample = Samples.GetSamples().FirstOrDefault(s => s.Name == "Get_Workers_Response.xml")
                         ?? Samples.GetSamples().FirstOrDefault();
            if (sample != null) OpenFile(DocKind.Xml, sample.Path, addToRecent: false);
            else SelectedXml = AddDocument(DocumentViewModel.CreateNew(DocKind.Xml));
        }

        ActiveDocument = SelectedXsl;
        OnPropertyChanged(nameof(WindowTitle));
        Status = "Workspace loaded";
    }

    private void RestoreDocs(List<OpenDocumentState> saved, DocKind kind, ObservableCollection<DocumentViewModel> target,
        int activeIndex, Action<DocumentViewModel?> select)
    {
        var restored = new List<DocumentViewModel>();
        foreach (var s in saved)
        {
            try
            {
                DocumentViewModel doc;
                var backup = _workspaceService.ReadBackup(s.BackupFile);
                if (s.Path != null && File.Exists(s.Path))
                {
                    doc = DocumentViewModel.FromFile(kind, s.Path);
                    if (backup != null && !doc.IsReadOnly)
                    {
                        doc.ReplaceText(backup);
                        doc.Document.UndoStack.ClearAll();
                    }
                }
                else if (backup != null)
                {
                    doc = DocumentViewModel.CreateNew(kind, backup);
                    if (!string.IsNullOrEmpty(s.Title)) doc.Rename(s.Title, s.Path);
                }
                else
                {
                    continue;
                }

                doc.CaretOffset = Math.Min(s.CaretOffset, doc.Document.TextLength);
                doc.VerticalOffset = s.VerticalOffset;
                restored.Add(doc);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A file that vanished or cannot be read is skipped rather than blocking startup.
            }
        }

        foreach (var d in restored) AddDocument(d, target);
        select(restored.Count == 0 ? null : restored[Math.Clamp(activeIndex, 0, restored.Count - 1)]);
    }

    private static void FillRecent(ObservableCollection<RecentFile> list, IEnumerable<string> paths)
    {
        list.Clear();
        foreach (var p in paths.Take(MaxRecent)) list.Add(new RecentFile { Path = p });
    }

    public void CaptureState()
    {
        _state.XslDocuments = XslDocs.Select(Capture).ToList();
        _state.XmlDocuments = XmlDocs.Select(Capture).ToList();
        _state.ActiveXslIndex = Math.Max(0, XslDocs.IndexOf(SelectedXsl!));
        _state.ActiveXmlIndex = Math.Max(0, XmlDocs.IndexOf(SelectedXml!));
        _state.CurrentXslPath = SelectedXsl?.FilePath;
        _state.CurrentXmlPath = SelectedXml?.FilePath;
        _state.RecentXsl = RecentXsl.Select(r => r.Path).ToList();
        _state.RecentXml = RecentXml.Select(r => r.Path).ToList();
        _state.RecentWorkspaces = RecentWorkspaces.ToList();
    }

    private OpenDocumentState Capture(DocumentViewModel d)
    {
        string? backup = null;
        // Unsaved edits (and untitled documents) are kept in a backup file so nothing is lost on exit.
        if ((d.IsDirty || d.FilePath == null) && !d.IsReadOnly)
        {
            try { backup = _workspaceService.WriteBackup(d.Text); }
            catch (IOException) { }
        }

        return new OpenDocumentState
        {
            Path = d.FilePath,
            Title = d.Title,
            BackupFile = backup,
            CaretOffset = d.CaretOffset,
            VerticalOffset = d.VerticalOffset
        };
    }

    public void SaveWorkspace(string? path = null)
    {
        try
        {
            CaptureState();
            var previous = _lastBackups;
            _lastBackups = _state.XslDocuments.Concat(_state.XmlDocuments)
                .Where(d => d.BackupFile != null).Select(d => d.BackupFile!).ToList();
            _workspaceService.Save(_state, path ?? _workspacePath);
            _workspaceService.SaveLastWorkspacePath(path ?? _workspacePath);
            foreach (var old in previous.Except(_lastBackups))
            {
                try { File.Delete(old); } catch { }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = "Could not save workspace: " + ex.Message;
        }
    }

    private void SaveWorkspaceAs()
    {
        var path = Dialogs.SaveFile("Save workspace as", Dialogs.WorkspaceFilter, _state.Name + ".hydrogen");
        if (path == null) return;
        _state.Name = Path.GetFileNameWithoutExtension(path);
        _workspacePath = path;
        AddRecentWorkspace(path);
        SaveWorkspace(path);
        OnPropertyChanged(nameof(WorkspaceName));
        OnPropertyChanged(nameof(WindowTitle));
        Status = $"Workspace saved to {path}";
    }

    private void OpenWorkspaceWithDialog()
    {
        var files = Dialogs.OpenFiles("Open workspace", Dialogs.WorkspaceFilter, false);
        if (files is { Length: 1 }) OpenWorkspace(files[0]);
    }

    public void OpenWorkspace(string path)
    {
        if (!File.Exists(path))
        {
            Dialogs.Error($"Workspace not found:\n{path}");
            return;
        }

        SaveWorkspace();
        CloseAllDocuments();
        _workspacePath = path;
        var state = _workspaceService.Load(path);
        if (string.IsNullOrWhiteSpace(state.Name)) state.Name = Path.GetFileNameWithoutExtension(path);
        ApplyState(state);
        AddRecentWorkspace(path);
        WorkspaceSwitched?.Invoke();
        Status = $"Opened workspace {state.Name}";
    }

    private void NewWorkspace()
    {
        var name = Dialogs.Prompt("New workspace", "Workspace name:", "My workspace");
        if (string.IsNullOrWhiteSpace(name)) return;
        var path = Dialogs.SaveFile("Create workspace file", Dialogs.WorkspaceFilter, name + ".hydrogen");
        if (path == null) return;
        SaveWorkspace();
        CloseAllDocuments();
        _workspacePath = path;
        var state = new WorkspaceState { Name = name };
        state.Layout = _state.Layout;
        state.RecentWorkspaces = RecentWorkspaces.ToList();
        ApplyState(state);
        AddRecentWorkspace(path);
        SaveWorkspace(path);
        WorkspaceSwitched?.Invoke();
    }

    public event Action? WorkspaceSwitched;

    private void AddRecentWorkspace(string path)
    {
        var existing = RecentWorkspaces.FirstOrDefault(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        if (existing != null) RecentWorkspaces.Remove(existing);
        RecentWorkspaces.Insert(0, path);
        while (RecentWorkspaces.Count > MaxRecent) RecentWorkspaces.RemoveAt(RecentWorkspaces.Count - 1);
    }

    private void CloseAllDocuments()
    {
        foreach (var d in XslDocs.Concat(XmlDocs).ToList()) Detach(d);
        XslDocs.Clear();
        XmlDocs.Clear();
        SelectedXsl = null;
        SelectedXml = null;
        ActiveDocument = null;
        _transformDiagnostics = new();
        _xpathDiagnostics = new();
        RefreshDiagnostics();
        RefreshBookmarks();
    }

    #endregion

    #region Documents

    private DocumentViewModel AddDocument(DocumentViewModel doc, ObservableCollection<DocumentViewModel>? target = null)
    {
        target ??= doc.Kind == DocKind.Xsl ? XslDocs : XmlDocs;
        target.Add(doc);
        doc.TextEdited += OnDocumentEdited;
        doc.MarkersChanged += (_, _) => RefreshDiagnostics();
        doc.BookmarksChanged += (_, _) => RefreshBookmarks();
        if (doc.Document.TextLength < MaxLiveValidationChars)
            ScheduleValidation(doc, TimeSpan.FromMilliseconds(150));
        return doc;
    }

    private void Detach(DocumentViewModel doc)
    {
        doc.TextEdited -= OnDocumentEdited;
        if (_validationTimers.Remove(doc, out var t)) t.Cancel();
    }

    private void NewDocument(DocKind kind)
    {
        var doc = AddDocument(DocumentViewModel.CreateNew(kind));
        Select(doc);
    }

    private void Select(DocumentViewModel doc)
    {
        if (doc.Kind == DocKind.Xsl) SelectedXsl = doc;
        else if (doc.Kind == DocKind.Xml) SelectedXml = doc;
        ActiveDocument = doc;
    }

    private void OpenWithDialog(DocKind kind)
    {
        var files = Dialogs.OpenFiles(kind == DocKind.Xsl ? "Open XSL stylesheet" : "Open XML source",
            kind == DocKind.Xsl ? Dialogs.XslFilter : Dialogs.XmlFilter);
        if (files == null) return;
        foreach (var f in files) OpenFile(kind, f);
    }

    public DocumentViewModel? OpenFile(DocKind kind, string path, bool addToRecent = true)
    {
        try
        {
            var full = Path.GetFullPath(path);
            var list = kind == DocKind.Xsl ? XslDocs : XmlDocs;
            var existing = list.FirstOrDefault(d => string.Equals(d.FilePath, full, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                Select(existing);
                return existing;
            }

            var doc = AddDocument(DocumentViewModel.FromFile(kind, full));
            Select(doc);
            if (addToRecent) AddRecent(kind, full);
            Status = doc.IsLargeFile
                ? $"Opened {doc.Title} ({FileService.FormatSize(doc.FileSize)}) as a read-only preview"
                : $"Opened {full}";
            return doc;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Dialogs.Error($"Could not open the file:\n{path}\n\n{ex.Message}");
            return null;
        }
    }

    /// <summary>Opens dropped files; stylesheets go to the XSL pane, everything else to the XML pane.</summary>
    public void OpenDropped(IEnumerable<string> paths, DocKind? forced = null)
    {
        foreach (var p in paths)
        {
            if (!File.Exists(p)) continue;
            var ext = Path.GetExtension(p).ToLowerInvariant();
            var kind = forced ?? (ext is ".xsl" or ".xslt" ? DocKind.Xsl : DocKind.Xml);
            OpenFile(kind, p);
        }
    }

    private void OpenRecent(object? p)
    {
        if (p is not RecentFile r) return;
        if (!File.Exists(r.Path))
        {
            Dialogs.Error($"The file no longer exists:\n{r.Path}");
            RecentXsl.Remove(r);
            RecentXml.Remove(r);
            return;
        }

        var kind = RecentXsl.Contains(r) ? DocKind.Xsl : DocKind.Xml;
        OpenFile(kind, r.Path);
    }

    private void AddRecent(DocKind kind, string path)
    {
        var list = kind == DocKind.Xsl ? RecentXsl : RecentXml;
        var existing = list.FirstOrDefault(r => string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase));
        if (existing != null) list.Remove(existing);
        list.Insert(0, new RecentFile { Path = path });
        while (list.Count > MaxRecent) list.RemoveAt(list.Count - 1);
    }

    public bool SaveDocument(DocumentViewModel? doc)
    {
        if (doc == null || doc.IsReadOnly) return false;
        if (doc.FilePath == null) return SaveDocumentAs(doc);

        try
        {
            if (File.Exists(doc.FilePath))
            {
                var info = new FileInfo(doc.FilePath);
                if (info.IsReadOnly)
                {
                    if (!Dialogs.Confirm($"{doc.Title} is read-only. Overwrite it anyway?")) return false;
                    info.IsReadOnly = false;
                }
                else if (info.LastWriteTimeUtc != doc.LastWriteUtc && info.LastWriteTimeUtc != doc.LastSelfWriteUtc
                         && !Dialogs.Confirm($"{doc.Title} was changed on disk since it was loaded.\nOverwrite the file with your version?"))
                {
                    return false;
                }
            }

            doc.Save();
            _notifiedChanges.Remove(doc.FilePath!);
            AddRecent(doc.Kind, doc.FilePath!);
            Status = $"Saved {doc.FilePath}";
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Dialogs.Error($"Could not save {doc.Title}:\n{ex.Message}");
            return false;
        }
    }

    public bool SaveDocumentAs(DocumentViewModel? doc)
    {
        if (doc == null || doc.IsReadOnly) return false;
        var path = Dialogs.SaveFile("Save as", doc.Kind == DocKind.Xsl ? Dialogs.XslFilter : Dialogs.XmlFilter,
            doc.Title, doc.FilePath != null ? Path.GetDirectoryName(doc.FilePath) : null);
        if (path == null) return false;
        try
        {
            doc.Save(path);
            AddRecent(doc.Kind, doc.FilePath!);
            OnPropertyChanged(nameof(WindowTitle));
            Status = $"Saved {doc.FilePath}";
            ScheduleValidation(doc, TimeSpan.FromMilliseconds(100));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Dialogs.Error($"Could not save the file:\n{ex.Message}");
            return false;
        }
    }

    private void SaveAll()
    {
        foreach (var d in XslDocs.Concat(XmlDocs).Where(d => d.IsDirty && !d.IsReadOnly))
            if (!SaveDocument(d)) break;
    }

    private void Reload(DocumentViewModel? doc)
    {
        if (doc?.FilePath == null) return;
        if (doc.IsDirty && !Dialogs.Confirm($"Discard your changes to {doc.Title} and reload it from disk?")) return;
        try
        {
            doc.Reload();
            Status = $"Reloaded {doc.Title}";
            if (AutoTransform) ScheduleAutoTransform();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Dialogs.Error($"Could not reload {doc.Title}:\n{ex.Message}");
        }
    }

    public bool CloseDocument(DocumentViewModel? doc)
    {
        if (doc == null || doc.Kind == DocKind.Output) return false;
        if (doc.IsDirty && !doc.IsReadOnly)
        {
            switch (Dialogs.AskSave($"Save changes to {doc.Title}?"))
            {
                case YesNoCancel.Cancel: return false;
                case YesNoCancel.Yes when !SaveDocument(doc): return false;
            }
        }

        var list = doc.Kind == DocKind.Xsl ? XslDocs : XmlDocs;
        var index = list.IndexOf(doc);
        Detach(doc);
        list.Remove(doc);
        doc.ClearBookmarks();
        var next = list.Count == 0 ? null : list[Math.Min(index, list.Count - 1)];
        if (doc.Kind == DocKind.Xsl) SelectedXsl = next;
        else SelectedXml = next;
        if (ActiveDocument == doc) ActiveDocument = next ?? SelectedXsl ?? SelectedXml;
        _transformDiagnostics.RemoveAll(d => doc.MatchesFile(d.File));
        RefreshDiagnostics();
        return true;
    }

    public void OpenInExplorer(DocumentViewModel? doc)
    {
        if (doc?.FilePath == null) return;
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{doc.FilePath}\"") { UseShellExecute = true });
    }

    private void CheckExternalChanges()
    {
        foreach (var doc in XslDocs.Concat(XmlDocs).ToList())
        {
            if (doc.FilePath == null) continue;
            DateTime stamp;
            try
            {
                if (!File.Exists(doc.FilePath)) continue;
                stamp = File.GetLastWriteTimeUtc(doc.FilePath);
            }
            catch (IOException) { continue; }

            if (stamp == doc.LastWriteUtc || stamp == doc.LastSelfWriteUtc) continue;
            var key = doc.FilePath + "|" + stamp.Ticks;

            if (!doc.IsDirty)
            {
                try
                {
                    doc.Reload();
                    Status = $"{doc.Title} changed on disk and was reloaded";
                    if (AutoTransform && (doc == SelectedXsl || doc == SelectedXml)) ScheduleAutoTransform();
                }
                catch (IOException) { }
            }
            else if (_notifiedChanges.Add(key))
            {
                if (Dialogs.Confirm($"{doc.Title} was changed outside HydrogenXML.\nReload it and lose your unsaved edits?"))
                {
                    try { doc.Reload(); }
                    catch (IOException) { }
                }
                else
                {
                    doc.AcknowledgeDiskChange(stamp);
                }
            }
        }
    }

    #endregion

    #region Validation and diagnostics

    private void OnDocumentEdited(object? sender, EventArgs e)
    {
        if (sender is not DocumentViewModel doc) return;
        _transformDiagnostics.RemoveAll(d => doc.MatchesFile(d.File));
        if (doc.TransformDiagnostics.Count > 0) doc.SetTransformDiagnostics(new());
        if (doc.Document.TextLength < MaxLiveValidationChars)
            ScheduleValidation(doc, TimeSpan.FromMilliseconds(600));
        if (AutoTransform && !doc.IsReadOnly && (doc == SelectedXsl || doc == SelectedXml))
            ScheduleAutoTransform();
    }

    private void ScheduleValidation(DocumentViewModel doc, TimeSpan delay)
    {
        if (!_validationTimers.TryGetValue(doc, out var timer))
            _validationTimers[doc] = timer = new Debouncer();
        timer.Run(delay, () => _ = ValidateAsync(doc, false));
    }

    private Task ValidateCurrentAsync()
    {
        var doc = CurrentDocument;
        return doc == null ? Task.CompletedTask : ValidateAsync(doc, true);
    }

    private async Task ValidateAsync(DocumentViewModel? doc, bool explicitRequest)
    {
        if (doc == null) return;
        var seq = ++doc.ValidationSeq;
        var kind = doc.Kind;
        var name = doc.FilePath ?? doc.Title;
        var large = doc.IsLargeFile && doc.FilePath != null;
        var text = large ? null : doc.Text;
        var path = doc.FilePath;
        if (explicitRequest) Status = $"Validating {doc.Title}…";

        var diags = await Task.Run(() =>
        {
            if (large) return XmlTools.ValidateWellFormedFile(path!, DiagnosticCategory.XmlValidation);
            if (kind == DocKind.Xsl)
            {
                var wf = XmlTools.ValidateWellFormed(text!, name, DiagnosticCategory.XslValidation);
                return wf.Count > 0 ? wf : _engine.CheckStylesheet(text!, path, doc.Title);
            }

            if (kind == DocKind.Output && string.IsNullOrWhiteSpace(text)) return new List<Diagnostic>();
            return XmlTools.ValidateWellFormed(text!, kind == DocKind.Output ? "Output" : name,
                DiagnosticCategory.XmlValidation);
        });

        if (seq != doc.ValidationSeq) return;
        doc.SetValidation(diags);

        if (explicitRequest)
        {
            var errors = diags.Count(d => d.Severity == DiagnosticSeverity.Error);
            Status = errors == 0
                ? $"{doc.Title}: no problems found"
                : $"{doc.Title}: {errors} error(s) found";
            BottomTabRequested?.Invoke(1);
            if (errors > 0)
            {
                var first = diags.First(d => d.Severity == DiagnosticSeverity.Error);
                if (first.Line > 0) doc.RequestNavigate(first.Line, first.Column);
            }
        }
    }

    public void RefreshDiagnostics()
    {
        var all = new List<Diagnostic>();
        var validation = new List<Diagnostic>();
        foreach (var doc in XslDocs.Concat(XmlDocs).Append(Output))
        {
            validation.AddRange(doc.ValidationDiagnostics);
        }

        all.AddRange(validation);
        all.AddRange(_transformDiagnostics);
        all.AddRange(_xpathDiagnostics);

        var distinct = all
            .GroupBy(d => (d.File, d.Line, d.Column, d.Message, d.Severity))
            .Select(g => g.First()).Take(2000).ToList();

        Diagnostics.Clear();
        foreach (var d in distinct) Diagnostics.Add(new DiagnosticRow(d));
        ValidationRows.Clear();
        foreach (var d in validation.GroupBy(d => (d.File, d.Line, d.Column, d.Message)).Select(g => g.First()).Take(2000))
            ValidationRows.Add(new DiagnosticRow(d));

        ErrorCount = distinct.Count(d => d.Severity == DiagnosticSeverity.Error);
        WarningCount = distinct.Count(d => d.Severity == DiagnosticSeverity.Warning);
        OnPropertyChanged(nameof(DiagnosticsHeader));
    }

    private void ClearDiagnostics()
    {
        _transformDiagnostics = new();
        _xpathDiagnostics = new();
        foreach (var d in XslDocs.Concat(XmlDocs).Append(Output))
        {
            d.SetTransformDiagnostics(new());
            d.SetValidation(new());
        }

        Messages.Clear();
        OnPropertyChanged(nameof(MessagesHeader));
        RefreshDiagnostics();
    }

    /// <summary>Navigates to the code a diagnostic refers to, opening the file first when needed.</summary>
    public void GoTo(string file, int line, int column)
    {
        var doc = XslDocs.Concat(XmlDocs).FirstOrDefault(d => d.MatchesFile(file));
        if (doc == null && !string.IsNullOrEmpty(file) && File.Exists(file))
        {
            var ext = Path.GetExtension(file).ToLowerInvariant();
            doc = OpenFile(ext is ".xml" ? DocKind.Xml : DocKind.Xsl, file);
        }

        if (doc == null)
        {
            Status = $"Cannot locate {file}";
            return;
        }

        Select(doc);
        if (line > 0) doc.RequestNavigate(line, Math.Max(1, column));
    }

    #endregion

    #region Transformation

    public void ScheduleAutoTransform()
    {
        if (!AutoTransform) return;
        _autoTransformTimer.Run(TimeSpan.FromMilliseconds(AutoDelayMs), () => _ = TransformAsync(true));
    }

    public async Task TransformAsync(bool auto)
    {
        if (_transforming)
        {
            _transformQueued = true;
            return;
        }

        _transforming = true;
        IsBusy = true;
        try
        {
            do
            {
                _transformQueued = false;
                await TransformOnceAsync(auto);
            } while (_transformQueued);
        }
        finally
        {
            _transforming = false;
            IsBusy = false;
        }
    }

    private async Task TransformOnceAsync(bool auto)
    {
        var xsl = SelectedXsl;
        var xml = SelectedXml;
        if (xsl == null)
        {
            Status = "Open or create an XSL stylesheet first.";
            return;
        }

        if (xml == null)
        {
            Status = "Open or create an XML source first.";
            return;
        }

        var parameters = new Dictionary<string, string>();
        foreach (var p in _state.Transform.Parameters.Where(p => !string.IsNullOrWhiteSpace(p.Name)))
            parameters[p.Name.Trim()] = p.Value;

        var useFile = xml.IsLargeFile && xml.FilePath != null;
        var request = new TransformRequest
        {
            StylesheetText = xsl.Text,
            StylesheetPath = xsl.FilePath,
            StylesheetName = xsl.Title,
            SourceText = useFile ? null : xml.Text,
            SourcePath = xml.FilePath,
            SourceName = xml.FilePath ?? xml.Title,
            Parameters = parameters,
            InitialTemplate = _state.Transform.InitialTemplate,
            InitialMode = _state.Transform.InitialMode,
            Indent = PrettyPrint,
            OutputDirectory = _tempDir
        };

        Status = auto ? "Auto-transforming…" : "Transforming…";
        var sw = Stopwatch.StartNew();
        TransformResult result;
        try
        {
            result = await _engine.TransformAsync(request);
        }
        catch (Exception ex)
        {
            Status = "Transformation failed: " + ex.Message;
            return;
        }

        _lastXmlTitleForOutput = xml.Title;
        ApplyResult(result, xsl, xml, sw.Elapsed);
    }

    private void ApplyResult(TransformResult result, DocumentViewModel xsl, DocumentViewModel xml, TimeSpan total)
    {
        _transformDiagnostics = result.Diagnostics;
        foreach (var doc in XslDocs.Concat(XmlDocs))
            doc.SetTransformDiagnostics(result.Diagnostics.Where(d => doc.MatchesFile(d.File)).ToList());

        Messages.Clear();
        foreach (var m in result.Messages) Messages.Add(new MessageRow { Message = m });
        OnPropertyChanged(nameof(MessagesHeader));
        RefreshDiagnostics();

        var errors = result.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);
        var warnings = result.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Warning);

        if (result.Success && result.OutputFile != null)
        {
            var previous = _currentOutputFile;
            _currentOutputFile = result.OutputFile;
            OutputChoices.Clear();
            OutputChoices.Add(new OutputChoice { Name = "Principal result", File = result.OutputFile });
            foreach (var kv in result.ResultDocuments)
                OutputChoices.Add(new OutputChoice { Name = kv.Key, File = kv.Value });
            _selectedOutputChoice = OutputChoices[0];
            OnPropertyChanged(nameof(SelectedOutputChoice));
            ShowOutputFile(result.OutputFile);
            DeleteTemp(previous);

            TransformInfo = $"{FileService.FormatSize(result.OutputLength)} in {total.TotalMilliseconds:0} ms " +
                            $"(compile {result.CompileTime.TotalMilliseconds:0} ms, transform {result.TransformTime.TotalMilliseconds:0} ms)";
            Status = warnings > 0
                ? $"Transformation succeeded with {warnings} warning(s)"
                : "Transformation succeeded";
            Output.SetValidation(new());
            RefreshDiagnostics();
            if (warnings > 0) BottomTabRequested?.Invoke(0);
        }
        else
        {
            Output.Banner = "The last transformation failed — showing the previous result. See Diagnostics.";
            Status = $"Transformation failed — {errors} error(s)";
            TransformInfo = "";
            BottomTabRequested?.Invoke(0);
            var first = result.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error && d.Line > 0);
            if (first != null && !AutoTransform)
                GoTo(first.File, first.Line, first.Column);
        }
    }

    private void ShowOutputFile(string? file)
    {
        if (file == null || !File.Exists(file)) return;
        try
        {
            var size = new FileInfo(file).Length;
            var loaded = FileService.ReadOutput(file, size > MaxOutputDisplayBytes ? OutputPreviewBytes : size);
            Output.SetText(loaded.Text, markClean: true);
            Output.Encoding = loaded.Encoding;
            _outputIsPreview = loaded.IsPreview;
            _displayedOutputFile = file;
            Output.Banner = loaded.IsPreview
                ? $"Output is {FileService.FormatSize(loaded.TotalBytes)}: showing the first {FileService.FormatSize(OutputPreviewBytes)}. Use Save to export the complete result."
                : null;
            OnPropertyChanged(nameof(OutputIsPreview));
            ScheduleValidation(Output, TimeSpan.FromMilliseconds(50));
        }
        catch (IOException ex)
        {
            Status = "Could not read the output: " + ex.Message;
        }
    }

    private string? _displayedOutputFile;

    private static void DeleteTemp(string? file)
    {
        if (file == null) return;
        try { File.Delete(file); } catch { }
    }

    public void CleanupTemp()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    private async Task FormatAsync(DocumentViewModel? doc)
    {
        if (doc == null) return;
        if (doc.IsReadOnly && doc.Kind != DocKind.Output)
        {
            Status = "Large file previews cannot be formatted.";
            return;
        }

        if (doc.Kind == DocKind.Output && _outputIsPreview)
        {
            Status = "The output is too large to format; save it and use an external tool.";
            return;
        }

        var text = doc.Text;
        try
        {
            var formatted = await Task.Run(() => XmlTools.Format(text));
            if (formatted != text)
            {
                doc.ReplaceText(formatted);
                Status = $"Formatted {doc.Title}";
            }
            else
            {
                Status = $"{doc.Title} is already formatted";
            }
        }
        catch (System.Xml.XmlException ex)
        {
            Status = $"Cannot format: {ex.Message}";
            await ValidateAsync(doc, true);
        }
    }

    private void CopyOutput()
    {
        if (string.IsNullOrEmpty(Output.Text))
        {
            Status = "There is no output to copy.";
            return;
        }

        try
        {
            Clipboard.SetText(Output.Text);
            Status = _outputIsPreview
                ? "Copied the displayed preview (the output is larger — use Save for the full result)"
                : "Output copied to the clipboard";
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            Status = "The clipboard is busy; try again.";
        }
    }

    private void SaveOutput()
    {
        if (string.IsNullOrEmpty(Output.Text) && _displayedOutputFile == null)
        {
            Status = "There is no output to save.";
            return;
        }

        var stem = Path.GetFileNameWithoutExtension(_lastXmlTitleForOutput ?? "output");
        var isXml = Output.Text.TrimStart().StartsWith('<');
        var ext = isXml ? ".xml" : ".txt";
        var path = Dialogs.SaveFile("Export transformed output",
            isXml ? "XML files (*.xml)|*.xml|All files (*.*)|*.*" : "Text files (*.txt)|*.txt|CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            stem + "-output" + ext, SelectedXml?.FilePath != null ? Path.GetDirectoryName(SelectedXml.FilePath) : null);
        if (path == null) return;
        try
        {
            if (_outputIsPreview && _displayedOutputFile != null)
                File.Copy(_displayedOutputFile, path, true);
            else
                FileService.Write(path, Output.Text, Output.Encoding);
            Status = $"Output saved to {path}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Dialogs.Error($"Could not save the output:\n{ex.Message}");
        }
    }

    #endregion

    #region XPath

    private async Task RunXPathAsync()
    {
        var xml = SelectedXml;
        if (xml == null)
        {
            XPathStatus = "Open an XML document first.";
            return;
        }

        var expr = XPathExpression;
        if (string.IsNullOrWhiteSpace(expr))
        {
            XPathStatus = "Enter an XPath expression.";
            return;
        }

        XPathStatus = "Evaluating…";
        var useFile = xml.IsLargeFile && xml.FilePath != null;
        var result = await _xpath.EvaluateAsync(expr, useFile ? null : xml.Text, xml.FilePath,
            xml.FilePath ?? xml.Title);

        XPathResults.Clear();
        _xpathDiagnostics = new();
        if (result.Error != null)
        {
            _xpathDiagnostics = new List<Diagnostic> { result.Error };
            XPathStatus = result.Error.Message;
        }
        else
        {
            foreach (var i in result.Items)
                XPathResults.Add(new XPathRow { Index = i.Index, Kind = i.Kind, Name = i.Name, Value = i.Value, Line = i.Line, Column = i.Column });
            XPathStatus = result.TotalCount > result.Items.Count
                ? $"{result.TotalCount} results (showing first {result.Items.Count}) in {result.Elapsed.TotalMilliseconds:0} ms"
                : $"{result.TotalCount} result(s) in {result.Elapsed.TotalMilliseconds:0} ms";
            var existing = XPathHistory.IndexOf(expr);
            if (existing >= 0) XPathHistory.RemoveAt(existing);
            XPathHistory.Insert(0, expr);
            while (XPathHistory.Count > 30) XPathHistory.RemoveAt(XPathHistory.Count - 1);
        }

        RefreshDiagnostics();
    }

    public void GoToXPathRow(XPathRow row)
    {
        if (SelectedXml == null || row.Line <= 0) return;
        SelectedXml.RequestNavigate(row.Line, Math.Max(1, row.Column));
        ActiveDocument = SelectedXml;
    }

    #endregion

    #region Bookmarks

    public void RefreshBookmarks()
    {
        BookmarkItems.Clear();
        foreach (var doc in XslDocs.Concat(XmlDocs))
        {
            foreach (var line in doc.BookmarkLines())
            {
                var text = doc.Document.GetText(doc.Document.GetLineByNumber(line)).Trim();
                BookmarkItems.Add(new BookmarkItem { Document = doc, Line = line, Preview = text.Length > 120 ? text[..120] : text });
            }
        }

        BookmarksRefreshed?.Invoke();
    }

    public void GoToBookmark(BookmarkItem item)
    {
        Select(item.Document);
        item.Document.RequestNavigate(item.Line, 1);
    }

    /// <summary>Moves to the next or previous bookmark in the document, wrapping around.</summary>
    public void StepBookmark(DocumentViewModel doc, int currentLine, bool forward)
    {
        var lines = doc.BookmarkLines();
        if (lines.Count == 0)
        {
            Status = "No bookmarks in this document";
            return;
        }

        var target = forward
            ? lines.FirstOrDefault(l => l > currentLine)
            : lines.LastOrDefault(l => l < currentLine);
        if (target == 0) target = forward ? lines[0] : lines[^1];
        doc.RequestNavigate(target, 1);
    }

    #endregion

    #region Library: samples, templates, snippets

    public void RefreshLibrary()
    {
        SampleItems.Clear();
        try
        {
            foreach (var s in Samples.GetSamples()) SampleItems.Add(s);
        }
        catch (IOException) { }

        TemplateItems.Clear();
        foreach (var t in TemplateLibrary.BuiltIn) TemplateItems.Add(t);
        foreach (var t in TemplateLibrary.LoadUserTemplates(UserTemplateDirectory)) TemplateItems.Add(t);
    }

    private string UserTemplateDirectory => Path.Combine(_workspaceService.RootDirectory, "Templates");

    private void NewFromTemplate(TemplateInfo template)
    {
        var doc = AddDocument(DocumentViewModel.CreateNew(DocKind.Xsl, template.Content));
        Select(doc);
        Status = $"New stylesheet from template “{template.Name}”";
    }

    private void SaveAsTemplate()
    {
        var xsl = SelectedXsl;
        if (xsl == null) return;
        var name = Dialogs.Prompt("Save as template", "Template name:", Path.GetFileNameWithoutExtension(xsl.Title));
        if (string.IsNullOrWhiteSpace(name)) return;
        try
        {
            Directory.CreateDirectory(UserTemplateDirectory);
            var safe = string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            File.WriteAllText(Path.Combine(UserTemplateDirectory, safe + ".xsl"), xsl.Text);
            RefreshLibrary();
            Status = $"Saved template “{name}”";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Dialogs.Error(ex.Message);
        }
    }

    private void DeleteTemplate(object? p)
    {
        if (p is not TemplateInfo { IsUser: true, Path: not null } t) return;
        if (!Dialogs.Confirm($"Delete template “{t.Name}”?")) return;
        try { File.Delete(t.Path); } catch (IOException ex) { Dialogs.Error(ex.Message); }
        RefreshLibrary();
    }

    private void AddCurrentAsSample()
    {
        var xml = SelectedXml;
        if (xml == null) return;
        var dataset = Dialogs.Prompt("Add as sample", "Dataset (folder) name:", SampleStore.DefaultDataset, Samples.GetDatasets());
        if (string.IsNullOrWhiteSpace(dataset)) return;
        var name = Dialogs.Prompt("Add as sample", "Sample name:", Path.GetFileNameWithoutExtension(xml.Title));
        if (string.IsNullOrWhiteSpace(name)) return;
        try
        {
            if (xml.IsLargeFile && xml.FilePath != null)
            {
                var copy = Samples.AddCopy(dataset, xml.FilePath);
                Status = $"Copied to {copy}";
            }
            else
            {
                var path = Samples.Add(dataset, name, xml.Text);
                Status = $"Saved sample {path}";
            }

            RefreshLibrary();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Dialogs.Error(ex.Message);
        }
    }

    private void AddSampleFiles()
    {
        var files = Dialogs.OpenFiles("Add sample XML files", Dialogs.XmlFilter);
        if (files == null) return;
        var dataset = Dialogs.Prompt("Add samples", "Dataset (folder) name:", SampleStore.DefaultDataset, Samples.GetDatasets());
        if (string.IsNullOrWhiteSpace(dataset)) return;
        try
        {
            foreach (var f in files) Samples.AddCopy(dataset, f);
            RefreshLibrary();
            Status = $"Added {files.Length} sample(s) to {dataset}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Dialogs.Error(ex.Message);
        }
    }

    private void NewDataset()
    {
        var name = Dialogs.Prompt("New dataset", "Dataset name:", "");
        if (string.IsNullOrWhiteSpace(name)) return;
        Samples.CreateDataset(name);
        RefreshLibrary();
        Status = $"Dataset “{name}” created";
    }

    private void NewSample(object? p)
    {
        var dataset = (p as SampleFile)?.Dataset
                      ?? Dialogs.Prompt("New sample", "Dataset (folder) name:", SampleStore.DefaultDataset, Samples.GetDatasets());
        if (string.IsNullOrWhiteSpace(dataset)) return;
        var name = Dialogs.Prompt("New sample", "Sample name:", "NewSample");
        if (string.IsNullOrWhiteSpace(name)) return;
        var path = Samples.Add(dataset, name, "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\r\n<root>\r\n  \r\n</root>\r\n");
        RefreshLibrary();
        OpenFile(DocKind.Xml, path);
    }

    private void DeleteSample(object? p)
    {
        if (p is not SampleFile s) return;
        if (!Dialogs.Confirm($"Delete sample “{s.Display}”?")) return;
        var open = XmlDocs.FirstOrDefault(d => string.Equals(d.FilePath, s.Path, StringComparison.OrdinalIgnoreCase));
        if (open != null && !CloseDocument(open)) return;
        try { Samples.Delete(s); } catch (IOException ex) { Dialogs.Error(ex.Message); }
        RefreshLibrary();
    }

    #endregion
}
