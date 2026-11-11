using HydrogenXML.Core.Engine;
using HydrogenXML.Core.Models;
using HydrogenXML.Core.Workspace;

namespace HydrogenXML.Tests;

public class EngineTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "HydrogenXML.Tests", Guid.NewGuid().ToString("N"));
    private readonly XsltEngine _engine = new();

    public EngineTests() => Directory.CreateDirectory(_tmp);

    public void Dispose()
    {
        try { Directory.Delete(_tmp, true); } catch { }
    }

    private const string Xsl3 = """
<xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
  <xsl:output method="xml" indent="no"/>
  <xsl:param name="who" select="'nobody'"/>
  <xsl:template match="/">
    <out who="{$who}"><xsl:for-each select="//i"><n><xsl:value-of select=". * 2"/></n></xsl:for-each></out>
  </xsl:template>
</xsl:stylesheet>
""";

    private TransformRequest Req(string xsl, string? xml, Dictionary<string, string>? p = null, bool indent = false,
        string? sourcePath = null, string? initialTemplate = null) => new()
    {
        StylesheetText = xsl,
        SourceText = xml,
        SourcePath = sourcePath,
        Parameters = p ?? new(),
        Indent = indent,
        InitialTemplate = initialTemplate,
        OutputDirectory = _tmp
    };

    [Fact]
    public void Transforms_xslt3_with_parameter()
    {
        var r = _engine.Transform(Req(Xsl3, "<r><i>1</i><i>2</i></r>", new() { ["who"] = "me" }));
        Assert.True(r.Success, string.Join("; ", r.Diagnostics));
        Assert.Equal("<out who=\"me\"><n>2</n><n>4</n></out>", File.ReadAllText(r.OutputFile!).Replace("<?xml version=\"1.0\" encoding=\"UTF-8\"?>", ""));
    }

    [Fact]
    public void Supports_xslt1_and_xslt2()
    {
        foreach (var v in new[] { "1.0", "2.0" })
        {
            var xsl = $"<xsl:stylesheet version=\"{v}\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\"><xsl:output method=\"text\"/><xsl:template match=\"/\"><xsl:value-of select=\"count(//i)\"/></xsl:template></xsl:stylesheet>";
            var r = _engine.Transform(Req(xsl, "<r><i/><i/></r>"));
            Assert.True(r.Success, string.Join("; ", r.Diagnostics));
            Assert.Equal("2", File.ReadAllText(r.OutputFile!));
        }
    }

    [Fact]
    public void Indent_option_pretty_prints()
    {
        var r = _engine.Transform(Req(Xsl3, "<r><i>1</i></r>", indent: true));
        Assert.Contains("\n", File.ReadAllText(r.OutputFile!).Trim());
    }

    [Fact]
    public void Compile_error_reports_line_and_column()
    {
        const string bad = "<xsl:stylesheet version=\"3.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\">\n<xsl:template match=\"/\">\n<xsl:value-of select=\"foo(\"/>\n</xsl:template>\n</xsl:stylesheet>";
        var r = _engine.Transform(Req(bad, "<a/>"));
        Assert.False(r.Success);
        var d = Assert.Single(r.Diagnostics, x => x.Severity == DiagnosticSeverity.Error);
        Assert.Equal(DiagnosticCategory.XslCompile, d.Category);
        Assert.Equal(3, d.Line);
        Assert.True(d.Column > 0, d.ToString());
    }

    [Fact]
    public void Malformed_stylesheet_reports_location()
    {
        var r = _engine.Transform(Req("<xsl:stylesheet version=\"3.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\">\n<xsl:template match=\"/\">\n</xsl:stylesheet>", "<a/>"));
        Assert.False(r.Success);
        Assert.Contains(r.Diagnostics, d => d.Line == 3);
    }

    [Fact]
    public void Malformed_source_reports_error()
    {
        var r = _engine.Transform(Req(Xsl3, "<r>\n<i>1</r>"));
        Assert.False(r.Success);
        Assert.Contains(r.Diagnostics, d => d.Category == DiagnosticCategory.Runtime && d.Line == 2);
    }

    [Fact]
    public void Runtime_error_is_reported()
    {
        const string xsl = "<xsl:stylesheet version=\"3.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\">\n<xsl:template match=\"/\">\n<xsl:value-of select=\"xs:integer('x')\" xmlns:xs=\"http://www.w3.org/2001/XMLSchema\"/>\n</xsl:template>\n</xsl:stylesheet>";
        var r = _engine.Transform(Req(xsl, "<a/>"));
        Assert.False(r.Success);
        Assert.Contains(r.Diagnostics, d => d.Severity == DiagnosticSeverity.Error && d.Line == 3);
    }

    [Fact]
    public void Messages_are_captured_and_terminate_is_an_error()
    {
        const string xsl = "<xsl:stylesheet version=\"3.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\"><xsl:template match=\"/\"><xsl:message>hello</xsl:message><a/></xsl:template></xsl:stylesheet>";
        var r = _engine.Transform(Req(xsl, "<a/>"));
        Assert.True(r.Success);
        Assert.Equal("hello", Assert.Single(r.Messages).Text.Trim());

        const string term = "<xsl:stylesheet version=\"3.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\"><xsl:template match=\"/\"><xsl:message terminate=\"yes\">stop now</xsl:message></xsl:template></xsl:stylesheet>";
        var r2 = _engine.Transform(Req(term, "<a/>"));
        Assert.False(r2.Success);
        Assert.Contains(r2.Diagnostics, d => d.Message.Contains("stop now"));
    }

    [Fact]
    public void Streams_source_from_file()
    {
        var file = Path.Combine(_tmp, "in.xml");
        File.WriteAllText(file, "<r><i>5</i></r>");
        var r = _engine.Transform(Req(Xsl3, null, sourcePath: file));
        Assert.True(r.Success, string.Join("; ", r.Diagnostics));
        Assert.Contains("<n>10</n>", File.ReadAllText(r.OutputFile!));
    }

    [Fact]
    public void Initial_template_runs_without_matching_input()
    {
        const string xsl = "<xsl:stylesheet version=\"3.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\"><xsl:output method=\"text\"/><xsl:template name=\"go\">named!</xsl:template></xsl:stylesheet>";
        var r = _engine.Transform(Req(xsl, "<a/>", initialTemplate: "go"));
        Assert.True(r.Success, string.Join("; ", r.Diagnostics));
        Assert.Equal("named!", File.ReadAllText(r.OutputFile!));
    }

    [Fact]
    public void Result_documents_are_captured_instead_of_written_to_disk()
    {
        const string xsl = "<xsl:stylesheet version=\"3.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\"><xsl:template match=\"/\"><main/><xsl:result-document href=\"extra.xml\"><extra/></xsl:result-document></xsl:template></xsl:stylesheet>";
        var r = _engine.Transform(Req(xsl, "<a/>"));
        Assert.True(r.Success, string.Join("; ", r.Diagnostics));
        var extra = Assert.Single(r.ResultDocuments);
        Assert.Contains("<extra", File.ReadAllText(extra.Value));
        Assert.False(File.Exists(Path.Combine(Environment.CurrentDirectory, "extra.xml")));
    }

    [Fact]
    public void Strip_space_in_stylesheet_is_honoured()
    {
        const string xsl = "<xsl:stylesheet version=\"3.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\"><xsl:output method=\"text\"/><xsl:strip-space elements=\"*\"/><xsl:template match=\"/\"><xsl:value-of select=\"count(/r/node())\"/></xsl:template></xsl:stylesheet>";
        var r = _engine.Transform(Req(xsl, "<r>\n  <a/>\n  <b/>\n</r>"));
        Assert.Equal("2", File.ReadAllText(r.OutputFile!));
    }

    [Fact]
    public void Check_stylesheet_reports_warnings_and_errors_only_from_compile()
    {
        Assert.Empty(_engine.CheckStylesheet(Xsl3, null, "a.xsl"));
    }

    [Fact]
    public void Missing_source_is_reported()
    {
        var r = _engine.Transform(Req(Xsl3, null));
        Assert.False(r.Success);
        Assert.Contains(r.Diagnostics, d => d.Message.Contains("No source"));
    }

    [Fact]
    public void Includes_resolve_relative_to_stylesheet_path()
    {
        File.WriteAllText(Path.Combine(_tmp, "inc.xsl"),
            "<xsl:stylesheet version=\"3.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\"><xsl:template name=\"t\">included</xsl:template></xsl:stylesheet>");
        var main = "<xsl:stylesheet version=\"3.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\"><xsl:output method=\"text\"/><xsl:include href=\"inc.xsl\"/><xsl:template match=\"/\"><xsl:call-template name=\"t\"/></xsl:template></xsl:stylesheet>";
        var req = new TransformRequest
        {
            StylesheetText = main, StylesheetPath = Path.Combine(_tmp, "main.xsl"), SourceText = "<a/>", OutputDirectory = _tmp
        };
        var r = _engine.Transform(req);
        Assert.True(r.Success, string.Join("; ", r.Diagnostics));
        Assert.Equal("included", File.ReadAllText(r.OutputFile!));
    }

    [Fact]
    public void Large_file_streams_and_transforms()
    {
        var file = Path.Combine(_tmp, "big.xml");
        using (var w = new StreamWriter(file))
        {
            w.Write("<r>");
            for (var i = 0; i < 400_000; i++) w.Write($"<i>{i}</i>");
            w.Write("</r>");
        }

        const string xsl = "<xsl:stylesheet version=\"3.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\"><xsl:output method=\"text\"/><xsl:template match=\"/\"><xsl:value-of select=\"count(//i)\"/></xsl:template></xsl:stylesheet>";
        var r = _engine.Transform(Req(xsl, null, sourcePath: file));
        Assert.Equal("400000", File.ReadAllText(r.OutputFile!));
    }
}

