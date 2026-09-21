namespace UltimatePos.Application.Common.Dtos;

public record PagedResult<T>(IEnumerable<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public record PagingMeta(int Page, int PageSize, int TotalCount, int TotalPages);