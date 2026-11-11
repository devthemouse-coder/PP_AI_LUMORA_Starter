namespace Lumora.Contracts.Chat;

/// <summary>자연어 대화 요청입니다.</summary>
public sealed record ChatTurnRequest
{
    /// <summary>사용자가 입력한 합성 또는 개인 메시지입니다.</summary>
    public required string Message { get; init; }

    /// <summary>응답 언어입니다. 현재 ko 또는 en을 지원합니다.</summary>
    public required string Language { get; init; }

    /// <summary>기존 대화 ID이며, 생략하면 새 대화를 시작합니다.</summary>
    public Guid? ConversationId { get; init; }
}

/// <summary>검증된 대화 처리 결과입니다.</summary>
public sealed record ChatTurnResponse(
    Guid? ConversationId,
    string Response,
    bool Persisted);