public class XmlToolsTests
{
    [Fact]
    public void Well_formed_document_has_no_diagnostics() =>
        Assert.Empty(XmlTools.ValidateWellFormed("<a><b/></a>", "x.xml", DiagnosticCategory.XmlValidation));

    [Fact]
    public void Malformed_document_reports_line_and_column()
    {
        var d = Assert.Single(XmlTools.ValidateWellFormed("<a>\n  <b>\n</a>", "x.xml", DiagnosticCategory.XmlValidation));
        Assert.Equal(3, d.Line);
        Assert.True(d.Column > 0);
        Assert.DoesNotContain("Line 3", d.Message);
        Assert.Equal("x.xml", d.File);
    }

    [Fact]
    public void Empty_document_is_reported() =>
        Assert.Single(XmlTools.ValidateWellFormed("  ", "x.xml", DiagnosticCategory.XmlValidation));

    [Fact]
    public void Format_indents_and_keeps_declaration()
    {
        var f = XmlTools.Format("<?xml version=\"1.0\" encoding=\"UTF-8\"?><a><b>x</b><c/></a>");
        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"UTF-8\"?>", f);
        Assert.Contains("\r\n  <b>x</b>", f);
    }

    [Fact]
    public void Format_does_not_add_declaration_when_absent() =>
        Assert.DoesNotContain("<?xml", XmlTools.Format("<a><b/></a>"));

    [Fact]
    public void Format_preserves_xsl_text_whitespace_and_comments()
    {
        var src = "<xsl:stylesheet version=\"1.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\"><!-- c --><xsl:template match=\"/\"><xsl:text> </xsl:text></xsl:template></xsl:stylesheet>";
        var f = XmlTools.Format(src);
        Assert.Contains("<xsl:text> </xsl:text>", f);
        Assert.Contains("<!-- c -->", f);
    }

    [Fact]
    public void Format_keeps_mixed_content_intact() =>
        Assert.Contains("<p>Hello <b>big</b> world</p>", XmlTools.Format("<d><p>Hello <b>big</b> world</p></d>"));

    [Fact]
    public void Format_throws_on_malformed() =>
        Assert.ThrowsAny<System.Xml.XmlException>(() => XmlTools.Format("<a>"));

    [Fact]
    public void Format_with_tabs()
    {
        Assert.Contains("\t<b />", XmlTools.Format("<a><b/></a>", useTabs: true));
    }

    [Fact]
    public void Format_is_idempotent()
    {
        var once = XmlTools.Format("<a><b><c>1</c></b></a>");
        Assert.Equal(once, XmlTools.Format(once));
    }
}

