using System.Text;
using System.Xml;
using System.Xml.Linq;
using HydrogenXML.Core.Models;

namespace HydrogenXML.Core.Engine;

public static class XmlTools
{
    private const string XslNs = "http://www.w3.org/1999/XSL/Transform";

    private static XmlReaderSettings ReaderSettings() => new()
    {
        DtdProcessing = DtdProcessing.Parse,
        XmlResolver = null,
        IgnoreWhitespace = false,
        CheckCharacters = true
    };

    /// <summary>Checks that the text is a well-formed XML document and reports the position of the first error.</summary>
    public static List<Diagnostic> ValidateWellFormed(string text, string fileName, DiagnosticCategory category)
    {
        var result = new List<Diagnostic>();
        if (string.IsNullOrWhiteSpace(text))
        {
            result.Add(new Diagnostic
            {
                Category = category,
                File = fileName,
                Line = 1,
                Column = 1,
                Message = "The document is empty."
            });
            return result;
        }

        try
        {
            using var reader = XmlReader.Create(new StringReader(text), ReaderSettings());
            while (reader.Read()) { }
        }
        catch (XmlException ex)
        {
            result.Add(FromXmlException(ex, fileName, category));
        }

        return result;
    }

    /// <summary>Streams a file to check well-formedness; suited to very large documents.</summary>
    public static List<Diagnostic> ValidateWellFormedFile(string path, DiagnosticCategory category,
        CancellationToken ct = default)
    {
        var result = new List<Diagnostic>();
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 20);
            using var reader = XmlReader.Create(stream, ReaderSettings());
            while (reader.Read())
                ct.ThrowIfCancellationRequested();
        }
        catch (XmlException ex)
        {
            result.Add(FromXmlException(ex, path, category));
        }
        catch (IOException ex)
        {
            result.Add(new Diagnostic { Category = category, File = path, Message = ex.Message });
        }

        return result;
    }

    private static Diagnostic FromXmlException(XmlException ex, string file, DiagnosticCategory category)
    {
        var message = ex.Message;
        // XmlException appends "Line n, position m." to the message; the location is reported separately.
        var idx = message.LastIndexOf(" Line ", StringComparison.Ordinal);
        if (idx > 0) message = message[..idx];
        return new Diagnostic
        {
            Category = category,
            File = file,
            Line = ex.LineNumber,
            Column = ex.LinePosition,
            Message = message.Trim()
        };
    }

    /// <summary>Pretty-prints an XML document. Whitespace is preserved where it is significant.</summary>
    public static string Format(string xml, bool useTabs = false, int indentSize = 2)
    {
        var settings = ReaderSettings();
        XDocument doc;
        using (var reader = XmlReader.Create(new StringReader(xml), settings))
            doc = XDocument.Load(reader, LoadOptions.PreserveWhitespace);

        StripInsignificantWhitespace(doc.Root);

        var sb = new StringBuilder();
        if (doc.Declaration != null)
            sb.Append(doc.Declaration.ToString()).Append("\r\n");

        var writerSettings = new XmlWriterSettings
        {
            Indent = true,
            IndentChars = useTabs ? "\t" : new string(' ', indentSize),
            OmitXmlDeclaration = true,
            NewLineHandling = NewLineHandling.Replace,
            NewLineChars = "\r\n",
            NewLineOnAttributes = false,
            CheckCharacters = false
        };

        using (var sw = new StringWriter(sb))
        using (var writer = XmlWriter.Create(sw, writerSettings))
            doc.Save(writer);

        return sb.ToString();
    }

    private static void StripInsignificantWhitespace(XElement? element)
    {
        if (element == null) return;

        var preserve = element.Name == XName.Get("text", XslNs)
            || (string?)element.Attribute(XNamespace.Xml + "space") == "preserve";
        var hasText = element.Nodes().OfType<XText>().Any(t => t is not XCData && !string.IsNullOrWhiteSpace(t.Value)
            || t is XCData);

        if (!preserve && !hasText)
        {
            foreach (var ws in element.Nodes().OfType<XText>().Where(t => t is not XCData).ToList())
                ws.Remove();
        }

        if (preserve) return;
        foreach (var child in element.Elements().ToList())
            StripInsignificantWhitespace(child);
    }
}
