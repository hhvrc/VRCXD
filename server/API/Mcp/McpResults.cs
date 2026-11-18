using Microsoft.EntityFrameworkCore;
using ModelContextProtocol;
using VRCX.Common.VrcxDb;
using VRCX.Vrchat;

namespace VRCX.API.Mcp;

/// <summary>
/// One page of rows. <see cref="Truncated"/> says more rows exist; ask again with
/// <c>offset</c> = <see cref="NextOffset"/> for them.
/// </summary>
public sealed record McpPage<T>(McpAccount? Account, IReadOnlyList<T> Items, int Offset, bool Truncated, int? NextOffset, int? Total = null);

/// <summary>A place as an AI caller wants it: the raw location tag plus the names it stands for.</summary>
public sealed record McpLocation(string Tag, string? WorldId, string? WorldName, string? InstanceType, string? GroupName);

public static class McpPaging
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 200;

    /// <summary>Row limits every list tool documents: 50 unless asked, never more than 200.</summary>
    public static int Limit(int limit) => Math.Clamp(limit <= 0 ? DefaultLimit : limit, 1, MaxLimit);

    public static int Offset(int offset) => Math.Max(0, offset);

    /// <summary>Rows offset..offset+limit of an already ordered sequence.</summary>
    public static McpPage<T> Page<T>(McpAccount? account, IEnumerable<T> ordered, int offset, int limit, int? total = null)
    {
        offset = Offset(offset);
        limit = Limit(limit);
        var rows = ordered.Skip(offset).Take(limit + 1).ToList();
        var truncated = rows.Count > limit;
        if (truncated) rows.RemoveAt(limit);
        return new McpPage<T>(account, rows, offset, truncated, truncated ? offset + limit : null, total);
    }

    /// <summary>The same for a query: only offset+limit+1 rows are read.</summary>
    public static async Task<McpPage<T>> PageAsync<T>(McpAccount? account, IQueryable<T> ordered, int offset, int limit, CancellationToken cancellationToken)
    {
        offset = Offset(offset);
        limit = Limit(limit);
        var rows = await ordered.Skip(offset).Take(limit + 1).ToListAsync(cancellationToken);
        var truncated = rows.Count > limit;
        if (truncated) rows.RemoveAt(limit);
        return new McpPage<T>(account, rows, offset, truncated, truncated ? offset + limit : null);
    }
}

public static class McpLocations
{
    /// <summary>World and group names for location tags, from the stored worlds and groups.</summary>
    public static async Task<Func<string?, McpLocation?>> NamerAsync(VrcxContext db, IEnumerable<string?> tags, CancellationToken cancellationToken)
    {
        var parsed = tags.OfType<string>().Distinct().Select(VrchatLocation.Parse).ToList();
        var worldIds = parsed.Select(l => l.WorldId).Where(id => id.Length > 0).Distinct().ToList();
        var groupIds = parsed.Select(l => l.GroupId).OfType<string>().Distinct().ToList();
        var worlds = worldIds.Count == 0 ? new Dictionary<string, string>() : await db.Worlds.AsNoTracking().Where(w => worldIds.Contains(w.Id)).ToDictionaryAsync(w => w.Id, w => w.Name, cancellationToken);
        var groups = groupIds.Count == 0 ? new Dictionary<string, string>() : await db.Groups.AsNoTracking().Where(g => groupIds.Contains(g.Id)).ToDictionaryAsync(g => g.Id, g => g.Name, cancellationToken);
        return tag =>
        {
            if (string.IsNullOrEmpty(tag)) return null;
            var location = VrchatLocation.Parse(tag);
            if (location.WorldId.Length == 0) return new McpLocation(tag, null, null, null, null);
            return new McpLocation(tag, location.WorldId, worlds.GetValueOrDefault(location.WorldId), location.AccessTypeName,
                location.GroupId is { } group ? groups.GetValueOrDefault(group) : null);
        };
    }

    public static string State(UserState state) => state switch
    {
        UserState.Online => "online",
        UserState.Active => "active",
        _ => "offline",
    };

    /// <summary>A required id argument with the expected prefix, or a tool error saying what to pass.</summary>
    public static string RequireId(string? value, string prefix, string what, string hint)
    {
        var id = value?.Trim() ?? "";
        if (!id.StartsWith(prefix, StringComparison.Ordinal))
            throw new McpException($"`{what}` must be a {prefix} id. {hint}");
        return id;
    }
}
