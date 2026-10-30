namespace JsonForge.Core.Json;

/// <summary>Converts between character offsets and 1-based line/column positions.</summary>
public static class SourceText
{
    /// <summary>Returns the 1-based line and column for a character offset.</summary>
    public static (int Line, int Column) GetLineColumn(string? text, int offset)
    {
        if (string.IsNullOrEmpty(text))
        {
            return (1, 1);
        }

        offset = Math.Clamp(offset, 0, text.Length);

        var line = 1;
        var column = 1;
        for (var i = 0; i < offset; i++)
        {
            if (text[i] == '\n')
            {
                line++;
                column = 1;
            }
            else
            {
                column++;
            }
        }

        return (line, column);
    }

    /// <summary>Returns the character offset for a 1-based line and column.</summary>
    public static int GetOffset(string? text, int line, int column)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        line = Math.Max(1, line);
        column = Math.Max(1, column);

        var currentLine = 1;
        var index = 0;
        while (index < text.Length && currentLine < line)
        {
            if (text[index] == '\n')
            {
                currentLine++;
            }

            index++;
        }

        var target = index + column - 1;
        return Math.Clamp(target, 0, text.Length);
    }

    /// <summary>Gets the zero-based start and length of the line containing <paramref name="offset"/>.</summary>
    public static (int Start, int Length) GetLineBounds(string? text, int offset)
    {
        if (string.IsNullOrEmpty(text))
        {
            return (0, 0);
        }

        offset = Math.Clamp(offset, 0, text.Length);

        var start = text.LastIndexOf('\n', Math.Max(0, Math.Min(offset, text.Length - 1)));
        start = start < 0 ? 0 : start + 1;

        var end = text.IndexOf('\n', Math.Min(start, text.Length));
        if (end < 0)
        {
            end = text.Length;
        }

        var length = end - start;
        if (length > 0 && text[start + length - 1] == '\r')
        {
            length--;
        }

        return (start, length);
    }
}