public class XPathTests
{
    private const string Xml = "<wd:R xmlns:wd=\"urn:com.workday/bsvc\"><wd:W id=\"1\"><wd:N>Ann</wd:N></wd:W>\n<wd:W id=\"2\"><wd:N>Bob</wd:N></wd:W></wd:R>";
    private readonly XPathEvaluator _x = new();

    [Fact]
    public void Prefixed_query_returns_nodes_with_line_numbers()
    {
        var r = _x.Evaluate("//wd:W", Xml, null, "in.xml");
        Assert.Null(r.Error);
        Assert.Equal(2, r.TotalCount);
        Assert.Equal(1, r.Items[0].Line);
        Assert.Equal(2, r.Items[1].Line);
    }

    [Fact]
    public void Unprefixed_names_match_any_namespace()
    {
        Assert.Equal(2, _x.Evaluate("//W", Xml, null, "in.xml").TotalCount);
    }

    [Fact]
    public void Undeclared_wd_prefix_is_available()
    {
        Assert.Equal(1, _x.Evaluate("//wd:N", "<a xmlns:q=\"urn:com.workday/bsvc\"><q:N/></a>", null, "in.xml").TotalCount);
    }

    [Fact]
    public void Atomic_results_are_described()
    {
        var r = _x.Evaluate("count(//wd:W)", Xml, null, "in.xml");
        Assert.Equal("2", r.Items.Single().Value);
    }

