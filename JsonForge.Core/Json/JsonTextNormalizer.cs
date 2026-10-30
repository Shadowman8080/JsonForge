namespace JsonForge.Core.Json;

/// <summary>
/// Normalizes characters that are visually similar to JSON syntax but are not valid JSON.
/// Every replacement is 1:1 in length so that character offsets stay meaningful for the UI.
/// </summary>
public static class JsonTextNormalizer
{
    public static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var needsWork = false;
        foreach (var c in text)
        {
            if (IsSuspect(c))
            {
                needsWork = true;
                break;
            }
        }

        if (!needsWork)
        {
            return text;
        }

        var buffer = new char[text.Length];
        for (var i = 0; i < text.Length; i++)
        {
            buffer[i] = Map(text[i]);
        }

        return new string(buffer);
    }

    private static bool IsSuspect(char c) => c is
        '\u00A0' or '\uFEFF' or '\u200B' or '\u200C' or '\u200D' or '\u2060' or
        '\u2018' or '\u2019' or '\u201A' or '\u201B' or
        '\u201C' or '\u201D' or '\u201E' or '\u201F' or
        '\u02BC' or '\uFF07' or '\uFF02';

    private static char Map(char c) => c switch
    {
        // Non-breaking / zero-width characters become ordinary spaces so offsets are preserved.
        '\u00A0' or '\uFEFF' or '\u200B' or '\u200C' or '\u200D' or '\u2060' => ' ',

        // Curly quotes (often introduced by word processors and copy/paste from docs).
        '\u2018' or '\u2019' or '\u201A' or '\u201B' or '\u02BC' or '\uFF07' => '\'',
        '\u201C' or '\u201D' or '\u201E' or '\u201F' or '\uFF02' => '"',

        _ => c,
    };
}
