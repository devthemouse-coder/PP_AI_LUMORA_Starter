using Lumora.Application.UseCases.StructuredFacts;
using Lumora.Application.UseCases.Conversations;
using Lumora.Application.Abstractions.Llm;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Lumora.Api.Middleware;

internal sealed class MemoryApiExceptionHandler(ILogger<MemoryApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (statusCode, title) = exception switch
        {
            DuplicateStructuredFactException => (StatusCodes.Status409Conflict, "Duplicate fact"),
            ConflictingStructuredFactException => (StatusCodes.Status409Conflict, "Conflicting fact"),
            DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "Concurrent modification"),
            ConversationNotFoundException => (StatusCodes.Status404NotFound, "Conversation not found"),
            LlmProviderException => (StatusCodes.Status503ServiceUnavailable, "Local LLM unavailable"),
            ArgumentException => (StatusCodes.Status400BadRequest, "Invalid request"),
            _ => (StatusCodes.Status500InternalServerError, "Request failed")
        };

        logger.Log(
            statusCode >= StatusCodes.Status500InternalServerError ? LogLevel.Error : LogLevel.Warning,
            "Request failed with {ExceptionType} ({StatusCode}).",
            exception.GetType().Name,
            statusCode);
        httpContext.Response.StatusCode = statusCode;
        var detail = statusCode switch
        {
            StatusCodes.Status400BadRequest => "요청 값을 확인하십시오.",
            StatusCodes.Status409Conflict when exception is DbUpdateConcurrencyException =>
                "데이터가 다른 요청에 의해 변경되었습니다. 다시 조회한 뒤 시도하십시오.",
            StatusCodes.Status409Conflict => "같은 대상과 정보 유형의 기존 사실을 확인하십시오.",
            StatusCodes.Status404NotFound => "대화를 찾을 수 없습니다.",
            _ => "요청을 처리할 수 없습니다. 입력을 확인하거나 잠시 후 다시 시도하십시오."
        };
        await httpContext.Response.WriteAsJsonAsync(
            new ProblemDetails
            {
                Status = statusCode,
                Title = title,
                Detail = detail,
                Instance = httpContext.Request.Path
            },
            cancellationToken);
        return true;
    }
}
