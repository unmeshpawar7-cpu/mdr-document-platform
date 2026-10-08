namespace Mdr.Application.Common.Paging;

/// <summary>A page of results plus the information a client needs to request further pages.</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => TotalCount == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    public static PagedResult<T> Empty(PageRequest request) =>
        new(Array.Empty<T>(), request.Page, request.PageSize, 0);
}
