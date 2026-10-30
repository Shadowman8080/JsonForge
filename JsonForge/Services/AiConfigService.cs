using JsonForge.Models;

namespace JsonForge.Services;

public sealed class AiConfigService(IUserSettingsService settings)
{
    private readonly IUserSettingsService _settings = settings;

    public AiRuntimeConfig? BuildFromSettings()
    {
        var s = _settings.Current;
        if (s.AiEnabled)
        {
            var endpoint = string.IsNullOrWhiteSpace(s.AiEndpoint) ? null : new Uri(s.AiEndpoint);
            var apiKey = string.IsNullOrWhiteSpace(s.AiApiKey) ? "local" : s.AiApiKey;
            var model = string.IsNullOrWhiteSpace(s.AiModel)
                ? (endpoint is null ? "gpt-4o-mini" : "qwen2.5-coder:3b")
                : s.AiModel;
            var provider = string.IsNullOrWhiteSpace(s.AiProviderName) ? "AI" : s.AiProviderName;
            var max = s.AiMaxTokens > 0 ? s.AiMaxTokens : 4096;
            return new AiRuntimeConfig(true, provider, endpoint, model, apiKey, max);
        }
        return null;
    }

    public void ApplyFromSettings(AiRuntimeState state)
    {
        var runtime = BuildFromSettings();
        if (runtime is not null)
        {
            state.Update(runtime);
        }
    }
}
