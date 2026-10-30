namespace JsonForge.Core.Json;

public enum JsonTextKind
{
    Empty,
    StrictJson,
    Jsonc,
    Jsonish,
    Ndjson,
    Invalid,
}

public sealed record JsonTextAnalysis(
    JsonTextKind Kind,
    bool IsStrictValid,
    bool IsRelaxedValid,
    int NdjsonDocumentCount,
    bool HasComments,
    bool HasSingleQuotedStrings,
    bool HasBarewords,
    bool HasTrailingCommas);
