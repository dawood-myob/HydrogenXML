using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;
using HydrogenXML.Core.Workspace;
using HydrogenXML.Editor;
using HydrogenXML.Infrastructure;
using HydrogenXML.ViewModels;
using HydrogenXML.Views;

namespace HydrogenXML;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private FindReplaceWindow? _findWindow;

    public MainWindow(MainViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
        DataContext = vm;

        vm.BottomTabRequested += ShowBottomTab;
        vm.WorkspaceSwitched += () => ApplyLayout(false);
        EditorTabView.CaretMoved += OnCaretMoved;
        PreviewDragOver += OnPreviewDragOver;
        PreviewDrop += OnPreviewDrop;
        Closing += OnClosing;
        SidebarTabs.SelectionChanged += (_, e) =>
        {
            if (e.Source == SidebarTabs && SidebarTabs.SelectedIndex >= 0) vm.Layout.SidebarTabIndex = SidebarTabs.SelectedIndex;
        };
        BottomTabs.SelectionChanged += (_, e) =>
        {
            if (e.Source == BottomTabs && BottomTabs.SelectedIndex >= 0) vm.Layout.BottomTabIndex = BottomTabs.SelectedIndex;
        };

        RegisterKeys();
    }

    #region Key bindings

    private void Bind(Key key, ModifierKeys modifiers, Action action) =>
        InputBindings.Add(new KeyBinding(new RelayCommand(action), key, modifiers));

    private void Bind(Key key, ModifierKeys modifiers, ICommand command) =>
        InputBindings.Add(new KeyBinding(command, key, modifiers));

    private void RegisterKeys()
    {
        const ModifierKeys ctrl = ModifierKeys.Control;
        const ModifierKeys ctrlShift = ModifierKeys.Control | ModifierKeys.Shift;
        Bind(Key.F5, ModifierKeys.None, _vm.TransformCommand);
        Bind(Key.Enter, ctrl, _vm.TransformCommand);
        Bind(Key.S, ctrl, _vm.SaveCommand);
        Bind(Key.S, ctrlShift, _vm.SaveAsCommand);
        Bind(Key.S, ctrl | ModifierKeys.Alt, _vm.SaveAllCommand);
        Bind(Key.O, ctrl, _vm.OpenXslCommand);
        Bind(Key.O, ctrlShift, _vm.OpenXmlCommand);
        Bind(Key.N, ctrl, _vm.NewXslCommand);
        Bind(Key.N, ctrlShift, _vm.NewXmlCommand);
        Bind(Key.W, ctrl, _vm.CloseDocumentCommand);
        Bind(Key.R, ctrl, _vm.ReloadCommand);
        Bind(Key.F7, ModifierKeys.None, _vm.ValidateCommand);
        Bind(Key.F, ctrlShift, _vm.FormatCommand);
        Bind(Key.T, ctrl, _vm.ToggleThemeCommand);
        Bind(Key.H, ctrl, () => ShowFind(true));
        Bind(Key.G, ctrl, () => GoToLine_Click(this, new RoutedEventArgs()));
        Bind(Key.B, ctrl, () => ToggleSidebar_Click(this, new RoutedEventArgs()));
        Bind(Key.J, ctrl, () => ToggleBottom_Click(this, new RoutedEventArgs()));
        Bind(Key.X, ctrlShift, () => XPathConsole_Click(this, new RoutedEventArgs()));
        Bind(Key.F2, ctrl, () => ToggleBookmark_Click(this, new RoutedEventArgs()));
        Bind(Key.F2, ModifierKeys.None, () => StepBookmark(true));
        Bind(Key.F2, ModifierKeys.Shift, () => StepBookmark(false));
        Bind(Key.OemComma, ctrl, () => Settings_Click(this, new RoutedEventArgs()));
        Bind(Key.OemPlus, ctrl, () => EditorSettings.FontSize += 1);
        Bind(Key.Add, ctrl, () => EditorSettings.FontSize += 1);
        Bind(Key.OemMinus, ctrl, () => EditorSettings.FontSize -= 1);
        Bind(Key.Subtract, ctrl, () => EditorSettings.FontSize -= 1);
        Bind(Key.D0, ctrl, () => EditorSettings.FontSize = 13);
    }

    #endregion

    #region Layout

    public void ApplyLayout(bool includeWindow = true)
    {
        var l = _vm.Layout;
        if (includeWindow)
        {
            Width = Math.Max(MinWidth, l.Width);
            Height = Math.Max(MinHeight, l.Height);
            var area = SystemParameters.WorkArea;
            if (!double.IsNaN(l.Left) && !double.IsNaN(l.Top) &&
                l.Left > area.Left - Width + 100 && l.Left < area.Right - 100 &&
                l.Top >= area.Top - 10 && l.Top < area.Bottom - 100)
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = l.Left;
                Top = l.Top;
            }
            else
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            if (l.Maximized) WindowState = WindowState.Maximized;
        }

        EditorSettings.OutputWordWrap = l.OutputWordWrap;
        EditorSettings.Split = l.XslSplit;
        ApplyPanels();
        SidebarTabs.SelectedIndex = Math.Clamp(l.SidebarTabIndex, 0, SidebarTabs.Items.Count - 1);
        BottomTabs.SelectedIndex = Math.Clamp(l.BottomTabIndex, 0, BottomTabs.Items.Count - 1);
    }

    private void ApplyPanels()
    {
        var l = _vm.Layout;
        SidebarCol.Width = new GridLength(l.SidebarVisible ? Math.Max(120, l.SidebarWidth) : 0);
        SidebarSplitCol.Width = new GridLength(l.SidebarVisible ? 4 : 0);
        SidebarTabs.Visibility = SidebarSplitter.Visibility = l.SidebarVisible ? Visibility.Visible : Visibility.Collapsed;

        BottomRow.Height = new GridLength(l.BottomPanelVisible ? Math.Max(60, l.BottomPanelHeight) : 0);
        BottomSplitRow.Height = new GridLength(l.BottomPanelVisible ? 4 : 0);
        BottomTabs.Visibility = BottomSplitter.Visibility = l.BottomPanelVisible ? Visibility.Visible : Visibility.Collapsed;

        XmlCol.Width = l.XmlPaneVisible ? new GridLength(Math.Max(0.2, l.XmlPaneWeight), GridUnitType.Star) : new GridLength(0);
        Split1Col.Width = new GridLength(l.XmlPaneVisible ? 4 : 0);
        XmlPane.Visibility = Split1.Visibility = l.XmlPaneVisible ? Visibility.Visible : Visibility.Collapsed;

        XslCol.Width = new GridLength(Math.Max(0.2, l.XslPaneWeight), GridUnitType.Star);

        OutCol.Width = l.OutputPaneVisible ? new GridLength(Math.Max(0.2, l.OutputPaneWeight), GridUnitType.Star) : new GridLength(0);
        Split2Col.Width = new GridLength(l.OutputPaneVisible ? 4 : 0);
        OutputPane.Visibility = Split2.Visibility = l.OutputPaneVisible ? Visibility.Visible : Visibility.Collapsed;

        ViewSidebar.IsChecked = l.SidebarVisible;
        ViewBottom.IsChecked = l.BottomPanelVisible;
        ViewXml.IsChecked = l.XmlPaneVisible;
        ViewOutput.IsChecked = l.OutputPaneVisible;
        ViewSplit.IsChecked = l.XslSplit;
        ViewWrap.IsChecked = l.OutputWordWrap;
        WrapCheck.IsChecked = l.OutputWordWrap;
    }

    /// <summary>Copies the current pixel sizes of visible panels into the layout so they survive hiding and restarts.</summary>
    private void CapturePanelSizes()
    {
        var l = _vm.Layout;
        if (l.SidebarVisible && SidebarCol.ActualWidth > 20) l.SidebarWidth = SidebarCol.ActualWidth;
        if (l.BottomPanelVisible && BottomRow.ActualHeight > 20) l.BottomPanelHeight = BottomRow.ActualHeight;
        if (XslCol.ActualWidth > 20) l.XslPaneWeight = XslCol.ActualWidth / 400;
        if (l.XmlPaneVisible && XmlCol.ActualWidth > 20) l.XmlPaneWeight = XmlCol.ActualWidth / 400;
        if (l.OutputPaneVisible && OutCol.ActualWidth > 20) l.OutputPaneWeight = OutCol.ActualWidth / 400;
    }

    private void CaptureLayout()
    {
        var l = _vm.Layout;
        CapturePanelSizes();
        l.Maximized = WindowState == WindowState.Maximized;
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        if (!bounds.IsEmpty)
        {
            l.Left = bounds.Left;
            l.Top = bounds.Top;
            l.Width = bounds.Width;
            l.Height = bounds.Height;
        }

        l.OutputWordWrap = EditorSettings.OutputWordWrap;
        l.XslSplit = EditorSettings.Split;
        l.Theme = _vm.Theme;
    }

    private void ShowBottomTab(int index)
    {
        var l = _vm.Layout;
        if (!l.BottomPanelVisible)
        {
            CapturePanelSizes();
            l.BottomPanelVisible = true;
            ApplyPanels();
        }

        BottomTabs.SelectedIndex = Math.Clamp(index, 0, BottomTabs.Items.Count - 1);
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        CaptureLayout();
        _vm.SaveWorkspace();
        _vm.CleanupTemp();
        if (_findWindow != null)
        {
            _findWindow.AllowClose = true;
            _findWindow.Close();
            _findWindow = null;
        }
    }

    private void ToggleSidebar_Click(object sender, RoutedEventArgs e)
    {
        CapturePanelSizes();
        _vm.Layout.SidebarVisible = !_vm.Layout.SidebarVisible;
        ApplyPanels();
    }

    private void ToggleBottom_Click(object sender, RoutedEventArgs e)
    {
        CapturePanelSizes();
        _vm.Layout.BottomPanelVisible = !_vm.Layout.BottomPanelVisible;
        ApplyPanels();
    }

    private void ToggleXmlPane_Click(object sender, RoutedEventArgs e)
    {
        CapturePanelSizes();
        _vm.Layout.XmlPaneVisible = !_vm.Layout.XmlPaneVisible;
        ApplyPanels();
    }

    private void ToggleOutputPane_Click(object sender, RoutedEventArgs e)
    {
        CapturePanelSizes();
        _vm.Layout.OutputPaneVisible = !_vm.Layout.OutputPaneVisible;
        ApplyPanels();
    }

    private void ToggleSplit_Click(object sender, RoutedEventArgs e)
    {
        _vm.Layout.XslSplit = !_vm.Layout.XslSplit;
        EditorSettings.Split = _vm.Layout.XslSplit;
        ViewSplit.IsChecked = _vm.Layout.XslSplit;
    }

    private void ToggleWrap_Click(object sender, RoutedEventArgs e) => SetWrap(!_vm.Layout.OutputWordWrap);

    private void WrapCheck_Click(object sender, RoutedEventArgs e) => SetWrap(WrapCheck.IsChecked == true);

    private void SetWrap(bool wrap)
    {
        _vm.Layout.OutputWordWrap = wrap;
        EditorSettings.OutputWordWrap = wrap;
        ViewWrap.IsChecked = wrap;
        WrapCheck.IsChecked = wrap;
    }

    private void ResetLayout_Click(object sender, RoutedEventArgs e)
    {
        var d = new LayoutState();
        var l = _vm.Layout;
        l.XmlPaneWeight = d.XmlPaneWeight;
        l.XslPaneWeight = d.XslPaneWeight;
        l.OutputPaneWeight = d.OutputPaneWeight;
        l.SidebarVisible = d.SidebarVisible;
        l.SidebarWidth = d.SidebarWidth;
        l.BottomPanelVisible = d.BottomPanelVisible;
        l.BottomPanelHeight = d.BottomPanelHeight;
        l.XmlPaneVisible = d.XmlPaneVisible;
        l.OutputPaneVisible = d.OutputPaneVisible;
        ApplyPanels();
    }

    #endregion

    #region Status bar and drag/drop

    private void OnCaretMoved(TextEditor editor)
    {
        var caret = editor.TextArea.Caret;
        var text = $"Ln {caret.Line}, Col {caret.Column}";
        if (editor.SelectionLength > 0) text += $"  ({editor.SelectionLength:N0} selected)";
        CaretText.Text = text;
    }

    private static bool HasFiles(DragEventArgs e) => e.Data.GetDataPresent(DataFormats.FileDrop);

    private void OnPreviewDragOver(object sender, DragEventArgs e)
    {
        if (!HasFiles(e)) return;
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void OnPreviewDrop(object sender, DragEventArgs e)
    {
        if (!HasFiles(e) || e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;
        e.Handled = true;
        DocKind? forced = null;
        if (e.OriginalSource is DependencyObject source)
        {
            if (IsInside(XmlPane, source)) forced = DocKind.Xml;
            else if (IsInside(XslPane, source)) forced = DocKind.Xsl;
        }

        _vm.OpenDropped(files, forced);
    }

    private static bool IsInside(DependencyObject container, DependencyObject node)
    {
        for (var cur = node; cur != null; cur = GetParent(cur))
            if (ReferenceEquals(cur, container)) return true;
        return false;
    }

    private static DependencyObject? GetParent(DependencyObject d) =>
        d is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
            ? System.Windows.Media.VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d)
            : LogicalTreeHelper.GetParent(d);

    #endregion

    #region File / tab commands

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private static DocumentViewModel? DocOf(object sender) => (sender as FrameworkElement)?.DataContext as DocumentViewModel;

    private void TabSave_Click(object sender, RoutedEventArgs e) => _vm.SaveDocument(DocOf(sender));
    private void TabSaveAs_Click(object sender, RoutedEventArgs e) => _vm.SaveDocumentAs(DocOf(sender));

    private void TabReload_Click(object sender, RoutedEventArgs e)
    {
        if (DocOf(sender) is { FilePath: not null } doc)
        {
            _vm.ActiveDocument = doc;
            _vm.ReloadCommand.Execute(null);
        }
    }

    private void TabCopyPath_Click(object sender, RoutedEventArgs e)
    {
        if (DocOf(sender) is { } doc) Clipboard.SetText(doc.FullTitle);
    }

    private void TabExplorer_Click(object sender, RoutedEventArgs e) => _vm.OpenInExplorer(DocOf(sender));
    private void TabClose_Click(object sender, RoutedEventArgs e) => _vm.CloseDocument(DocOf(sender));

    private void TabHeader_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle) return;
        e.Handled = true;
        _vm.CloseDocument(DocOf(sender));
    }

    private void SaveXsl_Click(object sender, RoutedEventArgs e) => _vm.SaveDocument(_vm.SelectedXsl);

    private void FormatXsl_Click(object sender, RoutedEventArgs e)
    {
        _vm.ActiveDocument = _vm.SelectedXsl;
        _vm.FormatCommand.Execute(null);
    }

    private void FormatXml_Click(object sender, RoutedEventArgs e)
    {
        _vm.ActiveDocument = _vm.SelectedXml;
        _vm.FormatCommand.Execute(null);
    }

    #endregion

    #region Edit commands

    private static TextEditor? Editor => EditorRegistry.Active;

    private void Undo_Click(object sender, RoutedEventArgs e) => Editor?.Undo();
    private void Redo_Click(object sender, RoutedEventArgs e) => Editor?.Redo();
    private void Cut_Click(object sender, RoutedEventArgs e) => Editor?.Cut();
    private void Copy_Click(object sender, RoutedEventArgs e) => Editor?.Copy();
    private void Paste_Click(object sender, RoutedEventArgs e) => Editor?.Paste();
    private void SelectAll_Click(object sender, RoutedEventArgs e) => Editor?.SelectAll();

    private void Find_Click(object sender, RoutedEventArgs e)
    {
        var editor = Editor;
        if (editor == null) return;
        editor.Focus();
        ApplicationCommands.Find.Execute(null, editor.TextArea);
    }

    private void Replace_Click(object sender, RoutedEventArgs e) => ShowFind(true);

    private void ShowFind(bool replace)
    {
        if (_findWindow == null)
        {
            _findWindow = new FindReplaceWindow { Owner = this };
        }

        var editor = Editor;
        var seed = editor is { SelectionLength: > 0 } ? editor.SelectedText : null;
        _findWindow.ShowFor(seed, replace);
    }

    private void GoToLine_Click(object sender, RoutedEventArgs e)
    {
        var doc = EditorRegistry.ActiveDocument ?? _vm.CurrentDocument;
        if (doc == null) return;
        var answer = Dialogs.Prompt("Go to line", $"Line number (1 – {doc.Document.LineCount}):", "");
        if (int.TryParse(answer, out var line)) doc.RequestNavigate(line, 1);
    }

    private void ToggleBookmark_Click(object sender, RoutedEventArgs e)
    {
        var doc = EditorRegistry.ActiveDocument;
        if (doc == null || doc.Kind == DocKind.Output || Editor == null) return;
        doc.ToggleBookmark(Editor.TextArea.Caret.Line);
    }

    private void StepBookmark(bool forward)
    {
        var doc = EditorRegistry.ActiveDocument;
        if (doc == null || Editor == null) return;
        _vm.StepBookmark(doc, Editor.TextArea.Caret.Line, forward);
    }

    private void NextBookmark_Click(object sender, RoutedEventArgs e) => StepBookmark(true);
    private void PrevBookmark_Click(object sender, RoutedEventArgs e) => StepBookmark(false);

    private void ClearBookmarks_Click(object sender, RoutedEventArgs e) => EditorRegistry.ActiveDocument?.ClearBookmarks();

    private void FoldAll_Click(object sender, RoutedEventArgs e) => SetFolds(true);
    private void UnfoldAll_Click(object sender, RoutedEventArgs e) => SetFolds(false);

    private static void SetFolds(bool folded)
    {
        if (Editor == null) return;
        var fm = EditorRegistry.GetFolding(Editor);
        if (fm == null) return;
        var all = fm.AllFoldings.ToList();
        if (all.Count == 0) return;
        var outermost = all.Min(s => s.StartOffset);
        foreach (var section in all)
        {
            if (folded && section.StartOffset == outermost) continue;
            section.IsFolded = folded;
        }
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e) => EditorSettings.FontSize += 1;
    private void ZoomOut_Click(object sender, RoutedEventArgs e) => EditorSettings.FontSize -= 1;
    private void ZoomReset_Click(object sender, RoutedEventArgs e) => EditorSettings.FontSize = 13;

    #endregion

    #region Tools / dialogs

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new TransformSettingsWindow(_vm) { Owner = this };
        if (dlg.ShowDialog() == true && _vm.AutoTransform) _vm.ScheduleAutoTransform();
    }

    private void XPathConsole_Click(object sender, RoutedEventArgs e)
    {
        ShowBottomTab(2);
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () => XPathBox.Focus());
    }

    private void XPathBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        _vm.XPathRunCommand.Execute(null);
    }

    private void NewFromTemplateMenu_Click(object sender, RoutedEventArgs e)
    {
        var l = _vm.Layout;
        if (!l.SidebarVisible)
        {
            CapturePanelSizes();
            l.SidebarVisible = true;
            ApplyPanels();
        }

        SidebarTabs.SelectedIndex = 1;
    }

    private void About_Click(object sender, RoutedEventArgs e) =>
        Dialogs.Info("HydrogenXML — a free, Workday-focused XSLT workbench.\n\n" +
                     "Transformations run on Saxon-HE (XSLT 1.0 / 2.0 / 3.0), bundled with the application.\n" +
                     "Workspace data is stored in %APPDATA%\\HydrogenXML.", "About HydrogenXML");

    private void Shortcuts_Click(object sender, RoutedEventArgs e) =>
        Dialogs.Info(
            "F5 / Ctrl+Enter\tTransform\n" +
            "Ctrl+S / Ctrl+Shift+S\tSave / Save as\n" +
            "Ctrl+O / Ctrl+Shift+O\tOpen XSL / Open XML\n" +
            "Ctrl+N / Ctrl+Shift+N\tNew XSL / New XML\n" +
            "Ctrl+W\tClose tab\n" +
            "F7\tValidate current document\n" +
            "Ctrl+Shift+F\tFormat document\n" +
            "Ctrl+F / Ctrl+H\tFind / Find and replace\n" +
            "Ctrl+G\tGo to line\n" +
            "Ctrl+F2, F2, Shift+F2\tToggle / next / previous bookmark\n" +
            "Ctrl+Shift+X\tXPath console\n" +
            "Ctrl+B / Ctrl+J\tToggle sidebar / diagnostics panel\n" +
            "Ctrl+T\tToggle theme\n" +
            "Ctrl+wheel, Ctrl +/-/0\tZoom editors\n" +
            "Ctrl+,\tTransformation settings",
            "Keyboard shortcuts");

    #endregion

    #region Diagnostics, messages, XPath, bookmarks

    private void DiagRow_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListViewItem { DataContext: DiagnosticRow row })
            _vm.GoTo(row.FullFile, row.Diagnostic.Line, row.Diagnostic.Column);
    }

    private void MessageRow_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (MessagesList.SelectedItem is MessageRow { Message.Line: > 0 } row)
            _vm.GoTo(row.Message.File, row.Message.Line, row.Message.Column);
    }

    private void XPathRow_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (XPathList.SelectedItem is XPathRow row) _vm.GoToXPathRow(row);
    }

    private void Bookmark_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (BookmarksList.SelectedItem is BookmarkItem item) _vm.GoToBookmark(item);
    }

    #endregion

    #region Sidebar

    private static bool OnItem(ListBox list, MouseButtonEventArgs e) =>
        e.OriginalSource is DependencyObject d && ItemsControl.ContainerFromElement(list, d) is ListBoxItem;

    private void SamplesList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (OnItem(SamplesList, e) && SamplesList.SelectedItem is SampleFile s) _vm.OpenSampleCommand.Execute(s);
    }

    private void SampleOpen_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is SampleFile s) _vm.OpenSampleCommand.Execute(s);
    }

    private void SampleNew_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is SampleFile s) _vm.NewSampleCommand.Execute(s);
    }

    private void SampleDelete_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is SampleFile s) _vm.DeleteSampleCommand.Execute(s);
    }

    private void TemplatesList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (OnItem(TemplatesList, e) && TemplatesList.SelectedItem is TemplateInfo t) _vm.NewFromTemplateCommand.Execute(t);
    }

    private void TemplateNew_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is TemplateInfo t) _vm.NewFromTemplateCommand.Execute(t);
    }

    private void TemplateDelete_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is TemplateInfo { IsUser: true } t) _vm.DeleteTemplateCommand.Execute(t);
        else _vm.Status = "Built-in templates cannot be deleted.";
    }

    private void RecentList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBox list && OnItem(list, e) && list.SelectedItem is RecentFile r)
            _vm.OpenRecentCommand.Execute(r);
    }

    private void SnippetsList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (OnItem(SnippetsList, e) && SnippetsList.SelectedItem is SnippetInfo s) InsertSnippet(s);
    }

    private void InsertSnippet(SnippetInfo snippet)
    {
        var editor = Editor is { IsReadOnly: false } && EditorRegistry.ActiveDocument?.Kind != DocKind.Output
            ? Editor
            : EditorRegistry.LastXsl;
        if (editor == null || editor.IsReadOnly)
        {
            _vm.Status = "Click into an editor first, then insert the snippet.";
            return;
        }

        var document = editor.Document;
        var line = document.GetLineByOffset(editor.SelectionStart);
        var indentLength = 0;
        while (indentLength < line.Length && document.GetCharAt(line.Offset + indentLength) is ' ' or '\t') indentLength++;
        var indent = document.GetText(line.Offset, indentLength);

        var content = snippet.Content.Replace("\r\n", "\n");
        var marker = content.IndexOf("$0", StringComparison.Ordinal);
        var before = marker >= 0 ? content[..marker] : content;
        var after = marker >= 0 ? content[(marker + 2)..] : "";
        string Indent(string s) => s.Replace("\n", Environment.NewLine + indent);
        before = Indent(before);
        after = Indent(after);

        var start = editor.SelectionStart;
        document.Replace(start, editor.SelectionLength, before + after);
        editor.TextArea.Caret.Offset = start + before.Length;
        editor.Focus();
        editor.TextArea.Focus();
        editor.TextArea.Caret.BringCaretToView();
    }

    #endregion
}
