using System.Text.Json.Nodes;

namespace JsonForge.Core.Json;

public static class JsonSmartFormatter
{
    public static bool TryFormat(
        string? text,
        JsonFormatOptions? options,
        out string result,
        out string status,
        out JsonDiagnostic? error)
    {
        options ??= JsonFormatOptions.Default;

        if (JsonFormatter.TryFormat(text, options, out result, out error))
        {
            status = options.Indent == JsonIndentStyle.Minified ? "Minified." : "Formatted.";
            return true;
        }

        if (!string.IsNullOrWhiteSpace(text) && NdjsonConverter.TryConvertToJsonArray(text!, out var array, out var count, out var ndError))
        {
            result = JsonFormatter.Format(array, options);
            status = $"Converted NDJSON ({count} value(s)) to a JSON array and formatted.";
            error = null;
            return true;
        }

        if (!string.IsNullOrWhiteSpace(text))
        {
            var repair = JsonRepairer.Repair(text, new JsonRepairOptions { PrettyPrint = false });
            if (repair.IsValid && JsonFormatter.TryFormat(repair.Repaired, options, out result, out _))
            {
                status = repair.WasModified
                    ? $"Formatted (after auto-repair: {repair.ActionCount} fix(es))."
                    : (options.Indent == JsonIndentStyle.Minified ? "Minified." : "Formatted.");
                error = null;
                return true;
            }
        }

        result = string.Empty;
        status = "Cannot format: " + (error?.Message ?? "The document is not valid JSON.");
        return false;
    }
}
