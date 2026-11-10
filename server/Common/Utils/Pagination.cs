using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace VRCX.Common.Utils.Pagination;

/// <summary>Page of results for a paginated list endpoint.</summary>
public sealed class PagedResult<T>
{
    public required IReadOnlyList<T> Items { get; set; }
    /// <summary>1-based current page index.</summary>
    public required uint Page { get; set; }
    public required uint PageSize { get; set; }
    public required int TotalCount { get; set; }
    /// <summary>Always at least 1, even when <see cref="TotalCount"/> is zero.</summary>
    public uint TotalPages => TotalCount <= 0 ? 1u : (uint)Math.Ceiling(TotalCount / (double)PageSize);
}

/// <summary>Common query parameters for paginated list endpoints. Bind with <c>[FromQuery] PaginationQuery query</c>.</summary>
public sealed class PaginationQuery
{
    public const uint DefaultPageSize = 50;
    public const uint MaxPageSize = 500;

    private uint _page = 1;
    private uint _pageSize = DefaultPageSize;

    /// <summary>1-based page index. Values below 1 are clamped to 1.</summary>
    [FromQuery(Name = "page")]
    public uint Page
    {
        get => _page;
        set => _page = value < 1 ? 1 : value;
    }

    /// <summary>Items per page, clamped to [1, <see cref="MaxPageSize"/>].</summary>
    [FromQuery(Name = "pageSize")]
    public uint PageSize
    {
        get => _pageSize;
        set => _pageSize = value < 1 ? 1 : value > MaxPageSize ? MaxPageSize : value;
    }
}

public static class QueryableExtensions
{
    /// <summary>
    /// One COUNT and one SELECT with Skip/Take. Order <paramref name="query"/> before calling
    /// this - pagination without a stable order produces non-deterministic pages.
    /// </summary>
    public static async Task<PagedResult<T>> ToPagedResultAsync<T>(this IQueryable<T> query, PaginationQuery pagination, CancellationToken cancellationToken)
    {
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((int)((pagination.Page - 1) * pagination.PageSize))
            .Take((int)pagination.PageSize)
            .ToListAsync(cancellationToken);
        return new PagedResult<T> { Items = items, Page = pagination.Page, PageSize = pagination.PageSize, TotalCount = totalCount };
    }
}
