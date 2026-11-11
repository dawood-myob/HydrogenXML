using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Indentation;
using ICSharpCode.AvalonEdit.Rendering;
using HydrogenXML.Core.Models;
using HydrogenXML.ViewModels;

namespace HydrogenXML.Editor;

/// <summary>Settings shared by every editor instance.</summary>
public static class EditorSettings
{
    private static double _fontSize = 13;
    private static bool _outputWordWrap;
    private static bool _split;

    public static event Action? Changed;

    public static double FontSize
    {
        get => _fontSize;
        set
        {
            value = Math.Clamp(value, 8, 40);
            if (Math.Abs(value - _fontSize) < 0.01) return;
            _fontSize = value;
            Changed?.Invoke();
        }
    }

    public static bool Split
    {
        get => _split;
        set
        {
            if (_split == value) return;
            _split = value;
            Changed?.Invoke();
        }
    }

    public static bool OutputWordWrap
    {
        get => _outputWordWrap;
        set
        {
            if (_outputWordWrap == value) return;
            _outputWordWrap = value;
            Changed?.Invoke();
        }
    }
}

/// <summary>Tracks the editor that last had keyboard focus so global commands can act on it.</summary>
public static class EditorRegistry
{
    public static TextEditor? Active { get; private set; }
    public static DocumentViewModel? ActiveDocument { get; private set; }
    public static TextEditor? LastXsl { get; private set; }

    private static readonly Dictionary<TextEditor, ICSharpCode.AvalonEdit.Folding.FoldingManager> Folds = new();

    public static void SetFolding(TextEditor editor, ICSharpCode.AvalonEdit.Folding.FoldingManager? manager)
    {
        if (manager == null) Folds.Remove(editor);
        else Folds[editor] = manager;
    }

    public static ICSharpCode.AvalonEdit.Folding.FoldingManager? GetFolding(TextEditor editor) =>
        Folds.GetValueOrDefault(editor);

    public static void SetActive(TextEditor editor, DocumentViewModel doc)
    {
        Active = editor;
        ActiveDocument = doc;
        if (doc.Kind == DocKind.Xsl) LastXsl = editor;
    }

    public static void Remove(TextEditor editor)
    {
        if (Active == editor) Active = null;
        if (LastXsl == editor) LastXsl = null;
        Folds.Remove(editor);
    }
}

public static class SyntaxTheme
{
    public static bool IsDark { get; private set; } = true;
    public static event Action? Changed;

    public static Color XslTagColor => IsDark ? Color.FromRgb(0xC5, 0x86, 0xC0) : Color.FromRgb(0x8E, 0x24, 0xAA);

    public static void Apply(bool dark)
    {
        IsDark = dark;
        var def = HighlightingManager.Instance.GetDefinition("XML");
        if (def != null)
        {
            Set(def, "Comment", dark ? "#6A9955" : "#008000");
            Set(def, "CData", dark ? "#CE9178" : "#A31515");
            Set(def, "DocType", dark ? "#C586C0" : "#800080");
            Set(def, "XmlDeclaration", dark ? "#808080" : "#808080");
            Set(def, "XmlTag", dark ? "#569CD6" : "#0000FF");
            Set(def, "AttributeName", dark ? "#9CDCFE" : "#E50000");
            Set(def, "AttributeValue", dark ? "#CE9178" : "#0451A5");
            Set(def, "Entity", dark ? "#D7BA7D" : "#FF8C00");
            Set(def, "BrokenEntity", dark ? "#F44747" : "#FF0000");
        }

        Changed?.Invoke();
    }

    private static void Set(IHighlightingDefinition def, string name, string hex)
    {
        var c = def.GetNamedColor(name);
        if (c == null) return;
        var color = (Color)ColorConverter.ConvertFromString(hex);
        c.Foreground = new SimpleHighlightingBrush(color);
    }
}

/// <summary>Colours xsl: instructions differently from literal result elements.</summary>
public sealed class XslTagColorizer : DocumentColorizingTransformer
{
    private static readonly Regex Tag = new(@"</?xsl:[\w.-]+", RegexOptions.Compiled);

    protected override void ColorizeLine(DocumentLine line)
    {
        var text = CurrentContext.Document.GetText(line);
        if (text.IndexOf("xsl:", StringComparison.Ordinal) < 0) return;
        foreach (Match m in Tag.Matches(text))
        {
            var brush = new SolidColorBrush(SyntaxTheme.XslTagColor);
            brush.Freeze();
            ChangeLinePart(line.Offset + m.Index, line.Offset + m.Index + m.Length, e =>
            {
                e.TextRunProperties.SetForegroundBrush(brush);
                e.TextRunProperties.SetTypeface(new Typeface(e.TextRunProperties.Typeface.FontFamily, FontStyles.Normal,
                    FontWeights.SemiBold, FontStretches.Normal));
            });
        }
    }
}

