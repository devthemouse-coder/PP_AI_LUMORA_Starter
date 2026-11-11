using System.Net;
using System.Net.Http.Json;
using Lumora.Application.Abstractions.Llm;
using Lumora.Application.Abstractions.Persistence;
using Lumora.Application.UseCases.Chat;
using Lumora.Application.UseCases.Conversations;
using Lumora.Contracts.Chat;
using Lumora.Contracts.Llm;
using Lumora.Domain.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lumora.Api.IntegrationTests;

public sealed class LlmApiTests
{
    [Fact]
    public async Task Llm_endpoints_are_not_mapped_outside_development()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseEnvironment("Production"));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/llm/status");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        using var chatResponse = await client.PostAsJsonAsync(
            "/api/chat",
            new { message = "synthetic", language = "ko" });
        Assert.Equal(HttpStatusCode.NotFound, chatResponse.StatusCode);
    }

    [Fact]
    public async Task Development_endpoints_return_live_provider_contracts()
    {
        await using var factory = CreateFactory(new FakeLlmProvider());
        using var client = factory.CreateClient();

        using var statusResponse = await client.GetAsync("/api/llm/status");
        Assert.Equal(HttpStatusCode.OK, statusResponse.StatusCode);
        var status = await statusResponse.Content.ReadFromJsonAsync<LlmProviderStatusResponse>();
        Assert.NotNull(status);
        Assert.Equal("Available", status.ConnectionState);
        Assert.Equal(1, status.InstalledModelCount);

        using var versionResponse = await client.GetAsync("/api/llm/version");
        Assert.Equal(HttpStatusCode.OK, versionResponse.StatusCode);

        using var modelsResponse = await client.GetAsync("/api/llm/models");
        Assert.Equal(HttpStatusCode.OK, modelsResponse.StatusCode);
        var models = await modelsResponse.Content.ReadFromJsonAsync<LlmModelListResponse>();
        Assert.NotNull(models);
        Assert.Single(models.Models);

        using var modelResponse = await client.GetAsync("/api/llm/models/synthetic%3Alatest");
        Assert.Equal(HttpStatusCode.OK, modelResponse.StatusCode);
        var availability = await modelResponse.Content.ReadFromJsonAsync<LlmModelAvailabilityResponse>();
        Assert.NotNull(availability);
        Assert.Equal("Installed", availability.State);

        using var openApiResponse = await client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, openApiResponse.StatusCode);
        var openApi = await openApiResponse.Content.ReadAsStringAsync();
        Assert.Contains("/api/llm/status", openApi, StringComparison.Ordinal);
        Assert.Contains("/api/llm/models/{modelName}", openApi, StringComparison.Ordinal);
        Assert.Contains("/api/chat", openApi, StringComparison.Ordinal);
        Assert.Contains("/api/conversations", openApi, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Chat_uses_only_the_configured_development_scope()
    {
        var fakeUseCases = new FakeChatUseCases();
        await using var factory = CreateChatFactory(fakeUseCases);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/chat",
            new { message = "홍길동의 생일은 5월 10일이야", language = "ko" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            new TenantUserScope(
                Guid.Parse("10000000-0000-0000-0000-000000000001"),
                Guid.Parse("20000000-0000-0000-0000-000000000002")),
            fakeUseCases.LastScope);
        var result = await response.Content.ReadFromJsonAsync<ChatTurnResponse>();
        Assert.NotNull(result);
        Assert.Equal("Synthetic response", result.Response);
        Assert.True(result.Persisted);
    }

    [Fact]
    public async Task Chat_fails_closed_without_development_scope()
    {
        var fakeUseCases = new FakeChatUseCases();
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.ConfigureTestServices(services =>
                    services.Replace(ServiceDescriptor.Singleton<IChatUseCases>(fakeUseCases)));
            });
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/chat",
            new { message = "synthetic", language = "ko" });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Null(fakeUseCases.LastScope);
    }

    [Fact]
    public async Task Conversation_history_routes_return_use_case_pages()
    {
        var fakeUseCases = new FakeConversationUseCases();
        await using var factory = CreateConversationFactory(fakeUseCases);
        using var client = factory.CreateClient();

        using var listResponse = await client.GetAsync("/api/conversations");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        using var messagesResponse = await client.GetAsync(
            $"/api/conversations/{fakeUseCases.ConversationId}/messages");
        Assert.Equal(HttpStatusCode.OK, messagesResponse.StatusCode);
        var page = await messagesResponse.Content.ReadFromJsonAsync<ConversationMessagePageResponse>();
        Assert.NotNull(page);
        Assert.Equal("Synthetic message", Assert.Single(page.Items).Content);
    }

    [Fact]
    public async Task Provider_failure_returns_safe_problem_details()
    {
        await using var factory = CreateFactory(new FakeLlmProvider(throwOnVersion: true));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/llm/version");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Local LLM unavailable", body, StringComparison.Ordinal);
        Assert.DoesNotContain("private", body, StringComparison.OrdinalIgnoreCase);
    }

    private static WebApplicationFactory<Program> CreateFactory(ILlmProvider provider) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ILlmProvider>();
                services.AddSingleton(provider);
            });
        });

    private static WebApplicationFactory<Program> CreateChatFactory(IChatUseCases chatUseCases) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["MemoryApi:EnableDevelopmentScope"] = "true",
                    ["MemoryApi:TenantId"] = "10000000-0000-0000-0000-000000000001",
                    ["MemoryApi:UserId"] = "20000000-0000-0000-0000-000000000002"
                }));
            builder.ConfigureTestServices(services =>
                services.Replace(ServiceDescriptor.Singleton(chatUseCases)));
        });

    private static WebApplicationFactory<Program> CreateConversationFactory(
        IConversationUseCases conversationUseCases) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["MemoryApi:EnableDevelopmentScope"] = "true",
                    ["MemoryApi:TenantId"] = "10000000-0000-0000-0000-000000000001",
                    ["MemoryApi:UserId"] = "20000000-0000-0000-0000-000000000002"
                }));
            builder.ConfigureTestServices(services =>
                services.Replace(ServiceDescriptor.Singleton(conversationUseCases)));
        });

    private sealed class FakeLlmProvider(bool throwOnVersion = false) : ILlmProvider
    {
        private static readonly LlmModelInfo Model = new(
            "synthetic:latest",
            "synthetic:latest",
            "synthetic-digest",
            1024,
            DateTimeOffset.UnixEpoch,
            "gguf",
            "synthetic",
            ["synthetic"],
            "1B",
            "Q4_K_M");

        public string Name => "Fake local provider";

        public Task<LlmProviderStatus> GetStatusAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new LlmProviderStatus(
                Name,
                LlmConnectionState.Available,
                "0.test",
                1,
                DateTimeOffset.UtcNow,
                null));

        public Task<string> GetVersionAsync(CancellationToken cancellationToken) =>
            throwOnVersion
                ? Task.FromException<string>(new LlmProviderException(LlmProviderErrorKind.RuntimeUnavailable))
                : Task.FromResult("0.test");

        public Task<IReadOnlyList<LlmModelInfo>> GetModelsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<LlmModelInfo>>([Model]);

        public Task<LlmModelAvailability> GetModelAvailabilityAsync(
            string modelName,
            CancellationToken cancellationToken) =>
            Task.FromResult(new LlmModelAvailability(
                modelName,
                LlmModelAvailabilityState.Installed,
                Model,
                null));
    }

    private sealed class FakeChatUseCases : IChatUseCases
    {
        public TenantUserScope? LastScope { get; private set; }

        public Task<ChatTurnResult> SendAsync(
            TenantUserScope scope,
            ChatTurnCommand command,
            CancellationToken cancellationToken)
        {
            LastScope = scope;
            return Task.FromResult(new ChatTurnResult(Guid.NewGuid(), "Synthetic response", true));
        }
    }

    private sealed class FakeConversationUseCases : IConversationUseCases
    {
        public Guid ConversationId { get; } = Guid.NewGuid();

        public Task<PageResult<ConversationSummary>> ListAsync(
            TenantUserScope scope,
            PageRequest page,
            CancellationToken cancellationToken) =>
            Task.FromResult(new PageResult<ConversationSummary>(
                [new ConversationSummary(
                    ConversationId,
                    DateTimeOffset.UnixEpoch,
                    DateTimeOffset.UnixEpoch,
                    DateTimeOffset.UnixEpoch)],
                page.PageNumber,
                page.PageSize,
                1));

        public Task<ConversationMessagePage?> ListMessagesAsync(
            TenantUserScope scope,
            Guid conversationId,
            PageRequest page,
            CancellationToken cancellationToken) =>
            Task.FromResult<ConversationMessagePage?>(conversationId == ConversationId
                ? new ConversationMessagePage(
                    [new ConversationMessageDetails(
                        Guid.NewGuid(),
                        MessageRole.Assistant,
                        "Synthetic message",
                        DateTimeOffset.UnixEpoch)],
                    page.PageNumber,
                    page.PageSize,
                    1)
                : null);

        public Task<IReadOnlyList<ConversationMessageDetails>> GetRecentMessagesAsync(
            TenantUserScope scope,
            Guid conversationId,
            int messageCount,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ConversationMessageDetails>>([]);

        public Task<Guid> SaveExchangeAsync(
            TenantUserScope scope,
            Guid? conversationId,
            string userMessage,
            string assistantMessage,
            CancellationToken cancellationToken) =>
            Task.FromResult(conversationId ?? ConversationId);
    }
}
