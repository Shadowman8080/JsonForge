using FluentAssertions;
using JsonForge.Core.Json;
using NUnit.Framework;

namespace JsonForge.Tests.Json;

[TestFixture]
public class JsonValidatorTests
{
    [Test]
    public void Validate_ValidDocument_HasNoErrors()
    {
        var result = JsonValidator.Validate("{\"a\": [1, 2, {\"b\": null}]}");

        result.IsValid.Should().BeTrue();
        result.HasErrors.Should().BeFalse();
    }

    [Test]
    public void Validate_EmptyDocument_IsError()
    {
        var result = JsonValidator.Validate("   ");

        result.IsValid.Should().BeFalse();
        result.Diagnostics.Should().Contain(d => d.Code == JsonDiagnosticCodes.InvalidJson);
    }

    [Test]
    public void Validate_TrailingComma_IsWarning()
    {
        var result = JsonValidator.Validate("{\"a\": 1,}");

        result.Diagnostics.Should().Contain(
            d => d.Code == JsonDiagnosticCodes.TrailingComma && d.Severity == JsonDiagnosticSeverity.Warning);
    }

    [Test]
    public void Validate_Comments_AreWarnings()
    {
        var result = JsonValidator.Validate("{\n // hi\n \"a\": 1\n}");

        result.Diagnostics.Should().Contain(d => d.Code == JsonDiagnosticCodes.CommentNotAllowed);
    }

    [Test]
    public void Validate_JsoncMode_AllowsComments()
    {
        var options = JsonValidationOptions.FromStrictness(JsonValidationStrictness.Jsonc);
        var result = JsonValidator.Validate("{\n // hi\n \"a\": 1,\n}", options);

        result.IsValid.Should().BeTrue();
        result.Diagnostics.Should().NotContain(d => d.Code == JsonDiagnosticCodes.CommentNotAllowed);
    }

    [Test]
    public void Validate_DuplicateKeys_AreWarnings()
    {
        var result = JsonValidator.Validate("{\"a\":1,\"a\":2}");

        result.Diagnostics.Should().Contain(d => d.Code == JsonDiagnosticCodes.DuplicateKey);
    }

    [Test]
    public void Validate_UnquotedKey_IsWarning()
    {
        var result = JsonValidator.Validate("{a: 1}");

        result.Diagnostics.Should().Contain(d => d.Code == JsonDiagnosticCodes.UnquotedKey);
    }

    [Test]
    public void Validate_UnclosedObject_IsErrorAtOpeningBrace()
    {
        var result = JsonValidator.Validate("{\"a\": [1, 2");

        result.Diagnostics.Should().Contain(
            d => d.Code == JsonDiagnosticCodes.UnclosedStructure && d.Severity == JsonDiagnosticSeverity.Error);
    }

    [Test]
    public void Validate_UnterminatedString_HasErrorWithPosition()
    {
        var result = JsonValidator.Validate("{\"a\": \"oops");

        var diagnostic = result.Diagnostics.FirstOrDefault(d => d.Code == JsonDiagnosticCodes.UnterminatedString);
        diagnostic.Should().NotBeNull();
        diagnostic!.Line.Should().Be(1);
    }

    [Test]
    public void Validate_TrailingGarbage_IsError()
    {
        var result = JsonValidator.Validate("{\"a\": 1} extra");

        result.Diagnostics.Should().Contain(d => d.Code == JsonDiagnosticCodes.TrailingContent);
    }

    [Test]
    public void Validate_DuplicateParseAndStructuralErrors_AreDeduplicatedByLine()
    {
        // Parser error and structural finding share the same line; only one Error should be reported.
        var result = JsonValidator.Validate("{\"a\": 1,}");

        result.Diagnostics.Count(d => d.Severity == JsonDiagnosticSeverity.Error && d.Line == 1).Should().Be(1);
    }

    [Test]
    public void Validate_Offsets_MapToCorrectLineAndColumn()
    {
        var text = "{\n  \"a\": 1,\n  \"b\": 2,\n}";
        var result = JsonValidator.Validate(text);

        var trailing = result.Diagnostics.First(d => d.Code == JsonDiagnosticCodes.TrailingComma);
        trailing.Line.Should().Be(3);
        trailing.Column.Should().Be(9);
    }

    [Test]
    public void Validate_MissingComma_DoesNotRecurseForever()
    {
        var result = JsonValidator.Validate("{\"a\": 1 \"b\": 2}");

        result.Diagnostics.Should().Contain(
            d => d.Severity == JsonDiagnosticSeverity.Warning && d.Message.Contains("Missing ','"));
    }

    [Test]
    public void Validate_HostileInputs_NeverThrow()
    {
        var hostile = new[]
        {
            "{{{{",
            "}}}}",
            "{[}]",
            "{\"a\": \"unterminated",
            "\"\"\"\"",
            ":,:,",
            "{\"a\" 1 2 3}",
            "]",
            "{\"a\": [1,,2]}",
        };

        foreach (var input in hostile)
        {
            var action = () => JsonValidator.Validate(input);
            action.Should().NotThrow();
        }
    }
}
