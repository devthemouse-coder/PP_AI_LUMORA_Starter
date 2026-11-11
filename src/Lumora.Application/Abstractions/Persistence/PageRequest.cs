namespace Lumora.Application.Abstractions.Persistence;

public sealed record PageRequest
{
    public const int MaximumPageSize = 200;

    public PageRequest(int pageNumber, int pageSize)
    {
        if (pageNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(pageNumber));
        }

        if (pageSize is < 1 or > MaximumPageSize)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize));
        }

        PageNumber = pageNumber;
        PageSize = pageSize;
    }

    public int PageNumber { get; }

    public int PageSize { get; }

    public int Offset => checked((PageNumber - 1) * PageSize);
}
