using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using ICSharpCode.AvalonEdit.Document;
using HydrogenXML.Core.Models;
using HydrogenXML.Core.Workspace;
using HydrogenXML.Infrastructure;

namespace HydrogenXML.ViewModels;

public enum DocKind
{
    Xsl,
    Xml,
    Output
}

public sealed class DocumentViewModel : ObservableObject
{
    private static int _untitledCounter;

    private string? _filePath;
    private string _title = "";
    private bool _isDirty;
    private bool _isReadOnly;
    private string? _banner;
    private bool _suppressDirty;
    private bool _isLargeFile;
    private long _fileSize;
    private readonly List<TextAnchor> _bookmarks = new();

    public DocumentViewModel(DocKind kind)
    {
        Kind = kind;
        Document.Changed += (_, _) =>
        {
            if (!_suppressDirty && !IsReadOnly && !IsDirty) IsDirty = true;
            TextEdited?.Invoke(this, EventArgs.Empty);
        };
    }

    public DocKind Kind { get; }
    public TextDocument Document { get; } = new();

    public string? FilePath
    {
        get => _filePath;
        private set
        {
            if (Set(ref _filePath, value))
            {
                OnPropertyChanged(nameof(FullTitle));
            }
        }
    }

    public string Title
    {
        get => _title;
        private set
        {
            if (Set(ref _title, value)) OnPropertyChanged(nameof(Header));
        }
    }

    public bool IsDirty
    {
        get => _isDirty;
        private set
        {
            if (Set(ref _isDirty, value)) OnPropertyChanged(nameof(Header));
        }
    }

    public bool IsReadOnly
    {
        get => _isReadOnly;
        set => Set(ref _isReadOnly, value);
    }

    /// <summary>Message shown above the editor, e.g. for previewed large files.</summary>
    public string? Banner
    {
        get => _banner;
        set
        {
            if (Set(ref _banner, value)) OnPropertyChanged(nameof(HasBanner));
        }
    }

    public bool HasBanner => !string.IsNullOrEmpty(_banner);

    public bool IsLargeFile
    {
        get => _isLargeFile;
        private set => Set(ref _isLargeFile, value);
    }

    public long FileSize
    {
        get => _fileSize;
        private set => Set(ref _fileSize, value);
    }

    public Encoding Encoding { get; set; } = new UTF8Encoding(false);
    public DateTime LastWriteUtc { get; private set; }
    public DateTime LastSelfWriteUtc { get; private set; }

    public string Header => IsDirty ? Title + " ●" : Title;
    public string FullTitle => FilePath ?? Title;

    /// <summary>Caret and scroll position remembered while the tab is not shown.</summary>
    public int CaretOffset { get; set; }
    public double VerticalOffset { get; set; }

    public List<Diagnostic> ValidationDiagnostics { get; private set; } = new();
    public List<Diagnostic> TransformDiagnostics { get; private set; } = new();
    public IEnumerable<Diagnostic> Markers => ValidationDiagnostics.Concat(TransformDiagnostics);

    public IReadOnlyList<TextAnchor> Bookmarks => _bookmarks;

    public event EventHandler? TextEdited;
    public event EventHandler? MarkersChanged;
    public event EventHandler? BookmarksChanged;
    public event Action<int, int>? NavigateRequested;

    public int? PendingLine { get; private set; }
    public int PendingColumn { get; private set; }

    public string Text => Document.Text;

    public static DocumentViewModel CreateNew(DocKind kind, string? text = null)
    {
        var doc = new DocumentViewModel(kind);
        var ext = kind == DocKind.Xsl ? "xsl" : "xml";
        doc.Title = $"Untitled-{++_untitledCounter}.{ext}";
        doc.SetText(text ?? DefaultText(kind), markClean: false);
        doc.IsDirty = false;
        return doc;
    }

    private static string DefaultText(DocKind kind) => kind switch
    {
        DocKind.Xsl => TemplateLibrary.BuiltIn.First(t => t.Name == "XSLT 3.0 starter").Content,
        DocKind.Xml => "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\r\n<root>\r\n  \r\n</root>\r\n",
        _ => ""
    };

    public static DocumentViewModel FromFile(DocKind kind, string path)
    {
        var doc = new DocumentViewModel(kind);
        doc.LoadFromDisk(path);
        return doc;
    }

    public void LoadFromDisk(string path)
    {
        var loaded = FileService.Read(path, allowPreview: Kind == DocKind.Xml);
        FilePath = Path.GetFullPath(path);
        Title = Path.GetFileName(path);
        Encoding = loaded.Encoding;
        FileSize = loaded.TotalBytes;
        IsLargeFile = loaded.IsPreview;
        IsReadOnly = loaded.IsPreview;
        Banner = loaded.IsPreview
            ? $"Large file ({FileService.FormatSize(loaded.TotalBytes)}): showing the first {FileService.FormatSize(loaded.Text.Length)} read-only. Transformations and XPath use the complete file on disk."
            : null;
        SetText(loaded.Text, markClean: true);
        LastWriteUtc = File.GetLastWriteTimeUtc(path);
        IsDirty = false;
    }

    public void Reload()
    {
        if (FilePath == null) return;
        LoadFromDisk(FilePath);
    }

