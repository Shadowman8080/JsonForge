using System.Globalization;
using System.Text;
using System.Text.Json;

namespace JsonForge.Core.Json;

/// <summary>
/// Repairs malformed JSON (or JSONC / "JSON-ish" text) into valid JSON, best-effort.
/// Every change is recorded so the UI can show what was fixed and the user can review it.
/// The result is always honest: <see cref="JsonRepairResult.IsValid"/> reflects whether the
/// repaired text actually parses.
/// </summary>
public static class JsonRepairer
{
    public static JsonRepairResult Repair(string? original, JsonRepairOptions? options = null)
    {
        options ??= JsonRepairOptions.Default;

        var source = JsonTextNormalizer.Normalize(original);
        var actions = new List<JsonRepairAction>();
        var lexemes = JsonLexer.Tokenize(source);

        if (options.RemoveComments)
        {
            foreach (var token in lexemes)
            {
                if (token.Kind == JsonLexemeKind.Comment)
                {
                    actions.Add(new JsonRepairAction(
                        JsonRepairActionKind.RemovedComment,
                        "Removed comment",
                        token.Start,
                        token.Length));
                }
            }
        }

        var tokens = lexemes
            .Where(t => t.Kind != JsonLexemeKind.Whitespace && t.Kind != JsonLexemeKind.Comment)
            .ToList();

        var output = new StringBuilder(Math.Max(16, source.Length));
        var frames = new List<Frame>();
        var rootComplete = false;
        var rootSeen = false;
        var index = 0;
        var mutationCount = actions.Count;

        while (index < tokens.Count)
        {
            var token = tokens[index];

            if (rootComplete)
            {
                if (index == tokens.Count - 1)
                {
                    actions.Add(new JsonRepairAction(
                        JsonRepairActionKind.DroppedTrailingContent,
                        "Dropped content after the root value",
                        token.Start,
                        source.Length - token.Start));
                }

                break;
            }

            if (frames.Count == 0)
            {
                HandleRoot(token, frames, output, actions, options, ref rootSeen, ref rootComplete);
                index++;
                continue;
            }

            var frame = frames[^1];
            var reprocess = frame.IsObject
                ? HandleObject(token, tokens, index, frames, output, actions, options, source)
                : HandleArray(token, tokens, index, frames, output, actions, options, source);

            if (!reprocess)
            {
                index++;
            }
        }

        while (frames.Count > 0)
        {
            var frame = frames[^1];
            var closer = frame.IsObject ? '}' : ']';
            output.Append(closer);
            actions.Add(new JsonRepairAction(
                JsonRepairActionKind.ClosedStructure,
                $"Closed unclosed {(frame.IsObject ? '{' : '[')}",
                frame.Offset,
                1));
            frames.RemoveAt(frames.Count - 1);
        }

        var repaired = output.ToString();
        var wasModified = actions.Count > mutationCount;

        if (options.PrettyPrint && TryFormatQuiet(repaired, options.FormatOptions, out var pretty))
        {
            repaired = pretty;
        }

        var isValid = TurnsValid(repaired);
        var remaining = isValid ? [] : JsonValidator.Validate(repaired).Diagnostics;

        return new JsonRepairResult(source, repaired, wasModified, isValid, actions, remaining);
    }

    private static void HandleRoot(
        JsonLexeme token,
        List<Frame> frames,
        StringBuilder output,
        List<JsonRepairAction> actions,
        JsonRepairOptions options,
        ref bool rootSeen,
        ref bool rootComplete)
    {
        if (rootSeen)
        {
            rootComplete = true;
            return;
        }

        if (!IsValueStart(token.Kind))
        {
            actions.Add(new JsonRepairAction(
                JsonRepairActionKind.DroppedToken,
                $"Dropped unexpected '{Truncate(token.Text)}'",
                token.Start,
                token.Length));
            return;
        }

        rootSeen = true;
        EmitValue(token, frames, output, actions, options);
    }

