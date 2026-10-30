namespace JsonForge.Models;

/// <summary>Bind to the "Ai" section of appsettings.json.</summary>
public sealed class AiOptions
{
    /// <summary>
    /// Explicit toggle. When false, JsonForge may still auto-enable AI if <see cref="AutoEnable"/> is true
    /// and a local OpenAI-compatible endpoint (e.g. Ollama) is detected.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// When true (default), JsonForge will attempt to auto-configure a self-hosted AI endpoint on startup.
    /// This keeps the app "just working" without requiring users to edit appsettings.
    /// </summary>
    public bool AutoEnable { get; init; } = true;

    public string ProviderName { get; init; } = "AI";

    /// <summary>Model id, e.g. "gpt-4o-mini", "llama3.2:3b", "qwen2.5-coder:3b", "deepseek-r1:8b".</summary>
    public string? Model { get; init; }

    /// <summary>
    /// Optional server URL. Leave empty for OpenAI. Point it at Ollama or LM Studio's
    /// OpenAI-compatible endpoint (e.g. http://localhost:11434/v1) for fully local AI.
    /// </summary>
    public string? Endpoint { get; init; }

    /// <summary>API key. Not needed for local endpoints such as Ollama.</summary>
    public string? ApiKey { get; init; }

    public int MaxTokens { get; init; } = 4096;

    /// <summary>
    /// Preferred model ids to pick when auto-configuring against a local endpoint.
    /// JsonForge will choose the first one found in /v1/models.
    /// </summary>
    public string[] ModelCandidates { get; init; } =
    [
        "qwen2.5-coder:7b",
        "qwen2.5-coder:3b",
        "llama3.2:3b",
        "deepseek-r1:8b",
    ];

    /// <summary>Timeout (ms) for auto-detection probes (e.g. calling /v1/models).</summary>
    public int AutoProbeTimeoutMs { get; init; } = 800;
}
