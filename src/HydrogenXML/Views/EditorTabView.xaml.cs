using System.ComponentModel;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Folding;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Rendering;
using ICSharpCode.AvalonEdit.Search;
using HydrogenXML.Editor;
using HydrogenXML.ViewModels;

namespace HydrogenXML.Views;

public partial class EditorTabView : UserControl
{
    private const int MaxFoldingChars = 20_000_000;
    private static readonly Regex OpenTag = new(@"<([A-Za-z_][\w.:-]*)(?:\s[^<>]*?)?(?<!/)>$", RegexOptions.Compiled);

    private readonly DispatcherTimer _foldTimer = new() { Interval = TimeSpan.FromMilliseconds(800) };
    private readonly List<EditorHost> _hosts = new();
    private DocumentViewModel? _doc;
    private bool _attached;
    private int _stamp;
    private bool _foldBusy;
    private TextEditor _focused = null!;

    public static event Action<TextEditor>? CaretMoved;

    private sealed class EditorHost
    {
        public required TextEditor Editor { get; init; }
        public FoldingManager? Folding { get; set; }
        public IconMargin? Icons { get; set; }
        public DiagnosticRenderer? Diagnostics { get; set; }
        public BookmarkRenderer? Bookmarks { get; set; }
        public XslTagColorizer? Colorizer { get; set; }
        public EventHandler? CaretHandler { get; set; }
    }

    public EditorTabView()
    {
        InitializeComponent();
        _focused = Editor1;
        _foldTimer.Tick += (_, _) =>
        {
            _foldTimer.Stop();
            UpdateFoldings();
        };
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
        PreviewMouseWheel += OnPreviewMouseWheel;
    }

