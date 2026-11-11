namespace Lumora.Contracts.Llm;

/// <summary>로컬 LLM Provider의 실시간 연결 상태입니다.</summary>
public sealed record LlmProviderStatusResponse(
    string ProviderName,
    string ConnectionState,
    string? RuntimeVersion,
    int? InstalledModelCount,
    DateTimeOffset CheckedAtUtc,
    string? ErrorKind);

/// <summary>Ollama 모델 태그 API가 제공한 로컬 모델 메타데이터입니다.</summary>
public sealed record LlmModelResponse(
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

/// <summary>설치된 로컬 모델 목록입니다.</summary>
public sealed record LlmModelListResponse(IReadOnlyList<LlmModelResponse> Models);

/// <summary>특정 모델의 설치 상태입니다. 추론 가능 여부를 의미하지 않습니다.</summary>
public sealed record LlmModelAvailabilityResponse(
    string ModelName,
    string State,
    LlmModelResponse? Model,
    string? ErrorKind);

/// <summary>런타임 버전 정보입니다.</summary>
public sealed record LlmVersionResponse(string ProviderName, string Version);
