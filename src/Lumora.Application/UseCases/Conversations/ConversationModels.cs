using Lumora.Application.Abstractions.Persistence;
using Lumora.Domain.Entities;

namespace Lumora.Application.UseCases.Conversations;

public sealed record ConversationSummary(
    Guid Id,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset LastMessageAtUtc);

public sealed record ConversationMessageDetails(
    Guid Id,
    MessageRole Role,
    string Content,
    DateTimeOffset CreatedAtUtc);

public sealed record ConversationMessagePage(
    IReadOnlyList<ConversationMessageDetails> Items,
    int PageNumber,
    int PageSize,
    long TotalCount);

public interface IConversationUseCases
{
    Task<PageResult<ConversationSummary>> ListAsync(
        TenantUserScope scope,
        PageRequest page,
        CancellationToken cancellationToken);

    Task<ConversationMessagePage?> ListMessagesAsync(
        TenantUserScope scope,
        Guid conversationId,
        PageRequest page,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ConversationMessageDetails>> GetRecentMessagesAsync(
        TenantUserScope scope,
        Guid conversationId,
        int messageCount,
        CancellationToken cancellationToken);

    Task<Guid> SaveExchangeAsync(
        TenantUserScope scope,
        Guid? conversationId,
        string userMessage,
        string assistantMessage,
        CancellationToken cancellationToken);
}

public sealed class ConversationNotFoundException()
    : KeyNotFoundException("대화를 찾을 수 없습니다.");
