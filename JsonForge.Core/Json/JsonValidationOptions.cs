namespace JsonForge.Core.Json;

public enum JsonValidationStrictness
{
    StrictJson,
    Jsonc,
    Jsonish,
}

public sealed record JsonValidationOptions
{
    public static JsonValidationOptions Strict { get; } = new();

    public JsonValidationStrictness Strictness { get; init; } = JsonValidationStrictness.StrictJson;

    /// <summary>If true, comments are accepted (JSONC). When false, comments are warned and parsing disallows them.</summary>
    public bool AllowComments { get; init; }

    /// <summary>If true, trailing commas are accepted when parsing. When false, they are warned/errors.</summary>
    public bool AllowTrailingCommas { get; init; }

    /// <summary>If false, diagnostics about single-quoted strings are suppressed.</summary>
    public bool WarnOnSingleQuotes { get; init; } = true;

    /// <summary>If false, diagnostics about unquoted keys are suppressed.</summary>
    public bool WarnOnUnquotedKeys { get; init; } = true;

    /// <summary>If false, diagnostics about non-standard literals are suppressed.</summary>
    public bool WarnOnNonStandardLiterals { get; init; } = true;

    /// <summary>If true, duplicate keys in an object are reported (warning).</summary>
    public bool ReportDuplicateKeys { get; init; } = true;

    public static JsonValidationOptions FromStrictness(JsonValidationStrictness strictness) => strictness switch
    {
        JsonValidationStrictness.Jsonc => new JsonValidationOptions
        {
            Strictness = strictness,
            AllowComments = true,
            AllowTrailingCommas = true,
        },
        JsonValidationStrictness.Jsonish => new JsonValidationOptions
        {
            Strictness = strictness,
            AllowComments = true,
            AllowTrailingCommas = true,
            // json-ish still warns on these; it just won't treat comment/trailing-comma as hard blockers.
        },
        _ => new JsonValidationOptions { Strictness = JsonValidationStrictness.StrictJson },
    };
}