    /// <summary>Returns true when the caller must re-process the same token (a separator was just inserted).</summary>
    private static bool HandleObject(
        JsonLexeme token,
        IReadOnlyList<JsonLexeme> tokens,
        int index,
        List<Frame> frames,
        StringBuilder output,
        List<JsonRepairAction> actions,
        JsonRepairOptions options,
        string source)
    {
        var frame = frames[^1];

        switch ((ObjectState)frame.State)
        {
            case ObjectState.KeyOrEnd:
                if (token.Kind == JsonLexemeKind.ObjectEnd)
                {
                    output.Append('}');
                    frames.RemoveAt(frames.Count - 1);
                    return false;
                }

                if (IsKeyLike(token.Kind))
                {
                    if (token.Kind is JsonLexemeKind.String
                        or JsonLexemeKind.SingleQuotedString
                        or JsonLexemeKind.UnterminatedString)
                    {
                        output.Append(RewriteString(token.Text, actions, null, out _));
                    }
                    else
                    {
                        if (options.QuoteUnquotedKeys)
                        {
                            output.Append(QuoteBody(token.Text));
                            actions.Add(new JsonRepairAction(
                                JsonRepairActionKind.QuotedKey,
                                $"Quoted property name '{Truncate(token.Text)}'",
                                token.Start,
                                token.Length));
                        }
                        else
                        {
                            output.Append(token.Text);
                        }
                    }

                    frame.PendingKey = token.Text;
                    frame.State = (int)ObjectState.Colon;
                    return false;
                }

                actions.Add(new JsonRepairAction(
                    JsonRepairActionKind.DroppedToken,
                    $"Dropped unexpected '{Truncate(token.Text)}'",
                    token.Start,
                    token.Length));
                return false;

            case ObjectState.Colon:
                if (token.Kind == JsonLexemeKind.Colon)
                {
                    output.Append(':');
                    frame.State = (int)ObjectState.Value;
                    return false;
                }

                if (options.InsertMissingColons)
                {
                    output.Append(':');
                    actions.Add(new JsonRepairAction(
                        JsonRepairActionKind.InsertedColon,
                        $"Inserted ':' after '{Truncate(frame.PendingKey ?? string.Empty)}'",
                        frame.PendingKeyOffset,
                        0));
                    frame.State = (int)ObjectState.Value;
                    return true;
                }

                frame.State = (int)ObjectState.Value;
                return true;

            case ObjectState.Value:
                if (!IsValueStart(token.Kind))
                {
                    actions.Add(new JsonRepairAction(
                        JsonRepairActionKind.DroppedToken,
                        $"Dropped unexpected '{Truncate(token.Text)}'",
                        token.Start,
                        token.Length));
                    frame.State = (int)ObjectState.CommaOrEnd;
                    return false;
                }

                EmitValue(token, frames, output, actions, options);
                frame.State = (int)ObjectState.CommaOrEnd;
                return false;

            case ObjectState.CommaOrEnd:
                if (token.Kind == JsonLexemeKind.Comma)
                {
                    var closers = options.RemoveTrailingCommas
                        ? index + 1 < tokens.Count ? tokens[index + 1] : (JsonLexeme?)null
                        : null;

                    if (closers?.Kind == JsonLexemeKind.ObjectEnd)
                    {
                        actions.Add(new JsonRepairAction(
                            JsonRepairActionKind.RemovedTrailingComma,
                            "Removed trailing comma",
                            token.Start,
                            token.Length));
                    }
                    else
                    {
                        output.Append(',');
                    }

                    frame.State = (int)ObjectState.KeyOrEnd;
                    return false;
                }

                if (token.Kind == JsonLexemeKind.ObjectEnd)
                {
                    output.Append('}');
                    frames.RemoveAt(frames.Count - 1);
                    return false;
                }

                if (options.InsertMissingCommas)
                {
                    output.Append(',');
                    actions.Add(new JsonRepairAction(
                        JsonRepairActionKind.InsertedComma,
                        "Inserted missing ','",
                        token.Start,
                        0));
                    frame.State = (int)ObjectState.KeyOrEnd;
                    return true;
                }

                frame.State = (int)ObjectState.KeyOrEnd;
                return true;
        }

        return false;
    }

