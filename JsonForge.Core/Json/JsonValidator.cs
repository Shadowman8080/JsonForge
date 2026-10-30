using System.Text.Json;

namespace JsonForge.Core.Json;

public sealed record JsonValidationResult(bool IsValid, IReadOnlyList<JsonDiagnostic> Diagnostics)
{
    public bool HasErrors => Diagnostics.Any(d => d.Severity == JsonDiagnosticSeverity.Error);

    public static JsonValidationResult Valid { get; } = new(true, []);
}

/// <summary>
/// Validates JSON text and produces actionable diagnostics.
/// </summary>
/// <remarks>
/// <see cref="JsonDocument"/> only ever reports the first syntax error, which makes for a poor
/// editing experience. So validation runs in two stages: a tolerant structural scan that reports
/// every recoverable problem it can find (unclosed brackets, trailing commas, comments, unquoted
/// keys, ...) and then the authoritative parser, whose error is merged in when the scan did not
/// already find an error on the same line.
/// </remarks>
public static class JsonValidator
{
    public static JsonValidationResult Validate(string? text)
        => Validate(text, JsonValidationOptions.Strict);

    public static JsonValidationResult Validate(string? text, JsonValidationOptions? options)
    {
        options ??= JsonValidationOptions.Strict;
        var diagnostics = new List<JsonDiagnostic>();

        if (string.IsNullOrWhiteSpace(text))
        {
            diagnostics.Add(JsonDiagnostic.Create(
                JsonDiagnosticSeverity.Error,
                "Document is empty. Expected a JSON value.",
                text,
                0,
                0,
                JsonDiagnosticCodes.InvalidJson));
            return new JsonValidationResult(false, diagnostics);
        }

        StructuralScan(text, diagnostics, options);

        JsonDocument? document = null;
        try
        {
            document = JsonDocument.Parse(text!, new JsonDocumentOptions
            {
                AllowTrailingCommas = options.AllowTrailingCommas,
                CommentHandling = options.AllowComments ? JsonCommentHandling.Skip : JsonCommentHandling.Disallow,
                MaxDepth = 256,
            });
        }
        catch (JsonException ex)
        {
            var offset = ex.BytePositionInLine is { } bytePos
                ? OffsetFromLineColumn(text!, (int)(ex.LineNumber ?? 0), (int)bytePos)
                : 0;
            var (line, _) = SourceText.GetLineColumn(text, offset);

            var alreadyReported = diagnostics.Any(d =>
                d.Severity == JsonDiagnosticSeverity.Error && d.Line == line);

            if (!alreadyReported)
            {
                diagnostics.Add(JsonDiagnostic.Create(
                    JsonDiagnosticSeverity.Error,
                    CleanParserMessage(ex.Message, offset),
                    text,
                    offset,
                    1,
                    JsonDiagnosticCodes.InvalidJson));
            }
        }

        var isValid = document is not null;
        document?.Dispose();

        diagnostics.Sort((a, b) => a.Offset.CompareTo(b.Offset));
        return new JsonValidationResult(isValid, diagnostics);
    }

    private static int OffsetFromLineColumn(string text, int zeroBasedLine, int zeroBasedByteColumn)
        => SourceText.GetOffset(text, zeroBasedLine + 1, zeroBasedByteColumn + 1);

    /// <summary>Trims the parser's byte-offset suffix, which is confusing once we show line/column.</summary>
    private static string CleanParserMessage(string message, int offset)
    {
        var lineInfo = message.IndexOf(" LineNumber:", StringComparison.Ordinal);
        if (lineInfo > 0)
        {
            message = message[..lineInfo];
        }

        return message.TrimEnd('.') + ".";
    }

