namespace Lumora.Application.Abstractions.Llm;

/// <summary>LLM 런타임 연결 상태입니다.</summary>
public enum LlmConnectionState
{
    Available,
    Unavailable,
    TimedOut,
    InvalidResponse
}

/// <summary>모델 설치 확인 결과입니다. 추론 성공 여부와는 별개입니다.</summary>
public enum LlmModelAvailabilityState
{
    RuntimeUnavailable,
    NotInstalled,
    Installed,
    Unknown
}

/// <summary>Provider 통신 실패의 안전한 분류입니다.</summary>
public enum LlmProviderErrorKind
{
    RuntimeUnavailable,
    Timeout,
    HttpError,
    InvalidResponse
}

/// <summary>Provider의 실시간 연결 확인 결과입니다.</summary>
public sealed record LlmProviderStatus(
    string ProviderName,
    LlmConnectionState ConnectionState,
    string? RuntimeVersion,
    int? InstalledModelCount,
    DateTimeOffset CheckedAtUtc,
    LlmProviderErrorKind? ErrorKind);

/// <summary>로컬 모델 목록에서 제공한 모델 메타데이터입니다.</summary>
public sealed record LlmModelInfo(
    string Name,
    string Identifier,
    string? Digest,
    long? SizeBytes,
    DateTimeOffset? ModifiedAtUtc,
    string? Format,
    string? Family,
    IReadOnlyList<string>? Families,
    string? ParameterSize,
    string? QuantizationLevel);

/// <summary>특정 모델의 로컬 설치 여부입니다.</summary>
public sealed record LlmModelAvailability(
    string ModelName,
    LlmModelAvailabilityState State,
    LlmModelInfo? Model,
    LlmProviderErrorKind? ErrorKind);

/// <summary>LLM 런타임과 모델 카탈로그를 제공하는 기술 독립 계약입니다.</summary>
public interface ILlmProvider
{
    string Name { get; }

    Task<LlmProviderStatus> GetStatusAsync(CancellationToken cancellationToken);

    Task<string> GetVersionAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<LlmModelInfo>> GetModelsAsync(CancellationToken cancellationToken);

    Task<LlmModelAvailability> GetModelAvailabilityAsync(
        string modelName,
        CancellationToken cancellationToken);
}

/// <summary>Provider 오류를 민감한 원문 없이 Application에 전달합니다.</summary>
public sealed class LlmProviderException(LlmProviderErrorKind kind)
    : Exception($"LLM Provider 요청 실패: {kind}.")
{
    public LlmProviderErrorKind Kind { get; } = kind;
}

/// <summary>향후 일반 대화 요청에 사용할 Provider 독립 메시지 역할입니다.</summary>
public enum LlmMessageRole
{
    System,
    User,
    Assistant
}

/// <summary>향후 일반 대화 요청의 단일 메시지입니다.</summary>
public sealed record LlmChatMessage(LlmMessageRole Role, string Content);

/// <summary>향후 일반 대화 생성 설정입니다. 실제 추론은 이번 단계에서 호출하지 않습니다.</summary>
public sealed record LlmGenerationOptions(
    double? Temperature = null,
    int? MaximumOutputTokens = null,
    int? ContextLength = null,
    bool? ThinkingEnabled = null,
    bool JsonFormat = false);

/// <summary>향후 Provider 독립 대화 요청입니다.</summary>
public sealed record LlmChatRequest(
    string ModelName,
    IReadOnlyList<LlmChatMessage> Messages,
    LlmGenerationOptions? Options = null);

/// <summary>향후 제공자가 반환할 수 있는 토큰 사용량입니다.</summary>
public sealed record LlmTokenUsage(
    int? PromptTokens,
    int? GeneratedTokens,
    int? TotalTokens);

/// <summary>향후 Provider 독립 대화 응답입니다.</summary>
public sealed record LlmChatResponse(
    string ModelName,
    string Content,
    string? FinishReason,
    LlmTokenUsage? Usage);

/// <summary>향후 대화 생성 기능의 교체 가능한 Application 계약입니다.</summary>
public interface ILlmChatService
{
    Task<LlmChatResponse> CompleteAsync(
        LlmChatRequest request,
        CancellationToken cancellationToken);
}
