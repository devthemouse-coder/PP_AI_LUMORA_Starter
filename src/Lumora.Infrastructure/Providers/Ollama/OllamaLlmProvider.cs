using System.Buffers;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lumora.Application.Abstractions.Llm;

namespace Lumora.Infrastructure.Providers.Ollama;

/// <summary>Loopback Ollama API의 런타임과 로컬 모델 카탈로그를 제공합니다.</summary>
public sealed class OllamaLlmProvider : ILlmProvider
{
    private const int MaximumResponseBytes = 1_048_576;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(8);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;

    /// <summary>고정된 numeric loopback 주소를 사용하는 Ollama Provider를 만듭니다.</summary>
    public OllamaLlmProvider(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _ = OllamaEndpoint.Parse(httpClient.BaseAddress?.AbsoluteUri);
        _httpClient = httpClient;
    }

    /// <inheritdoc />
    public string Name => "Ollama";

    /// <inheritdoc />
    public async Task<LlmProviderStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            var versionTask = GetVersionAsync(cancellationToken);
            var modelsTask = GetModelsAsync(cancellationToken);
            await Task.WhenAll(versionTask, modelsTask);
            return new LlmProviderStatus(
                Name,
                LlmConnectionState.Available,
                await versionTask,
                (await modelsTask).Count,
                DateTimeOffset.UtcNow,
                null);
        }
        catch (LlmProviderException exception)
        {
            var connectionState = exception.Kind switch
            {
                LlmProviderErrorKind.RuntimeUnavailable or LlmProviderErrorKind.HttpError =>
                    LlmConnectionState.Unavailable,
                LlmProviderErrorKind.Timeout => LlmConnectionState.TimedOut,
                _ => LlmConnectionState.InvalidResponse
            };
            return new LlmProviderStatus(
                Name,
                connectionState,
                null,
                null,
                DateTimeOffset.UtcNow,
                exception.Kind);
        }
    }

    /// <inheritdoc />
    public async Task<string> GetVersionAsync(CancellationToken cancellationToken)
    {
        var response = await SendAsync<OllamaVersionResponse>(
            HttpMethod.Get,
            "api/version",
            null,
            cancellationToken);
        if (string.IsNullOrWhiteSpace(response.Version))
        {
            throw new LlmProviderException(LlmProviderErrorKind.InvalidResponse);
        }

        return response.Version;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LlmModelInfo>> GetModelsAsync(CancellationToken cancellationToken)
    {
        var response = await SendAsync<OllamaTagsResponse>(
            HttpMethod.Get,
            "api/tags",
            null,
            cancellationToken);
        if (response.Models is null)
        {
            throw new LlmProviderException(LlmProviderErrorKind.InvalidResponse);
        }

        var models = new List<LlmModelInfo>(response.Models.Count);
        foreach (var model in response.Models)
        {
            if (!string.IsNullOrWhiteSpace(model.RemoteModel) || !string.IsNullOrWhiteSpace(model.RemoteHost))
            {
                continue;
            }

            var name = model.Name ?? model.Model;
            var identifier = model.Model ?? model.Name;
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(identifier))
            {
                throw new LlmProviderException(LlmProviderErrorKind.InvalidResponse);
            }

            DateTimeOffset? modifiedAtUtc = null;
            if (!string.IsNullOrWhiteSpace(model.ModifiedAt))
            {
                if (!DateTimeOffset.TryParse(
                        model.ModifiedAt,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                        out var modifiedAt))
                {
                    throw new LlmProviderException(LlmProviderErrorKind.InvalidResponse);
                }

                modifiedAtUtc = modifiedAt;
            }

            var details = model.Details;
            models.Add(new LlmModelInfo(
                name,
                identifier,
                model.Digest,
                model.Size,
                modifiedAtUtc,
                details?.Format,
                details?.Family,
                details?.Families,
                details?.ParameterSize,
                details?.QuantizationLevel));
        }

        return models;
    }

    /// <inheritdoc />
    public async Task<LlmModelAvailability> GetModelAvailabilityAsync(
        string modelName,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelName);
        var normalizedName = modelName.Trim();
        if (normalizedName.Length > 256 || normalizedName.Any(char.IsControl) ||
            normalizedName.Contains('/') || normalizedName.Contains('\\'))
        {
            throw new ArgumentException("모델 이름 형식이 올바르지 않습니다.", nameof(modelName));
        }

        try
        {
            var model = (await GetModelsAsync(cancellationToken))
                .FirstOrDefault(candidate =>
                    string.Equals(candidate.Name, normalizedName, StringComparison.Ordinal) ||
                    string.Equals(candidate.Identifier, normalizedName, StringComparison.Ordinal) ||
                    string.Equals(candidate.Digest, normalizedName, StringComparison.Ordinal));
            return model is null
                ? new LlmModelAvailability(normalizedName, LlmModelAvailabilityState.NotInstalled, null, null)
                : new LlmModelAvailability(normalizedName, LlmModelAvailabilityState.Installed, model, null);
        }
        catch (LlmProviderException exception)
        {
            var state = exception.Kind is LlmProviderErrorKind.RuntimeUnavailable or LlmProviderErrorKind.HttpError
                ? LlmModelAvailabilityState.RuntimeUnavailable
                : LlmModelAvailabilityState.Unknown;
            return new LlmModelAvailability(normalizedName, state, null, exception.Kind);
        }
    }

    internal static HttpClient CreateHttpClient(OllamaEndpoint endpoint, TimeSpan? requestTimeout = null)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            UseCookies = false,
            ConnectTimeout = TimeSpan.FromSeconds(2),
            PooledConnectionLifetime = TimeSpan.FromMinutes(2)
        };
        var client = new HttpClient(handler, disposeHandler: true)
        {
            BaseAddress = endpoint.BaseAddress,
            Timeout = requestTimeout ?? RequestTimeout,
            MaxResponseContentBufferSize = MaximumResponseBytes
        };
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    private async Task<TResponse> SendAsync<TResponse>(
        HttpMethod method,
        string relativePath,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, relativePath)
        {
            Content = content
        };

        try
        {
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new LlmProviderException(LlmProviderErrorKind.HttpError);
            }

            var json = await ReadBoundedContentAsync(response.Content, cancellationToken);
            try
            {
                return JsonSerializer.Deserialize<TResponse>(json, JsonOptions)
                    ?? throw new LlmProviderException(LlmProviderErrorKind.InvalidResponse);
            }
            catch (JsonException)
            {
                throw new LlmProviderException(LlmProviderErrorKind.InvalidResponse);
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
    }

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

    private sealed record OllamaVersionResponse(
        [property: JsonPropertyName("version")] string? Version);

    private sealed record OllamaTagsResponse(
        [property: JsonPropertyName("models")] List<OllamaModelResponse>? Models);

    private sealed record OllamaModelResponse(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("model")] string? Model,
        [property: JsonPropertyName("remote_model")] string? RemoteModel,
        [property: JsonPropertyName("remote_host")] string? RemoteHost,
        [property: JsonPropertyName("modified_at")] string? ModifiedAt,
        [property: JsonPropertyName("size")] long? Size,
        [property: JsonPropertyName("digest")] string? Digest,
        [property: JsonPropertyName("details")] OllamaModelDetails? Details);

    private sealed record OllamaModelDetails(
        [property: JsonPropertyName("format")] string? Format,
        [property: JsonPropertyName("family")] string? Family,
        [property: JsonPropertyName("families")] IReadOnlyList<string>? Families,
        [property: JsonPropertyName("parameter_size")] string? ParameterSize,
        [property: JsonPropertyName("quantization_level")] string? QuantizationLevel);
}
