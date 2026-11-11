namespace HydrogenXML.Core.Workspace;

public sealed class TemplateInfo
{
    public string Category { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public string Content { get; init; } = "";
    public bool IsUser { get; init; }
    public string? Path { get; init; }
}

public sealed class SnippetInfo
{
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    /// <summary>Snippet text; "$0" marks where the caret is placed after insertion.</summary>
    public string Content { get; init; } = "";
}

public static class TemplateLibrary
{
    public static IReadOnlyList<TemplateInfo> BuiltIn { get; } = new List<TemplateInfo>
    {
        new()
        {
            Category = "Starter", Name = "XSLT 1.0 starter",
            Description = "Minimal XSLT 1.0 stylesheet.",
            Content = """
<?xml version="1.0" encoding="UTF-8"?>
<xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
  <xsl:output method="xml" indent="yes" encoding="UTF-8"/>

  <xsl:template match="/">
    <root>
      <xsl:apply-templates/>
    </root>
  </xsl:template>

</xsl:stylesheet>
"""
        },
        new()
        {
            Category = "Starter", Name = "XSLT 2.0 starter",
            Description = "XSLT 2.0 stylesheet with xs namespace.",
            Content = """
<?xml version="1.0" encoding="UTF-8"?>
<xsl:stylesheet version="2.0"
    xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
    xmlns:xs="http://www.w3.org/2001/XMLSchema"
    exclude-result-prefixes="xs">
  <xsl:output method="xml" indent="yes" encoding="UTF-8"/>

  <xsl:template match="/">
    <root>
      <xsl:apply-templates/>
    </root>
  </xsl:template>

</xsl:stylesheet>
"""
        },
        new()
        {
            Category = "Starter", Name = "XSLT 3.0 starter",
            Description = "XSLT 3.0 stylesheet using shallow-copy default mode.",
            Content = """
<?xml version="1.0" encoding="UTF-8"?>
<xsl:stylesheet version="3.0"
    xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
    xmlns:xs="http://www.w3.org/2001/XMLSchema"
    exclude-result-prefixes="xs">
  <xsl:output method="xml" indent="yes" encoding="UTF-8"/>
  <xsl:mode on-no-match="shallow-copy"/>

  <xsl:template match="/">
    <xsl:apply-templates/>
  </xsl:template>

</xsl:stylesheet>
"""
        },
        new()
        {
            Category = "Starter", Name = "Identity transform",
            Description = "Copies the input unchanged; add templates to override.",
            Content = """
<?xml version="1.0" encoding="UTF-8"?>
<xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
  <xsl:output method="xml" indent="yes" encoding="UTF-8"/>

  <xsl:template match="@*|node()">
    <xsl:copy>
      <xsl:apply-templates select="@*|node()"/>
    </xsl:copy>
  </xsl:template>

</xsl:stylesheet>
"""
        },
        new()
        {
            Category = "Starter", Name = "Remove namespaces",
            Description = "Copies the document while stripping all namespaces.",
            Content = """
<?xml version="1.0" encoding="UTF-8"?>
<xsl:stylesheet version="2.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
  <xsl:output method="xml" indent="yes" encoding="UTF-8"/>

  <xsl:template match="*">
    <xsl:element name="{local-name()}">
      <xsl:apply-templates select="@*|node()"/>
    </xsl:element>
  </xsl:template>

  <xsl:template match="@*">
    <xsl:attribute name="{local-name()}">
      <xsl:value-of select="."/>
    </xsl:attribute>
  </xsl:template>

  <xsl:template match="text()|comment()|processing-instruction()">
    <xsl:copy/>
  </xsl:template>

</xsl:stylesheet>
"""
        },
        new()
        {
            Category = "Starter", Name = "XML to text",
            Description = "Text output skeleton with line breaks.",
            Content = """
<?xml version="1.0" encoding="UTF-8"?>
<xsl:stylesheet version="2.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
  <xsl:output method="text" encoding="UTF-8"/>

  <xsl:template match="/">
    <xsl:for-each select="//*[not(*)]">
      <xsl:value-of select="name()"/>
      <xsl:text>=</xsl:text>
      <xsl:value-of select="normalize-space(.)"/>
      <xsl:text>&#10;</xsl:text>
    </xsl:for-each>
  </xsl:template>

</xsl:stylesheet>
"""
        },
        new()
        {
            Category = "Workday", Name = "Get_Workers to flat XML",
            Description = "Flattens a Workday Get_Workers web service response into one element per worker.",
            Content = """
<?xml version="1.0" encoding="UTF-8"?>
<xsl:stylesheet version="3.0"
    xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
    xmlns:xs="http://www.w3.org/2001/XMLSchema"
    xmlns:wd="urn:com.workday/bsvc"
    exclude-result-prefixes="xs wd">
  <xsl:output method="xml" indent="yes" encoding="UTF-8"/>

  <xsl:template match="/">
    <Workers>
      <xsl:apply-templates select="//wd:Worker"/>
    </Workers>
  </xsl:template>

  <xsl:template match="wd:Worker">
    <xsl:variable name="personal" select="wd:Worker_Data/wd:Personal_Data"/>
    <Worker>
      <EmployeeID>
        <xsl:value-of select="wd:Worker_Reference/wd:ID[@wd:type='Employee_ID']"/>
      </EmployeeID>
      <FirstName>
        <xsl:value-of select="$personal/wd:Name_Data/wd:Legal_Name_Data/wd:Name_Detail_Data/wd:First_Name"/>
      </FirstName>
      <LastName>
        <xsl:value-of select="$personal/wd:Name_Data/wd:Legal_Name_Data/wd:Name_Detail_Data/wd:Last_Name"/>
      </LastName>
      <Email>
        <xsl:value-of select="($personal/wd:Contact_Data/wd:Email_Address_Data/wd:Email_Address)[1]"/>
      </Email>
      <Position>
        <xsl:value-of select="wd:Worker_Data/wd:Employment_Data/wd:Position_Data/wd:Business_Title"/>
      </Position>
    </Worker>
  </xsl:template>

</xsl:stylesheet>
"""
        },
        new()
        {
            Category = "Workday", Name = "RaaS report to CSV",
            Description = "Converts a Workday Report-as-a-Service (wd:Report_Data) XML into CSV with a header row.",
            Content = """
<?xml version="1.0" encoding="UTF-8"?>
<xsl:stylesheet version="3.0"
    xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
    xmlns:xs="http://www.w3.org/2001/XMLSchema"
    xmlns:f="urn:hydrogenxml:functions"
    exclude-result-prefixes="xs f">
  <xsl:output method="text" encoding="UTF-8"/>

  <xsl:param name="delimiter" select="','"/>

  <xsl:function name="f:csv" as="xs:string">
    <xsl:param name="value" as="xs:string?"/>
    <xsl:variable name="v" select="replace(string($value), '&quot;', '&quot;&quot;')"/>
    <xsl:sequence select="if (matches($v, concat('[', $delimiter, '&quot;&#10;&#13;]'))) then concat('&quot;', $v, '&quot;') else $v"/>
  </xsl:function>

  <xsl:template match="/">
    <xsl:variable name="entries" select="//*:Report_Entry"/>
    <xsl:variable name="columns" select="$entries[1]/*"/>
    <xsl:value-of select="string-join(for $c in $columns return f:csv(local-name($c)), $delimiter)"/>
    <xsl:text>&#13;&#10;</xsl:text>
    <xsl:for-each select="$entries">
      <xsl:variable name="row" select="."/>
      <xsl:value-of select="string-join(for $c in $columns return f:csv(string($row/*[local-name() = local-name($c)][1])), $delimiter)"/>
      <xsl:text>&#13;&#10;</xsl:text>
    </xsl:for-each>
  </xsl:template>

</xsl:stylesheet>
"""
        },
        new()
        {
            Category = "Workday", Name = "RaaS report grouped by field",
            Description = "Groups Report_Entry rows by a chosen field using xsl:for-each-group.",
            Content = """
<?xml version="1.0" encoding="UTF-8"?>
<xsl:stylesheet version="3.0"
    xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
    xmlns:xs="http://www.w3.org/2001/XMLSchema"
    exclude-result-prefixes="xs">
  <xsl:output method="xml" indent="yes" encoding="UTF-8"/>

  <xsl:param name="groupField" select="'Department'"/>

  <xsl:template match="/">
    <Groups>
      <xsl:for-each-group select="//*:Report_Entry" group-by="*[local-name() = $groupField]">
        <xsl:sort select="current-grouping-key()"/>
        <Group name="{current-grouping-key()}" count="{count(current-group())}">
          <xsl:copy-of select="current-group()"/>
        </Group>
      </xsl:for-each-group>
    </Groups>
  </xsl:template>

</xsl:stylesheet>
"""
        },
        new()
        {
            Category = "Workday", Name = "Integration: output envelope with parameters",
            Description = "Skeleton for a Workday integration XSLT step with launch parameters and a header/trailer.",
            Content = """
<?xml version="1.0" encoding="UTF-8"?>
<xsl:stylesheet version="3.0"
    xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
    xmlns:xs="http://www.w3.org/2001/XMLSchema"
    xmlns:wd="urn:com.workday/bsvc"
    exclude-result-prefixes="xs wd">
  <xsl:output method="xml" indent="yes" encoding="UTF-8"/>

  <!-- Parameters usually supplied by the integration (set test values under Transform > Settings) -->
  <xsl:param name="IntegrationName" select="'MyIntegration'"/>
  <xsl:param name="RunDate" select="format-date(current-date(), '[Y0001]-[M01]-[D01]')"/>

  <xsl:template match="/">
    <Export integration="{$IntegrationName}" runDate="{$RunDate}">
      <Header>
        <RecordCount>
          <xsl:value-of select="count(//wd:Worker)"/>
        </RecordCount>
      </Header>
      <Body>
        <xsl:apply-templates select="//wd:Worker"/>
      </Body>
    </Export>
  </xsl:template>

  <xsl:template match="wd:Worker">
    <Record id="{wd:Worker_Reference/wd:ID[@wd:type='Employee_ID']}"/>
  </xsl:template>

</xsl:stylesheet>
"""
        },
        new()
        {
            Category = "Workday", Name = "Date and value helper functions",
            Description = "Reusable xsl:function helpers for Workday dates and empty-value handling.",
            Content = """
<?xml version="1.0" encoding="UTF-8"?>
<xsl:stylesheet version="3.0"
    xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
    xmlns:xs="http://www.w3.org/2001/XMLSchema"
    xmlns:f="urn:hydrogenxml:functions"
    exclude-result-prefixes="xs f">
  <xsl:output method="xml" indent="yes" encoding="UTF-8"/>

  <!-- Workday dates look like 2024-03-15-07:00; keep only yyyy-MM-dd -->
  <xsl:function name="f:date-only" as="xs:string">
    <xsl:param name="value" as="xs:string?"/>
    <xsl:sequence select="substring(string($value), 1, 10)"/>
  </xsl:function>

  <!-- yyyy-MM-dd to MM/dd/yyyy -->
  <xsl:function name="f:us-date" as="xs:string">
    <xsl:param name="value" as="xs:string?"/>
    <xsl:variable name="d" select="f:date-only($value)"/>
    <xsl:sequence select="if (matches($d, '^\d{4}-\d{2}-\d{2}$'))
        then format-date(xs:date($d), '[M01]/[D01]/[Y0001]')
        else ''"/>
  </xsl:function>

  <xsl:function name="f:default" as="xs:string">
    <xsl:param name="value" as="xs:string?"/>
    <xsl:param name="fallback" as="xs:string"/>
    <xsl:sequence select="if (normalize-space($value) = '') then $fallback else normalize-space($value)"/>
  </xsl:function>

  <xsl:template match="/">
    <Result>
      <Example>
        <xsl:value-of select="f:us-date('2024-03-15-07:00')"/>
      </Example>
    </Result>
  </xsl:template>

</xsl:stylesheet>
"""
        }
    };

