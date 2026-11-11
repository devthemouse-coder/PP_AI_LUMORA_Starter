using System.Net;
using System.Text.Json;
using Lumora.Application.Abstractions.Llm;
using Lumora.Infrastructure.Providers.Ollama;

namespace Lumora.Infrastructure.IntegrationTests;

public sealed class OllamaLlmChatServiceTests
{
    [Fact]
    public async Task Sends_loopback_chat_request_with_json_thinking_and_generation_options()
    {
        using var service = CreateService((request, _) =>
        {
            Assert.Equal("/api/chat", request.RequestUri?.AbsolutePath);
            Assert.Equal("127.0.0.1", request.RequestUri?.Host);
            using var payload = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            var root = payload.RootElement;
            Assert.Equal("synthetic:latest", root.GetProperty("model").GetString());
            Assert.False(root.GetProperty("stream").GetBoolean());
            Assert.False(root.GetProperty("think").GetBoolean());
            Assert.Equal("json", root.GetProperty("format").GetString());
            Assert.Equal(0, root.GetProperty("options").GetProperty("temperature").GetDouble());
            Assert.Equal(256, root.GetProperty("options").GetProperty("num_predict").GetInt32());
            Assert.Equal(4096, root.GetProperty("options").GetProperty("num_ctx").GetInt32());
            Assert.Equal("system", root.GetProperty("messages")[0].GetProperty("role").GetString());
            return Task.FromResult(JsonResponse(
                """{"model":"synthetic:latest","message":{"role":"assistant","content":"서울입니다.","thinking":"ignored"},"done":true,"done_reason":"stop","prompt_eval_count":20,"eval_count":3}"""));
        });

        var result = await service.CompleteAsync(CreateRequest(), CancellationToken.None);

        Assert.Equal("서울입니다.", result.Content);
        Assert.Equal("stop", result.FinishReason);
        Assert.Equal(20, result.Usage?.PromptTokens);
        Assert.Equal(3, result.Usage?.GeneratedTokens);
    }

    [Theory]
    [InlineData("""{"message":{"role":"assistant","content":""},"done":true}""")]
    [InlineData("""{"message":{"role":"assistant","content":"not final"},"done":false}""")]
    [InlineData("{broken")]
    public async Task Empty_incomplete_or_invalid_response_is_rejected(string responseBody)
    {
        using var service = CreateService((_, _) => Task.FromResult(JsonResponse(responseBody)));

        var exception = await Assert.ThrowsAsync<LlmProviderException>(
            () => service.CompleteAsync(CreateRequest(), CancellationToken.None));

        Assert.Equal(LlmProviderErrorKind.InvalidResponse, exception.Kind);
    }

    [Fact]
    public async Task Length_finish_reason_is_preserved_for_application_to_reject()
    {
        using var service = CreateService((_, _) => Task.FromResult(JsonResponse(
            """{"message":{"role":"assistant","content":"{}"},"done":true,"done_reason":"length"}""")));

        var result = await service.CompleteAsync(CreateRequest(), CancellationToken.None);

        Assert.Equal("length", result.FinishReason);
    }

    [Fact]
    public async Task Missing_done_reason_uses_completion_and_token_limit_metadata()
    {
        using var completeService = CreateService((_, _) => Task.FromResult(JsonResponse(
            """{"message":{"role":"assistant","content":"{}"},"done":true,"eval_count":3}""")));
        var complete = await completeService.CompleteAsync(CreateRequest(), CancellationToken.None);
        Assert.Equal("stop", complete.FinishReason);

        using var limitedService = CreateService((_, _) => Task.FromResult(JsonResponse(
            """{"message":{"role":"assistant","content":"{}"},"done":true,"eval_count":256}""")));
        var limited = await limitedService.CompleteAsync(CreateRequest(), CancellationToken.None);
        Assert.Equal("length", limited.FinishReason);
    }

    [Fact]
    public async Task Http_error_does_not_expose_response_body()
    {
        using var service = CreateService((_, _) => Task.FromResult(new HttpResponseMessage(
            HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("private server detail")
        }));

        var exception = await Assert.ThrowsAsync<LlmProviderException>(
            () => service.CompleteAsync(CreateRequest(), CancellationToken.None));

        Assert.Equal(LlmProviderErrorKind.HttpError, exception.Kind);
        Assert.DoesNotContain("private", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Timeout_is_classified_and_caller_cancellation_is_propagated()
    {
        using var timeoutService = CreateService(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return JsonResponse("{}");
        }, TimeSpan.FromMilliseconds(50));

        var timeout = await Assert.ThrowsAsync<LlmProviderException>(
            () => timeoutService.CompleteAsync(CreateRequest(), CancellationToken.None));
        Assert.Equal(LlmProviderErrorKind.Timeout, timeout.Kind);

        using var cancellationService = CreateService(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return JsonResponse("{}");
        });
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => cancellationService.CompleteAsync(CreateRequest(), cancellation.Token));
    }

    [Fact]
    public void Public_host_is_rejected_by_chat_adapter()
    {
        using var client = new HttpClient(new DelegateHandler((_, _) =>
            throw new InvalidOperationException("The handler must not be reached")))
        {
            BaseAddress = new Uri("http://8.8.8.8:11434/")
        };

        Assert.Throws<InvalidOperationException>(() => new OllamaLlmChatService(client));
    }

    private static LlmChatRequest CreateRequest() =>
        new(
            "synthetic:latest",
            [
                new LlmChatMessage(LlmMessageRole.System, "system instructions"),
                new LlmChatMessage(LlmMessageRole.User, "합성 테스트")
            ],
            new LlmGenerationOptions(0, 256, 4096, false, true));

    private static OllamaLlmChatService CreateService(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory,
        TimeSpan? timeout = null)
    {
        var client = new HttpClient(new DelegateHandler(responseFactory))
        {
            BaseAddress = new Uri("http://127.0.0.1:11434/"),
            Timeout = timeout ?? TimeSpan.FromSeconds(5)
        };
        return new OllamaLlmChatService(client);
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
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
}
