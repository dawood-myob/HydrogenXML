using HydrogenXML.Core.Models;
using Saxon.Api;

namespace HydrogenXML.Core.Engine;

public sealed class XPathItemInfo
{
    public string Kind { get; init; } = "";
    public string Name { get; init; } = "";
    public string Value { get; init; } = "";
    public int Line { get; init; }
    public int Column { get; init; }
    public int Index { get; init; }
}

public sealed class XPathEvaluation
{
    public List<XPathItemInfo> Items { get; } = new();
    public int TotalCount { get; set; }
    public Diagnostic? Error { get; set; }
    public TimeSpan Elapsed { get; set; }
}

/// <summary>Evaluates XPath expressions against the current XML source, caching the parsed document between calls.</summary>
public sealed class XPathEvaluator
{
    public const int MaxItems = 1000;
    private const int MaxValueLength = 400;

    private readonly object _gate = new();
    private XdmNode? _cached;
    private string? _cacheKey;

    public Task<XPathEvaluation> EvaluateAsync(string expression, string? xmlText, string? xmlPath, string sourceName) =>
        Task.Run(() => Evaluate(expression, xmlText, xmlPath, sourceName));

    public XPathEvaluation Evaluate(string expression, string? xmlText, string? xmlPath, string sourceName)
    {
        var result = new XPathEvaluation();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        if (string.IsNullOrWhiteSpace(expression))
        {
            result.Error = Err("Enter an XPath expression.", sourceName, DiagnosticSeverity.Info);
            return result;
        }

        try
        {
            XdmNode doc;
            lock (_gate)
                doc = GetDocument(xmlText, xmlPath);

            var compiler = XsltEngine.Processor.NewXPathCompiler();
            compiler.BaseUri = doc.BaseUri;
            compiler.UnprefixedElementMatchingPolicy = UnprefixedElementMatchingPolicy.AnyNamespace;
            var declared = new HashSet<string>(StringComparer.Ordinal);
            var root = doc.Children().FirstOrDefault(n => n.NodeType == System.Xml.XmlNodeType.Element);
            if (root != null)
            {
                var ns = root.EnumerateAxis(XdmAxis.Namespace);
                while (ns.MoveNext())
                {
                    var prefix = ns.Current.NodeName?.LocalName ?? "";
                    if (prefix.Length == 0 || prefix == "xml" || !declared.Add(prefix)) continue;
                    compiler.DeclareNamespace(prefix, ns.Current.StringValue);
                }
            }

            if (declared.Add("wd"))
                compiler.DeclareNamespace("wd", "urn:com.workday/bsvc");

            var selector = compiler.Compile(expression).Load();
            selector.ContextItem = doc;
            var value = selector.Evaluate();
            result.TotalCount = value.Count;
            var i = 0;
            foreach (var item in value)
            {
                if (i >= MaxItems) break;
                result.Items.Add(Describe(item, ++i));
            }
        }
        catch (SaxonApiException ex)
        {
            result.Error = new Diagnostic
            {
                Category = DiagnosticCategory.XPath,
                File = sourceName,
                Line = 0,
                Message = ex.Message,
                Code = ex.ErrorCode?.LocalName
            };
        }
        catch (Exception ex)
        {
            result.Error = Err(ex.Message, sourceName, DiagnosticSeverity.Error);
        }

        result.Elapsed = sw.Elapsed;
        return result;
    }

    private static Diagnostic Err(string message, string file, DiagnosticSeverity severity) => new()
    {
        Severity = severity,
        Category = DiagnosticCategory.XPath,
        File = file,
        Message = message
    };

    private XdmNode GetDocument(string? text, string? path)
    {
        var key = text != null
            ? $"t:{text.Length}:{text.GetHashCode()}"
            : $"p:{path}:{(path != null && File.Exists(path) ? File.GetLastWriteTimeUtc(path).Ticks : 0)}";
        if (_cached != null && key == _cacheKey) return _cached;

        var builder = XsltEngine.Processor.NewDocumentBuilder();
        builder.LineNumbering = true;
        XdmNode doc;
        if (text != null)
        {
            if (path != null) builder.BaseUri = new Uri(Path.GetFullPath(path));
            doc = builder.Build(new StringReader(text));
        }
        else if (path != null)
        {
            builder.BaseUri = new Uri(Path.GetFullPath(path));
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 20);
            doc = builder.Build(fs);
        }
        else
        {
            throw new InvalidOperationException("No source XML is loaded.");
        }

        _cached = doc;
        _cacheKey = key;
        return doc;
    }

    private static XPathItemInfo Describe(XdmItem item, int index)
    {
        if (item is XdmNode node)
        {
            string kind = node.NodeKind.ToString();
            string name = node.NodeName?.ToString() ?? "";
            string value = node.NodeType is System.Xml.XmlNodeType.Element or System.Xml.XmlNodeType.Document
                ? node.OuterXml
                : node.StringValue;
            return new XPathItemInfo
            {
                Index = index,
                Kind = kind,
                Name = name,
                Value = Trim(value),
                Line = node.LineNumber,
                Column = node.ColumnNumber
            };
        }

        if (item is XdmAtomicValue atomic)
            return new XPathItemInfo { Index = index, Kind = atomic.PrimitiveTypeName?.LocalName ?? "atomic", Value = Trim(atomic.ToString()) };

        return new XPathItemInfo { Index = index, Kind = "item", Value = Trim(item.ToString()) };
    }

    private static string Trim(string s)
    {
        s = s.Trim();
        return s.Length <= MaxValueLength ? s : s[..MaxValueLength] + "…";
    }
}