/// <summary>Draws a line tint and a wavy underline for diagnostics.</summary>
public sealed class DiagnosticRenderer : IBackgroundRenderer
{
    private readonly DocumentViewModel _doc;

    public DiagnosticRenderer(DocumentViewModel doc) => _doc = doc;

    public KnownLayer Layer => KnownLayer.Selection;

    public void Draw(TextView textView, DrawingContext dc)
    {
        var markers = _doc.Markers.Where(m => m.Line > 0).ToList();
        if (markers.Count == 0 || textView.Document == null) return;
        var document = textView.Document;

        foreach (var m in markers.Where(m => m.Line <= document.LineCount))
        {
            var line = document.GetLineByNumber(m.Line);
            var isError = m.Severity == DiagnosticSeverity.Error;
            var tint = isError ? Brushes.Red : Brushes.Orange;
            var lineBrush = new SolidColorBrush(((SolidColorBrush)tint).Color) { Opacity = 0.12 };
            lineBrush.Freeze();

            var lineBuilder = new BackgroundGeometryBuilder { AlignToWholePixels = true };
            lineBuilder.AddSegment(textView, new TextSegment { StartOffset = line.Offset, EndOffset = line.EndOffset });
            var geo = lineBuilder.CreateGeometry();
            if (geo != null) dc.DrawGeometry(lineBrush, null, geo);

            var (start, end) = ErrorRange(document, line, m.Column);
            var segment = new TextSegment { StartOffset = start, EndOffset = end };
            var pen = new Pen(tint, 1);
            pen.Freeze();
            foreach (var r in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment))
                DrawSquiggle(dc, pen, r.Left, r.Right, r.Bottom - 1);
        }
    }

    private static (int start, int end) ErrorRange(TextDocument doc, DocumentLine line, int column)
    {
        var text = doc.GetText(line);
        if (text.Length == 0) return (line.Offset, line.Offset);
        var col = Math.Clamp(column - 1, 0, text.Length - 1);
        if (column <= 0)
        {
            var first = text.Length - text.TrimStart().Length;
            return (line.Offset + first, line.EndOffset);
        }

        var end = col + 1;
        while (end < text.Length && !char.IsWhiteSpace(text[end]) && text[end] is not ('<' or '>' or '"' or '\'' or '='))
            end++;
        return (line.Offset + col, line.Offset + end);
    }

    private static void DrawSquiggle(DrawingContext dc, Pen pen, double left, double right, double y)
    {
        if (right - left < 4) right = left + 4;
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(left, y + 1), false, false);
            var up = false;
            for (var x = left + 2; x <= right + 2; x += 2)
            {
                ctx.LineTo(new Point(x, y + (up ? 1 : -1) + 1), true, false);
                up = !up;
            }
        }

        geometry.Freeze();
        dc.DrawGeometry(null, pen, geometry);
    }
}

public sealed class BookmarkRenderer : IBackgroundRenderer
{
    private static readonly Brush LineBrush = Make();
    private readonly DocumentViewModel _doc;

    public BookmarkRenderer(DocumentViewModel doc) => _doc = doc;

    public KnownLayer Layer => KnownLayer.Background;

    private static Brush Make()
    {
        var b = new SolidColorBrush(Color.FromArgb(40, 0x30, 0x80, 0xFF));
        b.Freeze();
        return b;
    }

    public void Draw(TextView textView, DrawingContext dc)
    {
        var document = textView.Document;
        if (document == null) return;
        foreach (var l in _doc.BookmarkLines().Where(l => l <= document.LineCount))
        {
            var line = document.GetLineByNumber(l);
            var builder = new BackgroundGeometryBuilder { AlignToWholePixels = true };
            builder.AddSegment(textView, new TextSegment { StartOffset = line.Offset, EndOffset = line.EndOffset });
            var geo = builder.CreateGeometry();
            if (geo != null) dc.DrawGeometry(LineBrush, null, geo);
        }
    }
}

/// <summary>Gutter showing bookmarks and diagnostic icons; clicking toggles a bookmark.</summary>
public sealed class IconMargin : AbstractMargin
{
    private readonly DocumentViewModel _doc;

    public IconMargin(DocumentViewModel doc) => _doc = doc;

    public Action<int>? Clicked { get; set; }

