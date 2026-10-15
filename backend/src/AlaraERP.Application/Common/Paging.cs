namespace AlaraERP.Application.Common;

public record PageRequest
{
    public const int MaxPageSize = 100;

    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 25;

    public int SafePage => Page < 1 ? 1 : Page;
    public int SafePageSize => PageSize switch { < 1 => 25, > MaxPageSize => MaxPageSize, _ => PageSize };
    public int Skip => (SafePage - 1) * SafePageSize;
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}
