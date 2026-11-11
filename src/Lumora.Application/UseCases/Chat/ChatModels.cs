using Lumora.Application.Abstractions.Persistence;

namespace Lumora.Application.UseCases.Chat;

public sealed record ChatModelOptions(string ModelName);

public sealed record ChatTurnCommand(
    string Message,
    string Language,
    Guid? ConversationId = null);

public sealed record ChatTurnResult(
    Guid? ConversationId,
    string Response,
    bool Persisted);

public interface IChatUseCases
{
    Task<ChatTurnResult> SendAsync(
        TenantUserScope scope,
        ChatTurnCommand command,
        CancellationToken cancellationToken);
}
