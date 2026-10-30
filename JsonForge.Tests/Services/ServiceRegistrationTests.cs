using FluentAssertions;
using JsonForge.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace JsonForge.Tests.Services;

/// <summary>
/// Guards the service graph. A circular singleton dependency here does not fail
/// loudly at runtime, it deadlocks the UI thread while resolving MainViewModel,
/// which looks exactly like "the app never opens a window".
/// </summary>
[TestFixture]
public class ServiceRegistrationTests
{
    /// <summary>
    /// Minimal host services that Uno's builder normally supplies, so the graph
    /// under test is the one JsonForge itself registers.
    /// </summary>
    private static ServiceCollection CreateCollection()
    {
        var configuration = new ConfigurationBuilder()
            // Keep the graph test hermetic and fast: no local endpoint probing.
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ai:AutoEnable"] = "false",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddJsonForgeServices();
        return services;
    }

    [Test]
    public void Service_graph_builds_without_validation_errors()
    {
        var services = CreateCollection();

        var act = () => services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });

        act.Should().NotThrow(
            "every registered service must be constructible");
    }

    /// <summary>
    /// The regression test for the startup hang: a circular
    /// AiConfigService -> AiRuntimeState -> AiRuntimeConfig -> AiConfigService
    /// loop blocks the resolving thread forever instead of throwing,
    /// which shows up as "the app launched but no window ever appears".
    /// </summary>
    [Test]
    public void AiRuntimeConfig_resolves_without_deadlocking()
    {
        using var provider = CreateCollection().BuildServiceProvider(validateScopes: true);

        var resolve = Task.Run(() => provider.GetRequiredService<AiRuntimeConfig>());

        resolve.Wait(TimeSpan.FromSeconds(15)).Should().BeTrue(
            "a circular singleton dependency deadlocks resolution on the calling thread");

        resolve.Result.Should().NotBeNull();
    }

    [Test]
    public void AiConfigService_does_not_depend_on_AiRuntimeState()
    {
        // AiRuntimeState -> AiRuntimeConfig -> AiConfigService.
        // If AiConfigService depends on AiRuntimeState again the graph is circular.
        var parameterTypes = typeof(AiConfigService)
            .GetConstructors()
            .SelectMany(c => c.GetParameters())
            .Select(p => p.ParameterType)
            .ToArray();

        parameterTypes.Should().NotContain(
            typeof(AiRuntimeState),
            "AiConfigService must not close the AiRuntimeState/AiRuntimeConfig loop");
    }

    [Test]
    public void AiConfigService_reads_explicit_settings()
    {
        var settings = new FakeSettings(new UserSettings
        {
            AiEnabled = true,
            AiProviderName = "Ollama (local)",
            AiEndpoint = "http://localhost:11434/v1",
            AiModel = "qwen2.5-coder:3b",
            AiApiKey = "secret",
            AiMaxTokens = 2048,
        });

        var config = new AiConfigService(settings).BuildFromSettings();

        config.Should().NotBeNull();
        config!.Enabled.Should().BeTrue();
        config.ProviderName.Should().Be("Ollama (local)");
        config.Endpoint.Should().Be(new Uri("http://localhost:11434/v1"));
        config.Model.Should().Be("qwen2.5-coder:3b");
        config.MaxTokens.Should().Be(2048);
    }

    [Test]
    public void AiConfigService_returns_null_when_ai_is_disabled()
    {
        var settings = new FakeSettings(new UserSettings { AiEnabled = false });

        new AiConfigService(settings).BuildFromSettings().Should().BeNull();
    }

    private sealed class FakeSettings(UserSettings current) : IUserSettingsService
    {
        public UserSettings Current { get; private set; } = current;

        public event Action<UserSettings>? Changed;

        public void Update(UserSettings next)
        {
            Current = next;
            Changed?.Invoke(next);
        }
    }
}