    public static IReadOnlyList<SnippetInfo> Snippets { get; } = new List<SnippetInfo>
    {
        new() { Name = "xsl:template (match)", Description = "Template with a match pattern",
            Content = "<xsl:template match=\"$0\">\r\n  \r\n</xsl:template>" },
        new() { Name = "xsl:template (named)", Description = "Named template",
            Content = "<xsl:template name=\"$0\">\r\n  \r\n</xsl:template>" },
        new() { Name = "xsl:call-template", Description = "Call a named template with a parameter",
            Content = "<xsl:call-template name=\"$0\">\r\n  <xsl:with-param name=\"\" select=\"\"/>\r\n</xsl:call-template>" },
        new() { Name = "xsl:apply-templates", Description = "Apply templates",
            Content = "<xsl:apply-templates select=\"$0\"/>" },
        new() { Name = "xsl:for-each", Description = "Loop over nodes",
            Content = "<xsl:for-each select=\"$0\">\r\n  \r\n</xsl:for-each>" },
        new() { Name = "xsl:for-each-group", Description = "Group nodes (XSLT 2.0+)",
            Content = "<xsl:for-each-group select=\"$0\" group-by=\"\">\r\n  <xsl:sort select=\"current-grouping-key()\"/>\r\n  \r\n</xsl:for-each-group>" },
        new() { Name = "xsl:if", Description = "Conditional",
            Content = "<xsl:if test=\"$0\">\r\n  \r\n</xsl:if>" },
        new() { Name = "xsl:choose", Description = "Multi-branch conditional",
            Content = "<xsl:choose>\r\n  <xsl:when test=\"$0\">\r\n    \r\n  </xsl:when>\r\n  <xsl:otherwise>\r\n    \r\n  </xsl:otherwise>\r\n</xsl:choose>" },
        new() { Name = "xsl:variable", Description = "Variable",
            Content = "<xsl:variable name=\"$0\" select=\"\"/>" },
        new() { Name = "xsl:param", Description = "Parameter",
            Content = "<xsl:param name=\"$0\" select=\"''\"/>" },
        new() { Name = "xsl:value-of", Description = "Output a value",
            Content = "<xsl:value-of select=\"$0\"/>" },
        new() { Name = "xsl:attribute", Description = "Create an attribute",
            Content = "<xsl:attribute name=\"$0\">\r\n  <xsl:value-of select=\"\"/>\r\n</xsl:attribute>" },
        new() { Name = "xsl:element", Description = "Create an element with a computed name",
            Content = "<xsl:element name=\"$0\">\r\n  \r\n</xsl:element>" },
        new() { Name = "xsl:copy-of", Description = "Deep copy",
            Content = "<xsl:copy-of select=\"$0\"/>" },
        new() { Name = "xsl:sort", Description = "Sort inside for-each / apply-templates",
            Content = "<xsl:sort select=\"$0\" order=\"ascending\" data-type=\"text\"/>" },
        new() { Name = "xsl:key", Description = "Declare a key for lookups",
            Content = "<xsl:key name=\"$0\" match=\"\" use=\"\"/>" },
        new() { Name = "xsl:function", Description = "Stylesheet function (XSLT 2.0+)",
            Content = "<xsl:function name=\"f:$0\" as=\"xs:string\">\r\n  <xsl:param name=\"value\" as=\"xs:string?\"/>\r\n  <xsl:sequence select=\"string($value)\"/>\r\n</xsl:function>" },
        new() { Name = "xsl:try / xsl:catch", Description = "Error handling (XSLT 3.0)",
            Content = "<xsl:try>\r\n  $0\r\n  <xsl:catch>\r\n    <xsl:message select=\"concat('Error: ', $err:description)\"/>\r\n  </xsl:catch>\r\n</xsl:try>" },
        new() { Name = "xsl:analyze-string", Description = "Regex matching (XSLT 2.0+)",
            Content = "<xsl:analyze-string select=\"$0\" regex=\"\">\r\n  <xsl:matching-substring>\r\n    \r\n  </xsl:matching-substring>\r\n  <xsl:non-matching-substring>\r\n    \r\n  </xsl:non-matching-substring>\r\n</xsl:analyze-string>" },
        new() { Name = "xsl:message", Description = "Write a message to the Messages panel",
            Content = "<xsl:message>$0</xsl:message>" },
        new() { Name = "xsl:text (newline)", Description = "Line break in text output",
            Content = "<xsl:text>&#13;&#10;</xsl:text>$0" },
        new() { Name = "format-date", Description = "Format a date value",
            Content = "format-date(xs:date(substring($0, 1, 10)), '[M01]/[D01]/[Y0001]')" },
        new() { Name = "string-join", Description = "Join values with a delimiter",
            Content = "string-join($0, ',')" },
        new() { Name = "Workday ID lookup", Description = "Workday reference ID by type",
            Content = "wd:Worker_Reference/wd:ID[@wd:type='Employee_ID']$0" },
        new() { Name = "Workday namespace declaration", Description = "wd namespace for the Web Services (SOAP) format",
            Content = "xmlns:wd=\"urn:com.workday/bsvc\"$0" }
    };

