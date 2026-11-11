using Lumora.Application.Abstractions.Llm;
using Lumora.Application.UseCases.Chat;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lumora.Infrastructure.Providers.Ollama;

/// <summary>loopback으로 제한된 Ollama Provider를 등록합니다.</summary>
public static class OllamaProviderServiceCollectionExtensions
{
    public static IServiceCollection AddOllamaLlmProvider(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var endpoint = OllamaEndpoint.Parse(configuration["Llm:Ollama:BaseAddress"]);
        services.AddSingleton(endpoint);
        services.AddSingleton(provider =>
            OllamaLlmProvider.CreateHttpClient(provider.GetRequiredService<OllamaEndpoint>()));
        services.AddSingleton<ILlmProvider, OllamaLlmProvider>();
        services.AddSingleton<ILlmChatService>(provider =>
            new OllamaLlmChatService(
                OllamaLlmProvider.CreateHttpClient(
                    provider.GetRequiredService<OllamaEndpoint>(),
                    TimeSpan.FromSeconds(60))));
        services.TryAddSingleton(new ChatModelOptions(
            configuration["Llm:ModelName"] ?? "qwen3.5:4b-q4_K_M"));
        return services;
    }
}
