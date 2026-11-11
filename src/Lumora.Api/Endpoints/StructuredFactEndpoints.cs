using Lumora.Api.Services;
using Lumora.Application.Abstractions.Persistence;
using Lumora.Application.UseCases.StructuredFacts;
using Lumora.Contracts.StructuredFacts;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Lumora.Api.Endpoints;

internal static class StructuredFactEndpoints
{
    public static WebApplication MapStructuredFactEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/structured-facts").WithTags("Structured facts");
        group.MapPost("/", CreateAsync)
            .WithName("CreateStructuredFact")
            .WithSummary("구조화 사실 생성")
            .WithDescription("사실 값은 암호화하고 유형·대상·정확한 값의 검색 토큰을 별도로 저장합니다.")
            .Produces<StructuredFactResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        group.MapGet("/", SearchAsync)
            .WithName("SearchStructuredFacts")
            .WithSummary("구조화 사실 검색")
            .WithDescription("정보 유형, 대상, 정확한 텍스트 값, 날짜 범위 또는 생일 월로 검색합니다.")
            .Produces<StructuredFactPageResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        group.MapGet("/birthdays", SearchBirthdaysAsync)
            .WithName("SearchBirthdays")
            .WithSummary("월별 생일 검색")
            .WithDescription("생년을 제공하지 않은 생일도 월·일 HMAC 토큰을 이용해 조회합니다.")
            .Produces<StructuredFactPageResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        group.MapGet("/{id:guid}", GetAsync)
            .WithName("GetStructuredFact")
            .WithSummary("구조화 사실 상세 조회")
            .Produces<StructuredFactResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        group.MapPut("/{id:guid}", UpdateAsync)
            .WithName("UpdateStructuredFact")
            .WithSummary("구조화 사실 수정")
            .Produces<StructuredFactResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        group.MapDelete("/{id:guid}", DeleteAsync)
            .WithName("DeleteStructuredFact")
            .WithSummary("구조화 사실 및 관련 기억 삭제")
            .WithDescription("기억, 암호문, 출처, 검색 토큰 및 개정 이력을 물리 삭제합니다. 백업에 남은 데이터에는 영향을 주지 않습니다.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        return app;
    }

    private static async Task<Results<Created<StructuredFactResponse>, ProblemHttpResult>> CreateAsync(
        CreateStructuredFactRequest request,
        ITenantUserScopeAccessor scopeAccessor,
        IStructuredFactUseCases useCases,
        CancellationToken cancellationToken)
    {
        var scope = scopeAccessor.GetScope();
        if (scope is null)
        {
            return ScopeUnavailable();
        }

        var result = await useCases.CreateAsync(
            scope.Value,
            new CreateStructuredFactCommand(request.FactType, ToValue(request.Value)),
            cancellationToken);
        var response = ToResponse(result);
        return TypedResults.Created($"/api/structured-facts/{response.MemoryId}", response);
    }

    private static async Task<Results<Ok<StructuredFactPageResponse>, ProblemHttpResult>> SearchAsync(
        ITenantUserScopeAccessor scopeAccessor,
        IStructuredFactUseCases useCases,
        CancellationToken cancellationToken,
        string? factType = null,
        string? subject = null,
        string? exactValue = null,
        int? month = null,
        DateOnly? dateFrom = null,
        DateOnly? dateTo = null,
        string? sort = null,
        int pageNumber = 1,
        int pageSize = 50)
    {
        var scope = scopeAccessor.GetScope();
        if (scope is null)
        {
            return ScopeUnavailable();
        }

        var result = await useCases.SearchAsync(
            scope.Value,
            new StructuredFactSearch(
                factType,
                subject,
                exactValue,
                month,
                dateFrom,
                dateTo,
                ParseSortOrder(sort)),
            new PageRequest(pageNumber, pageSize),
            cancellationToken);
        return TypedResults.Ok(ToResponse(result));
    }

