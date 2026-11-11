using System.Diagnostics;
using HydrogenXML.Core.Models;
using Saxon.Api;

namespace HydrogenXML.Core.Engine;

public sealed class TransformRequest
{
    public string StylesheetText { get; init; } = "";
    public string? StylesheetPath { get; init; }
    public string StylesheetName { get; init; } = "stylesheet.xsl";

    /// <summary>Source XML text. When null, <see cref="SourcePath"/> is streamed from disk (used for very large files).</summary>
    public string? SourceText { get; init; }
    public string? SourcePath { get; init; }
    public string SourceName { get; init; } = "input.xml";

    public IReadOnlyDictionary<string, string> Parameters { get; init; } = new Dictionary<string, string>();
    public string? InitialTemplate { get; init; }
    public string? InitialMode { get; init; }
    public bool Indent { get; init; }
    public string OutputDirectory { get; init; } = Path.Combine(Path.GetTempPath(), "HydrogenXML");
}

public sealed class TransformResult
{
    public bool Success { get; set; }
    public string? OutputFile { get; set; }
    public long OutputLength { get; set; }
    public List<Diagnostic> Diagnostics { get; } = new();
    public List<TransformMessage> Messages { get; } = new();
    public Dictionary<string, string> ResultDocuments { get; } = new();
    public TimeSpan CompileTime { get; set; }
    public TimeSpan TransformTime { get; set; }
}

public sealed class XsltEngine
{
    private static readonly Lazy<Processor> SharedProcessor = new(() => new Processor(false));

    public static Processor Processor => SharedProcessor.Value;

    /// <summary>Forces Saxon to load and JIT so the first real transform is fast.</summary>
    public static void WarmUp()
    {
        try
        {
            var compiler = Processor.NewXsltCompiler();
            var exe = compiler.Compile(new StringReader(
                "<xsl:stylesheet version=\"3.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\"><xsl:template match=\"/\"><r/></xsl:template></xsl:stylesheet>"));
            using var sw = new StringWriter();
            exe.Load30().ApplyTemplates(new StringReader("<a/>"), Processor.NewSerializer(sw));
        }
        catch
        {
            // warm-up is best effort
        }
    }

    public Task<TransformResult> TransformAsync(TransformRequest request, CancellationToken ct = default) =>
        Task.Run(() => Transform(request, ct), ct);

    public Task<List<Diagnostic>> CompileAsync(string text, string? path, string name) =>
        Task.Run(() => CheckStylesheet(text, path, name));

    /// <summary>Compiles a stylesheet only and returns its diagnostics.</summary>
    public List<Diagnostic> CheckStylesheet(string text, string? path, string name)
    {
        var diags = new List<Diagnostic>();
        TryCompile(text, path, name, diags, DiagnosticCategory.XslCompile, out _);
        return diags;
    }

