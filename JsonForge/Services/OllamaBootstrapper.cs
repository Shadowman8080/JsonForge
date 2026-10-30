using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using JsonForge.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JsonForge.Services;

/// <summary>
/// Best-effort local AI bootstrap:
/// - starts Ollama (if installed) when not running
/// - pulls a small default model if none are installed
/// - updates <see cref="AiRuntimeState"/> as configuration becomes available
/// </summary>
public sealed class OllamaBootstrapper(
    AiRuntimeState state,
    IOptions<AiOptions> options,
    ILogger<OllamaBootstrapper> logger)
{
    private readonly AiRuntimeState _state = state;
    private readonly AiOptions _options = options.Value;
    private readonly ILogger<OllamaBootstrapper> _logger = logger;
    private int _started;

    public void StartInBackground()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await EnsureAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Local AI bootstrap failed.");
            }
        });
    }

    private async Task EnsureAsync()
    {
        // Respect explicit config.
        if (_options.Enabled || !_options.AutoEnable)
        {
            return;
        }

        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        // Prefer the default Ollama endpoint.
        const string endpoint = "http://localhost:11434/v1";

        if (!OllamaLocator.TryFindExe(out var exe))
        {
            return;
        }

        // Make sure server is up.
        var models = await ProbeModelsAsync(endpoint, _options.AutoProbeTimeoutMs).ConfigureAwait(false);
        if (models is null)
        {
            UpdateMessage("Starting Ollama...");
            TryStartOllamaServe(exe);

            // Retry a few times.
            for (var i = 0; i < 10; i++)
            {
                await Task.Delay(250).ConfigureAwait(false);
                models = await ProbeModelsAsync(endpoint, 1200).ConfigureAwait(false);
                if (models is not null)
                {
                    break;
                }
            }
        }

        models ??= [];

        if (models.Count == 0)
        {
            // Pull a reasonable default automatically.
            var toPull = PickModelToPull();
            UpdateMessage($"Downloading model '{toPull}' via Ollama (first run)...");

            var ok = await RunOllamaAsync(exe, $"pull {toPull}", TimeSpan.FromHours(2)).ConfigureAwait(false);
            if (!ok)
            {
                UpdateMessage($"Ollama is installed, but model download failed. Try running: ollama pull {toPull}");
                return;
            }

            // Re-probe
            models = await ProbeModelsAsync(endpoint, 2000).ConfigureAwait(false) ?? [];
        }

        if (models.Count == 0)
        {
            UpdateMessage("Ollama is running, but no models are available.");
            return;
        }

        var selected = AiAutoConfiguratorPickModel(models, _options.ModelCandidates);
        if (string.IsNullOrWhiteSpace(selected))
        {
            selected = models[0];
        }

        _state.Update(new AiRuntimeConfig(
            Enabled: true,
            ProviderName: "Ollama (local)",
            Endpoint: new Uri(endpoint),
            Model: selected,
            ApiKey: "local",
            MaxTokens: _options.MaxTokens,
            DiagnosticsMessage: $"Auto-configured from {endpoint} (model: {selected})."));
    }

    private void UpdateMessage(string message)
    {
        var current = _state.Current;
        if (current.DiagnosticsMessage == message)
        {
            return;
        }

        _state.Update(current with
        {
            ProviderName = "Ollama (local)",
            Endpoint = new Uri("http://localhost:11434/v1"),
            DiagnosticsMessage = message,
        });
    }

    private string PickModelToPull()
    {
        // Avoid surprising huge downloads on first run; prefer a small model unless user already has larger ones.
        var small = _options.ModelCandidates.FirstOrDefault(m => m.Contains(":3b", StringComparison.OrdinalIgnoreCase));
        return small ?? _options.ModelCandidates.FirstOrDefault() ?? "qwen2.5-coder:3b";
    }

    private static void TryStartOllamaServe(string exe)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = "serve",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            };

            Process.Start(psi);
        }
        catch
        {
            // ignore
        }
    }

    private static async Task<bool> RunOllamaAsync(string exe, string args, TimeSpan timeout)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var p = Process.Start(psi);
            if (p is null)
            {
                return false;
            }

            using var cts = new CancellationTokenSource(timeout);
            await p.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            return p.ExitCode == 0;
        }
        catch
        {
            return false;
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
            using var resp = await http.SendAsync(req).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                return null;
            }

            var json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var modelIds = new List<string>();
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

            return modelIds;
        }
        catch
        {
            return null;
        }
    }

    private static string AiAutoConfiguratorPickModel(IReadOnlyList<string> available, IReadOnlyList<string> candidates)
    {
        foreach (var c in candidates)
        {
            var match = available.FirstOrDefault(m => string.Equals(m, c, StringComparison.Ordinal));
            if (match is not null)
            {
                return match;
            }
        }

        return available.Count > 0 ? available[0] : string.Empty;
    }
}