    /// <summary>Returns true when the caller must re-process the same token (a separator was just inserted).</summary>
    private static bool HandleArray(
        JsonLexeme token,
        IReadOnlyList<JsonLexeme> tokens,
        int index,
        List<Frame> frames,
        StringBuilder output,
        List<JsonRepairAction> actions,
        JsonRepairOptions options,
        string source)
    {
        var frame = frames[^1];

        switch ((ArrayState)frame.State)
        {
            case ArrayState.ValueOrEnd:
                if (token.Kind == JsonLexemeKind.ArrayEnd)
                {
                    output.Append(']');
                    frames.RemoveAt(frames.Count - 1);
                    return false;
                }

                if (token.Kind == JsonLexemeKind.Comma)
                {
                    actions.Add(new JsonRepairAction(
                        JsonRepairActionKind.DroppedToken,
                        "Dropped unexpected ','",
                        token.Start,
                        token.Length));
                    return false;
                }

                if (!IsValueStart(token.Kind))
                {
                    actions.Add(new JsonRepairAction(
                        JsonRepairActionKind.DroppedToken,
                        $"Dropped unexpected '{Truncate(token.Text)}'",
                        token.Start,
                        token.Length));
                    return false;
                }

                EmitValue(token, frames, output, actions, options);
                frame.State = (int)ArrayState.CommaOrEnd;
                return false;

            case ArrayState.CommaOrEnd:
                if (token.Kind == JsonLexemeKind.Comma)
                {
                    var closers = options.RemoveTrailingCommas
                        ? index + 1 < tokens.Count ? tokens[index + 1] : (JsonLexeme?)null
                        : null;

                    if (closers?.Kind == JsonLexemeKind.ArrayEnd)
                    {
                        actions.Add(new JsonRepairAction(
                            JsonRepairActionKind.RemovedTrailingComma,
                            "Removed trailing comma",
                            token.Start,
                            token.Length));
                    }
                    else
                    {
                        output.Append(',');
                    }

                    frame.State = (int)ArrayState.ValueOrEnd;
                    return false;
                }

                if (token.Kind == JsonLexemeKind.ArrayEnd)
                {
                    output.Append(']');
                    frames.RemoveAt(frames.Count - 1);
                    return false;
                }

                if (options.InsertMissingCommas)
                {
                    output.Append(',');
                    actions.Add(new JsonRepairAction(
                        JsonRepairActionKind.InsertedComma,
                        "Inserted missing ','",
                        token.Start,
                        0));
                    frame.State = (int)ArrayState.ValueOrEnd;
                    return true;
                }

                frame.State = (int)ArrayState.ValueOrEnd;
                return true;
        }

        return false;
    }

    private static void EmitValue(
        JsonLexeme token,
        List<Frame> frames,
        StringBuilder output,
        List<JsonRepairAction> actions,
        JsonRepairOptions options)
    {
        switch (token.Kind)
        {
            case JsonLexemeKind.ObjectStart:
                output.Append('{');
                frames.Add(Frame.Object(token.Start));
                break;

            case JsonLexemeKind.ArrayStart:
                output.Append('[');
                frames.Add(Frame.Array(token.Start));
                break;

            case JsonLexemeKind.String:
            case JsonLexemeKind.SingleQuotedString:
            case JsonLexemeKind.UnterminatedString:
                if (!options.NormalizeQuotes &&
                    token.Kind == JsonLexemeKind.String)
                {
                    output.Append(token.Text);
                }
                else
                {
                    output.Append(RewriteString(token.Text, actions, null, out _));
                }

                break;

            case JsonLexemeKind.Number:
                var number = NormalizeNumber(token.Text, out var numberChanged);
                output.Append(number);
                if (numberChanged)
                {
                    actions.Add(new JsonRepairAction(
                        JsonRepairActionKind.NormalizedNumber,
                        $"Normalized number '{Truncate(token.Text)}' to '{number}'",
                        token.Start,
                        token.Length));
                }

                break;

            case JsonLexemeKind.Bool:
            case JsonLexemeKind.Null:
                var canonical = token.Kind == JsonLexemeKind.Bool
                    ? token.Text.ToLowerInvariant()
                    : "null";
                output.Append(canonical);
                if (token.Text != canonical)
                {
                    actions.Add(new JsonRepairAction(
                        JsonRepairActionKind.NormalizedLiteral,
                        $"Replaced '{Truncate(token.Text)}' with '{canonical}'",
                        token.Start,
                        token.Length));
                }

                break;

            case JsonLexemeKind.Bareword:
                var lower = token.Text.ToLowerInvariant();
                if (lower is "infinity" or "nan" or "-infinity")
                {
                    output.Append("null");
                    actions.Add(new JsonRepairAction(
                        JsonRepairActionKind.NormalizedLiteral,
                        $"Replaced '{Truncate(token.Text)}' with 'null'",
                        token.Start,
                        token.Length));
                }
                else if (options.QuoteBareValues)
                {
                    output.Append(QuoteBody(token.Text));
                    actions.Add(new JsonRepairAction(
                        JsonRepairActionKind.QuotedValue,
                        $"Quoted value '{Truncate(token.Text)}'",
                        token.Start,
                        token.Length));
                }

                break;
        }
    }

