using System.ComponentModel;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using VRCX.Common.Features.Friends;
using VRCX.Common.Features.Locations;
using VRCX.Common.Services.Presence;
using VRCX.Common.Features.Users;
using VRCX.Common.VrcxDb;

namespace VRCX.API.Mcp;

public sealed record McpUserCandidate(string UserId, string DisplayName, bool IsFriend, string MatchedOn, DateTimeOffset LastUpdatedAt);

public sealed record McpUserCandidates(McpAccount Account, string Query, IReadOnlyList<McpUserCandidate> Candidates, bool Truncated);

public sealed record McpPresence(string State, McpLocation? Location, McpLocation? TravelingTo, DateTimeOffset? StateSince, DateTimeOffset? LocationSince, string? Platform);

public sealed record McpRelationship(bool IsFriend, int? FriendNumber, DateTimeOffset? FriendsSince, IReadOnlyList<FriendLogDate> FriendLog, IReadOnlyList<string> Moderations, string? Note);

public sealed record McpTimeTogether(int JoinCount, long TimeTogetherMs, DateTimeOffset? LastSeen);

public sealed record McpUser(
    McpAccount Account, string Id, string DisplayName, string Status, string? StatusDescription, string TrustLevel, string? Pronouns,
    string? Bio, IReadOnlyList<string> BioLinks, string? LastPlatform, DateTimeOffset? LastLogin, DateTimeOffset? LastActivity, DateOnly? DateJoined,
    bool AgeVerified, bool IsStub, DateTimeOffset FirstSeenAt, DateTimeOffset UpdatedAt, DateTimeOffset? LastFullFetchAt,
    McpPresence Presence, string? Memo, McpRelationship Relationship, McpTimeTogether TimeTogether);

/// <summary>One change in a user's history. Only the fields of its kind are set.</summary>
public sealed record McpUserChange(
    DateTimeOffset At, string Kind, string? Value = null, string? PreviousValue = null, string? Detail = null, string? PreviousDetail = null,
    McpLocation? Location = null, McpLocation? PreviousLocation = null, DateTimeOffset? LeftAt = null, string? Source = null);

public sealed record McpUserHistory(string UserId, string DisplayName, IReadOnlyList<McpUserChange> Items, bool Truncated, DateTimeOffset? NextBefore);

[McpServerToolType]
public sealed class UserTools(McpAccountResolver accounts, IPresenceCache presence, FriendQueries friends, UserQueries users, LocationQueries locations)
{
    private static readonly string[] HistoryKinds = ["displayName", "trust", "status", "bio", "avatar", "presence", "visit"];

