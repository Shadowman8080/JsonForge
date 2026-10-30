using FluentAssertions;
using JsonForge.Core.Json;
using NUnit.Framework;
using System.Text.Json.Nodes;

namespace JsonForge.Tests.Json;

[TestFixture]
public class JsonSmartFormatterTests
{
    [Test]
    public void SmartFormat_StrictJson_FormatsNormally()
    {
        var ok = JsonSmartFormatter.TryFormat("{\"a\":1}", JsonFormatOptions.Default, out var result, out var status, out _);

        ok.Should().BeTrue();
        status.Should().Contain("Formatted");
        JsonNode.DeepEquals(JsonNode.Parse("{\"a\":1}"), JsonNode.Parse(result)).Should().BeTrue();
    }

    [Test]
    public void SmartFormat_JsoncOrJsonish_FallsBackToRepair()
    {
        var source = "{ // c\n a: 'b', }";
        var ok = JsonSmartFormatter.TryFormat(source, JsonFormatOptions.Default, out var result, out var status, out _);

        ok.Should().BeTrue();
        status.Should().Contain("auto-repair");
        result.Should().Contain("\"a\"");
        JsonNode.DeepEquals(JsonNode.Parse("{\"a\":\"b\"}"), JsonNode.Parse(result)).Should().BeTrue();
    }

    [Test]
    public void SmartFormat_Ndjson_ConvertsToArray()
    {
        var source = "{\"a\":1}\n{\"a\":2}\n";
        var ok = JsonSmartFormatter.TryFormat(source, JsonFormatOptions.Default, out var result, out var status, out _);

        ok.Should().BeTrue();
        status.Should().Contain("NDJSON");

        var parsed = JsonNode.Parse(result)!.AsArray();
        parsed.Count.Should().Be(2);
        parsed[0]!["a"]!.GetValue<int>().Should().Be(1);
        parsed[1]!["a"]!.GetValue<int>().Should().Be(2);
    }

    [Test]
    public void Analyzer_DetectsJsonc()
    {
        var analysis = JsonTextAnalyzer.Analyze("{ /*c*/ \"a\": 1 }");
        analysis.Kind.Should().Be(JsonTextKind.Jsonc);
        analysis.HasComments.Should().BeTrue();
    }
}
