using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace JsonForge.Core.Json;

public enum JsonIndentStyle
{
    Minified,
    TwoSpaces,
    FourSpaces,
    Tab,
}

public sealed record JsonFormatOptions
{
    public JsonIndentStyle Indent { get; init; } = JsonIndentStyle.TwoSpaces;

    /// <summary>Sorts object properties alphabetically, recursively.</summary>
    public bool SortKeys { get; init; }

    /// <summary>
    /// When true, only characters that must be escaped are escaped. This keeps accented text and
    /// URLs readable instead of emitting \uXXXX sequences.
    /// </summary>
    public bool RelaxedEscaping { get; init; } = true;

    public bool TrailingNewline { get; init; }

    public static JsonFormatOptions Default { get; } = new();

    public static JsonFormatOptions Minified { get; } = new() { Indent = JsonIndentStyle.Minified };

    public static JsonFormatOptions Sorted { get; } = new() { SortKeys = true };
}

public sealed class JsonFormatException(JsonDiagnostic diagnostic, string message, Exception? inner = null)
    : Exception(message, inner)
{
    public JsonDiagnostic Diagnostic { get; } = diagnostic;
}

/// <summary>Pretty-prints, minifies and re-orders JSON documents.</summary>
public static class JsonFormatter
{
    private const int MaxDepth = 256;

    /// <summary>Formats <paramref name="json"/> or throws <see cref="JsonFormatException"/>.</summary>
    public static string Format(string json, JsonFormatOptions? options = null)
    {
        if (TryFormat(json, options, out var result, out var error))
        {
            return result;
        }

        throw new JsonFormatException(
            error!,
            $"Cannot format the document: {error!.Message}");
    }

    /// <summary>Formats a parsed JSON node.</summary>
    public static string Format(JsonNode node, JsonFormatOptions? options = null)
    {
        options ??= JsonFormatOptions.Default;

        if (options.SortKeys)
        {
            node = SortNode(node) ?? node;
        }

        return Write(node, options);
    }

    public static bool TryFormat(
        string? json,
        JsonFormatOptions? options,
        out string result,
        out JsonDiagnostic? error)
    {
        options ??= JsonFormatOptions.Default;
        result = string.Empty;

        if (string.IsNullOrWhiteSpace(json))
        {
            error = JsonDiagnostic.Create(
                JsonDiagnosticSeverity.Error,
                "Document is empty. Expected a JSON value.",
                json,
                0,
                0,
                JsonDiagnosticCodes.InvalidJson);
            return false;
        }

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(
                json!,
                new JsonNodeOptions { PropertyNameCaseInsensitive = false },
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = MaxDepth,
                });
        }
        catch (JsonException ex)
        {
            var offset = ex.BytePositionInLine is { } bytePosition
                ? SourceText.GetOffset(json!, (int)(ex.LineNumber ?? 0) + 1, (int)bytePosition + 1)
                : 0;

            error = JsonDiagnostic.Create(
                JsonDiagnosticSeverity.Error,
                "The document is not valid JSON, so it cannot be formatted.",
                json,
                offset,
                1,
                JsonDiagnosticCodes.InvalidJson);
            return false;
        }

        if (root is null)
        {
            // JsonNode.Parse returns null only for a literal "null" document.
            result = "null";
            error = null;
            return true;
        }

        if (options.SortKeys)
        {
            root = SortNode(root) ?? root;
        }

        result = Write(root, options);
        error = null;
        return true;
    }

    private static string Write(JsonNode root, JsonFormatOptions options)
    {
        var indented = options.Indent != JsonIndentStyle.Minified;

        var serializerOptions = new JsonSerializerOptions
        {
            Encoder = options.RelaxedEscaping ? JavaScriptEncoder.UnsafeRelaxedJsonEscaping : JavaScriptEncoder.Default,
            MaxDepth = MaxDepth,
        };

        var writerOptions = new JsonWriterOptions
        {
            Indented = indented,
            IndentCharacter = options.Indent == JsonIndentStyle.Tab ? '\t' : ' ',
            IndentSize = options.Indent switch
            {
                JsonIndentStyle.FourSpaces => 4,
                JsonIndentStyle.Tab => 1,
                _ => 2,
            },
            Encoder = serializerOptions.Encoder,
            SkipValidation = false,
        };

        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, writerOptions))
        {
            root.WriteTo(writer, serializerOptions);
        }

        var text = Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);

        // Normalize to LF: a formatter's output must be stable across platforms so diffs stay clean.
        text = text.Replace("\r\n", "\n");

        if (options.TrailingNewline && !text.EndsWith('\n'))
        {
            text += "\n";
        }

        return text;
    }

    /// <summary>Rebuilds the tree with object properties in ordinal sort order.</summary>
    private static JsonNode? SortNode(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
            {
                var sorted = new JsonObject();
                foreach (var property in obj.OrderBy(p => p.Key, StringComparer.Ordinal))
                {
                    sorted[property.Key] = SortNode(property.Value);
                }

                return sorted;
            }

            case JsonArray array:
            {
                var sorted = new JsonArray();
                foreach (var item in array)
                {
                    sorted.Add(SortNode(item));
                }

                return sorted;
            }

            default:
                return node?.DeepClone();
        }
    }
}