    [McpServerTool(Name = "find_user", Title = "Find a user", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("""
        Turn a name into usr_ ids: ranked candidates among users this server has stored, the
        account's friends first. Matches the display name (exact, then starting with, then
        containing, ignoring case), for friends also the account's memo and VRChat note on them, and
        names a user had before. Only finds users the server has seen (friends, people met in
        instances, notification senders); it does not search VRChat. Pass the chosen userId to
        get_user and the other user tools.
        """)]
    public async Task<McpUserCandidates> FindUser(
        [Description("Display name or part of one (or a usr_ id).")] string query,
        [Description("usr_ id or display name of the logged-in account whose friends rank first; optional when only one account exists.")] string? account = null,
        [Description("Candidates to return, 1-200 (default 20).")] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        var who = await accounts.ResolveAsync(account, cancellationToken);
        var text = query?.Trim() ?? "";
        if (text.Length == 0) throw new McpException("`query` must not be empty.");
        var found = await users.FindAsync(who.Id, text, McpPaging.Limit(limit), cancellationToken);
        return new McpUserCandidates(who, text,
            found.Candidates.Select(c => new McpUserCandidate(c.UserId, c.DisplayName, c.IsFriend, c.MatchedOn, c.LastUpdatedAt)).ToList(), found.Truncated);
    }

    [McpServerTool(Name = "get_user", Title = "Get a user", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("""
        Everything stored about one user, as this account sees them: profile as last fetched from
        VRChat (updatedAt says when it last changed here), where they are now, the VRCXD memo on
        them, the account's VRChat note, the relationship (friend or not, since when, friend and
        unfriend dates, blocks and mutes the account applied) and time together (how often the
        account's game log saw them join and the total time in the same instance; lastSeen is when
        the account last saw them in an instance). Never asks VRChat, so the profile can be stale.
        Time together only counts sessions the agent logged on the account's PC.
        """)]
    public async Task<McpUser> GetUser(
        [Description("usr_ id of the user. Use find_user to turn a name into an id.")] string userId,
        [Description("usr_ id or display name of the logged-in account; optional when only one account exists.")] string? account = null,
        CancellationToken cancellationToken = default)
    {
        var id = McpLocations.RequireId(userId, "usr_", "userId", "Use find_user to turn a name into an id.");
        var who = await accounts.ResolveAsync(account, cancellationToken);
        var stored = await users.StoredAsync(who.Id, id, cancellationToken)
            ?? throw new McpException($"This server has never seen {id}. Use find_user to search the stored users by name.");
        var user = stored.User;

        var now = await presence.GetAsync(id, cancellationToken);
        var name = await McpLocations.NamerAsync(locations, [now.Location, now.TravelingToLocation], cancellationToken);
        var together = await friends.TimeTogetherAsync(who.Id, id, cancellationToken);

        return new McpUser(who, user.Id, user.DisplayName, user.Status, user.StatusDescription, user.TrustLevel, user.Pronouns,
            user.Bio, user.BioLinks, user.LastPlatform, user.LastLogin, user.LastActivity, user.DateJoined,
            user.AgeVerified, user.IsStub, user.FirstSeenAt, user.UpdatedAt, user.LastFullFetchAt,
            new McpPresence(McpLocations.State(now.State), name(now.Location), name(now.TravelingToLocation), now.StateSince ?? now.PresenceChangedAt, now.LocationSince, now.Platform),
            string.IsNullOrEmpty(stored.Memo) ? null : stored.Memo,
            new McpRelationship(stored.IsFriend, stored.FriendNumber, stored.FriendsSince, together.FriendLog, stored.Moderations, string.IsNullOrEmpty(stored.Note) ? null : stored.Note),
            new McpTimeTogether(together.JoinCount, together.TimeTogetherMs, together.LastSeen));
    }

    [McpServerTool(Name = "get_user_history", Title = "User history", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("""
        What changed about a user, newest first, merged into one timeline: displayName, trust,
        status (value = status, detail = status text), bio, avatar (value = avatar name, detail = its avtr_ id when known), presence
        (value = online/active/offline/location, with the location moved to and from) and visit (an
        instance stay: location, joined at, leftAt; source = game_log when an account's PC logged it,
        pipeline when VRChat's presence reported it, api when an instance lookup listed them). Only covers what the server observed while it
        was running: nothing recorded for a time does not mean nothing happened, and the first time
        a value is seen produces no change. Not account-scoped: users are shared by all accounts.
        """)]
    public async Task<McpUserHistory> GetUserHistory(
        [Description("usr_ id of the user. Use find_user to turn a name into an id.")] string userId,
        [Description("Only these kinds, comma separated: displayName, trust, status, bio, avatar, presence, visit. Default all.")] string? kinds = null,
        [Description("Only changes before this instant (ISO 8601), from a previous result's nextBefore.")] DateTimeOffset? before = null,
        [Description("Rows to return, 1-200 (default 50).")] int limit = McpPaging.DefaultLimit,
        CancellationToken cancellationToken = default)
    {
        var id = McpLocations.RequireId(userId, "usr_", "userId", "Use find_user to turn a name into an id.");
        var displayName = await users.DisplayNameAsync(id, cancellationToken)
            ?? throw new McpException($"This server has never seen {id}. Use find_user to search the stored users by name.");
        var wanted = (kinds ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (wanted.Count == 0) wanted = HistoryKinds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        limit = McpPaging.Limit(limit);

        // Each list is read newest first with one row more than asked, so the merge knows whether any is left.
        var history = await users.HistoryAsync(id, limit + 1, before, cancellationToken);
        var name = await McpLocations.NamerAsync(locations, history.Presence.SelectMany(p => new[] { p.Location, p.PreviousLocation }).Concat(history.Visits.Select(v => v.InstanceLocation)), cancellationToken);

        var changes = new List<McpUserChange>();
        if (wanted.Contains("displayName")) changes.AddRange(history.DisplayNames.Select(c => new McpUserChange(c.ChangedAt, "displayName", c.DisplayName, c.PreviousDisplayName)));
        if (wanted.Contains("trust")) changes.AddRange(history.Trust.Select(c => new McpUserChange(c.ChangedAt, "trust", c.TrustLevel, c.PreviousTrustLevel)));
        if (wanted.Contains("status")) changes.AddRange(history.Status.Select(c => new McpUserChange(c.ChangedAt, "status", c.Status, c.PreviousStatus, c.StatusDescription, c.PreviousStatusDescription)));
        if (wanted.Contains("bio")) changes.AddRange(history.Bio.Select(c => new McpUserChange(c.ChangedAt, "bio", c.Bio, c.PreviousBio)));
        if (wanted.Contains("avatar")) changes.AddRange(history.Avatars.Select(c => new McpUserChange(c.ChangedAt, "avatar", c.AvatarName, c.PreviousAvatarName, c.AvatarId)));
        if (wanted.Contains("presence"))
            changes.AddRange(history.Presence.Select(p => new McpUserChange(p.At, "presence", p.Kind.ToString().ToLowerInvariant(), p.PreviousState is { } s ? McpLocations.State(s) : null,
                Location: name(p.Location), PreviousLocation: name(p.PreviousLocation))));
        if (wanted.Contains("visit"))
            changes.AddRange(history.Visits.Select(v => new McpUserChange(v.JoinedAt, "visit", Location: name(v.InstanceLocation), LeftAt: v.LeftAt,
                Source: v.Source == VisitSource.GameLog ? "game_log" : v.Source.ToString().ToLowerInvariant())));

        var ordered = changes.OrderByDescending(c => c.At).ToList();
        var page = ordered.Take(limit).ToList();
        var truncated = ordered.Count > limit;
        return new McpUserHistory(id, displayName, page, truncated, truncated ? page[^1].At : null);
    }
}
