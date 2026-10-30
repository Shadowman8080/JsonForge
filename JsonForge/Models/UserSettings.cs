using JsonForge.Core.Json;

namespace JsonForge.Models;

public sealed record RecentFileEntry(string Token, string DisplayName, DateTimeOffset LastOpenedUtc);

public sealed record UserSettings
{
    public JsonIndentStyle Indent { get; init; } = JsonIndentStyle.TwoSpaces;

    public bool SortKeys { get; init; }

    public bool RelaxedEscaping { get; init; } = true;

    public bool TrailingNewline { get; init; }

    public JsonValidationStrictness Strictness { get; init; } = JsonValidationStrictness.StrictJson;

    public bool AutoFormatOnPaste { get; init; } = true;

    public bool AutoFormatOnIdle { get; init; }

    public int AutoFormatIdleMs { get; init; } = 900;

    public int RecentFilesMax { get; init; } = 10;

    public IReadOnlyList<RecentFileEntry> RecentFiles { get; init; } = Array.Empty<RecentFileEntry>();

    // AI configuration
    public bool AiEnabled { get; init; }
    public bool AiAutoEnable { get; init; } = true;
    public string AiProviderName { get; init; } = "AI";
    public string? AiModel { get; init; }
    public string? AiEndpoint { get; init; }
    public string? AiApiKey { get; init; }
    public int AiMaxTokens { get; init; } = 4096;
}