    private static void StructuralScan(string text, List<JsonDiagnostic> diagnostics, JsonValidationOptions options)
    {
        var tokens = JsonLexer.Tokenize(text);
        var significant = new List<JsonLexeme>();
        foreach (var token in tokens)
        {
            if (token.Kind == JsonLexemeKind.Comment)
            {
                if (!options.AllowComments)
                {
                    diagnostics.Add(JsonDiagnostic.Create(
                        JsonDiagnosticSeverity.Warning,
                        "Comments are not valid JSON. Remove them or use JSON with comments (JSONC).",
                        text,
                        token.Start,
                        token.Length,
                        JsonDiagnosticCodes.CommentNotAllowed));
                }
                continue;
            }

            if (!token.IsTrivia)
            {
                significant.Add(token);
            }
        }

        if (significant.Count == 0)
        {
            return;
        }

        var frames = new List<Frame>();
        var rootSeen = false;
        var index = 0;

        while (index < significant.Count)
        {
            var token = significant[index];

            if (frames.Count == 0)
            {
                if (rootSeen)
                {
                    if (token.Kind != JsonLexemeKind.ObjectEnd && token.Kind != JsonLexemeKind.ArrayEnd)
                    {
                        diagnostics.Add(JsonDiagnostic.Create(
                            JsonDiagnosticSeverity.Error,
                            "Unexpected content after the root value.",
                            text,
                            token.Start,
                            token.Length,
                            JsonDiagnosticCodes.TrailingContent));
                        break;
                    }

                    index++;
                    continue;
                }

                if (!IsValueStart(token.Kind))
                {
                    diagnostics.Add(JsonDiagnostic.Create(
                        JsonDiagnosticSeverity.Error,
                        $"Unexpected '{Truncate(token.Text)}'.",
                        text,
                        token.Start,
                        token.Length,
                        JsonDiagnosticCodes.InvalidJson));
                    break;
                }

                ReportLiteralProblems(token, text, diagnostics, asValue: true, options);
                rootSeen = true;

                if (token.Kind is JsonLexemeKind.ObjectStart or JsonLexemeKind.ArrayStart)
                {
                    frames.Add(new Frame(token.Kind == JsonLexemeKind.ObjectStart, token.Start));
                }

                index++;
                continue;
            }

            var frame = frames[^1];
            var consumed = HandleToken(token, significant, ref index, frame, frames, text, diagnostics, options);
            if (!consumed)
            {
                index++;
            }
        }

        foreach (var frame in frames)
        {
            diagnostics.Add(JsonDiagnostic.Create(
                JsonDiagnosticSeverity.Error,
                $"Unclosed '{(frame.IsObject ? '{' : '[')}'. Expected a matching '{(frame.IsObject ? '}' : ']')}'.",
                text,
                frame.Offset,
                1,
                JsonDiagnosticCodes.UnclosedStructure));
        }
    }

