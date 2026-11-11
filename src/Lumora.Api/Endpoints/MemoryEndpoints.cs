using Lumora.Api.Services;
using Lumora.Application.Abstractions.Persistence;
using Lumora.Application.UseCases.Memories;
using Lumora.Contracts.Memories;
using Lumora.Domain.Entities;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Lumora.Api.Endpoints;

internal static class MemoryEndpoints
{
    public static WebApplication MapMemoryEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/memories").WithTags("Memories");
        group.MapPost("/", CreateAsync)
            .WithName("CreateMemory")
            .WithSummary("기억 생성")
            .WithDescription("현재 개발용 서버 범위에 기억을 암호화해 저장합니다.")
            .Produces<MemoryResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        group.MapGet("/", SearchAsync)
            .WithName("SearchMemories")
            .WithSummary("기억 목록 및 검색")
            .WithDescription("기억 유형, 상태 시각, 정렬 및 페이지 조건으로 범위 내 기억을 조회합니다.")
            .Produces<MemoryPageResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        group.MapGet("/{id:guid}", GetAsync)
            .WithName("GetMemory")
            .WithSummary("기억 상세 조회")
            .Produces<MemoryResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        group.MapPut("/{id:guid}", UpdateAsync)
            .WithName("UpdateMemory")
            .WithSummary("기억 수정")
            .Produces<MemoryResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        group.MapDelete("/{id:guid}", DeleteAsync)
            .WithName("DeleteMemory")
            .WithSummary("기억 및 관련 이력 삭제")
            .WithDescription("기억, 암호문, 출처, 검색 토큰 및 개정 이력을 물리 삭제합니다. 백업에 남은 데이터에는 영향을 주지 않습니다.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        return app;
    }

    private static async Task<Results<Created<MemoryResponse>, ProblemHttpResult>> CreateAsync(
        CreateMemoryRequest request,
        ITenantUserScopeAccessor scopeAccessor,
        IMemoryUseCases useCases,
        CancellationToken cancellationToken)
    {
        var scope = scopeAccessor.GetScope();
        if (scope is null)
        {
            return TypedResults.Problem(
                title: "Memory API unavailable",
                detail: "인증이 구현되기 전까지 개발용 고정 범위를 명시적으로 설정해야 합니다.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var result = await useCases.CreateAsync(
            scope.Value,
            new CreateMemoryCommand(
                ParseEnum<MemoryType>(request.Type),
                request.Content,
                request.ConfidenceScore,
                ParseEnum<MemorySensitivity>(request.Sensitivity)),
            cancellationToken);
        var response = ToResponse(result);
        return TypedResults.Created($"/api/memories/{response.Id}", response);
    }

    private static async Task<Results<Ok<MemoryPageResponse>, ProblemHttpResult>> SearchAsync(
        ITenantUserScopeAccessor scopeAccessor,
        IMemoryUseCases useCases,
        CancellationToken cancellationToken,
        string? type = null,
        DateTimeOffset? createdFromUtc = null,
        DateTimeOffset? createdToUtc = null,
        DateTimeOffset? updatedFromUtc = null,
        DateTimeOffset? updatedToUtc = null,
        string? sort = null,
        int pageNumber = 1,
        int pageSize = 50,
        bool includeInactive = false)
    {
        var scope = scopeAccessor.GetScope();
        if (scope is null)
        {
            return TypedResults.Problem(
                title: "Memory API unavailable",
                detail: "인증이 구현되기 전까지 개발용 고정 범위를 명시적으로 설정해야 합니다.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var result = await useCases.SearchAsync(
            scope.Value,
            new MemorySearch(
                string.IsNullOrWhiteSpace(type) ? null : ParseEnum<MemoryType>(type),
                createdFromUtc?.UtcDateTime,
                createdToUtc?.UtcDateTime,
                updatedFromUtc?.UtcDateTime,
                updatedToUtc?.UtcDateTime,
                includeInactive,
                ParseSortOrder(sort)),
            new PageRequest(pageNumber, pageSize),
            cancellationToken);
        return TypedResults.Ok(new MemoryPageResponse(result.Items.Select(ToResponse).ToArray(), result.PageNumber, result.PageSize, result.TotalCount));
    }

    private static async Task<Results<Ok<MemoryResponse>, NotFound, ProblemHttpResult>> GetAsync(
        Guid id,
        ITenantUserScopeAccessor scopeAccessor,
        IMemoryUseCases useCases,
        CancellationToken cancellationToken)
    {
        var scope = scopeAccessor.GetScope();
        if (scope is null)
        {
            return TypedResults.Problem(
                title: "Memory API unavailable",
                detail: "인증이 구현되기 전까지 개발용 고정 범위를 명시적으로 설정해야 합니다.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var result = await useCases.GetAsync(scope.Value, id, cancellationToken);
        return result is null ? TypedResults.NotFound() : TypedResults.Ok(ToResponse(result));
    }

    private static async Task<Results<Ok<MemoryResponse>, NotFound, ProblemHttpResult>> UpdateAsync(
        Guid id,
        UpdateMemoryRequest request,
        ITenantUserScopeAccessor scopeAccessor,
        IMemoryUseCases useCases,
        CancellationToken cancellationToken)
    {
        var scope = scopeAccessor.GetScope();
        if (scope is null)
        {
            return TypedResults.Problem(
                title: "Memory API unavailable",
                detail: "인증이 구현되기 전까지 개발용 고정 범위를 명시적으로 설정해야 합니다.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var result = await useCases.UpdateAsync(
            scope.Value,
            id,
            new UpdateMemoryCommand(
                ParseEnum<MemoryType>(request.Type),
                request.Content,
                request.ConfidenceScore,
                ParseEnum<MemoryVerificationStatus>(request.VerificationStatus),
                ParseEnum<MemorySensitivity>(request.Sensitivity)),
            cancellationToken);
        return result is null ? TypedResults.NotFound() : TypedResults.Ok(ToResponse(result));
    }

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> DeleteAsync(
        Guid id,
        ITenantUserScopeAccessor scopeAccessor,
        IMemoryUseCases useCases,
        CancellationToken cancellationToken)
    {
        var scope = scopeAccessor.GetScope();
        if (scope is null)
        {
            return TypedResults.Problem(
                title: "Memory API unavailable",
                detail: "인증이 구현되기 전까지 개발용 고정 범위를 명시적으로 설정해야 합니다.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        return await useCases.DeleteAsync(scope.Value, id, cancellationToken)
            ? TypedResults.NoContent()
            : TypedResults.NotFound();
    }

    private static MemoryResponse ToResponse(MemoryDetails result) =>
        new(
            result.Id,
            result.MemoryType.ToString(),
            result.Content,
            result.ConfidenceScore,
            result.VerificationStatus.ToString(),
            result.Sensitivity.ToString(),
            result.CreatedAtUtc,
            result.UpdatedAtUtc,
            result.IsActive,
            result.Version);

    private static TEnum ParseEnum<TEnum>(string value)
        where TEnum : struct, Enum =>
        Enum.TryParse<TEnum>(value, ignoreCase: true, out var result) && Enum.IsDefined(result)
            ? result
            : throw new ArgumentException($"지원하지 않는 {typeof(TEnum).Name} 값입니다.");

    private static MemorySortOrder ParseSortOrder(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? MemorySortOrder.UpdatedDescending
            : ParseEnum<MemorySortOrder>(value);
}
