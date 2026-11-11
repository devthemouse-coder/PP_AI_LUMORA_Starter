using System.Security.Cryptography;
using System.Text;
using Lumora.Application.Abstractions.Persistence;
using Lumora.Application.Abstractions.Security;
using Lumora.Domain.Entities;

namespace Lumora.Application.UseCases.Conversations;

public sealed class ConversationUseCases(
    IConversationRepository conversationRepository,
    IMessageRepository messageRepository,
    IUnitOfWork unitOfWork,
    IEncryptionService encryptionService) : IConversationUseCases
{
    private const int MaximumMessageLength = 2_000;
    private const int MaximumRecentMessages = 2;
    private const int MaximumHistoryPageSize = 100;

    public async Task<PageResult<ConversationSummary>> ListAsync(
        TenantUserScope scope,
        PageRequest page,
        CancellationToken cancellationToken)
    {
        var result = await conversationRepository.ListAsync(scope, page, cancellationToken);
        return new PageResult<ConversationSummary>(
            result.Items.Select(ToSummary).ToArray(),
            result.PageNumber,
            result.PageSize,
            result.TotalCount);
    }

    public async Task<ConversationMessagePage?> ListMessagesAsync(
        TenantUserScope scope,
        Guid conversationId,
        PageRequest page,
        CancellationToken cancellationToken)
    {
        if (await conversationRepository.GetByIdAsync(scope, conversationId, cancellationToken) is null)
        {
            return null;
        }

        var result = await messageRepository.ListAsync(
            scope,
            conversationId,
            page,
            cancellationToken);
        var messages = await DecryptMessagesAsync(scope, result.Items, cancellationToken);
        return new ConversationMessagePage(messages, result.PageNumber, result.PageSize, result.TotalCount);
    }

    public async Task<IReadOnlyList<ConversationMessageDetails>> GetRecentMessagesAsync(
        TenantUserScope scope,
        Guid conversationId,
        int messageCount,
        CancellationToken cancellationToken)
    {
        if (messageCount is < 1 or > MaximumRecentMessages)
        {
            throw new ArgumentOutOfRangeException(nameof(messageCount));
        }

        if (await conversationRepository.GetByIdAsync(scope, conversationId, cancellationToken) is null)
        {
            throw new ConversationNotFoundException();
        }

        var recentMessages = await messageRepository.ListRecentAsync(
            scope,
            conversationId,
            messageCount,
            cancellationToken);
        return await DecryptMessagesAsync(scope, recentMessages, cancellationToken);
    }

    public async Task<Guid> SaveExchangeAsync(
        TenantUserScope scope,
        Guid? conversationId,
        string userMessage,
        string assistantMessage,
        CancellationToken cancellationToken)
    {
        ValidateMessage(userMessage, nameof(userMessage));
        ValidateMessage(assistantMessage, nameof(assistantMessage));

        var now = DateTime.UtcNow;
        var conversation = conversationId is { } existingId
            ? await conversationRepository.GetByIdAsync(scope, existingId, cancellationToken)
                ?? throw new ConversationNotFoundException()
            : new Conversation(Guid.NewGuid(), scope.TenantId, scope.UserId, now);
        var isNewConversation = conversationId is null;
        var conversationKey = conversation.Id;
        var userMessageId = Guid.NewGuid();
        var assistantMessageId = Guid.NewGuid();
        var userBody = await EncryptMessageAsync(
            scope,
            userMessageId,
            userMessage,
            cancellationToken);
        var assistantBody = await EncryptMessageAsync(
            scope,
            assistantMessageId,
            assistantMessage,
            cancellationToken);

        await unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                var lastSequenceNumber = isNewConversation
                    ? 0
                    : await messageRepository.GetLastSequenceNumberAsync(scope, conversationKey, token);
                var userEntity = new Message(
                    userMessageId,
                    scope.TenantId,
                    scope.UserId,
                    conversationKey,
                    checked(lastSequenceNumber + 1),
                    MessageRole.User,
                    userBody,
                    now);
                var assistantEntity = new Message(
                    assistantMessageId,
                    scope.TenantId,
                    scope.UserId,
                    conversationKey,
                    checked(lastSequenceNumber + 2),
                    MessageRole.Assistant,
                    assistantBody,
                    now);

                if (isNewConversation)
                {
                    await conversationRepository.AddAsync(scope, conversation, token);
                }
                else
                {
                    conversation.RecordMessage(now);
                    await conversationRepository.UpdateAsync(scope, conversation, token);
                }

                await messageRepository.AddAsync(scope, userEntity, token);
                await messageRepository.AddAsync(scope, assistantEntity, token);
            },
            cancellationToken);

        return conversationKey;
    }

    private async Task<EncryptedPayload> EncryptMessageAsync(
        TenantUserScope scope,
        Guid messageId,
        string message,
        CancellationToken cancellationToken)
    {
        var plaintext = Encoding.UTF8.GetBytes(message);
        try
        {
            return await encryptionService.EncryptAsync(
                plaintext,
                new EncryptionContext(
                    scope.TenantId,
                    scope.UserId,
                    messageId,
                    nameof(Message),
                    nameof(Message.Body)),
                cancellationToken);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private async Task<IReadOnlyList<ConversationMessageDetails>> DecryptMessagesAsync(
        TenantUserScope scope,
        IReadOnlyList<Message> messages,
        CancellationToken cancellationToken)
    {
        var results = new List<ConversationMessageDetails>(messages.Count);
        foreach (var message in messages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (message.Role is not (MessageRole.User or MessageRole.Assistant))
            {
                continue;
            }

            var plaintext = await encryptionService.DecryptAsync(
                message.Body,
                new EncryptionContext(
                    scope.TenantId,
                    scope.UserId,
                    message.Id,
                    nameof(Message),
                    nameof(Message.Body)),
                cancellationToken);
            try
            {
                results.Add(new ConversationMessageDetails(
                    message.Id,
                    message.Role,
                    Encoding.UTF8.GetString(plaintext),
                    new DateTimeOffset(message.CreatedAtUtc, TimeSpan.Zero)));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }

        return results;
    }

    private static ConversationSummary ToSummary(Conversation conversation) =>
        new(
            conversation.Id,
            new DateTimeOffset(conversation.CreatedAtUtc, TimeSpan.Zero),
            new DateTimeOffset(conversation.UpdatedAtUtc, TimeSpan.Zero),
            new DateTimeOffset(conversation.LastMessageAtUtc, TimeSpan.Zero));

    private static void ValidateMessage(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > MaximumMessageLength)
        {
            throw new ArgumentOutOfRangeException(parameterName, "메시지는 2,000자를 초과할 수 없습니다.");
        }
    }
}
