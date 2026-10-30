using System.Text;

namespace JsonForge.Core.Ai;

/// <summary>Builds the system and user prompts for each AI task.</summary>
public static class JsonAiPrompts
{
    public const string JsonOnlySystemPrompt =
        """
        You are JsonForge, an expert JSON tool embedded in a cross-platform desktop editor.
        You fix, format, transform and generate JSON documents.

        CRITICAL OUTPUT RULES:
        - Put ONLY your final JSON document in the last fenced code block of your answer, opened with ```json.
        - The JSON must be valid: double quotes for keys and strings, no trailing commas, no comments, no markdown.
        - Preserve every field and every value unless the user's instruction changes it. Never invent data.
        - If the input cannot be repaired, output the JSON object {"error": "short reason"} in the fenced block.
        - Keep the document itself compact; the app handles presentation.
        - A short human-readable note (one or two sentences maximum) may precede the fenced block.
        """;

    public const string ExplainSystemPrompt =
        """
        You are JsonForge, an expert JSON tool embedded in a desktop editor. Explain JSON
        documents to a developer who is debugging them. Be concise and specific. Do NOT wrap
        your answer in code fences and do not emit JSON.
        """;

    public static string SystemPromptFor(AiJsonTaskKind kind)
        => kind == AiJsonTaskKind.Explain ? ExplainSystemPrompt : JsonOnlySystemPrompt;

    public static string BuildUserPrompt(AiJsonRequest request)
    {
        var sb = new StringBuilder();

        sb.Append(TaskLeadIn(request.Kind));
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(request.FormatDescription))
        {
            sb.Append("Formatting preferences: ").AppendLine(request.FormatDescription.Trim());
        }

        if (request.Diagnostics.Count > 0)
        {
            sb.AppendLine("Syntax problems that were detected (line numbers are 1-based):");
            foreach (var d in request.Diagnostics.Take(12))
            {
                sb.Append("- Line ").Append(d.Line)
                  .Append(": ").AppendLine(d.Message);
            }

            if (request.Diagnostics.Count > 12)
            {
                sb.AppendLine($"- ... and {request.Diagnostics.Count - 12} more.");
            }

            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(request.Instruction))
        {
            sb.Append("Instruction: ").AppendLine(request.Instruction.Trim());
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(request.SourceJson))
        {
            sb.AppendLine("Document:");
            sb.AppendLine("```json");
            sb.Append(request.SourceJson.Trim());
            sb.AppendLine();
            sb.AppendLine("```");
        }

        return sb.ToString();
    }

    private static string TaskLeadIn(AiJsonTaskKind kind) => kind switch
    {
        AiJsonTaskKind.Repair =>
            "Repair the JSON document below. Fix syntax errors and invalid JSON constructs while keeping the data and structure as close to the original as possible.",

        AiJsonTaskKind.Format =>
            "Reformat the JSON document below. Keep the data identical - do not rename keys, truncate, reorder (unless instructed) or reword any value.",

        AiJsonTaskKind.Transform =>
            "Transform the JSON document below according to the instruction. Return the full resulting document.",

        AiJsonTaskKind.Generate =>
            "Generate a JSON document from the description below. Use sensible, realistic sample values.",

        AiJsonTaskKind.Explain =>
            "Explain the JSON document below: what it represents, its notable parts, and anything risky or surprising. Be specific and cite paths like config.plugins[0].name.",

        _ => string.Empty,
    };
}