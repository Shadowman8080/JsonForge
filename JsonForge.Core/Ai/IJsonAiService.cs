using JsonForge.Core.Json;

namespace JsonForge.Core.Ai;

/// <summary>
/// The AI capability surface the editor needs. Implementations are provider-agnostic:
/// the app wires cloud or local models behind this interface (see JsonForge.Main).
/// </summary>
public interface IJsonAiService
{
    string ProviderName { get; }

    bool IsConfigured { get; }

    Task<AiJsonResult> CompleteAsync(AiJsonRequest request, CancellationToken ct = default);
}