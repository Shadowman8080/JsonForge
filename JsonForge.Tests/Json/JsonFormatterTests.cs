using FluentAssertions;
using JsonForge.Core.Json;
using NUnit.Framework;
using System.Text.Json.Nodes;

namespace JsonForge.Tests.Json;

[TestFixture]
public class JsonFormatterTests
{
    [Test]
    public void Format_Pretty_IndentsAndPreservesData()
    {
        var source = "{\"a\":1,\"b\":[true,null],\"c\":{\"d\":\"x\"}}";

        var result = JsonFormatter.Format(source, JsonFormatOptions.Default);

        result.Should().Contain("{\n");
        result.Should().Contain("\"a\": 1");
        result.Should().Contain("\"b\": [");
        JsonNode.DeepEquals(JsonNode.Parse(source), JsonNode.Parse(result)).Should().BeTrue();
    }

    [Test]
    public void Format_Minify_FlattensTheDocument()
    {
        var source = "{\n  \"a\": 1,\n  \"b\": [1, 2]\n}";

        var result = JsonFormatter.Format(source, JsonFormatOptions.Minified);

        result.Should().Be("{\"a\":1,\"b\":[1,2]}");
    }

    [Test]
    public void Format_FourSpaces_UsesFourSpaces()
    {
        var result = JsonFormatter.Format("{\"a\":1}", new JsonFormatOptions { Indent = JsonIndentStyle.FourSpaces });

        result.Should().Contain("    \"a\": 1");
    }

    [Test]
    public void Format_Tab_UsesTabs()
    {
        var result = JsonFormatter.Format("{\"a\":1}", new JsonFormatOptions { Indent = JsonIndentStyle.Tab });

        result.Should().Contain("\t\"a\": 1");
    }

    [Test]
    public void Format_SortKeys_OrdersRecursively()
    {
        var result = JsonFormatter.Format(
            "{\"z\":1,\"a\":{\"y\":2,\"b\":3}}",
            new JsonFormatOptions { SortKeys = true });

        result.IndexOf("\"a\"", StringComparison.Ordinal).Should().BeLessThan(result.IndexOf("\"z\"", StringComparison.Ordinal));
        result.IndexOf("\"b\"", StringComparison.Ordinal).Should().BeLessThan(result.IndexOf("\"y\"", StringComparison.Ordinal));
    }

    [Test]
    public void Format_Unicode_StaysReadableWithRelaxedEscaping()
    {
        var source = "{\"name\":\"café ☕\"}";

        var result = JsonFormatter.Format(source, JsonFormatOptions.Default);

        result.Should().Contain("café ☕");
        result.Should().NotContain("\\u00");
    }

    [Test]
    public void TryFormat_InvalidJson_ReturnsErrorDiagnostic()
    {
        var ok = JsonFormatter.TryFormat("{'a':}", JsonFormatOptions.Default, out _, out var error);

        ok.Should().BeFalse();
        error.Should().NotBeNull();
        error!.Severity.Should().Be(JsonDiagnosticSeverity.Error);
    }

    [Test]
    public void Format_ValidJson_IsRoundTrippable()
    {
        var sources = new[]
        {
            "{\"a\":[1,2,{\"b\":\"c\"}],\"d\":null,\"e\":true}",
            "[{\"x\":1},{\"x\":2}]",
            "42",
        };

        foreach (var source in sources)
        {
            var formatted = JsonFormatter.Format(source, JsonFormatOptions.Default);
            JsonNode.DeepEquals(JsonNode.Parse(source), JsonNode.Parse(formatted)).Should().BeTrue();
        }
    }
}