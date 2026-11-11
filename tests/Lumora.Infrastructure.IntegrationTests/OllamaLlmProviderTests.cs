using System.Net;
using System.Text;
using Lumora.Application.Abstractions.Llm;
using Lumora.Infrastructure.Providers.Ollama;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lumora.Infrastructure.IntegrationTests;

public sealed class OllamaLlmProviderTests
{
    [Fact]
    public async Task Status_reports_runtime_version_and_local_model_count()
    {
        using var provider = CreateProvider((request, _) =>
            Task.FromResult(JsonResponse(request.RequestUri!.AbsolutePath switch
            {
                "/api/version" => """{"version":"0.40.15"}""",
                "/api/tags" => """{"models":[{"name":"local:latest","model":"local:latest","size":123,"modified_at":"2025-01-01T00:00:00Z","digest":"abc","details":{"format":"gguf","family":"test","families":["test"],"parameter_size":"7B","quantization_level":"Q4_K_M"}},{"name":"remote:latest","model":"remote:latest","remote_model":"remote:latest","remote_host":"https://example.invalid"}]}""",
                _ => throw new InvalidOperationException()
            })));

        var status = await provider.GetStatusAsync(CancellationToken.None);
        var models = await provider.GetModelsAsync(CancellationToken.None);

        Assert.Equal(LlmConnectionState.Available, status.ConnectionState);
        Assert.Equal("0.40.15", status.RuntimeVersion);
        Assert.Equal(1, status.InstalledModelCount);
        var model = Assert.Single(models);
        Assert.Equal("local:latest", model.Identifier);
        Assert.Equal(123, model.SizeBytes);
        Assert.Equal("Q4_K_M", model.QuantizationLevel);
    }

    [Fact]
    public async Task Connection_failure_is_reported_without_internal_exception_details()
    {
        using var provider = CreateProvider((_, _) =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException("private endpoint details")));

        var status = await provider.GetStatusAsync(CancellationToken.None);