    /// <summary>Returns true when the token was consumed (including reprocessing) and the loop must not advance.</summary>
    private static bool HandleToken(
        JsonLexeme token,
        List<JsonLexeme> tokens,
        ref int index,
        Frame frame,
        List<Frame> frames,
        string text,
        List<JsonDiagnostic> diagnostics,
        JsonValidationOptions options)
    {
        if (frame.IsObject)
        {
            switch ((ObjectState)frame.State)
            {
                case ObjectState.KeyOrEnd:
                    if (token.Kind == JsonLexemeKind.ObjectEnd)
                    {
                        frames.RemoveAt(frames.Count - 1);
                        return false;
                    }

                    if (token.Kind == JsonLexemeKind.Comma)
                    {
                        diagnostics.Add(JsonDiagnostic.Create(
                            JsonDiagnosticSeverity.Warning,
                            "Unexpected ','.",
                            text,
                            token.Start,
                            token.Length,
                            JsonDiagnosticCodes.TrailingComma));
                        return false;
                    }

                    if (token.Kind == JsonLexemeKind.String)
                    {
                        frame.PendingKey = Unquote(token.Text);
                        if (options.ReportDuplicateKeys)
                        {
                            ReportDuplicateKey(frame, frame.PendingKey, token.Start, token.Length, text, diagnostics);
                        }
                    }
                    else if (IsValueStart(token.Kind))
                    {
                        frame.PendingKey = token.Text;
                        if (options.WarnOnUnquotedKeys)
                        {
                            diagnostics.Add(JsonDiagnostic.Create(
                                JsonDiagnosticSeverity.Warning,
                                $"Unquoted key '{Truncate(token.Text)}'. JSON requires double-quoted property names.",
                                text,
                                token.Start,
                                token.Length,
                                JsonDiagnosticCodes.UnquotedKey));
                        }

                        if (options.ReportDuplicateKeys)
                        {
                            ReportDuplicateKey(frame, frame.PendingKey, token.Start, token.Length, text, diagnostics);
                        }
                    }
                    else
                    {
                        diagnostics.Add(JsonDiagnostic.Create(
                            JsonDiagnosticSeverity.Error,
                            $"Expected a property name but found '{Truncate(token.Text)}'.",
                            text,
                            token.Start,
                            token.Length,
                            JsonDiagnosticCodes.InvalidJson));
                        return false;
                    }

                    frame.State = (int)ObjectState.Colon;
                    return false;

                case ObjectState.Colon:
                    if (token.Kind == JsonLexemeKind.Colon)
                    {
                        frame.State = (int)ObjectState.Value;
                        return false;
                    }

                    diagnostics.Add(JsonDiagnostic.Create(
                        JsonDiagnosticSeverity.Error,
                        $"Expected ':' after property '{Truncate(frame.PendingKey ?? string.Empty)}'.",
                        text,
                        token.Start,
                        token.Length,
                        JsonDiagnosticCodes.InvalidJson));

                    // Fall through and treat this token as the property value.
                    return HandleObjectValue(token, frame, frames, text, diagnostics, options);

                case ObjectState.Value:
                    return HandleObjectValue(token, frame, frames, text, diagnostics, options);

                case ObjectState.CommaOrEnd:
                    if (token.Kind == JsonLexemeKind.Comma)
                    {
                        var next = Peek(tokens, index);
                        if (next is { Kind: JsonLexemeKind.ObjectEnd })
                        {
                            diagnostics.Add(JsonDiagnostic.Create(
                                JsonDiagnosticSeverity.Warning,
                                "Trailing comma before '}' is not valid JSON.",
                                text,
                                token.Start,
                                token.Length,
                                JsonDiagnosticCodes.TrailingComma));
                        }

                        frame.State = (int)ObjectState.KeyOrEnd;
                        return false;
                    }

                    if (token.Kind == JsonLexemeKind.ObjectEnd)
                    {
                        frames.RemoveAt(frames.Count - 1);
                        return false;
                    }

                    diagnostics.Add(JsonDiagnostic.Create(
                        JsonDiagnosticSeverity.Warning,
                        "Missing ',' between properties.",
                        text,
                        token.Start,
                        0,
                        JsonDiagnosticCodes.InvalidJson));

                    // Reprocess the token as the next property name.
                    frame.State = (int)ObjectState.KeyOrEnd;
                    return HandleToken(token, tokens, ref index, frame, frames, text, diagnostics, options);
            }

            return false;
        }

        switch ((ArrayState)frame.State)
        {
            case ArrayState.ValueOrEnd:
                if (token.Kind == JsonLexemeKind.ArrayEnd)
                {
                    frames.RemoveAt(frames.Count - 1);
                    return false;
                }

                if (token.Kind == JsonLexemeKind.Comma)
                {
                    diagnostics.Add(JsonDiagnostic.Create(
                        JsonDiagnosticSeverity.Warning,
                        "Unexpected ','.",
                        text,
                        token.Start,
                        token.Length,
                        JsonDiagnosticCodes.TrailingComma));
                    return false;
                }

                if (!IsValueStart(token.Kind))
                {
                    diagnostics.Add(JsonDiagnostic.Create(
                        JsonDiagnosticSeverity.Error,
                        $"Unexpected '{Truncate(token.Text)}' in array.",
                        text,
                        token.Start,
                        token.Length,
                        JsonDiagnosticCodes.InvalidJson));
                    return false;
                }

                return HandleArrayValue(token, frame, frames, text, diagnostics, options);

            case ArrayState.CommaOrEnd:
                if (token.Kind == JsonLexemeKind.Comma)
                {
                    var next = Peek(tokens, index);
                    if (next is { Kind: JsonLexemeKind.ArrayEnd })
                    {
                        diagnostics.Add(JsonDiagnostic.Create(
                            JsonDiagnosticSeverity.Warning,
                            "Trailing comma before ']' is not valid JSON.",
                            text,
                            token.Start,
                            token.Length,
                            JsonDiagnosticCodes.TrailingComma));
                    }

                    frame.State = (int)ArrayState.ValueOrEnd;
                    return false;
                }

                if (token.Kind == JsonLexemeKind.ArrayEnd)
                {
                    frames.RemoveAt(frames.Count - 1);
                    return false;
                }

                diagnostics.Add(JsonDiagnostic.Create(
                    JsonDiagnosticSeverity.Warning,
                    "Missing ',' between array items.",
                    text,
                    token.Start,
                    0,
                    JsonDiagnosticCodes.InvalidJson));
                return HandleArrayValue(token, frame, frames, text, diagnostics, options);
        }

        return false;
    }

