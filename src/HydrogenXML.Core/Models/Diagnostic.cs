namespace HydrogenXML.Core.Models;

public enum DiagnosticSeverity
{
    Error,
    Warning,
    Info
}

public enum DiagnosticCategory
{
    XslCompile,
    XmlValidation,
    XslValidation,
    Runtime,
    XPath,
    Warning
}

public sealed class Diagnostic
{
    public DiagnosticSeverity Severity { get; init; } = DiagnosticSeverity.Error;
    public DiagnosticCategory Category { get; init; }
    public string File { get; init; } = "";
    public int Line { get; init; }
    public int Column { get; init; }
    public string Message { get; init; } = "";
    public string? Code { get; init; }

    public string Location => Line > 0 ? (Column > 0 ? $"{Line}:{Column}" : Line.ToString()) : "";

    public override string ToString() =>
        $"{Severity} [{Category}] {File}({Location}): {Message}";
}

public sealed class TransformMessage
{
    public string Text { get; init; } = "";
    public string File { get; init; } = "";
    public int Line { get; init; }
    public int Column { get; init; }
    public bool Terminate { get; init; }
}