    private static bool IsKeyLike(JsonLexemeKind kind) => kind is
        JsonLexemeKind.String or
        JsonLexemeKind.SingleQuotedString or
        JsonLexemeKind.UnterminatedString or
        JsonLexemeKind.Bareword or
        JsonLexemeKind.Number or
        JsonLexemeKind.Bool or
        JsonLexemeKind.Null;

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

    /// <summary>
    /// Rewrites any string lexeme (double, single or unterminated quoted) into a valid
    /// double-quoted JSON string with all control characters and quotes escaped.
    /// </summary>
    private static string RewriteString(
        string raw,
        List<JsonRepairAction>? actions,
        string? source,
        out bool changed)
    {
        changed = false;
        var quote = raw.Length > 0 ? raw[0] : '"';
        var closed = raw.Length >= 2 && raw[^1] == quote;
        var start = Math.Min(1, raw.Length);
        var end = closed ? raw.Length - 1 : raw.Length;
        var inner = raw[start..end];

        var body = EscapeStringBody(inner, out var bodyChanged);
        var rebuilt = '"' + body + '"';
        changed = !closed || quote != '"' || bodyChanged || rebuilt != raw;

        if (changed && actions is not null)
        {
            actions.Add(new JsonRepairAction(
                JsonRepairActionKind.RewroteString,
                "Rewrote string to valid JSON escaping",
                start - 1,
                raw.Length));
        }

        return rebuilt;
    }

    private static string QuoteBody(string inner)
        => '"' + EscapeStringBody(inner, out _) + '"';

