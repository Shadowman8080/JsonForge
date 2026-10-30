using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using JsonForge.Models;
using Microsoft.Extensions.Logging;

namespace JsonForge.Services;

public sealed class LocalLlmManager(
    AiRuntimeState state,
    AiConfigService aiConfig,
    IUserSettingsService settings,
    ILogger<LocalLlmManager> logger)
{
    private readonly AiRuntimeState _state = state;
    private readonly AiConfigService _aiConfig = aiConfig;
    private readonly IUserSettingsService _settings = settings;
    private readonly ILogger<LocalLlmManager> _logger = logger;

    public async Task StartAsync(string model = "qwen2.5-coder:3b")
    {
        try
        {
            if (!OperatingSystem.IsWindows()) return;
            if (!OllamaLocator.TryFindExe(out var exe)) return;

            const string endpoint = "http://localhost:11434/v1";
            var models = await ProbeModelsAsync(endpoint, 2000);
            if (models is null)
            {
                TryStartOllamaServe(exe);
                for (var i = 0; i < 15; i++)
                {
                    await Task.Delay(300);
                    models = await ProbeModelsAsync(endpoint, 1500);
                    if (models is not null) break;
                }
            }

            models ??= [];
            if (models.Count == 0)
            {
                await RunOllamaAsync(exe, $"pull {model}", TimeSpan.FromHours(2));
                models = await ProbeModelsAsync(endpoint, 2000) ?? [];
            }

            var selected = models.FirstOrDefault(m => string.Equals(m, model, StringComparison.Ordinal)) ?? models.FirstOrDefault() ?? model;

            var current = _settings.Current;
            _settings.Update(current with
            {
                AiEnabled = true,
                AiAutoEnable = true,
                AiProviderName = "Ollama (local)",
                AiModel = selected,
                AiEndpoint = endpoint,
                AiApiKey = null,
                AiMaxTokens = current.AiMaxTokens > 0 ? current.AiMaxTokens : 4096,
            });

            _state.Update(new AiRuntimeConfig(true, "Ollama (local)", new Uri(endpoint), selected, "local", current.AiMaxTokens > 0 ? current.AiMaxTokens : 4096));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to start local LLM");
        }
    }

    private static async Task<List<string>?> ProbeModelsAsync(string endpoint, int timeoutMs)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMilliseconds(Math.Clamp(timeoutMs, 200, 10000)) };
            var uri = endpoint.TrimEnd('/') + "/models";
            using var req = new HttpRequestMessage(HttpMethod.Get, uri);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var resp = await http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return null;
            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return null;
            var list = new List<string>();
            foreach (var item in data.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                {
                    var v = id.GetString();
                    if (!string.IsNullOrWhiteSpace(v)) list.Add(v!);
                }
            }
            return list;
        }
        catch { return null; }
    }

    private static void TryStartOllamaServe(string exe)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = exe, Arguments = "serve", UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden });
        }
        catch { }
    }

    private static async Task<bool> RunOllamaAsync(string exe, string args, TimeSpan timeout)
    {
        try
        {
            var psi = new ProcessStartInfo { FileName = exe, Arguments = args, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            using var p = Process.Start(psi);
            if (p is null) return false;
            using var cts = new CancellationTokenSource(timeout);
            await p.WaitForExitAsync(cts.Token);
            return p.ExitCode == 0;
        }
        catch { return false; }
    }
}
