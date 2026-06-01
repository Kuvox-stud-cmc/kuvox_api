namespace Kuvox.Api.Modules.Shared.Dtos;

/// <summary>Standard envelope for paginated list responses across modules.</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}
