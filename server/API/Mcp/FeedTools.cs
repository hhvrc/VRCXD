using System.ComponentModel;
using ModelContextProtocol.Server;
using VRCX.Common.Features.Locations;
using VRCX.Common.Features.Charts;
using VRCX.Common.Features.Feed;
using VRCX.Common.VrcxDb;

namespace VRCX.API.Mcp;

/// <summary>A feed row. Which fields are set depends on Type.</summary>
public sealed record McpFeedRow(
    string Type, DateTimeOffset At, string UserId, string DisplayName,
    McpLocation? Location = null, McpLocation? PreviousLocation = null, long? TimeMs = null,
    string? Status = null, string? StatusDescription = null, string? PreviousStatus = null, string? PreviousStatusDescription = null,
    string? Bio = null, string? PreviousBio = null, string? AvatarName = null, string? PreviousAvatarName = null);

public sealed record McpHotWorld(string WorldId, string WorldName, int UniqueFriends, int VisitCount, DateTimeOffset LastVisited, string Trend);

public sealed record McpHotWorlds(McpAccount Account, int Days, IReadOnlyList<McpHotWorld> Worlds);

[McpServerToolType]
public sealed class FeedTools(McpAccountResolver accounts, IFeedService feed, IChartsService charts, LocationQueries locations)
{
    [McpServerTool(Name = "search_feed", Title = "Search the feed", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("""
        The account's friend feed, newest first: GPS (a friend moved to another instance; timeMs is
        how long they were in the previous one), Online, Offline (timeMs: how long they were at the
        last location), Status, Bio and Avatar changes. Answers "where was X last night", "when did X
        come online", "who changed status". search matches names, world and group names, status
        and bio text and avatar names; a wrld_ or grp_ id searches locations. Only covers what the
        server observed while it was running and connected to VRChat: a gap means nothing was
        observed, not that nothing happened. Friends whose location VRChat hides show as private.
        """)]
    public async Task<McpPage<McpFeedRow>> SearchFeed(
        [Description("usr_ id or display name of the logged-in account; optional when only one account exists.")] string? account = null,
        [Description("Only these types, comma separated: GPS, Online, Offline, Status, Bio, Avatar. Default all.")] string? types = null,
        [Description("Text to search for (case-insensitive), or a wrld_/grp_ id.")] string? search = null,
        [Description("Only the account's favorite friends.")] bool favoritesOnly = false,
        [Description("Only rows at or after this instant (ISO 8601).")] DateTimeOffset? from = null,
        [Description("Only rows at or before this instant (ISO 8601).")] DateTimeOffset? to = null,
        [Description("Rows to return, 1-200 (default 50).")] int limit = McpPaging.DefaultLimit,
        [Description("Rows to skip, from a previous result's nextOffset.")] int offset = 0,
        CancellationToken cancellationToken = default)
    {
        var who = await accounts.ResolveAsync(account, cancellationToken);
        offset = McpPaging.Offset(offset);
        limit = McpPaging.Limit(limit);
        var typeList = (types ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => FeedService.AllTypes.FirstOrDefault(a => string.Equals(a, t, StringComparison.OrdinalIgnoreCase)))
            .OfType<string>().ToArray();
        // The feed reads its newest rows per source; asking for the rows up to the end of this page is enough.
        var rows = await feed.QueryAsync(who.Id, new FeedQuery { Types = typeList, Search = search ?? "", Vip = favoritesOnly, From = from, To = to, Limit = offset + limit + 1 }, cancellationToken);
        var page = McpPaging.Page(who, rows, offset, limit);
        var name = await McpLocations.NamerAsync(locations, page.Items.SelectMany(r => new[] { r.Location, r.PreviousLocation }), cancellationToken);
        return new McpPage<McpFeedRow>(who, page.Items.Select(r => new McpFeedRow(
            r.Type, r.CreatedAt, r.UserId, r.DisplayName,
            Location: name(r.Location) is { } l ? l with { WorldName = l.WorldName ?? r.WorldName, GroupName = l.GroupName ?? r.GroupName } : null,
            PreviousLocation: name(r.PreviousLocation) is { } p ? p with { WorldName = p.WorldName ?? r.PreviousWorldName } : null,
            TimeMs: r.Time,
            Status: r.Status, StatusDescription: r.StatusDescription, PreviousStatus: r.PreviousStatus, PreviousStatusDescription: r.PreviousStatusDescription,
            Bio: r.Bio, PreviousBio: r.PreviousBio, AvatarName: r.AvatarName, PreviousAvatarName: r.PreviousAvatarName)).ToList(),
            page.Offset, page.Truncated, page.NextOffset);
    }

    [McpServerTool(Name = "get_hot_worlds", Title = "Hot worlds", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("""
        Worlds the account's friends moved into most over the last days, ranked by how many
        different friends went there, then by visits. trend compares unique friends in the second
        half of the period with the first: rising, cooling or stable. Counted from the friend moves
        the server observed while it was running.
        """)]
    public async Task<McpHotWorlds> GetHotWorlds(
        [Description("usr_ id or display name of the logged-in account; optional when only one account exists.")] string? account = null,
        [Description("How many days back to look, 1-365 (default 30).")] int days = 30,
        [Description("Worlds to return, 1-200 (default 20).")] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        var who = await accounts.ResolveAsync(account, cancellationToken);
        days = Math.Clamp(days, 1, 365);
        var worlds = await charts.GetHotWorldsAsync(who.Id, days, McpPaging.Limit(limit), cancellationToken);
        return new McpHotWorlds(who, days, worlds.Select(w => new McpHotWorld(w.WorldId, w.WorldName, w.UniqueFriends, w.VisitCount, w.LastVisited, w.Trend)).ToList());
    }
}
