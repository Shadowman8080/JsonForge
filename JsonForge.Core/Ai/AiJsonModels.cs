using JsonForge.Core.Json;

namespace JsonForge.Core.Ai;

public enum AiJsonTaskKind
{
    /// <summary>Fix syntax errors while preserving data.</summary>
    Repair,

    /// <summary>Reformat an existing, valid-ish document per a style instruction.</summary>
    Format,

    /// <summary>Apply a free-form transformation instruction.</summary>
    Transform,

    /// <summary>Create new JSON from a description.</summary>
    Generate,

    /// <summary>Explain a document in prose (no JSON output).</summary>
    Explain,
}

public sealed record AiJsonRequest(
    AiJsonTaskKind Kind,
    string? SourceJson,
    string? Instruction,
    IReadOnlyList<JsonDiagnostic> Diagnostics,
    string? FormatDescription)
{
    public static AiJsonRequest Repair(AiJsonRequest source, string instruction)
        => source with { Kind = AiJsonTaskKind.Repair, Instruction = instruction };

    public static AiJsonRequest Simple(AiJsonTaskKind kind, string? json = null, string? instruction = null)
        => new(kind, json, instruction, [], null);
}

public sealed record AiJsonResult(
    bool Succeeded,
    string? Json,
    string? Explanation,
    string RawResponse,
    string? Error = null)
{
    public static AiJsonResult Failed(string error, string rawResponse = "") => new(false, null, null, rawResponse, error);
}