    public TransformResult Transform(TransformRequest request, CancellationToken ct = default)
    {
        var result = new TransformResult();
        var sw = Stopwatch.StartNew();

        if (!TryCompile(request.StylesheetText, request.StylesheetPath, request.StylesheetName,
                result.Diagnostics, DiagnosticCategory.XslCompile, out var exe) || exe == null)
        {
            result.CompileTime = sw.Elapsed;
            return result;
        }

        result.CompileTime = sw.Elapsed;
        ct.ThrowIfCancellationRequested();

        if (request.SourceText == null && string.IsNullOrEmpty(request.SourcePath))
        {
            result.Diagnostics.Add(new Diagnostic
            {
                Category = DiagnosticCategory.Runtime,
                File = request.SourceName,
                Message = "No source XML is loaded."
            });
            return result;
        }

        Directory.CreateDirectory(request.OutputDirectory);
        var outputFile = Path.Combine(request.OutputDirectory, $"out-{Guid.NewGuid():N}.tmp");
        var openStreams = new List<Stream>();
        sw.Restart();
        try
        {
            var transformer = exe.Load30();
            transformer.ErrorReporter = e => AddError(result.Diagnostics, e, DiagnosticCategory.Warning,
                request.StylesheetName, DiagnosticCategory.Runtime);
            transformer.MessageListener = m => result.Messages.Add(new TransformMessage
            {
                Text = m.StringValue,
                File = FileOf(m.Location?.SystemId, request.StylesheetName),
                Line = m.Location?.LineNumber ?? 0,
                Column = m.Location?.ColumnNumber ?? 0,
                Terminate = m.Terminate
            });

            transformer.BaseOutputURI = new Uri(Path.GetFullPath(Path.Combine(request.OutputDirectory, "result.xml"))).AbsoluteUri;

            transformer.ResultDocumentHandler = (href, baseUri) =>
            {
                var file = Path.Combine(request.OutputDirectory, $"res-{Guid.NewGuid():N}.tmp");
                var fs = new FileStream(file, FileMode.Create, FileAccess.Write);
                openStreams.Add(fs);
                result.ResultDocuments[href] = file;
                var s = Processor.NewSerializer(fs);
                if (request.Indent) s.SetOutputProperty(Serializer.INDENT, "yes");
                return s;
            };

            if (request.Parameters.Count > 0)
            {
                var p = new Dictionary<QName, XdmValue>();
                foreach (var kv in request.Parameters)
                    p[new QName(kv.Key)] = new XdmAtomicValue(kv.Value);
                transformer.SetStylesheetParameters(p);
            }

            if (!string.IsNullOrWhiteSpace(request.InitialMode))
                transformer.InitialMode = new QName(request.InitialMode.Trim());

            using (var output = new FileStream(outputFile, FileMode.Create, FileAccess.Write, FileShare.Read, 81920))
            {
                var serializer = Processor.NewSerializer(output);
                if (request.Indent) serializer.SetOutputProperty(Serializer.INDENT, "yes");

                var baseUri = SourceUri(request);
                if (!string.IsNullOrWhiteSpace(request.InitialTemplate))
                {
                    var builder = Processor.NewDocumentBuilder();
                    builder.BaseUri = baseUri;
                    // SaxonCS does not expose the stylesheet's whitespace policy, so xsl:strip-space is not
                    // applied to the source when an initial template is used.
                    XdmNode doc;
                    if (request.SourceText != null)
                        doc = builder.Build(new StringReader(request.SourceText));
                    else
                        using (var fs = File.OpenRead(request.SourcePath!))
                            doc = builder.Build(fs);
                    transformer.GlobalContextItem = doc;
                    transformer.CallTemplate(new QName(request.InitialTemplate.Trim()), serializer);
                }
                else if (request.SourceText != null)
                {
                    transformer.ApplyTemplates(new StringReader(request.SourceText), baseUri, serializer);
                }
                else
                {
                    using var input = new FileStream(request.SourcePath!, FileMode.Open, FileAccess.Read,
                        FileShare.ReadWrite, 1 << 20);
                    transformer.ApplyTemplates(input, baseUri, serializer);
                }
            }

            result.TransformTime = sw.Elapsed;
            result.OutputFile = outputFile;
            result.OutputLength = new FileInfo(outputFile).Length;
            result.Success = !result.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (SaxonApiException ex)
        {
            AddException(result.Diagnostics, ex, DiagnosticCategory.Runtime, request.StylesheetName);
            var terminated = result.Messages.LastOrDefault(m => m.Terminate);
            if (terminated != null)
            {
                var last = result.Diagnostics.Last();
                result.Diagnostics[^1] = new Diagnostic
                {
                    Severity = last.Severity, Category = last.Category, File = last.File, Line = last.Line,
                    Column = last.Column, Code = last.Code,
                    Message = $"{last.Message} — {terminated.Text.Trim()}"
                };
            }

            result.TransformTime = sw.Elapsed;
        }
        catch (Exception ex)
        {
            result.Diagnostics.Add(new Diagnostic
            {
                Category = DiagnosticCategory.Runtime,
                File = request.SourceName,
                Message = ex.Message
            });
            result.TransformTime = sw.Elapsed;
        }
        finally
        {
            foreach (var s in openStreams)
            {
                try { s.Dispose(); } catch { }
            }
        }

        if (!result.Success && result.OutputFile != null && result.OutputLength == 0)
        {
            TryDelete(outputFile);
            result.OutputFile = null;
        }
        else if (!result.Success && result.OutputFile == null)
        {
            TryDelete(outputFile);
        }

        return result;
    }

    private static Uri SourceUri(TransformRequest r)
    {
        if (!string.IsNullOrEmpty(r.SourcePath))
            return new Uri(Path.GetFullPath(r.SourcePath));
        return new Uri(Path.Combine(Environment.CurrentDirectory, r.SourceName));
    }

    private static bool TryCompile(string text, string? path, string name, List<Diagnostic> diags,
        DiagnosticCategory category, out XsltExecutable? exe)
    {
        exe = null;
        var reported = new List<Diagnostic>();
        try
        {
            var compiler = Processor.NewXsltCompiler();
            compiler.BaseUri = path != null
                ? new Uri(Path.GetFullPath(path))
                : new Uri(Path.Combine(Environment.CurrentDirectory, name));
            compiler.ErrorReporter = e => AddError(reported, e, DiagnosticCategory.Warning, name, category);
            exe = compiler.Compile(new StringReader(text));
            diags.AddRange(reported);
            return !reported.Any(d => d.Severity == DiagnosticSeverity.Error);
        }
        catch (SaxonApiException ex)
        {
            exe = null;
            diags.AddRange(reported);
            // The reporter normally captured the detail; only add the exception when it did not.
            if (!reported.Any(d => d.Severity == DiagnosticSeverity.Error))
                AddException(diags, ex, category, name);
            return false;
        }
        catch (Exception ex)
        {
            exe = null;
            diags.AddRange(reported);
            diags.Add(new Diagnostic { Category = category, File = name, Message = ex.Message });
            return false;
        }
    }

    private static void AddError(List<Diagnostic> list, Error e, DiagnosticCategory warningCategory, string defaultFile,
        DiagnosticCategory errorCategory)
    {
        var d = new Diagnostic
        {
            Severity = e.IsWarning ? DiagnosticSeverity.Warning : DiagnosticSeverity.Error,
            Category = e.IsWarning ? warningCategory : errorCategory,
            File = FileOf(e.Location?.SystemId, defaultFile),
            Line = e.Location?.LineNumber ?? 0,
            Column = e.Location?.ColumnNumber ?? 0,
            Message = e.Message ?? "",
            Code = e.ErrorCode?.LocalName
        };
        if (!list.Any(x => x.Message == d.Message && x.Line == d.Line && x.File == d.File))
            list.Add(d);
    }

    private static void AddException(List<Diagnostic> list, SaxonApiException ex, DiagnosticCategory category,
        string defaultFile)
    {
        var d = new Diagnostic
        {
            Severity = ex.IsWarning ? DiagnosticSeverity.Warning : DiagnosticSeverity.Error,
            Category = category,
            File = FileOf(ex.ModuleUri, defaultFile),
            Line = ex.LineNumber,
            Column = ex.ColumnNumber,
            Message = ex.Message ?? ex.ToString(),
            Code = ex.ErrorCode?.LocalName
        };
        if (!list.Any(x => x.Message == d.Message && x.Line == d.Line && x.File == d.File))
            list.Add(d);
    }

    private static string FileOf(Uri? uri, string fallback)
    {
        if (uri == null) return fallback;
        return uri.IsFile ? uri.LocalPath : uri.ToString();
    }

    private static string FileOf(string? uri, string fallback)
    {
        if (string.IsNullOrEmpty(uri)) return fallback;
        return Uri.TryCreate(uri, UriKind.Absolute, out var u) ? FileOf(u, fallback) : uri;
    }

    private static void TryDelete(string file)
    {
        try { File.Delete(file); } catch { }
    }
}