    private static async Task<Results<Ok<StructuredFactPageResponse>, ProblemHttpResult>> SearchBirthdaysAsync(
        int month,
        ITenantUserScopeAccessor scopeAccessor,
        IStructuredFactUseCases useCases,
        CancellationToken cancellationToken,
        int pageNumber = 1,
        int pageSize = 50)
    {
        var scope = scopeAccessor.GetScope();
        if (scope is null)
        {
            return ScopeUnavailable();
        }

        var result = await useCases.SearchAsync(
            scope.Value,
            new StructuredFactSearch(FactType: "Person.Birthday", BirthdayMonth: month),
            new PageRequest(pageNumber, pageSize),
            cancellationToken);
        return TypedResults.Ok(ToResponse(result));
    }

    private static async Task<Results<Ok<StructuredFactResponse>, NotFound, ProblemHttpResult>> GetAsync(
        Guid id,
        ITenantUserScopeAccessor scopeAccessor,
        IStructuredFactUseCases useCases,
        CancellationToken cancellationToken)
    {
        var scope = scopeAccessor.GetScope();
        if (scope is null)
        {
            return ScopeUnavailable();
        }

        var result = await useCases.GetAsync(scope.Value, id, cancellationToken);
        return result is null ? TypedResults.NotFound() : TypedResults.Ok(ToResponse(result));
    }

    private static async Task<Results<Ok<StructuredFactResponse>, NotFound, ProblemHttpResult>> UpdateAsync(
        Guid id,
        UpdateStructuredFactRequest request,
        ITenantUserScopeAccessor scopeAccessor,
        IStructuredFactUseCases useCases,
        CancellationToken cancellationToken)
    {
        var scope = scopeAccessor.GetScope();
        if (scope is null)
        {
            return ScopeUnavailable();
        }

        var result = await useCases.UpdateAsync(
            scope.Value,
            id,
            new UpdateStructuredFactCommand(request.FactType, ToValue(request.Value)),
            cancellationToken);
        return result is null ? TypedResults.NotFound() : TypedResults.Ok(ToResponse(result));
    }

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> DeleteAsync(
        Guid id,
        ITenantUserScopeAccessor scopeAccessor,
        IStructuredFactUseCases useCases,
        CancellationToken cancellationToken)
    {
        var scope = scopeAccessor.GetScope();
        if (scope is null)
        {
            return ScopeUnavailable();
        }

        return await useCases.DeleteAsync(scope.Value, id, cancellationToken)
            ? TypedResults.NoContent()
            : TypedResults.NotFound();
    }

    private static ProblemHttpResult ScopeUnavailable() =>
        TypedResults.Problem(
            title: "Memory API unavailable",
            detail: "인증이 구현되기 전까지 개발용 고정 범위를 명시적으로 설정해야 합니다.",
            statusCode: StatusCodes.Status503ServiceUnavailable);

    private static StructuredFactValue ToValue(StructuredFactValueRequest value) =>
        new(value.Subject, value.TextValue, value.Year, value.Month, value.Day);

    private static StructuredFactResponse ToResponse(StructuredFactDetails result) =>
        new(
            result.MemoryId,
            result.FactType,
            new StructuredFactValueResponse(
                result.Value.Subject,
                result.Value.TextValue,
                result.Value.Year,
                result.Value.Month,
                result.Value.Day),
            result.CreatedAtUtc,
            result.UpdatedAtUtc,
            result.ConfidenceScore,
            result.Version);

    private static StructuredFactPageResponse ToResponse(StructuredFactPage result) =>
        new(result.Items.Select(ToResponse).ToArray(), result.PageNumber, result.PageSize, result.TotalCount);

    private static TEnum ParseEnum<TEnum>(string value)
        where TEnum : struct, Enum =>
        Enum.TryParse<TEnum>(value, ignoreCase: true, out var result) && Enum.IsDefined(result)
            ? result
            : throw new ArgumentException($"지원하지 않는 {typeof(TEnum).Name} 값입니다.");

    private static Lumora.Application.Abstractions.Persistence.MemorySortOrder ParseSortOrder(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? Lumora.Application.Abstractions.Persistence.MemorySortOrder.UpdatedDescending
            : ParseEnum<Lumora.Application.Abstractions.Persistence.MemorySortOrder>(value);
}
