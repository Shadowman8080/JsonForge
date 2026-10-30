using JsonForge.Core.Ai;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpenAI;
using System.ClientModel;

namespace JsonForge.Services;

/// <summary>
/// An <see cref="IJsonAiService"/> built on the OpenAI wire protocol via Microsoft.Extensions.AI.
/// One implementation serves every backend because they all speak the same protocol:
/// OpenAI, Azure OpenAI, Ollama (http://localhost:11434/v1), LM Studio, vLLM, ...
/// </summary>
public sealed class OpenAiCompatibleJsonAiService(
    AiRuntimeState state,
    ILogger<OpenAiCompatibleJsonAiService> logger)
    : IJsonAiService
{
    private readonly AiRuntimeState _state = state;
    private readonly ILogger<OpenAiCompatibleJsonAiService> _logger = logger;

    public string ProviderName => _state.Current.ProviderName;

    public bool IsConfigured => _state.Current.Enabled;

    public async Task<AiJsonResult> CompleteAsync(AiJsonRequest request, CancellationToken ct = default)
    {
        var config = _state.Current;
        if (!config.Enabled)
        {
            return AiJsonResult.Failed(config.DiagnosticsMessage ?? "AI is not configured.");
        }

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, JsonAiPrompts.SystemPromptFor(request.Kind)),
            new(ChatRole.User, JsonAiPrompts.BuildUserPrompt(request)),
        };

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));

        try
        {
            var chatClient = CreateClient(config);
            var response = await chatClient.GetResponseAsync(
                messages,
                new ChatOptions { MaxOutputTokens = config.MaxTokens },
                timeout.Token);

            var text = response.Text ?? string.Empty;

            if (request.Kind == AiJsonTaskKind.Explain)
            {
                var trimmed = text.Trim();
                return new AiJsonResult(true, null, trimmed, text);
            }

            if (LlmJsonExtractor.TryExtractJson(text, out var json, out var parseError))
            {
                return new AiJsonResult(true, json, TakeSummary(text), text);
            }

            return AiJsonResult.Failed(
                "The model's response did not contain valid JSON: " + parseError,
                text);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return AiJsonResult.Failed("The AI request timed out. The local model may still be loading, or the endpoint is unreachable.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI request failed for task {TaskKind}", request.Kind);
            return AiJsonResult.Failed(ex.Message);
        }
    }

    private static IChatClient CreateClient(AiRuntimeConfig config)
    {
        var apiKey = string.IsNullOrWhiteSpace(config.ApiKey) ? "local" : config.ApiKey;
        var clientOptions = new OpenAIClientOptions { Endpoint = config.Endpoint };
        var client = new OpenAIClient(new ApiKeyCredential(apiKey), clientOptions);
        return client.GetChatClient(config.Model).AsIChatClient();
    }

    private static string TakeSummary(string text)
    {
        var fence = text.IndexOf("```", StringComparison.Ordinal);
        var head = fence > 0 ? text[..fence] : text;
        var trimmed = head.Trim();
        if (trimmed.Length > 240)
        {
            trimmed = trimmed[..237] + "...";
        }

        return trimmed;
    }
}
