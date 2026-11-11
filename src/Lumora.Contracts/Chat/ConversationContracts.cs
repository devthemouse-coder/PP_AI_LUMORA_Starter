namespace Lumora.Contracts.Chat;

/// <summary>사용자 대화 목록입니다.</summary>
public sealed record ConversationListResponse(
    IReadOnlyList<ConversationResponse> Items,
    int PageNumber,
    int PageSize,
    long TotalCount);

/// <summary>대화 요약입니다.</summary>
public sealed record ConversationResponse(
    Guid Id,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset LastMessageAtUtc);

/// <summary>대화 메시지의 페이지입니다.</summary>
public sealed record ConversationMessagePageResponse(
    IReadOnlyList<ConversationMessageResponse> Items,
    int PageNumber,
    int PageSize,
    long TotalCount);

/// <summary>복호화된 대화 메시지입니다.</summary>
public sealed record ConversationMessageResponse(
    Guid Id,
    string Role,
    string Content,
    DateTimeOffset CreatedAtUtc);
