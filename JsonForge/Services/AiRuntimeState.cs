namespace JsonForge.Services;

/// <summary>
/// Mutable runtime AI configuration that can be updated after startup (e.g. after starting Ollama
/// or pulling a model). Consumers should read <see cref="Current"/> when needed.
/// </summary>
public sealed class AiRuntimeState
{
    private readonly object _gate = new();
    private AiRuntimeConfig _current;

    public AiRuntimeState(AiRuntimeConfig initial)
    {
        _current = initial;
    }

    public event Action<AiRuntimeConfig>? Changed;

    public AiRuntimeConfig Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public void Update(AiRuntimeConfig next)
    {
        lock (_gate)
        {
            _current = next;
        }

        Changed?.Invoke(next);
    }
}
