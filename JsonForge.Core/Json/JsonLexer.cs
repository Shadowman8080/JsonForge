using System.Globalization;

namespace JsonForge.Core.Json;

/// <summary>
/// A non-throwing JSON/JSONC lexer. Used for syntax highlighting, structural analysis
/// and as the front end of the repair pipeline.
/// </summary>
public static class JsonLexer
{
    private static readonly char[] Delimiters =
        ['{', '}', '[', ']', ':', ',', '"', '\''];

    public static IReadOnlyList<JsonLexeme> Tokenize(string? text)
    {
        var result = new List<JsonLexeme>();
        if (string.IsNullOrEmpty(text))
        {
            return result;
        }

        var n = text.Length;
        var i = 0;

        while (i < n)
        {
            var c = text[i];

            if (char.IsWhiteSpace(c))
            {
                var start = i;
                while (i < n && char.IsWhiteSpace(text[i]))
                {
                    i++;
                }

                result.Add(new JsonLexeme(JsonLexemeKind.Whitespace, start, i - start, text[start..i]));
                continue;
            }

            if (c == '/' && i + 1 < n && (text[i + 1] == '/' || text[i + 1] == '*'))
            {
                var start = i;
                if (text[i + 1] == '/')
                {
                    i += 2;
                    while (i < n && text[i] is not ('\n' or '\r'))
                    {
                        i++;
                    }
                }
                else
                {
                    i += 2;
                    while (i + 1 < n && !(text[i] == '*' && text[i + 1] == '/'))
                    {
                        i++;
                    }

                    i = Math.Min(n, i + 2);
                }

                result.Add(new JsonLexeme(JsonLexemeKind.Comment, start, i - start, text[start..i]));
                continue;
            }

            switch (c)
            {
                case '{':
                    result.Add(new JsonLexeme(JsonLexemeKind.ObjectStart, i, 1, "{"));
                    i++;
                    continue;
                case '}':
                    result.Add(new JsonLexeme(JsonLexemeKind.ObjectEnd, i, 1, "}"));
                    i++;
                    continue;
                case '[':
                    result.Add(new JsonLexeme(JsonLexemeKind.ArrayStart, i, 1, "["));
                    i++;
                    continue;
                case ']':
                    result.Add(new JsonLexeme(JsonLexemeKind.ArrayEnd, i, 1, "]"));
                    i++;
                    continue;
                case ':':
                    result.Add(new JsonLexeme(JsonLexemeKind.Colon, i, 1, ":"));
                    i++;
                    continue;
                case ',':
                    result.Add(new JsonLexeme(JsonLexemeKind.Comma, i, 1, ","));
                    i++;
                    continue;
            }

            if (c is '"' or '\'')
            {
                var start = i;
                var quote = c;
                i++;

                var closed = false;
                while (i < n)
                {
                    if (text[i] == '\\')
                    {
                        i += 2;
                        continue;
                    }

                    if (text[i] == quote)
                    {
                        i++;
                        closed = true;
                        break;
                    }

                    i++;
                }

                if (i > n)
                {
                    i = n;
                }

                var kind = (quote, closed) switch
                {
                    ('"', true) => JsonLexemeKind.String,
                    ('\'', true) => JsonLexemeKind.SingleQuotedString,
                    _ => JsonLexemeKind.UnterminatedString,
                };

                result.Add(new JsonLexeme(kind, start, i - start, text[start..i]));
                continue;
            }

            var bareStart = i;
            while (i < n && !char.IsWhiteSpace(text[i]) && Array.IndexOf(Delimiters, text[i]) < 0)
            {
                i++;
            }

            if (i == bareStart)
            {
                // A delimiter we do not understand (should not happen); consume defensively.
                i++;
            }

            var bare = text[bareStart..i];
            result.Add(new JsonLexeme(ClassifyBareword(bare), bareStart, i - bareStart, bare));
        }

        return result;
    }

    internal static JsonLexemeKind ClassifyBareword(string token)
    {
        if (token.Length == 0)
        {
            return JsonLexemeKind.Bareword;
        }

        switch (token.ToLowerInvariant())
        {
            // Python-style literals are treated as booleans/nulls so repair can map them.
            case "true":
            case "false":
                return JsonLexemeKind.Bool;
            case "null":
            case "none":
                return JsonLexemeKind.Null;
        }

        return LooksNumeric(token) ? JsonLexemeKind.Number : JsonLexemeKind.Bareword;
    }

    private static bool LooksNumeric(string token)
    {
        var first = token[0];
        if (first is not ('-' or '+' or '.' or >= '0' and <= '9'))
        {
            return false;
        }

        return double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
    }
}