    private void Attach()
    {
        if (_attached) return;
        _doc = DataContext as DocumentViewModel;
        if (_doc == null) return;
        _attached = true;

        foreach (var editor in new[] { Editor1, Editor2 })
        {
            var host = new EditorHost { Editor = editor };
            _hosts.Add(host);
            ConfigureEditor(host);
        }

        _doc.PropertyChanged += OnDocPropertyChanged;
        _doc.MarkersChanged += OnMarkersChanged;
        _doc.BookmarksChanged += OnMarkersChanged;
        _doc.NavigateRequested += OnNavigateRequested;
        _doc.Document.Changed += OnDocumentChanged;
        EditorSettings.Changed += ApplySettings;
        SyntaxTheme.Changed += ApplyHighlighting;

        ApplySettings();
        ApplyHighlighting();
        UpdateFoldings();

        if (_doc.PendingLine is { } line)
        {
            var col = _doc.PendingColumn;
            _doc.ClearPendingNavigation();
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => Navigate(line, col));
        }
        else
        {
            var caret = Math.Clamp(_doc.CaretOffset, 0, _doc.Document.TextLength);
            var scroll = _doc.VerticalOffset;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            {
                if (!_attached) return;
                Editor1.TextArea.Caret.Offset = caret;
                Editor1.ScrollToVerticalOffset(scroll);
            });
        }
    }

    private void Detach()
    {
        if (!_attached || _doc == null) return;
        _attached = false;
        _foldTimer.Stop();

        _doc.CaretOffset = Editor1.CaretOffset;
        _doc.VerticalOffset = Editor1.VerticalOffset;
        _doc.PropertyChanged -= OnDocPropertyChanged;
        _doc.MarkersChanged -= OnMarkersChanged;
        _doc.BookmarksChanged -= OnMarkersChanged;
        _doc.NavigateRequested -= OnNavigateRequested;
        _doc.Document.Changed -= OnDocumentChanged;
        EditorSettings.Changed -= ApplySettings;
        SyntaxTheme.Changed -= ApplyHighlighting;

        foreach (var host in _hosts)
        {
            var area = host.Editor.TextArea;
            if (host.Folding != null)
            {
                FoldingManager.Uninstall(host.Folding);
                EditorRegistry.SetFolding(host.Editor, null);
            }

            if (host.Icons != null) area.LeftMargins.Remove(host.Icons);
            if (host.Diagnostics != null) area.TextView.BackgroundRenderers.Remove(host.Diagnostics);
            if (host.Bookmarks != null) area.TextView.BackgroundRenderers.Remove(host.Bookmarks);
            if (host.Colorizer != null) area.TextView.LineTransformers.Remove(host.Colorizer);
            if (host.CaretHandler != null) area.Caret.PositionChanged -= host.CaretHandler;
            area.TextEntered -= OnTextEntered;
            area.GotKeyboardFocus -= OnEditorFocused;
            EditorRegistry.Remove(host.Editor);
            host.Editor.Document = new ICSharpCode.AvalonEdit.Document.TextDocument();
        }

        _hosts.Clear();
    }

    private void ConfigureEditor(EditorHost host)
    {
        var editor = host.Editor;
        var doc = _doc!;
        editor.Document = doc.Document;
        editor.IsReadOnly = doc.IsReadOnly;
        editor.Options.ConvertTabsToSpaces = true;
        editor.Options.IndentationSize = 2;
        editor.Options.HighlightCurrentLine = true;
        editor.Options.EnableEmailHyperlinks = false;
        editor.Options.EnableHyperlinks = false;
        editor.Options.AllowScrollBelowDocument = true;
        editor.Options.ShowSpaces = false;
        editor.TextArea.IndentationStrategy = new XmlIndentationStrategy();
        editor.TextArea.TextView.SetResourceReference(TextView.CurrentLineBackgroundProperty, "App.CurrentLine");
        editor.TextArea.TextView.CurrentLineBorder = new Pen(new SolidColorBrush(Color.FromArgb(40, 128, 128, 128)), 1);

        SearchPanel.Install(editor);

        host.Folding = FoldingManager.Install(editor.TextArea);
        EditorRegistry.SetFolding(editor, host.Folding);

        host.Diagnostics = new DiagnosticRenderer(doc);
        host.Bookmarks = new BookmarkRenderer(doc);
        editor.TextArea.TextView.BackgroundRenderers.Add(host.Diagnostics);
        editor.TextArea.TextView.BackgroundRenderers.Add(host.Bookmarks);

        host.Icons = new IconMargin(doc)
        {
            Clicked = line =>
            {
                if (doc.Kind == DocKind.Output) return;
                doc.ToggleBookmark(line);
            }
        };
        editor.TextArea.LeftMargins.Insert(0, host.Icons);

        if (doc.Kind == DocKind.Xsl)
        {
            host.Colorizer = new XslTagColorizer();
            editor.TextArea.TextView.LineTransformers.Add(host.Colorizer);
        }

        host.CaretHandler = (_, _) =>
        {
            if (editor == Editor1) doc.CaretOffset = editor.CaretOffset;
            CaretMoved?.Invoke(editor);
        };
        editor.TextArea.Caret.PositionChanged += host.CaretHandler;
        if (editor == Editor1)
        {
            editor.TextArea.TextView.ScrollOffsetChanged += (_, _) =>
            {
                if (_attached) doc.VerticalOffset = editor.VerticalOffset;
            };
        }
        editor.TextArea.TextEntered += OnTextEntered;
        editor.TextArea.GotKeyboardFocus += OnEditorFocused;
    }

    private void OnEditorFocused(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_doc == null) return;
        var editor = _hosts.Select(h => h.Editor).FirstOrDefault(ed => ReferenceEquals(ed.TextArea, sender));
        if (editor == null) return;
        _focused = editor;
        EditorRegistry.SetActive(editor, _doc);
        App.ViewModel.ActiveDocument = _doc;
        CaretMoved?.Invoke(editor);
    }

    private void ApplySettings()
    {
        if (_doc == null) return;
        var split = EditorSettings.Split && _doc.Kind != DocKind.Output;
        SplitterRow.Height = split ? new GridLength(4) : new GridLength(0);
        SecondRow.Height = split ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        Splitter.Visibility = split ? Visibility.Visible : Visibility.Collapsed;
        Editor2.Visibility = split ? Visibility.Visible : Visibility.Collapsed;

        foreach (var host in _hosts)
        {
            host.Editor.FontSize = EditorSettings.FontSize;
            host.Editor.WordWrap = _doc.Kind == DocKind.Output && EditorSettings.OutputWordWrap;
        }
    }

    private void ApplyHighlighting()
    {
        var def = HighlightingManager.Instance.GetDefinition("XML");
        foreach (var host in _hosts)
        {
            host.Editor.SyntaxHighlighting = null;
            host.Editor.SyntaxHighlighting = def;
            host.Editor.TextArea.TextView.Redraw();
        }
    }

    private void OnDocPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DocumentViewModel.IsReadOnly) && _doc != null)
            foreach (var h in _hosts) h.Editor.IsReadOnly = _doc.IsReadOnly;
    }

    private void OnMarkersChanged(object? sender, EventArgs e)
    {
        foreach (var host in _hosts)
        {
            host.Editor.TextArea.TextView.Redraw();
            host.Icons?.Refresh();
        }
    }

    private void OnDocumentChanged(object? sender, ICSharpCode.AvalonEdit.Document.DocumentChangeEventArgs e)
    {
        _stamp++;
        _foldTimer.Stop();
        _foldTimer.Start();
    }

    private async void UpdateFoldings()
    {
        if (_doc == null || !_attached || _foldBusy) return;
        if (_doc.Document.TextLength > MaxFoldingChars) return;
        _foldBusy = true;
        var stamp = _stamp;
        try
        {
            var snapshot = _doc.Document.CreateSnapshot();
            var result = await Task.Run(() =>
            {
                try
                {
                    var strategy = new XmlFoldingStrategy { ShowAttributesWhenFolded = true };
                    var copy = new ICSharpCode.AvalonEdit.Document.TextDocument(snapshot);
                    var foldings = strategy.CreateNewFoldings(copy, out var firstError).ToList();
                    return (Foldings: foldings, FirstError: firstError);
                }
                catch (Exception)
                {
                    return (Foldings: new List<NewFolding>(), FirstError: 0);
                }
            });

            if (!_attached || stamp != _stamp) return;
            foreach (var host in _hosts)
                host.Folding?.UpdateFoldings(result.Foldings, result.FirstError);
        }
        finally
        {
            _foldBusy = false;
        }

        // Edits made while the parse was running need another pass.
        if (_attached && stamp != _stamp)
        {
            _foldTimer.Stop();
            _foldTimer.Start();
        }
    }

    private void OnNavigateRequested(int line, int column)
    {
        _doc?.ClearPendingNavigation();
        Navigate(line, column);
    }

    private void Navigate(int line, int column)
    {
        if (_doc == null || !_attached) return;
        var editor = _focused;
        var document = editor.Document;
        line = Math.Clamp(line, 1, document.LineCount);
        var dl = document.GetLineByNumber(line);
        column = Math.Clamp(column, 1, dl.Length + 1);
        var offset = dl.Offset + column - 1;

        var host = _hosts.FirstOrDefault(h => h.Editor == editor);
        if (host?.Folding != null)
            foreach (var section in host.Folding.GetFoldingsContaining(offset).ToList())
                section.IsFolded = false;

        editor.TextArea.Caret.Offset = offset;
        editor.ScrollTo(line, column);
        editor.TextArea.Caret.BringCaretToView();
        editor.Focus();
        editor.TextArea.Focus();
    }

    private void OnTextEntered(object sender, TextCompositionEventArgs e)
    {
        if (e.Text != ">" || _doc == null || _doc.IsReadOnly || _doc.Kind == DocKind.Output) return;
        if (sender is not ICSharpCode.AvalonEdit.Editing.TextArea area) return;
        var document = area.Document;
        var caret = area.Caret.Offset;
        var line = document.GetLineByOffset(caret);
        var before = document.GetText(line.Offset, caret - line.Offset);
        var m = OpenTag.Match(before);
        if (!m.Success) return;

        var closing = "</" + m.Groups[1].Value + ">";
        var after = document.GetText(caret, Math.Min(closing.Length, document.TextLength - caret));
        if (after == closing) return;
        document.Insert(caret, closing);
        area.Caret.Offset = caret;
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        EditorSettings.FontSize += e.Delta > 0 ? 1 : -1;
        e.Handled = true;
    }

    #region Context menu

    private TextEditor? MenuEditor(object sender)
    {
        if (sender is MenuItem { Parent: ContextMenu { PlacementTarget: TextEditor te } }) return te;
        return _focused;
    }

    private void Menu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu { PlacementTarget: TextEditor te }) return;
        _focused = te;
        te.Focus();
    }

    private void Cut_Click(object sender, RoutedEventArgs e) => MenuEditor(sender)?.Cut();
    private void Copy_Click(object sender, RoutedEventArgs e) => MenuEditor(sender)?.Copy();
    private void Paste_Click(object sender, RoutedEventArgs e) => MenuEditor(sender)?.Paste();
    private void SelectAll_Click(object sender, RoutedEventArgs e) => MenuEditor(sender)?.SelectAll();
    private void Undo_Click(object sender, RoutedEventArgs e) => MenuEditor(sender)?.Undo();
    private void Redo_Click(object sender, RoutedEventArgs e) => MenuEditor(sender)?.Redo();

    private void Format_Click(object sender, RoutedEventArgs e)
    {
        if (_doc == null) return;
        App.ViewModel.ActiveDocument = _doc;
        if (_doc.Kind == DocKind.Output) App.ViewModel.FormatOutputCommand.Execute(null);
        else App.ViewModel.FormatCommand.Execute(null);
    }

    private void Bookmark_Click(object sender, RoutedEventArgs e)
    {
        if (_doc == null || _doc.Kind == DocKind.Output) return;
        _doc.ToggleBookmark(MenuEditor(sender)?.TextArea.Caret.Line ?? 1);
    }

    private void FoldAll_Click(object sender, RoutedEventArgs e) => SetFolds(MenuEditor(sender), true);
    private void UnfoldAll_Click(object sender, RoutedEventArgs e) => SetFolds(MenuEditor(sender), false);

    private static void SetFolds(TextEditor? editor, bool folded)
    {
        if (editor == null) return;
        var fm = EditorRegistry.GetFolding(editor);
        if (fm == null) return;
        var all = fm.AllFoldings.ToList();
        if (all.Count == 0) return;
        var outermost = all.Min(s => s.StartOffset);
        foreach (var section in all)
        {
            // Keep the root element visible so a collapsed document still shows its structure.
            if (folded && section.StartOffset == outermost) continue;
            section.IsFolded = folded;
        }
    }

    #endregion
}
