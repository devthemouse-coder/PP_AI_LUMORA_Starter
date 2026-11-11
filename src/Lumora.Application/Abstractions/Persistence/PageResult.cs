namespace Lumora.Application.Abstractions.Persistence;

public sealed record PageResult<T>(IReadOnlyList<T> Items, int PageNumber, int PageSize, long TotalCount);