    private static string EscapeStringBody(string inner, out bool changed)
    {
        changed = false;
        var sb = new StringBuilder(inner.Length + 4);

        for (var i = 0; i < inner.Length; i++)
        {
            var c = inner[i];

            if (c == '\\' && i + 1 < inner.Length)
            {
                var next = inner[i + 1];
                switch (next)
                {
                    case '"':
                        sb.Append("\\\"");
                        i++;
                        break;
                    case '\'':
                        sb.Append('\'');
                        changed = true;
                        i++;
                        break;
                    case '\\':
                        sb.Append("\\\\");
                        i++;
                        break;
                    case '/':
                        sb.Append('/');
                        changed = true;
                        i++;
                        break;
                    case 'n':
                    case 'r':
                    case 't':
                    case 'b':
                    case 'f':
                        sb.Append('\\').Append(next);
                        i++;
                        break;
                    case 'u':
                        if (i + 5 < inner.Length && inner.AsSpan(i + 2, 4).IndexOfAnyExcept(HexDigits) < 0)
                        {
                            sb.Append(inner, i, 6);
                            i += 5;
                        }
                        else
                        {
                            sb.Append('\\');
                            changed = true;
                            i++; // consume the backslash, next char printed literally
                        }

                        break;
                    default:
                        // Invalid escape: keep the character, drop the backslash.
                        sb.Append(next);
                        changed = true;
                        i++;
                        break;
                }

                continue;
            }

            if (c == '"')
            {
                sb.Append("\\\"");
                changed = true;
            }
            else if (char.IsControl(c))
            {
                sb.Append(c switch
                {
                    '\n' => "\\n",
                    '\r' => "\\r",
                    '\t' => "\\t",
                    '\b' => "\\b",
                    '\f' => "\\f",
                    _ => "\\u" + ((int)c).ToString("X4", CultureInfo.InvariantCulture),
                });
                changed = true;
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    private static readonly char[] HexDigits = ['0', '1', '2', '3', '4', '5', '6', '7', '8', '9', 'a', 'b', 'c', 'd', 'e', 'f', 'A', 'B', 'C', 'D', 'E', 'F'];

    private static string NormalizeNumber(string token, out bool changed)
    {
        changed = false;

        var s = token;
        var i = 0;
        var minus = false;
        if (i < s.Length && (s[i] == '-' || s[i] == '+'))
        {
            minus = s[i] == '-';
            changed |= s[i] == '+';
            i++;
        }

        var intEnd = i;
        while (intEnd < s.Length && char.IsDigit(s[intEnd]))
        {
            intEnd++;
        }

        var fracEnd = intEnd;
        if (fracEnd < s.Length && s[fracEnd] == '.')
        {
            fracEnd++;
            while (fracEnd < s.Length && char.IsDigit(s[fracEnd]))
            {
                fracEnd++;
            }
        }

        var exponent = s[fracEnd..];
        var validExponent = exponent.Length == 0 ||
                            (exponent[0] is 'e' or 'E' &&
                             (exponent.Length == 1 || AreDigits(exponent[1..]) ||
                              (exponent.Length > 1 && exponent[1] is '+' or '-' && AreDigits(exponent[2..]))));

        if (!validExponent)
        {
            // Not a well-formed number; hand back unchanged and let validation complain.
            return token;
        }

        var intPart = s[i..intEnd];
        var fracPart = s[intEnd..fracEnd];

        if (intPart.Length == 0 && fracPart.Length == 0)
        {
            return token;
        }

        // "007" -> "7", leading zeros are not valid JSON
        if (intPart.Length > 1)
        {
            var trimmed = intPart.TrimStart('0');
            if (trimmed.Length == 0)
            {
                trimmed = "0";
            }

            if (trimmed.Length != intPart.Length)
            {
                changed = true;
                intPart = trimmed;
            }
        }

        // Drop a '.' with no digits ("1." -> "1")
        if (fracPart == ".")
        {
            fracPart = string.Empty;
            changed = true;
        }

        // ".5" -> "0.5"
        if (intPart.Length == 0 && fracPart.Length > 0)
        {
            intPart = "0";
            changed = true;
        }

        var normalized = (minus ? "-" : string.Empty) + intPart + fracPart + exponent;

        if (double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
        {
            changed |= normalized != token;
            return normalized;
        }

        changed |= normalized != token;
        return normalized;
    }

    private static bool AreDigits(string s)
    {
        foreach (var c in s)
        {
            if (!char.IsDigit(c))
            {
                return false;
            }
        }

        return s.Length > 0;
    }

    private static bool TurnsValid(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 256 });
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryFormatQuiet(string text, JsonFormatOptions options, out string formatted)
        => JsonFormatter.TryFormat(text, options, out formatted, out _);

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

    private sealed class Frame
    {
        public bool IsObject { get; init; }

        public int State { get; set; }

        public int Offset { get; init; }

        public string? PendingKey { get; set; }

        public int PendingKeyOffset { get; set; }

        public static Frame Object(int offset) => new() { IsObject = true, State = (int)ObjectState.KeyOrEnd, Offset = offset };

        public static Frame Array(int offset) => new() { IsObject = false, State = (int)ArrayState.ValueOrEnd, Offset = offset };
    }
}