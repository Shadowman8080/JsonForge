namespace JsonForge.Services;

/// <summary>
/// Resolved AI configuration used at runtime.
/// This may come directly from appsettings or be auto-detected (e.g. local Ollama).
/// </summary>
public sealed record AiRuntimeConfig(
    bool Enabled,
    string ProviderName,
    Uri? Endpoint,
    string Model,
    string ApiKey,
    int MaxTokens,
    string? DiagnosticsMessage = null);