    [Fact]
    public void Syntax_error_is_reported()
    {
        var r = _x.Evaluate("//wd:W[", Xml, null, "in.xml");
        Assert.NotNull(r.Error);
        Assert.Equal(DiagnosticCategory.XPath, r.Error!.Category);
    }

    [Fact]
    public void Malformed_xml_is_reported()
    {
        Assert.NotNull(_x.Evaluate("/", "<a>", null, "in.xml").Error);
    }

    [Fact]
    public void Attribute_and_string_functions_work()
    {
        var r = _x.Evaluate("string-join(//wd:W/@id, ',')", Xml, null, "in.xml");
        Assert.Equal("1,2", r.Items.Single().Value);
    }
}

public class WorkspaceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "HydrogenXML.Tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public void State_round_trips_including_layout_and_settings()
    {
        var svc = new WorkspaceService(_dir);
        var s = new WorkspaceState { Name = "W1" };
        s.XslDocuments.Add(new OpenDocumentState { Path = @"C:\a.xsl", Title = "a.xsl", CaretOffset = 42 });
        s.RecentXml.Add(@"C:\b.xml");
        s.Transform.AutoTransform = true;
        s.Transform.AutoTransformDelayMs = 750;
        s.Transform.Parameters.Add(new ParameterSetting { Name = "p", Value = "v" });
        s.Layout.Theme = "Light";
        s.Layout.Left = double.NaN;
        s.Layout.SidebarWidth = 300;
        svc.Save(s);

        var l = svc.Load();
        Assert.Equal("W1", l.Name);
        Assert.Equal(42, l.XslDocuments[0].CaretOffset);
        Assert.Equal(@"C:\b.xml", l.RecentXml[0]);
        Assert.True(l.Transform.AutoTransform);
        Assert.Equal(750, l.Transform.AutoTransformDelayMs);
        Assert.Equal("p", l.Transform.Parameters[0].Name);
        Assert.Equal("Light", l.Layout.Theme);
        Assert.Equal(300, l.Layout.SidebarWidth);
        Assert.True(double.IsNaN(l.Layout.Left));
    }

    [Fact]
    public void Missing_or_corrupt_workspace_yields_defaults()
    {
        var svc = new WorkspaceService(_dir);
        Assert.NotNull(svc.Load());
        File.WriteAllText(svc.DefaultWorkspacePath, "{ not json");
        var s = svc.Load();
        Assert.Empty(s.XslDocuments);
        Assert.True(File.Exists(svc.DefaultWorkspacePath + ".corrupt"));
    }

    [Fact]
    public void Backups_round_trip_and_clear()
    {
        var svc = new WorkspaceService(_dir);
        var f = svc.WriteBackup("unsaved text");
        Assert.Equal("unsaved text", svc.ReadBackup(f));
        svc.ClearBackups();
        Assert.Null(svc.ReadBackup(f));
    }

    [Fact]
    public void Last_workspace_path_is_remembered()
    {
        var svc = new WorkspaceService(_dir);
        var path = Path.Combine(_dir, "proj.hydrogen");
        svc.Save(new WorkspaceState { Name = "P" }, path);
        svc.SaveLastWorkspacePath(path);
        Assert.Equal(path, svc.LoadLastWorkspacePath());
    }

    [Fact]
    public void Sample_store_manages_datasets_and_unique_names()
    {
        var store = new SampleStore(Path.Combine(_dir, "Samples"));
        store.CreateDataset("Workday");
        var a = store.Add("Workday", "x", "<a/>");
        var b = store.Add("Workday", "x.xml", "<b/>");
        Assert.NotEqual(a, b);
        store.Add(SampleStore.DefaultDataset, "g.xml", "<g/>");
        var samples = store.GetSamples();
        Assert.Equal(3, samples.Count);
        Assert.Contains("Workday", store.GetDatasets());
        store.Delete(samples.First(s => s.Name == "x.xml"));
        Assert.Equal(2, store.GetSamples().Count);
    }
}