        Assert.Equal(LlmConnectionState.Unavailable, status.ConnectionState);
        Assert.Equal(LlmProviderErrorKind.RuntimeUnavailable, status.ErrorKind);
        Assert.Null(status.RuntimeVersion);
    }

    [Fact]
    public async Task Request_timeout_is_classified()
    {
        using var client = CreateClient(new DelegateHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return JsonResponse("{}");
        }));
        client.Timeout = TimeSpan.FromMilliseconds(50);
        var provider = new OllamaLlmProvider(client);

        var exception = await Assert.ThrowsAsync<LlmProviderException>(
            () => provider.GetVersionAsync(CancellationToken.None));

        Assert.Equal(LlmProviderErrorKind.Timeout, exception.Kind);
        Assert.DoesNotContain("private", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Version_and_empty_model_catalog_are_supported()
    {
        using var provider = CreateProvider((request, _) =>
            Task.FromResult(JsonResponse(request.RequestUri!.AbsolutePath == "/api/version"
                ? """{"version":"0.40.15"}"""
                : """{"models":[]}""")));

        Assert.Equal("0.40.15", await provider.GetVersionAsync(CancellationToken.None));
        Assert.Empty(await provider.GetModelsAsync(CancellationToken.None));
        var availability = await provider.GetModelAvailabilityAsync("missing:latest", CancellationToken.None);
        Assert.Equal(LlmModelAvailabilityState.NotInstalled, availability.State);
    }

    [Fact]
    public async Task Invalid_json_and_http_errors_are_classified()
    {
        using var invalidJsonProvider = CreateProvider((_, _) =>
            Task.FromResult(JsonResponse("{not-json")));
        var jsonException = await Assert.ThrowsAsync<LlmProviderException>(
            () => invalidJsonProvider.GetVersionAsync(CancellationToken.None));
        Assert.Equal(LlmProviderErrorKind.InvalidResponse, jsonException.Kind);

        using var httpErrorProvider = CreateProvider((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("sensitive server response")
            }));
        var httpException = await Assert.ThrowsAsync<LlmProviderException>(
            () => httpErrorProvider.GetVersionAsync(CancellationToken.None));
        Assert.Equal(LlmProviderErrorKind.HttpError, httpException.Kind);
        Assert.DoesNotContain("sensitive", httpException.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("http://8.8.8.8:11434/")]
    [InlineData("http://localhost:11434/")]
    [InlineData("https://127.0.0.1:11434/")]
    [InlineData("http://127.0.0.1:11435/")]
    [InlineData("http://127.0.0.1:11434/api/")]
    public void Public_addresses_hostnames_and_non_default_endpoints_are_rejected(string address)
    {
        using var client = new HttpClient(new DelegateHandler((_, _) =>
            throw new InvalidOperationException("The handler must not be reached")))
        {
            BaseAddress = new Uri(address)
        };

        Assert.Throws<InvalidOperationException>(() => new OllamaLlmProvider(client));
    }

    [Fact]
    public void Ipv6_loopback_is_allowed()
    {
        using var client = CreateClient(new DelegateHandler((_, _) =>
            throw new InvalidOperationException("No request is expected")));
        client.BaseAddress = new Uri("http://[::1]:11434/");

        _ = new OllamaLlmProvider(client);
    }

    [Fact]
    public void Service_registration_rejects_external_addresses()
    {
        var configuration = new ConfigurationManager
        {
            ["Llm:Ollama:BaseAddress"] = "http://8.8.8.8:11434/"
        };

        Assert.Throws<InvalidOperationException>(
            () => new ServiceCollection().AddOllamaLlmProvider(configuration));
    }

    [Fact]
    public async Task Redirect_response_is_not_followed()
    {
        var requestCount = 0;
        using var provider = CreateProvider((_, _) =>
        {
            requestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect)
            {
                Headers = { Location = new Uri("https://example.invalid/collect") }
            });
        });

        var exception = await Assert.ThrowsAsync<LlmProviderException>(
            () => provider.GetVersionAsync(CancellationToken.None));

        Assert.Equal(LlmProviderErrorKind.HttpError, exception.Kind);
        Assert.Equal(1, requestCount);
    }

    [Fact]
    public async Task Caller_cancellation_is_propagated()
    {
        using var provider = CreateProvider(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return JsonResponse("{}");
        });
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(40));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.GetVersionAsync(cancellation.Token));
    }

    [Fact]
    public async Task Oversized_response_is_rejected()
    {
        using var provider = CreateProvider((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"version\":\"" + new string('x', 1_048_600) + "\"}",
                    Encoding.UTF8,
                    "application/json")
            }));

        var exception = await Assert.ThrowsAsync<LlmProviderException>(
            () => provider.GetVersionAsync(CancellationToken.None));

        Assert.Equal(LlmProviderErrorKind.InvalidResponse, exception.Kind);
    }

    private static ProviderFixture CreateProvider(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory) =>
        new(new DelegateHandler(responseFactory));

    private static HttpClient CreateClient(HttpMessageHandler handler) =>
        new(handler)
        {
            BaseAddress = new Uri(OllamaEndpointAddress),
            Timeout = Timeout.InfiniteTimeSpan
        };

    private const string OllamaEndpointAddress = "http://127.0.0.1:11434/";

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class DelegateHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            responseFactory(request, cancellationToken);
    }

    private sealed class ProviderFixture : ILlmProvider, IDisposable
    {
        private readonly HttpClient _client;
        private readonly OllamaLlmProvider _provider;

        public ProviderFixture(HttpMessageHandler handler)
        {
            _client = CreateClient(handler);
            _provider = new OllamaLlmProvider(_client);
        }

        public string Name => _provider.Name;

        public Task<LlmProviderStatus> GetStatusAsync(CancellationToken cancellationToken) =>
            _provider.GetStatusAsync(cancellationToken);

        public Task<string> GetVersionAsync(CancellationToken cancellationToken) =>
            _provider.GetVersionAsync(cancellationToken);

        public Task<IReadOnlyList<LlmModelInfo>> GetModelsAsync(CancellationToken cancellationToken) =>
            _provider.GetModelsAsync(cancellationToken);

        public Task<LlmModelAvailability> GetModelAvailabilityAsync(
            string modelName,
            CancellationToken cancellationToken) =>
            _provider.GetModelAvailabilityAsync(modelName, cancellationToken);

        public void Dispose() => _client.Dispose();
    }
}
