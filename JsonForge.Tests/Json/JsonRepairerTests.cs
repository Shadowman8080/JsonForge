using FluentAssertions;
using JsonForge.Core.Json;
using NUnit.Framework;
using System.Text.Json.Nodes;

namespace JsonForge.Tests.Json;

[TestFixture]
public class JsonRepairerTests
{
    [Test]
    public void Repair_ValidJson_IsLeftSemanticallyUnchanged()
    {
        var source = "{\"a\": 1, \"b\": [true, null, \"x\"]}";

        var result = JsonRepairer.Repair(source);

        result.IsValid.Should().BeTrue();
        result.WasModified.Should().BeFalse();
        JsonNode.DeepEquals(JsonNode.Parse(source), JsonNode.Parse(result.Repaired)).Should().BeTrue();
    }

    [Test]
    public void Repair_KitchenSink_ProducesValidJsonPreservingData()
    {
        var source = """
            {
              // config file
              'name': "JsonForge",      /* multiline
              comment */
              version: '0.1.0',
              plugins: [
                "formatter",
                'validator',
                "ai",   // trailing comment
              ],
              settings: {
                indent: 4,
                theme: "dark",
                decimals: .5,
                count: 007,
                enabled: True,
                homepage: null,
                meta: NaN,
              }
            }
            """;

        var result = JsonRepairer.Repair(source);

        result.IsValid.Should().BeTrue();
        result.WasModified.Should().BeTrue();
        result.Actions.Should().NotBeEmpty();

        var node = JsonNode.Parse(result.Repaired)!;
        node["name"]!.GetValue<string>().Should().Be("JsonForge");
        node["version"]!.GetValue<string>().Should().Be("0.1.0");
        node["plugins"]!.AsArray().Count.Should().Be(3);
        node["settings"]!["indent"]!.GetValue<int>().Should().Be(4);
        node["settings"]!["decimals"]!.GetValue<double>().Should().Be(0.5);
        node["settings"]!["count"]!.GetValue<int>().Should().Be(7);
        node["settings"]!["enabled"]!.GetValue<bool>().Should().BeTrue();
        node["settings"]!["homepage"]!.Should().BeNull();
        node["settings"]!["meta"]!.Should().BeNull();
    }

    [Test]
    public void Repair_UnclosedStructures_ClosesThem()
    {
        var result = JsonRepairer.Repair("{\"a\": [1, 2");

        result.IsValid.Should().BeTrue();
        result.Repaired.Should().NotContain("] }");
        JsonNode.DeepEquals(JsonNode.Parse(result.Repaired), JsonNode.Parse("{\"a\":[1,2]}")).Should().BeTrue();
        result.Actions.Should().Contain(a => a.Kind == JsonRepairActionKind.ClosedStructure);
    }

    [Test]
    public void Repair_MissingCommas_AreInserted()
    {
        var result = JsonRepairer.Repair("{\"a\": 1 \"b\": 2}");

        result.IsValid.Should().BeTrue();
        result.Actions.Should().Contain(a => a.Kind == JsonRepairActionKind.InsertedComma);
        JsonNode.DeepEquals(JsonNode.Parse(result.Repaired), JsonNode.Parse("{\"a\":1,\"b\":2}")).Should().BeTrue();
    }

    [Test]
    public void Repair_MissingColon_IsInserted()
    {
        var result = JsonRepairer.Repair("{\"a\" 1}");

        result.IsValid.Should().BeTrue();
        result.Actions.Should().Contain(a => a.Kind == JsonRepairActionKind.InsertedColon);
    }

    [Test]
    public void Repair_SingleQuotedStrings_AreConverted()
    {
        var result = JsonRepairer.Repair("{'name': 'JsonForge'}");

        result.IsValid.Should().BeTrue();
        result.Repaired.Should().Contain("\"JsonForge\"");
        result.Actions.Should().Contain(a => a.Kind == JsonRepairActionKind.RewroteString);
    }

    [Test]
    public void Repair_SmartQuotes_AreNormalized()
    {
        var result = JsonRepairer.Repair("{\u201Cname\u201D: \u2018JsonForge\u2019}");

        result.IsValid.Should().BeTrue();
        JsonNode.DeepEquals(JsonNode.Parse(result.Repaired), JsonNode.Parse("{\"name\":\"JsonForge\"}")).Should().BeTrue();
    }

    [Test]
    public void Repair_PythonLiterals_AreNormalized()
    {
        var result = JsonRepairer.Repair("{\"a\": True, \"b\": False, \"c\": None}");

        result.IsValid.Should().BeTrue();
        result.Actions.Should().Contain(a => a.Kind == JsonRepairActionKind.NormalizedLiteral);
        JsonNode.DeepEquals(JsonNode.Parse(result.Repaired), JsonNode.Parse("{\"a\":true,\"b\":false,\"c\":null}")).Should().BeTrue();
    }

    [Test]
    public void Repair_TrailingGarbage_IsDropped()
    {
        var result = JsonRepairer.Repair("{\"a\": 1} hello world");

        result.IsValid.Should().BeTrue();
        result.Actions.Should().Contain(a => a.Kind == JsonRepairActionKind.DroppedTrailingContent);
    }

    [Test]
    public void Repair_Numbers_AreNormalized()
    {
        var result = JsonRepairer.Repair("{\"a\": .5, \"b\": 007, \"c\": 1., \"d\": +5}");

        result.IsValid.Should().BeTrue();
        result.Actions.Should().Contain(a => a.Kind == JsonRepairActionKind.NormalizedNumber);
        JsonNode.DeepEquals(JsonNode.Parse(result.Repaired), JsonNode.Parse("{\"a\":0.5,\"b\":7,\"c\":1,\"d\":5}")).Should().BeTrue();
    }

    [Test]
    public void Repair_TrailingCommas_AreRemovedInBothContainers()
    {
        var result = JsonRepairer.Repair("{\"a\": [1, 2,],}");

        result.IsValid.Should().BeTrue();
        result.Actions.Should().Contain(a => a.Kind == JsonRepairActionKind.RemovedTrailingComma);
    }

    [Test]
    public void Repair_EmptyQuotesHelp_DoesNotCrash()
    {
        var result = JsonRepairer.Repair("");

        result.IsValid.Should().BeFalse();
    }

    [Test]
    public void Repair_UnescapedControlCharactersInStrings_AreEscaped()
    {
        var result = JsonRepairer.Repair("{\"a\": \"line1\nline2\"}");

        result.IsValid.Should().BeTrue();
        result.Repaired.Should().Contain("\\n");
    }

    [Test]
    public void Repair_NoRepairOptions_TurnsEverythingOff()
    {
        var options = new JsonRepairOptions
        {
            RemoveComments = false,
            NormalizeQuotes = false,
            RemoveTrailingCommas = false,
            InsertMissingCommas = false,
            InsertMissingColons = false,
            QuoteUnquotedKeys = false,
            QuoteBareValues = false,
            NormalizeLiterals = false,
            NormalizeNumbers = false,
            CloseUnclosed = false,
            DropTrailingContent = false,
            PrettyPrint = false,
        };

        var source = "{a: 1,}";
        var result = JsonRepairer.Repair(source, options);

        result.WasModified.Should().BeFalse();
        result.Repaired.Should().Be("{a:1,}");
    }
}