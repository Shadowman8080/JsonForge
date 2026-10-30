namespace JsonForge.Core.Json;

public sealed record JsonRepairOptions
{
    public static JsonRepairOptions Default { get; } = new();

    /// <summary>Removes // and /* */ comments (JSONC).</summary>
    public bool RemoveComments { get; init; } = true;

    /// <summary>Converts single-quoted and curly-quoted strings to double-quoted strings.</summary>
    public bool NormalizeQuotes { get; init; } = true;

    public bool RemoveTrailingCommas { get; init; } = true;

    public bool InsertMissingCommas { get; init; } = true;

    public bool InsertMissingColons { get; init; } = true;

    public bool QuoteUnquotedKeys { get; init; } = true;

    public bool QuoteBareValues { get; init; } = true;

    /// <summary>Maps True/False/None, NaN and Infinity to valid JSON literals.</summary>
    public bool NormalizeLiterals { get; init; } = true;

    /// <summary>Fixes 007, 1., .5, +1 and similar number spellings.</summary>
    public bool NormalizeNumbers { get; init; } = true;

    public bool CloseUnclosed { get; init; } = true;

    public bool DropTrailingContent { get; init; } = true;

    /// <summary>Runs the repaired document through <see cref="JsonFormatter"/> afterwards.</summary>
    public bool PrettyPrint { get; init; } = true;

    /// <summary>Formatting applied when <see cref="PrettyPrint"/> is enabled.</summary>
    public JsonFormatOptions FormatOptions { get; init; } = JsonFormatOptions.Default;
}

public enum JsonRepairActionKind
{
    RemovedComment,
    NormalizedQuotes,
    RemovedTrailingComma,
    InsertedComma,
    InsertedColon,
    QuotedKey,
    QuotedValue,
    NormalizedLiteral,
    NormalizedNumber,
    RewroteString,
    ClosedStructure,
    DroppedTrailingContent,
    DroppedToken,
}

/// <summary>A single edit the repair engine made, so the change can be explained and highlighted.</summary>
public sealed record JsonRepairAction(JsonRepairActionKind Kind, string Description, int Offset, int Length)
{
    public static JsonRepairAction Create(JsonRepairActionKind kind, string description, string? source, int offset, int length)
        => new(kind, description, offset, length);
}

public sealed record JsonRepairResult(
    string Original,
    string Repaired,
    bool WasModified,
    bool IsValid,
    IReadOnlyList<JsonRepairAction> Actions,
    IReadOnlyList<JsonDiagnostic> RemainingDiagnostics)
{
    public int ActionCount => Actions.Count;
}
