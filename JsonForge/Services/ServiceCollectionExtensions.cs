using JsonForge.Core.Ai;
using JsonForge.Models;
using JsonForge.ViewModels;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace JsonForge.Services;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddJsonForgeServices(this IServiceCollection services)
    {
        services.AddOptions<AiOptions>().BindConfiguration("Ai");

        services.AddSingleton<IUserSettingsService, UserSettingsService>();
        services.AddSingleton<IFilePickerService, FilePickerService>();

        services.AddSingleton<AiConfigService>();
        services.AddSingleton(provider =>
        {
            var cfg = provider.GetRequiredService<AiConfigService>();
            var runtime = cfg.BuildFromSettings();
            if (runtime is not null) return runtime;
            var opts = provider.GetRequiredService<IOptions<AiOptions>>().Value;
            return AiAutoConfigurator.Resolve(opts);
        });

        services.AddSingleton(provider => new AiRuntimeState(provider.GetRequiredService<AiRuntimeConfig>()));
        services.AddSingleton<OllamaBootstrapper>();
        services.AddSingleton<LocalLlmManager>();

        services.AddSingleton<IJsonAiService, OpenAiCompatibleJsonAiService>();
        services.AddSingleton<MainViewModel>();

        return services;
    }
}
