using FluentAssertions;
using JsonForge.Core.Json;
using NUnit.Framework;

namespace JsonForge.Tests.Json;

[TestFixture]
public class JsonLexerTests
{
    [Test]
    public void Tokenize_ValidJson_ProducesExpectedKinds()
    {
        var tokens = JsonLexer.Tokenize("""{"a": [1, true, null]}""");

        tokens.Where(t => !t.IsTrivia).Select(t => t.Kind).Should().Equal(
            JsonLexemeKind.ObjectStart,
            JsonLexemeKind.String,
            JsonLexemeKind.Colon,
            JsonLexemeKind.ArrayStart,
            JsonLexemeKind.Number,
            JsonLexemeKind.Comma,
            JsonLexemeKind.Bool,
            JsonLexemeKind.Comma,
            JsonLexemeKind.Null,
            JsonLexemeKind.ArrayEnd,
            JsonLexemeKind.ObjectEnd);
    }

    [Test]
    public void Tokenize_CommentsAndWhitespace_AreTrivia()
    {
        var tokens = JsonLexer.Tokenize("  // comment\n { } ");

        tokens.Should().Contain(t => t.Kind == JsonLexemeKind.Comment);
        tokens.Should().Contain(t => t.Kind == JsonLexemeKind.Whitespace);
        tokens.Where(t => !t.IsTrivia).Select(t => t.Kind).Should().Equal(
            JsonLexemeKind.ObjectStart,
            JsonLexemeKind.ObjectEnd);
    }

    [Test]
    public void Tokenize_SingleQuotedAndUnterminatedStrings_AreClassified()
    {
        var tokens = JsonLexer.Tokenize("{'a': \"open}");

        tokens.Should().Contain(t => t.Kind == JsonLexemeKind.SingleQuotedString);
        tokens.Should().Contain(t => t.Kind == JsonLexemeKind.UnterminatedString);
    }

    [Test]
    public void Tokenize_Barewords_IncludePythonLiterals()
    {
        var tokens = JsonLexer.Tokenize("x True None");

        tokens.Where(t => !t.IsTrivia).Select(t => t.Kind).Should().Equal(
            JsonLexemeKind.Bareword,
            JsonLexemeKind.Bool,
            JsonLexemeKind.Null);
    }

    [Test]
    public void Tokenize_Offsets_AreContiguousAndInOrder()
    {
        var text = "{\"a\":1}";
        var tokens = JsonLexer.Tokenize(text);

        var last = 0;
        foreach (var token in tokens)
        {
            token.Start.Should().Be(last);
            last = token.End;
        }

        last.Should().Be(text.Length);
    }
}