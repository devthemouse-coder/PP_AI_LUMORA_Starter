using System.Buffers;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lumora.Application.Abstractions.Llm;

namespace Lumora.Infrastructure.Providers.Ollama;

/// <summary>Loopback Ollama의 비스트리밍 대화 생성을 수행합니다.</summary>
public sealed class OllamaLlmChatService : ILlmChatService, IDisposable
{
    private const int MaximumRequestBytes = 32_768;
    private const int MaximumResponseBytes = 262_144;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;

    public OllamaLlmChatService(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _ = OllamaEndpoint.Parse(httpClient.BaseAddress?.AbsoluteUri);
        _httpClient = httpClient;
    }

    public async Task<LlmChatResponse> CompleteAsync(
        LlmChatRequest request,
        CancellationToken cancellationToken)
    {
        ValidateRequest(request);
        var payload = new OllamaChatRequest(
            request.ModelName,
            request.Messages.Select(message => new OllamaChatMessage(
                message.Role switch
                {
                    LlmMessageRole.System => "system",
                    LlmMessageRole.User => "user",
                    LlmMessageRole.Assistant => "assistant",
                    _ => throw new LlmProviderException(LlmProviderErrorKind.InvalidResponse)
                },
                message.Content)).ToArray(),
            Stream: false,
            Think: request.Options?.ThinkingEnabled,
            Format: request.Options?.JsonFormat == true ? "json" : null,
            Options: new OllamaGenerationOptions(
                request.Options?.Temperature,
                request.Options?.MaximumOutputTokens,
                request.Options?.ContextLength));

        var requestBytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        if (requestBytes.Length > MaximumRequestBytes)
        {
            throw new LlmProviderException(LlmProviderErrorKind.InvalidResponse);
        }

        using var message = new HttpRequestMessage(HttpMethod.Post, "api/chat")
        {
            Content = new ByteArrayContent(requestBytes)
        };
        message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        try
        {
            using var response = await _httpClient.SendAsync(
                message,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new LlmProviderException(LlmProviderErrorKind.HttpError);
            }

            var responseBytes = await ReadBoundedContentAsync(response.Content, cancellationToken);
            try
            {
                OllamaChatResponse? result;
                try
                {
                    result = JsonSerializer.Deserialize<OllamaChatResponse>(responseBytes, JsonOptions);
                }
                catch (JsonException)
                {
                    throw new LlmProviderException(LlmProviderErrorKind.InvalidResponse);
                }

                if (result?.Done != true ||
                    result.Message is null ||
                    !string.Equals(result.Message.Role, "assistant", StringComparison.Ordinal) ||
                    string.IsNullOrWhiteSpace(result.Message.Content))
                {
                    throw new LlmProviderException(LlmProviderErrorKind.InvalidResponse);
                }

                var finishReason = result.DoneReason;
                if (string.IsNullOrWhiteSpace(finishReason))
                {
                    finishReason = request.Options?.MaximumOutputTokens is { } maximumOutputTokens &&
                        result.EvalCount is { } generatedTokens &&
                        generatedTokens >= maximumOutputTokens
                        ? "length"
                        : "stop";
                }

                return new LlmChatResponse(
                    string.IsNullOrWhiteSpace(result.Model) ? request.ModelName : result.Model,
                    result.Message.Content,
                    finishReason,
                    new LlmTokenUsage(
                        result.PromptEvalCount,
                        result.EvalCount,
                        result.PromptEvalCount + result.EvalCount));
            }
            finally
            {
                Array.Clear(responseBytes);
            }
        }
        catch (LlmProviderException)
        {
            throw;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new LlmProviderException(LlmProviderErrorKind.Timeout);
        }
        catch (HttpRequestException)
        {
            throw new LlmProviderException(LlmProviderErrorKind.RuntimeUnavailable);
        }
        catch (IOException)
        {
            throw new LlmProviderException(LlmProviderErrorKind.RuntimeUnavailable);
        }
        finally
        {
            Array.Clear(requestBytes);
        }
    }

    public void Dispose() => _httpClient.Dispose();

    private static async Task<byte[]> ReadBoundedContentAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is > MaximumResponseBytes)
        {
            throw new LlmProviderException(LlmProviderErrorKind.InvalidResponse);
        }

        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var rentedBuffer = ArrayPool<byte>.Shared.Rent(16_384);
        try
        {
            while (true)
            {
                var read = await stream.ReadAsync(rentedBuffer.AsMemory(0, rentedBuffer.Length), cancellationToken);
                if (read == 0)
                {
                    return buffer.ToArray();
                }

                if (buffer.Length + read > MaximumResponseBytes)
                {
                    throw new LlmProviderException(LlmProviderErrorKind.InvalidResponse);
                }

                await buffer.WriteAsync(rentedBuffer.AsMemory(0, read), cancellationToken);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rentedBuffer, clearArray: true);
        }
    }

    private static void ValidateRequest(LlmChatRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ModelName);
        if (request.ModelName.Length > 256 ||
            request.ModelName.Any(char.IsControl) ||
            request.Messages is null ||
            request.Messages.Count is < 1 or > 20 ||
            request.Messages.Any(item => string.IsNullOrWhiteSpace(item.Content)))
        {
            throw new ArgumentException("대화 요청 형식이 올바르지 않습니다.", nameof(request));
        }
    }

    private sealed record OllamaChatRequest(
        string Model,
        IReadOnlyList<OllamaChatMessage> Messages,
        bool Stream,
        bool? Think,
        string? Format,
        OllamaGenerationOptions Options);

    private sealed record OllamaChatMessage(string Role, string Content);

    private sealed record OllamaGenerationOptions(
        double? Temperature,
        [property: JsonPropertyName("num_predict")] int? MaximumOutputTokens,
        [property: JsonPropertyName("num_ctx")] int? ContextLength);

    private sealed record OllamaChatResponse(
        string? Model,
        OllamaChatResponseMessage? Message,
        bool? Done,
        [property: JsonPropertyName("done_reason")] string? DoneReason,
        [property: JsonPropertyName("prompt_eval_count")] int? PromptEvalCount,
        [property: JsonPropertyName("eval_count")] int? EvalCount);

    private sealed record OllamaChatResponseMessage(string? Role, string? Content);
}
