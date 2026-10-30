using System.Text.Json;
using System.Text.Json.Nodes;

namespace JsonForge.Core.Json;

public static class NdjsonConverter
{
    /// <summary>
    /// Heuristic: returns true if the text looks like NDJSON (2+ JSON values, one per non-empty line).
    /// </summary>
    public static bool TryCountDocuments(string text, out int documentCount)
    {
        documentCount = 0;
        if (string.IsNullOrWhiteSpace(text) || !text.Contains('\n'))
        {
            return false;
        }

        var lineCount = 0;
        var badLines = 0;

        foreach (var line in EnumerateNonEmptyLines(text))
        {
            lineCount++;

            if (IsStrictJsonLine(line))
            {
                documentCount++;
            }
            else
            {
                badLines++;
            }
        }

        // Conservative classification: require at least two documents and no malformed lines.
        return lineCount >= 2 && documentCount == lineCount && badLines == 0;
    }

    public static bool TryConvertToJsonArray(
        string? text,
        out JsonArray array,
        out int documentCount,
        out JsonDiagnostic? error)
    {
        array = [];
        documentCount = 0;
        error = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            error = JsonDiagnostic.Create(
                JsonDiagnosticSeverity.Error,
                "Document is empty. Expected NDJSON.",
                text,
                0,
                0,
                JsonDiagnosticCodes.InvalidJson);
            return false;
        }

        var normalized = JsonTextNormalizer.Normalize(text);
        var offset = 0;

        foreach (var rawLine in EnumerateLinesPreservingOffsets(normalized))
        {
            var line = rawLine.Line;
            if (string.IsNullOrWhiteSpace(line))
            {
                offset = rawLine.NextOffset;
                continue;
            }

            JsonNode? node = null;
            try
            {
                node = JsonNode.Parse(line);
            }
            catch (JsonException)
            {
                error = JsonDiagnostic.Create(
                    JsonDiagnosticSeverity.Error,
                    "This line is not valid JSON (NDJSON expects one JSON value per line).",
                    normalized,
                    offset,
                    Math.Max(1, line.Length),
                    JsonDiagnosticCodes.InvalidJson);
                return false;
            }

            array.Add(node);
            documentCount++;
            offset = rawLine.NextOffset;
        }

        if (documentCount < 2)
        {
            error = JsonDiagnostic.Create(
                JsonDiagnosticSeverity.Error,
                "Not enough JSON values to be NDJSON (need at least 2 non-empty lines).",
                normalized,
                0,
                Math.Min(1, normalized.Length),
                JsonDiagnosticCodes.InvalidJson);
            return false;
        }

        return true;
    }

    private static bool IsStrictJsonLine(string line)
    {
        try
        {
            using var _ = JsonDocument.Parse(line, new JsonDocumentOptions
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

    private static IEnumerable<string> EnumerateNonEmptyLines(string text)
    {
        foreach (var line in text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            yield return trimmed;
        }
    }

    private readonly record struct LineWithOffset(string Line, int NextOffset);

    private static IEnumerable<LineWithOffset> EnumerateLinesPreservingOffsets(string text)
    {
        // JsonTextNormalizer preserves length 1:1; treat offsets in the normalized text as offsets in the original.
        var normalized = text.Replace("\r\n", "\n").Replace("\r", "\n");
        var start = 0;
        for (var i = 0; i <= normalized.Length; i++)
        {
            if (i == normalized.Length || normalized[i] == '\n')
            {
                var line = normalized[start..i];
                var nextOffset = i == normalized.Length ? i : i + 1;
                yield return new LineWithOffset(line, nextOffset);
                start = nextOffset;
            }
        }
    }
}
