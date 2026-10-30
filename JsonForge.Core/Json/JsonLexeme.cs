namespace JsonForge.Core.Json;

/// <summary>
/// Lexical categories produced by <see cref="JsonLexer"/>. The lexer is deliberately
/// tolerant: it never throws and will happily tokenize text that is not valid JSON.
/// That is what lets validation, repair and highlighting all share one scanner.
/// </summary>
public enum JsonLexemeKind
{
    Whitespace,
    Comment,

    ObjectStart,
    ObjectEnd,
    ArrayStart,
    ArrayEnd,

    Colon,
    Comma,

    /// <summary>A well-formed double-quoted string.</summary>
    String,

    /// <summary>A single-quoted string. Not valid JSON; a repair candidate.</summary>
    SingleQuotedString,

    /// <summary>A string literal that ran to end of input without a closing quote.</summary>
    UnterminatedString,

    Number,
    Bool,
    Null,

    /// <summary>An unquoted token such as an object key or a stray identifier.</summary>
    Bareword,
}

/// <summary>A span of source text with its lexical category.</summary>
public readonly record struct JsonLexeme(JsonLexemeKind Kind, int Start, int Length, string Text)
{
    public int End => Start + Length;

    public bool IsTrivia => Kind is JsonLexemeKind.Whitespace or JsonLexemeKind.Comment;
}
