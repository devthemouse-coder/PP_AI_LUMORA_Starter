using Lumora.Api.Services;
using Lumora.Application.Abstractions.Persistence;
using Lumora.Application.UseCases.Chat;
using Lumora.Application.UseCases.Conversations;
using Lumora.Contracts.Chat;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Lumora.Api.Endpoints;

internal static class ChatEndpoints
{
    public static WebApplication MapChatEndpoints(this WebApplication app)
    {
        var chatGroup = app.MapGroup("/api/chat").WithTags("Chat");
        chatGroup.MapPost("/", SendAsync)
            .WithName("SendChatTurn")
            .WithSummary("자연어 대화 처리")
            .WithDescription("로컬 LLM의 제한된 의도 JSON을 검증하고 허용된 생일 UseCase만 실행합니다.")
            .Produces<ChatTurnResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        var conversationGroup = app.MapGroup("/api/conversations").WithTags("Conversations");
        conversationGroup.MapGet("/", ListAsync)
            .WithName("ListConversations")
            .WithSummary("대화 목록 조회")
            .Produces<ConversationListResponse>()
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        conversationGroup.MapGet("/{conversationId:guid}/messages", ListMessagesAsync)
            .WithName("ListConversationMessages")
            .WithSummary("대화 메시지 조회")
            .Produces<ConversationMessagePageResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        return app;
    }

    private static async Task<Results<Ok<ChatTurnResponse>, ProblemHttpResult>> SendAsync(
        ChatTurnRequest request,
        ITenantUserScopeAccessor scopeAccessor,
        IChatUseCases useCases,
        CancellationToken cancellationToken)
    {
        var scope = scopeAccessor.GetScope();
        if (scope is null)
        {
            return ScopeUnavailable();
        }

        var result = await useCases.SendAsync(
            scope.Value,
            new ChatTurnCommand(request.Message, request.Language, request.ConversationId),
            cancellationToken);
        return TypedResults.Ok(new ChatTurnResponse(
            result.ConversationId,
            result.Response,
            result.Persisted));
    }

    private static async Task<Results<Ok<ConversationListResponse>, ProblemHttpResult>> ListAsync(
        ITenantUserScopeAccessor scopeAccessor,
        IConversationUseCases useCases,
        CancellationToken cancellationToken,
        int pageNumber = 1,
        int pageSize = 50)
    {
        var scope = scopeAccessor.GetScope();
        if (scope is null)
        {
            return ScopeUnavailable();
        }

        var page = await useCases.ListAsync(
            scope.Value,
            new PageRequest(pageNumber, pageSize),
            cancellationToken);
        return TypedResults.Ok(new ConversationListResponse(
            page.Items.Select(item => new ConversationResponse(
                item.Id,
                item.CreatedAtUtc,
                item.UpdatedAtUtc,
                item.LastMessageAtUtc)).ToArray(),
            page.PageNumber,
            page.PageSize,
            page.TotalCount));
    }

    private static async Task<Results<Ok<ConversationMessagePageResponse>, NotFound, ProblemHttpResult>>
        ListMessagesAsync(
            Guid conversationId,
            ITenantUserScopeAccessor scopeAccessor,
            IConversationUseCases useCases,
            CancellationToken cancellationToken,
            int pageNumber = 1,
            int pageSize = 50)
    {
        var scope = scopeAccessor.GetScope();
        if (scope is null)
        {
            return ScopeUnavailable();
        }

        var page = await useCases.ListMessagesAsync(
            scope.Value,
            conversationId,
            new PageRequest(pageNumber, pageSize),
            cancellationToken);
        return page is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(new ConversationMessagePageResponse(
                page.Items.Select(item => new ConversationMessageResponse(
                    item.Id,
                    item.Role.ToString(),
                    item.Content,
                    item.CreatedAtUtc)).ToArray(),
                page.PageNumber,
                page.PageSize,
                page.TotalCount));
    }

    private static ProblemHttpResult ScopeUnavailable() =>
        TypedResults.Problem(
            title: "Chat API unavailable",
            detail: "인증이 구현되기 전까지 개발용 고정 범위를 명시적으로 설정해야 합니다.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
}
