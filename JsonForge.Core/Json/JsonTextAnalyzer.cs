using System.Text.Json;

namespace JsonForge.Core.Json;

public static class JsonTextAnalyzer
{
    public static JsonTextAnalysis Analyze(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new JsonTextAnalysis(
                JsonTextKind.Empty,
                IsStrictValid: false,
                IsRelaxedValid: false,
                NdjsonDocumentCount: 0,
                HasComments: false,
                HasSingleQuotedStrings: false,
                HasBarewords: false,
                HasTrailingCommas: false);
        }

        var normalized = JsonTextNormalizer.Normalize(text);

        var isStrictValid = IsStrictJson(normalized);

        var tokens = JsonLexer.Tokenize(normalized);
        var hasComments = tokens.Any(t => t.Kind == JsonLexemeKind.Comment);
        var hasSingleQuotes = tokens.Any(t => t.Kind == JsonLexemeKind.SingleQuotedString);
        var hasBarewords = tokens.Any(t => t.Kind == JsonLexemeKind.Bareword);
        var hasTrailingCommas = HasTrailingComma(tokens);

        var kind = JsonTextKind.Invalid;
        var relaxedValid = isStrictValid;
        var ndjsonCount = 0;

        if (!isStrictValid && NdjsonConverter.TryCountDocuments(normalized, out ndjsonCount))
        {
            kind = JsonTextKind.Ndjson;
            relaxedValid = true;
        }
        else if (hasComments)
        {
            kind = JsonTextKind.Jsonc;
        }
        else if (hasSingleQuotes || hasBarewords || hasTrailingCommas)
        {
            kind = JsonTextKind.Jsonish;
        }
        else
        {
            kind = JsonTextKind.StrictJson;
        }

        if (!relaxedValid)
        {
            // Best-effort relaxed validity: if the repair pipeline produces parseable JSON, treat the input as relaxed-valid.
            // This is intentionally non-throwing and is only used for UX hints.
            relaxedValid = JsonRepairer.Repair(normalized).IsValid;
        }

        if (kind == JsonTextKind.StrictJson && !isStrictValid)
        {
            kind = JsonTextKind.Invalid;
        }

        return new JsonTextAnalysis(
            kind,
            isStrictValid,
            relaxedValid,
            ndjsonCount,
            hasComments,
            hasSingleQuotes,
            hasBarewords,
            hasTrailingCommas);
    }

    private static bool IsStrictJson(string text)
    {
        try
        {
            using var _ = JsonDocument.Parse(text, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 256,
            });
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool HasTrailingComma(IReadOnlyList<JsonLexeme> tokens)
    {
        var significant = tokens.Where(t => !t.IsTrivia).ToList();
        for (var i = 0; i + 1 < significant.Count; i++)
        {
            if (significant[i].Kind != JsonLexemeKind.Comma)
            {
                continue;
            }

            if (significant[i + 1].Kind is JsonLexemeKind.ObjectEnd or JsonLexemeKind.ArrayEnd)
            {
                return true;
            }
        }

        return false;
    }
}