    /// <summary>Replaces the whole text without marking the document modified and clears undo history.</summary>
    public void SetText(string text, bool markClean)
    {
        _suppressDirty = true;
        try
        {
            Document.Text = text;
            Document.UndoStack.ClearAll();
        }
        finally
        {
            _suppressDirty = false;
        }

        if (markClean) IsDirty = false;
    }

    /// <summary>Replaces the text as a single undoable edit.</summary>
    public void ReplaceText(string text)
    {
        Document.Replace(0, Document.TextLength, text);
    }

    public void Save(string? path = null)
    {
        if (IsReadOnly) throw new InvalidOperationException("This document is a read-only preview and cannot be saved.");
        path ??= FilePath ?? throw new InvalidOperationException("No file path.");
        FileService.Write(path, Document.Text, Encoding);
        FilePath = Path.GetFullPath(path);
        Title = Path.GetFileName(path);
        LastWriteUtc = File.GetLastWriteTimeUtc(path);
        LastSelfWriteUtc = LastWriteUtc;
        IsDirty = false;
    }

    public void MarkSaved() => IsDirty = false;

    /// <summary>Restores the identity of a document that was saved to a path which no longer exists.</summary>
    public void Rename(string title, string? path)
    {
        Title = title;
        FilePath = path;
    }

    /// <summary>Records that the user chose to keep their version of a file that changed on disk.</summary>
    public void AcknowledgeDiskChange(DateTime stampUtc) => LastWriteUtc = stampUtc;

    public void SetValidation(List<Diagnostic> diagnostics)
    {
        ValidationDiagnostics = diagnostics;
        MarkersChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetTransformDiagnostics(List<Diagnostic> diagnostics)
    {
        TransformDiagnostics = diagnostics;
        MarkersChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RequestNavigate(int line, int column)
    {
        PendingLine = line;
        PendingColumn = column;
        NavigateRequested?.Invoke(line, column);
    }

    public void ClearPendingNavigation() => PendingLine = null;

    public bool ToggleBookmark(int line)
    {
        if (line < 1 || line > Document.LineCount) return false;
        PruneBookmarks();
        var existing = _bookmarks.FirstOrDefault(a => a.Line == line);
        bool added;
        if (existing != null)
        {
            _bookmarks.Remove(existing);
            added = false;
        }
        else
        {
            var anchor = Document.CreateAnchor(Document.GetLineByNumber(line).Offset);
            anchor.MovementType = AnchorMovementType.BeforeInsertion;
            _bookmarks.Add(anchor);
            added = true;
        }

        BookmarksChanged?.Invoke(this, EventArgs.Empty);
        return added;
    }

    public void PruneBookmarks()
    {
        _bookmarks.RemoveAll(a => a.IsDeleted);
    }

    public IReadOnlyList<int> BookmarkLines()
    {
        PruneBookmarks();
        return _bookmarks.Select(a => a.Line).Distinct().OrderBy(l => l).ToList();
    }

    public void ClearBookmarks()
    {
        _bookmarks.Clear();
        BookmarksChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool MatchesFile(string file)
    {
        if (string.IsNullOrEmpty(file)) return false;
        if (FilePath != null)
        {
            try
            {
                if (string.Equals(Path.GetFullPath(file), FilePath, StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch (ArgumentException) { }
        }

        return string.Equals(file, Title, StringComparison.OrdinalIgnoreCase)
            || (FilePath == null && string.Equals(SafeFileName(file), Title, StringComparison.OrdinalIgnoreCase));
    }

    private static string SafeFileName(string file)
    {
        try { return Path.GetFileName(file); }
        catch (ArgumentException) { return file; }
    }

    public int ValidationSeq { get; set; }
}

public sealed class BookmarkItem
{
    public DocumentViewModel Document { get; init; } = null!;
    public int Line { get; init; }
    public string Preview { get; init; } = "";
    public string DocumentTitle => Document.Title;
}

public sealed class DiagnosticRow
{
    public Diagnostic Diagnostic { get; }
    public DiagnosticRow(Diagnostic d) => Diagnostic = d;
    public string Icon => Diagnostic.Severity switch
    {
        DiagnosticSeverity.Error => "✖",
        DiagnosticSeverity.Warning => "⚠",
        _ => "ℹ"
    };
    public string Category => Diagnostic.Category switch
    {
        DiagnosticCategory.XslCompile => "XSL compile",
        DiagnosticCategory.XmlValidation => "XML validation",
        DiagnosticCategory.XslValidation => "XSL validation",
        DiagnosticCategory.Runtime => "Runtime",
        DiagnosticCategory.XPath => "XPath",
        _ => "Warning"
    };
    public string FileName => string.IsNullOrEmpty(Diagnostic.File) ? "" : Path.GetFileName(Diagnostic.File);
    public string FullFile => Diagnostic.File;
    public string Line => Diagnostic.Line > 0 ? Diagnostic.Line.ToString() : "";
    public string Column => Diagnostic.Column > 0 ? Diagnostic.Column.ToString() : "";
    public string Message => Diagnostic.Code != null ? $"[{Diagnostic.Code}] {Diagnostic.Message}" : Diagnostic.Message;
}

public sealed class XPathRow
{
    public int Index { get; init; }
    public string Kind { get; init; } = "";
    public string Name { get; init; } = "";
    public string Value { get; init; } = "";
    public int Line { get; init; }
    public int Column { get; init; }
    public string LineText => Line > 0 ? Line.ToString() : "";
}