    protected override void OnTextViewChanged(TextView oldTextView, TextView newTextView)
    {
        if (oldTextView != null) oldTextView.VisualLinesChanged -= Redraw;
        base.OnTextViewChanged(oldTextView!, newTextView);
        if (newTextView != null) newTextView.VisualLinesChanged += Redraw;
        InvalidateVisual();
    }

    private void Redraw(object? sender, EventArgs e) => InvalidateVisual();

    public void Refresh() => InvalidateVisual();

    protected override Size MeasureOverride(Size availableSize) => new(18, 0);

    protected override void OnRender(DrawingContext dc)
    {
        var tv = TextView;
        if (tv == null || !tv.VisualLinesValid) return;
        var bookmarks = new HashSet<int>(_doc.BookmarkLines());
        var errors = new HashSet<int>();
        var warnings = new HashSet<int>();
        foreach (var m in _doc.Markers.Where(m => m.Line > 0))
            (m.Severity == DiagnosticSeverity.Error ? errors : warnings).Add(m.Line);

        foreach (var vl in tv.VisualLines)
        {
            var lineNo = vl.FirstDocumentLine.LineNumber;
            var top = vl.GetTextLineVisualYPosition(vl.TextLines[0], VisualYPosition.LineTop) - tv.VerticalOffset;
            var h = vl.TextLines[0].Height;
            var center = new Point(9, top + h / 2);
            if (errors.Contains(lineNo))
                dc.DrawEllipse(Brushes.Crimson, null, center, 5, 5);
            else if (warnings.Contains(lineNo))
                dc.DrawEllipse(Brushes.DarkOrange, null, center, 5, 5);

            if (bookmarks.Contains(lineNo))
            {
                var g = new StreamGeometry();
                using (var ctx = g.Open())
                {
                    ctx.BeginFigure(new Point(2, top + 2), true, true);
                    ctx.LineTo(new Point(15, top + 2), true, false);
                    ctx.LineTo(new Point(15, top + h - 2), true, false);
                    ctx.LineTo(new Point(8.5, top + h - 5), true, false);
                    ctx.LineTo(new Point(2, top + h - 2), true, false);
                }

                g.Freeze();
                dc.DrawGeometry(Brushes.DodgerBlue, null, g);
            }
        }
    }

    protected override void OnMouseDown(System.Windows.Input.MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        var tv = TextView;
        if (tv == null || e.ChangedButton != System.Windows.Input.MouseButton.Left) return;
        var pos = e.GetPosition(tv);
        pos.X = 0;
        pos.Y += tv.VerticalOffset;
        var vl = tv.GetVisualLineFromVisualTop(pos.Y);
        if (vl != null) Clicked?.Invoke(vl.FirstDocumentLine.LineNumber);
        e.Handled = true;
    }
}

/// <summary>Keeps the previous line's indentation and indents after an opening tag.</summary>
public sealed class XmlIndentationStrategy : IIndentationStrategy
{
    private static readonly Regex LastTag = new(@"<[^<>]*>\s*$", RegexOptions.Compiled);

    public string Unit { get; set; } = "  ";

    public void IndentLine(TextDocument document, DocumentLine line)
    {
        var prev = line.PreviousLine;
        if (prev == null) return;

        var prevText = document.GetText(prev);
        var indent = LeadingWhitespace(prevText);
        var opens = EndsWithOpeningTag(prevText);
        var current = document.GetText(line);
        var currentTrim = current.TrimStart();
        var newIndent = indent;
        if (opens && !currentTrim.StartsWith("</", StringComparison.Ordinal)) newIndent = indent + Unit;
        else if (!opens && currentTrim.StartsWith("</", StringComparison.Ordinal) && indent.EndsWith(Unit, StringComparison.Ordinal))
            newIndent = indent[..^Unit.Length];

        var existing = LeadingWhitespace(current);
        if (existing != newIndent)
            document.Replace(line.Offset, existing.Length, newIndent);
    }

    public void IndentLines(TextDocument document, int beginLine, int endLine)
    {
        for (var i = beginLine; i <= endLine; i++)
            IndentLine(document, document.GetLineByNumber(i));
    }

    private static string LeadingWhitespace(string s)
    {
        var i = 0;
        while (i < s.Length && (s[i] == ' ' || s[i] == '\t')) i++;
        return s[..i];
    }

    private static bool EndsWithOpeningTag(string text)
    {
        var m = LastTag.Match(text);
        if (!m.Success) return false;
        var tag = m.Value.TrimEnd();
        if (tag.StartsWith("</") || tag.StartsWith("<?") || tag.StartsWith("<!") || tag.EndsWith("/>")) return false;
        return tag.Length > 2 && (char.IsLetter(tag[1]) || tag[1] == '_');
    }
}