    private static bool HandleObjectValue(
        JsonLexeme token,
        Frame frame,
        List<Frame> frames,
        string text,
        List<JsonDiagnostic> diagnostics,
        JsonValidationOptions options)
    {
        if (!IsValueStart(token.Kind))
        {
            diagnostics.Add(JsonDiagnostic.Create(
                JsonDiagnosticSeverity.Error,
                $"Expected a value for property '{Truncate(frame.PendingKey ?? string.Empty)}'.",
                text,
                token.Start,
                token.Length,
                JsonDiagnosticCodes.InvalidJson));
            frame.State = (int)ObjectState.CommaOrEnd;
            return false;
        }

        ReportLiteralProblems(token, text, diagnostics, asValue: true, options);
        frame.State = (int)ObjectState.CommaOrEnd;

        if (token.Kind is JsonLexemeKind.ObjectStart or JsonLexemeKind.ArrayStart)
        {
            frames.Add(new Frame(token.Kind == JsonLexemeKind.ObjectStart, token.Start));
        }

        return false;
    }

    private static bool HandleArrayValue(
        JsonLexeme token,
        Frame frame,
        List<Frame> frames,
        string text,
        List<JsonDiagnostic> diagnostics,
        JsonValidationOptions options)
    {
        if (!IsValueStart(token.Kind))
        {
            frame.State = (int)ArrayState.CommaOrEnd;
            return false;
        }

        ReportLiteralProblems(token, text, diagnostics, asValue: true, options);
        frame.State = (int)ArrayState.CommaOrEnd;

        if (token.Kind is JsonLexemeKind.ObjectStart or JsonLexemeKind.ArrayStart)
        {
            frames.Add(new Frame(token.Kind == JsonLexemeKind.ObjectStart, token.Start));
        }

        return false;
    }

