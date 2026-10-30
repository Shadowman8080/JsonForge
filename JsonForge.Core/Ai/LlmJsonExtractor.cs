using System.Text.RegularExpressions;

namespace JsonForge.Core.Ai;

/// <summary>
/// Extracts a JSON document from a model response that may contain prose, multiple code
/// fences, or trailing text. Tolerant by design: models frequently wrap output in
/// ```json fences, bold text or explanatory paragraphs.
/// </summary>
public static partial class LlmJsonExtractor
{
    /// <summary>Returns the first plausible JSON value in the response, or null.</summary>
    public static string? ExtractJson(string? response)
    {
        if (string.IsNullOrWhiteSpace(response))
        {
            return null;
        }

        if (TryExtractFenced(response, out var fenced))
        {
            return fenced;
        }

        return ExtractBalanced(response);
    }

    /// <summary>Extracts and validates the JSON; returns a machine-friendly verdict as well.</summary>
    public static bool TryExtractJson(string? response, out string? json, out string? error)
    {
        json = ExtractJson(response);
        if (json is null)
        {
            error = "The model response did not contain a JSON document.";
            return false;
        }

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json, new System.Text.Json.JsonDocumentOptions { MaxDepth = 256 });
            error = null;
            return true;
        }
        catch (System.Text.Json.JsonException ex)
        {
            error = ex.Message;
            return false;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static bool TryExtractFenced(string text, out string? json)
    {
        foreach (Match match in FenceRegex().Matches(text))
        {
            var content = match.Groups[1].Value;
            if (content.Contains('{') || content.Contains('['))
            {
                json = content.Trim();
                return true;
            }
        }

        json = null;
        return false;
    }

    /// <summary>Scans for the first balanced {…} or […] value, respecting strings and escapes.</summary>
    private static string? ExtractBalanced(string text)
    {
        var start = -1;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] is '{' or '[')
            {
                start = i;
                break;
            }
        }

        if (start < 0)
        {
            return null;
        }

        var depth = 0;
        var inString = false;
        var escaped = false;

        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];

            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (c == '\\')
                {
                    escaped = true;
                }
                else if (c == '"')
                {
                    inString = false;
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    inString = true;
                    break;
                case '{':
                case '[':
                    depth++;
                    break;
                case '}':
                case ']':
                    depth--;
                    if (depth == 0)
                    {
                        return text[start..(i + 1)].Trim();
                    }

                    break;
            }
        }

        return null;
    }

    [GeneratedRegex(@"```(?:json)?\s*([\s\S]*?)```", RegexOptions.IgnoreCase)]
    private static partial Regex FenceRegex();
}