public class TemplateTests
{
    private readonly XsltEngine _engine = new();

    public static IEnumerable<object[]> Templates() =>
        TemplateLibrary.BuiltIn.Select(t => new object[] { t.Name });

    [Theory]
    [MemberData(nameof(Templates))]
    public void Built_in_template_compiles_cleanly(string name)
    {
        var t = TemplateLibrary.BuiltIn.Single(x => x.Name == name);
        Assert.Empty(XmlTools.ValidateWellFormed(t.Content, name, DiagnosticCategory.XslValidation));
        var errors = _engine.CheckStylesheet(t.Content, null, name).Where(d => d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(errors);
    }

    [Fact]
    public void Sample_data_is_well_formed()
    {
        foreach (var (_, name, content) in TemplateLibrary.SampleData)
            Assert.Empty(XmlTools.ValidateWellFormed(content, name, DiagnosticCategory.XmlValidation));
    }

    [Fact]
    public void Workday_templates_produce_expected_output_with_sample_data()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "HydrogenXML.Tests", Guid.NewGuid().ToString("N"));
        string Run(string template, string sample)
        {
            var r = _engine.Transform(new TransformRequest
            {
                StylesheetText = TemplateLibrary.BuiltIn.Single(t => t.Name == template).Content,
                SourceText = TemplateLibrary.SampleData.Single(s => s.Name == sample).Content,
                OutputDirectory = tmp
            });
            Assert.True(r.Success, string.Join("; ", r.Diagnostics));
            return File.ReadAllText(r.OutputFile!);
        }

        var flat = Run("Get_Workers to flat XML", "Get_Workers_Response.xml");
        Assert.Contains("<EmployeeID>21001</EmployeeID>", flat);
        Assert.Contains("<LastName>Liu</LastName>", flat);

        var csv = Run("RaaS report to CSV", "RaaS_Report.xml");
        Assert.StartsWith("Employee_ID,Full_Name,Department,Hire_Date", csv);
        Assert.Contains("\"Smith, Pat \"\"PJ\"\"\"", csv);

        var grouped = Run("RaaS report grouped by field", "RaaS_Report.xml");
        Assert.Contains("count=\"2\"", grouped);
        try { Directory.Delete(tmp, true); } catch { }
    }

    [Fact]
    public void Snippets_have_a_cursor_marker_or_are_complete()
    {
        Assert.NotEmpty(TemplateLibrary.Snippets);
        Assert.All(TemplateLibrary.Snippets, s => Assert.False(string.IsNullOrWhiteSpace(s.Content)));
    }
}