    private static void ReportLiteralProblems(
        JsonLexeme token,
        string text,
        List<JsonDiagnostic> diagnostics,
        bool asValue,
        JsonValidationOptions options)
    {
        switch (token.Kind)
        {
            case JsonLexemeKind.UnterminatedString:
                diagnostics.Add(JsonDiagnostic.Create(
                    JsonDiagnosticSeverity.Error,
                    "Unterminated string. Add a closing quote.",
                    text,
                    token.Start,
                    token.Length,
                    JsonDiagnosticCodes.UnterminatedString));
                break;

            case JsonLexemeKind.SingleQuotedString:
                if (options.WarnOnSingleQuotes)
                {
                    diagnostics.Add(JsonDiagnostic.Create(
                        JsonDiagnosticSeverity.Warning,
                        "Single-quoted string. JSON requires double quotes.",
                        text,
                        token.Start,
                        token.Length,
                        JsonDiagnosticCodes.SingleQuotedString));
                }
                break;

            case JsonLexemeKind.Bareword when asValue:
                if (options.WarnOnNonStandardLiterals)
                {
                    diagnostics.Add(JsonDiagnostic.Create(
                        JsonDiagnosticSeverity.Warning,
                        $"'{Truncate(token.Text)}' is not a valid JSON value.",
                        text,
                        token.Start,
                        token.Length,
                        JsonDiagnosticCodes.NonStandardLiteral));
                }
                break;

            case JsonLexemeKind.Bool or JsonLexemeKind.Null:
                var canonical = token.Kind == JsonLexemeKind.Bool
                    ? token.Text.ToLowerInvariant()
                    : "null";
                if (token.Text != canonical)
                {
                    if (options.WarnOnNonStandardLiterals)
                    {
                        diagnostics.Add(JsonDiagnostic.Create(
                            JsonDiagnosticSeverity.Warning,
                            $"'{Truncate(token.Text)}' is not valid JSON. Use '{canonical}'.",
                            text,
                            token.Start,
                            token.Length,
                            JsonDiagnosticCodes.NonStandardLiteral));
                    }
                }

                break;
        }
    }

    private static void ReportDuplicateKey(
        Frame frame,
        string? key,
        int offset,
        int length,
        string text,
        List<JsonDiagnostic> diagnostics)
    {
        if (string.IsNullOrEmpty(key))
        {
            return;
        }

        frame.Keys ??= new Dictionary<string, int>(StringComparer.Ordinal);
        if (frame.Keys.TryGetValue(key, out var firstOffset))
        {
            diagnostics.Add(JsonDiagnostic.Create(
                JsonDiagnosticSeverity.Warning,
                $"Duplicate key '{Truncate(key)}'. Most parsers keep only the last value.",
                text,
                offset,
                length,
                JsonDiagnosticCodes.DuplicateKey));
        }
        else
        {
            frame.Keys[key] = offset;
        }
    }

    private static JsonLexeme? Peek(List<JsonLexeme> tokens, int index)
        => index + 1 < tokens.Count ? tokens[index + 1] : null;

    private static bool IsValueStart(JsonLexemeKind kind) => kind is
        JsonLexemeKind.String or
        JsonLexemeKind.SingleQuotedString or
        JsonLexemeKind.UnterminatedString or
        JsonLexemeKind.Number or
        JsonLexemeKind.Bool or
        JsonLexemeKind.Null or
        JsonLexemeKind.Bareword or
        JsonLexemeKind.ObjectStart or
        JsonLexemeKind.ArrayStart;

    internal static string Unquote(string raw)
    {
        if (raw.Length < 2)
        {
            return raw;
        }

        var inner = raw[1..^1];
        return inner.Replace("\\\"", "\"").Replace("\\'", "'").Replace("\\\\", "\\");
    }

    private static string Truncate(string value)
        => value.Length <= 24 ? value : value[..21] + "...";

    private enum ObjectState
    {
        KeyOrEnd,
        Colon,
        Value,
        CommaOrEnd,
    }

    private enum ArrayState
    {
        ValueOrEnd,
        CommaOrEnd,
    }

    private sealed class Frame(bool isObject, int offset)
    {
        public bool IsObject { get; } = isObject;

        public int Offset { get; } = offset;

        public int State { get; set; }

        public string? PendingKey { get; set; }

        public Dictionary<string, int>? Keys { get; set; }
    }
}
