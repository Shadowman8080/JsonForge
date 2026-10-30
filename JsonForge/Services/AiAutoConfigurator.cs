using System.Net.Http.Headers;
using System.Text.Json;
using JsonForge.Models;

namespace JsonForge.Services;

public static class AiAutoConfigurator
{
    // Ordered by "best default" for fully local.
    private static readonly (string ProviderName, string Endpoint)[] LocalCandidates =
    [
        ("Ollama (local)", "http://localhost:11434/v1"),
        ("Ollama (local)", "http://127.0.0.1:11434/v1"),
        ("LM Studio (local)", "http://localhost:1234/v1"),
    ];

    public static AiRuntimeConfig Resolve(AiOptions options)
    {
        // 1) Explicit configuration always wins.
        if (options.Enabled)
        {
            var endpoint = string.IsNullOrWhiteSpace(options.Endpoint) ? null : new Uri(options.Endpoint);
            var apiKey = string.IsNullOrWhiteSpace(options.ApiKey) ? "local" : options.ApiKey;
            var model = string.IsNullOrWhiteSpace(options.Model)
                ? (endpoint is null ? "gpt-4o-mini" : "qwen2.5-coder:3b")
                : options.Model;

            return new AiRuntimeConfig(
                Enabled: true,
                ProviderName: options.ProviderName,
                Endpoint: endpoint,
                Model: model,
                ApiKey: apiKey,
                MaxTokens: options.MaxTokens);
        }

        // 2) Auto-configure a self-hosted endpoint.
        if (!options.AutoEnable)
        {
            return new AiRuntimeConfig(
                Enabled: false,
                ProviderName: "AI (disabled)",
                Endpoint: null,
                Model: string.Empty,
                ApiKey: "local",
                MaxTokens: options.MaxTokens,
                DiagnosticsMessage: "AI is disabled (AutoEnable=false)."
            );
        }

        foreach (var candidate in LocalCandidates)
        {
            if (TryProbeModels(candidate.Endpoint, options.AutoProbeTimeoutMs, out var models, out var probeMessage))
            {
                var model = PickModel(models, options.ModelCandidates);
                if (string.IsNullOrWhiteSpace(model))
                {
                    return new AiRuntimeConfig(
                        Enabled: false,
                        ProviderName: candidate.ProviderName,
                        Endpoint: new Uri(candidate.Endpoint),
                        Model: string.Empty,
                        ApiKey: "local",
                        MaxTokens: options.MaxTokens,
                        DiagnosticsMessage: "A local AI endpoint was found but no models were reported. Pull a model (e.g. `ollama pull qwen2.5-coder:7b`) and restart the app."
                    );
                }

                return new AiRuntimeConfig(
                    Enabled: true,
                    ProviderName: candidate.ProviderName,
                    Endpoint: new Uri(candidate.Endpoint),
                    Model: model,
                    ApiKey: "local",
                    MaxTokens: options.MaxTokens,
                    DiagnosticsMessage: probeMessage);
            }
        }

        // Ollama may be installed but not running yet; the bootstrapper can start it.
        if (OperatingSystem.IsWindows() && OllamaLocator.TryFindExe(out _))
        {
            return new AiRuntimeConfig(
                Enabled: false,
                ProviderName: "Ollama (local)",
                Endpoint: new Uri("http://localhost:11434/v1"),
                Model: string.Empty,
                ApiKey: "local",
                MaxTokens: options.MaxTokens,
                DiagnosticsMessage: "Ollama is installed. JsonForge will start it and download a model on first run (this can take a few minutes)."
            );
        }

        return new AiRuntimeConfig(
            Enabled: false,
            ProviderName: "AI not configured",
            Endpoint: null,
            Model: string.Empty,
            ApiKey: "local",
            MaxTokens: options.MaxTokens,
            DiagnosticsMessage: "No local OpenAI-compatible endpoint detected. Install Ollama and start it (default: http://localhost:11434/v1)."
        );
    }
    private static string PickModel(IReadOnlyList<string> available, IReadOnlyList<string> candidates)
    {
        foreach (var c in candidates)
        {
            var match = available.FirstOrDefault(m => string.Equals(m, c, StringComparison.Ordinal));
            if (match is not null)
            {
                return match;
            }
        }

        // Fallback: first available.
        return available.Count > 0 ? available[0] : string.Empty;
    }

    private static bool TryProbeModels(
        string endpoint,
        int timeoutMs,
        out List<string> modelIds,
        out string message)
    {
        modelIds = [];
        message = string.Empty;

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMilliseconds(Math.Clamp(timeoutMs, 200, 5000)) };

            var uri = endpoint.TrimEnd('/') + "/models";
            using var req = new HttpRequestMessage(HttpMethod.Get, uri);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var resp = http.Send(req);
            if (!resp.IsSuccessStatusCode)
            {
                message = $"Probe failed: {endpoint} returned {(int)resp.StatusCode}.";
                return false;
            }

            var json = resp.Content.ReadAsStringAsync().ConfigureAwait(false).GetAwaiter().GetResult();
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            {
                message = $"Probe failed: {endpoint} did not return a /v1/models list.";
                return false;
            }

            foreach (var item in data.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                {
                    var value = id.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        modelIds.Add(value!);
                    }
                }
            }

            message = $"Auto-configured from {endpoint}.";
            return true;
        }
        catch
        {
            return false;
        }
    }
}
