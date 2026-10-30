namespace JsonForge.Core.Json;

public enum JsonDiagnosticSeverity
{
    Info,
    Warning,
    Error,
}

/// <summary>
/// A single problem (or hint) found in a JSON document. Offsets are character based and
/// <see cref="Line"/>/<see cref="Column"/> are 1-based so they can be shown to users directly.
/// </summary>
public sealed record JsonDiagnostic(
    JsonDiagnosticSeverity Severity,
    string Message,
    int Offset,
    int Length,
    int Line,
    int Column,
    string? Code = null)
{
    /// <summary>Human-readable line:column location, e.g. "12:4".</summary>
    public string Location => $"{Line}:{Column}";

    public static JsonDiagnostic Create(
        JsonDiagnosticSeverity severity,
        string message,
        string? source,
        int offset,
        int length,
        string? code = null)
    {
        var (line, column) = SourceText.GetLineColumn(source, offset);
        return new JsonDiagnostic(severity, message, offset, Math.Max(0, length), line, column, code);
    }
}

/// <summary>Stable identifiers for diagnostics so the UI and AI layer can reason about them.</summary>
public static class JsonDiagnosticCodes
{
    public const string InvalidJson = "JF0001";
    public const string UnclosedStructure = "JF0002";
    public const string UnterminatedString = "JF0003";
    public const string TrailingComma = "JF0004";
    public const string CommentNotAllowed = "JF0005";
    public const string UnquotedKey = "JF0006";
    public const string SingleQuotedString = "JF0007";
    public const string DuplicateKey = "JF0008";
    public const string NonStandardLiteral = "JF0009";
    public const string TrailingContent = "JF0010";
}