    /// <summary>Sample XML documents seeded into the sample store on first run.</summary>
    public static IReadOnlyList<(string Dataset, string Name, string Content)> SampleData { get; } = new List<(string, string, string)>
    {
        ("Workday", "Get_Workers_Response.xml", """
<?xml version="1.0" encoding="UTF-8"?>
<wd:Get_Workers_Response xmlns:wd="urn:com.workday/bsvc" wd:version="v42.0">
  <wd:Response_Data>
    <wd:Worker>
      <wd:Worker_Reference>
        <wd:ID wd:type="WID">3aa5550b7fe348b98d7b5741afc65534</wd:ID>
        <wd:ID wd:type="Employee_ID">21001</wd:ID>
      </wd:Worker_Reference>
      <wd:Worker_Data>
        <wd:Personal_Data>
          <wd:Name_Data>
            <wd:Legal_Name_Data>
              <wd:Name_Detail_Data wd:Formatted_Name="Logan McNeil">
                <wd:First_Name>Logan</wd:First_Name>
                <wd:Last_Name>McNeil</wd:Last_Name>
              </wd:Name_Detail_Data>
            </wd:Legal_Name_Data>
          </wd:Name_Data>
          <wd:Contact_Data>
            <wd:Email_Address_Data>
              <wd:Email_Address>logan.mcneil@example.com</wd:Email_Address>
            </wd:Email_Address_Data>
          </wd:Contact_Data>
        </wd:Personal_Data>
        <wd:Employment_Data>
          <wd:Position_Data>
            <wd:Business_Title>Chief Executive Officer</wd:Business_Title>
          </wd:Position_Data>
        </wd:Employment_Data>
      </wd:Worker_Data>
    </wd:Worker>
    <wd:Worker>
      <wd:Worker_Reference>
        <wd:ID wd:type="WID">8f1b0d9c5e0a4c0d8e1a7f6b3c2d1e0f</wd:ID>
        <wd:ID wd:type="Employee_ID">21002</wd:ID>
      </wd:Worker_Reference>
      <wd:Worker_Data>
        <wd:Personal_Data>
          <wd:Name_Data>
            <wd:Legal_Name_Data>
              <wd:Name_Detail_Data wd:Formatted_Name="Betty Liu">
                <wd:First_Name>Betty</wd:First_Name>
                <wd:Last_Name>Liu</wd:Last_Name>
              </wd:Name_Detail_Data>
            </wd:Legal_Name_Data>
          </wd:Name_Data>
          <wd:Contact_Data>
            <wd:Email_Address_Data>
              <wd:Email_Address>betty.liu@example.com</wd:Email_Address>
            </wd:Email_Address_Data>
          </wd:Contact_Data>
        </wd:Personal_Data>
        <wd:Employment_Data>
          <wd:Position_Data>
            <wd:Business_Title>Chief Operating Officer</wd:Business_Title>
          </wd:Position_Data>
        </wd:Employment_Data>
      </wd:Worker_Data>
    </wd:Worker>
  </wd:Response_Data>
</wd:Get_Workers_Response>
"""),
        ("Workday", "RaaS_Report.xml", """
<?xml version="1.0" encoding="UTF-8"?>
<wd:Report_Data xmlns:wd="urn:com.workday.report/INT_Worker_Report">
  <wd:Report_Entry>
    <wd:Employee_ID>21001</wd:Employee_ID>
    <wd:Full_Name>Logan McNeil</wd:Full_Name>
    <wd:Department>Executive</wd:Department>
    <wd:Hire_Date>2015-01-12-08:00</wd:Hire_Date>
  </wd:Report_Entry>
  <wd:Report_Entry>
    <wd:Employee_ID>21002</wd:Employee_ID>
    <wd:Full_Name>Betty Liu</wd:Full_Name>
    <wd:Department>Operations</wd:Department>
    <wd:Hire_Date>2016-06-20-07:00</wd:Hire_Date>
  </wd:Report_Entry>
  <wd:Report_Entry>
    <wd:Employee_ID>21003</wd:Employee_ID>
    <wd:Full_Name>Smith, Pat "PJ"</wd:Full_Name>
    <wd:Department>Operations</wd:Department>
    <wd:Hire_Date>2019-09-02-07:00</wd:Hire_Date>
  </wd:Report_Entry>
</wd:Report_Data>
"""),
        ("General", "Books.xml", """
<?xml version="1.0" encoding="UTF-8"?>
<catalog>
  <book id="bk101"><author>Gambardella, Matthew</author><title>XML Developer's Guide</title><genre>Computer</genre><price>44.95</price></book>
  <book id="bk102"><author>Ralls, Kim</author><title>Midnight Rain</title><genre>Fantasy</genre><price>5.95</price></book>
  <book id="bk103"><author>Corets, Eva</author><title>Maeve Ascendant</title><genre>Fantasy</genre><price>5.95</price></book>
</catalog>
""")
    };

    public static IReadOnlyList<TemplateInfo> LoadUserTemplates(string directory)
    {
        var list = new List<TemplateInfo>();
        try
        {
            if (!Directory.Exists(directory)) return list;
            foreach (var f in Directory.GetFiles(directory, "*.xsl*"))
                list.Add(new TemplateInfo
                {
                    Category = "My Templates",
                    Name = System.IO.Path.GetFileNameWithoutExtension(f),
                    Description = f,
                    Content = File.ReadAllText(f),
                    IsUser = true,
                    Path = f
                });
        }
        catch (IOException) { }

        return list.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
