using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using ICSharpCode.AvalonEdit;
using HydrogenXML.Editor;

namespace HydrogenXML.Views;

public partial class FindReplaceWindow : Window
{
    public FindReplaceWindow()
    {
        InitializeComponent();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Hide();
        };
        Closing += (_, e) =>
        {
            // Keep the window alive so its settings survive between uses.
            if (!AllowClose)
            {
                e.Cancel = true;
                Hide();
            }
        };
    }

    public bool AllowClose { get; set; }

    public void ShowFor(string? seed, bool focusReplace = false)
    {
        if (!string.IsNullOrEmpty(seed) && !seed.Contains('\n')) FindBox.Text = seed;
        if (!IsVisible) Show();
        Activate();
        (focusReplace ? ReplaceBox : FindBox).Focus();
        FindBox.SelectAll();
        StatusText.Text = "";
    }

    private TextEditor? Target => EditorRegistry.Active;

    private Regex? BuildRegex(bool rightToLeft = false)
    {
        var pattern = FindBox.Text;
        if (string.IsNullOrEmpty(pattern))
        {
            StatusText.Text = "Enter text to find.";
            return null;
        }

        if (UseRegex.IsChecked != true) pattern = Regex.Escape(pattern);
        if (WholeWord.IsChecked == true) pattern = @"\b" + pattern + @"\b";
        var options = RegexOptions.CultureInvariant | RegexOptions.Multiline;
        if (MatchCase.IsChecked != true) options |= RegexOptions.IgnoreCase;
        if (rightToLeft) options |= RegexOptions.RightToLeft;
        try
        {
            return new Regex(pattern, options, TimeSpan.FromSeconds(5));
        }
        catch (ArgumentException ex)
        {
            StatusText.Text = "Invalid regular expression: " + ex.Message;
            return null;
        }
    }

    private bool Find(bool forward, bool wrap = true)
    {
        var editor = Target;
        if (editor == null)
        {
            StatusText.Text = "No active editor.";
            return false;
        }

        var regex = BuildRegex(!forward);
        if (regex == null) return false;
        var text = editor.Document.Text;
        Match m;
        try
        {
            if (forward)
            {
                var start = Math.Min(editor.SelectionStart + editor.SelectionLength, text.Length);
                m = regex.Match(text, start);
                if (!m.Success && wrap)
                {
                    m = regex.Match(text, 0);
                    if (m.Success) StatusText.Text = "Wrapped to the start of the document.";
                }
            }
            else
            {
                var end = editor.SelectionStart;
                m = regex.Match(text, 0, end);
                if (!m.Success && wrap)
                {
                    m = regex.Match(text);
                    if (m.Success) StatusText.Text = "Wrapped to the end of the document.";
                }
            }
        }
        catch (RegexMatchTimeoutException)
        {
            StatusText.Text = "The search timed out.";
            return false;
        }

        if (!m.Success)
        {
            StatusText.Text = "Not found.";
            return false;
        }

        if (!StatusText.Text.StartsWith("Wrapped", StringComparison.Ordinal)) StatusText.Text = "";
        editor.Select(m.Index, m.Length);
        editor.TextArea.Caret.BringCaretToView();
        editor.ScrollToLine(editor.TextArea.Caret.Line);
        return true;
    }

    private void FindNext_Click(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "";
        Find(true);
    }

    private void FindPrev_Click(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "";
        Find(false);
    }

    private void Replace_Click(object sender, RoutedEventArgs e)
    {
        var editor = Target;
        if (editor == null || EditorRegistry.ActiveDocument is { IsReadOnly: true })
        {
            StatusText.Text = "The active editor is read-only.";
            return;
        }

        var regex = BuildRegex();
        if (regex == null) return;
        if (editor.SelectionLength > 0)
        {
            var selected = editor.SelectedText;
            var m = regex.Match(selected);
            if (m.Success && m.Index == 0 && m.Length == selected.Length)
                editor.SelectedText = Expand(m);
        }

        Find(true);
    }

    private string Expand(Match m) =>
        UseRegex.IsChecked == true ? m.Result(ReplaceBox.Text) : ReplaceBox.Text;

    private void ReplaceAll_Click(object sender, RoutedEventArgs e)
    {
        var editor = Target;
        if (editor == null || EditorRegistry.ActiveDocument is { IsReadOnly: true })
        {
            StatusText.Text = "The active editor is read-only.";
            return;
        }

        var regex = BuildRegex();
        if (regex == null) return;
        var text = editor.Document.Text;
        MatchCollection matches;
        try { matches = regex.Matches(text); }
        catch (RegexMatchTimeoutException)
        {
            StatusText.Text = "The search timed out.";
            return;
        }

        if (matches.Count == 0)
        {
            StatusText.Text = "Not found.";
            return;
        }

        var count = matches.Count;
        var replacements = matches.Select(m => (m.Index, m.Length, Text: Expand(m))).ToList();
        editor.Document.BeginUpdate();
        try
        {
            for (var i = replacements.Count - 1; i >= 0; i--)
            {
                var r = replacements[i];
                editor.Document.Replace(r.Index, r.Length, r.Text);
            }
        }
        finally
        {
            editor.Document.EndUpdate();
        }

        StatusText.Text = $"Replaced {count} occurrence(s).";
    }

    private void Count_Click(object sender, RoutedEventArgs e)
    {
        var editor = Target;
        var regex = BuildRegex();
        if (editor == null || regex == null) return;
        try
        {
            StatusText.Text = $"{regex.Matches(editor.Document.Text).Count} match(es).";
        }
        catch (RegexMatchTimeoutException)
        {
            StatusText.Text = "The search timed out.";
        }
    }
}
