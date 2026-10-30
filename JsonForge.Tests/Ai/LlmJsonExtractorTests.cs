using FluentAssertions;
using JsonForge.Core.Ai;
using NUnit.Framework;

namespace JsonForge.Tests.Ai;

[TestFixture]
public class LlmJsonExtractorTests
{
    [Test]
    public void ExtractJson_FencedBlock_ReturnsContent()
    {
        var response = """
            Here is the fixed document:

            ```json
            {"name": "JsonForge", "fixed": true}
            ```

            Hope that helps!
            """;

        var json = LlmJsonExtractor.ExtractJson(response);

        json.Should().Be("""{"name": "JsonForge", "fixed": true}""");
    }

    [Test]
    public void ExtractJson_FenceWithJsonKeyword_ReturnsContent()
    {
        var response = "```json\n[1, 2, 3]\n```";

        var json = LlmJsonExtractor.ExtractJson(response);

        json.Should().Be("[1, 2, 3]");
    }

    [Test]
    public void ExtractJson_ProseWrapped_ReturnsBalancedValue()
    {
        var response = "The corrected document is: {\"a\": {\"nested\": [1,2,3]}} and that's all.";

        var json = LlmJsonExtractor.ExtractJson(response);

        json.Should().Be("""{"a": {"nested": [1,2,3]}}""");
    }

    [Test]
    public void ExtractJson_StringWithBrackets_DoesNotConfuseBalancing()
    {
        var response = "Result: {\"msg\": \"a } b { c\", \"ok\": true} trailing";

        var json = LlmJsonExtractor.ExtractJson(response);

        json.Should().Contain("\"ok\": true");
        json!.EndsWith("}").Should().BeTrue();
    }

    [Test]
    public void ExtractJson_NoJson_ReturnsNull()
    {
        LlmJsonExtractor.ExtractJson("Sorry, I could not parse that.").Should().BeNull();
    }

    [Test]
    public void TryExtractJson_ValidatesResult()
    {
        var ok = LlmJsonExtractor.TryExtractJson("```json\n{\"a\": 1}\n```", out var json, out var error);

        ok.Should().BeTrue();
        json.Should().NotBeNull();
        error.Should().BeNull();
    }

    [Test]
    public void TryExtractJson_InvalidJson_ReturnsError()
    {
        var ok = LlmJsonExtractor.TryExtractJson("```json\n{\"a\": 1,}\n```", out _, out var error);

        ok.Should().BeFalse();
        error.Should().NotBeNullOrEmpty();
    }
}