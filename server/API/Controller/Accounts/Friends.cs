using VRCX.Common.VrcxDb;
using System.Net.Mime;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenShock.Internal.Common.Problems;
using VRCX.API.Models.Response;
using VRCX.Common.Errors;
using VRCX.Common.Utils.Pagination;

namespace VRCX.API.Controller.Accounts;

public sealed partial class AccountsController
{
    /// <summary>
    /// The account's current friends with their latest known presence, in friend-number order.
    /// </summary>
    /// <response code="200">The friends</response>
    /// <response code="404">No such account</response>
    [HttpGet("{accountId}/friends")]
    [ProducesResponseType<FriendResponse[]>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> ListFriends([FromRoute] string accountId, [FromServices] VRCX.Common.Services.Presence.IPresenceCache presence, [FromServices] VRCX.Common.Services.Presence.IPendingPresenceQueue pending, CancellationToken cancellationToken)
    {
        if (!await _db.Accounts.AnyAsync(a => a.UserId == accountId, cancellationToken)) return Problem(AccountError.AccountNotFound);

        var friends = await _db.Friendships.AsNoTracking()
            .Where(f => f.OwnerUserId == accountId && f.EndedAt == null)
            .OrderBy(f => f.FriendNumber)
            .Select(f => new
            {
                f.FriendNumber,
                f.StartedAt,
                f.Friend.Id,
                f.Friend.DisplayName,
                f.Friend.TrustLevel,
                f.Friend.Status,
                f.Friend.StatusDescription,
                f.Friend.LastActivity,
                f.Friend.LastLogin,
                ThumbnailImageUrl = UserImage.Pick(f.Friend.IconUrl, f.Friend.CurrentAvatarThumbnailImageUrl, f.Friend.CurrentAvatarImageUrl),
                f.Friend.Tags,
                f.Friend.DeveloperType,
                f.Friend.LastPlatform,
                f.Friend.IconFrame,
                f.Friend.ProfileEffect,
                Memo = _db.UserMemos.Where(m => m.UserId == f.FriendUserId).Select(m => m.Memo).FirstOrDefault(),
            })
            .ToArrayAsync(cancellationToken);
        var ids = friends.Select(f => f.Id).ToArray();
        var current = await presence.GetManyAsync(ids, cancellationToken);
        var waiting = await pending.PendingAsync(ids, cancellationToken);
        return Ok(friends.Select(f =>
        {
            var where = current.GetValueOrDefault(f.Id);
            return new FriendResponse
            {
                FriendNumber = f.FriendNumber,
                FriendsSince = f.StartedAt,
                UserId = f.Id,
                DisplayName = f.DisplayName,
                TrustLevel = f.TrustLevel,
                State = where?.State ?? UserState.Offline,
                Status = f.Status,
                StatusDescription = f.StatusDescription,
                Location = where?.Location,
                TravelingToLocation = where?.TravelingToLocation,
                LocationSince = where?.LocationSince,
                OnlineFor = where?.StateSince ?? where?.PresenceChangedAt,
                LastActivity = f.LastActivity,
                LastLogin = f.LastLogin,
                ThumbnailImageUrl = f.ThumbnailImageUrl,
                Tags = f.Tags,
                DeveloperType = f.DeveloperType,
                // The original's computeUserPlatform: VRChat's platform unless it is empty, offline or web.
                Platform = where?.Platform is { Length: > 0 } live && live is not ("offline" or "web") ? live : f.LastPlatform,
                Nickname = string.IsNullOrEmpty(f.Memo) ? null : f.Memo.Split('\n')[0],
                PendingOffline = waiting.Contains(f.Id),
                NameHue = NameHue(f.Id),
                IconFrame = string.IsNullOrEmpty(f.IconFrame) ? null : f.IconFrame,
                ProfileEffect = string.IsNullOrEmpty(f.ProfileEffect) ? null : f.ProfileEffect,
            };
        }).ToArray());
    }

    /// <summary>Every current friend with what the Friend List page shows and searches.</summary>
    /// <response code="200">The friends</response>
    /// <response code="404">No such account</response>
    [HttpGet("{accountId}/friends/details")]
    [ProducesResponseType<FriendDetailsResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> ListFriendDetails([FromRoute] string accountId, CancellationToken cancellationToken)
    {
        var account = await _db.Accounts.AsNoTracking().Where(a => a.UserId == accountId).Select(a => new { a.SharedConnectionsOptOut }).FirstOrDefaultAsync(cancellationToken);
        if (account is null) return Problem(AccountError.AccountNotFound);

        var friends = await _db.Friendships.AsNoTracking()
            .Where(f => f.OwnerUserId == accountId && f.EndedAt == null)
            .Select(f => new { f.FriendNumber, User = f.Friend })
            .ToListAsync(cancellationToken);
        var ids = friends.Select(f => f.User.Id).ToArray();

        var notes = await _db.UserNotes.AsNoTracking().Where(n => n.OwnerUserId == accountId && ids.Contains(n.TargetUserId))
            .Select(n => new { n.TargetUserId, n.Note }).ToListAsync(cancellationToken);
        var noteByUser = notes.GroupBy(n => n.TargetUserId).ToDictionary(g => g.Key, g => g.First().Note);
        var memos = await _db.UserMemos.AsNoTracking().Where(m => ids.Contains(m.UserId)).ToDictionaryAsync(m => m.UserId, m => m.Memo, cancellationToken);

        // Only the game log says they were in the same instance as the account. Identical rows are
        // counted once: the same join has been stored twice in places.
        var visits = await _db.InstanceVisits.AsNoTracking()
            .Where(v => v.Source == VisitSource.GameLog && ids.Contains(v.UserId))
            .Select(v => new { v.UserId, v.InstanceLocation, v.JoinedAt, v.LeftAt })
            .Distinct()
            .ToListAsync(cancellationToken);
        var visitsByUser = visits.ToLookup(v => v.UserId);

        var mutualCounts = await _db.MutualFriendLinks.AsNoTracking().Where(l => l.OwnerUserId == accountId)
            .GroupBy(l => l.FriendUserId).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);
        var optedOut = (await _db.MutualGraphMetas.AsNoTracking().Where(m => m.OwnerUserId == accountId && m.OptedOut)
            .Select(m => m.FriendUserId).ToListAsync(cancellationToken)).ToHashSet();

        var favorites = await VRCX.Common.Services.FavoriteFriends.IdSetAsync(_db, accountId, cancellationToken);

        return Ok(new FriendDetailsResponse
        {
            MutualOptOut = account.SharedConnectionsOptOut,
            Friends = friends.Select(f =>
            {
                var user = f.User;
                var seen = visitsByUser[user.Id].ToList();
                return new FriendDetailResponse
                {
                    FriendNumber = f.FriendNumber,
                    UserId = user.Id,
                    DisplayName = user.DisplayName,
                    TrustLevel = user.TrustLevel,
                    Tags = user.Tags,
                    DeveloperType = user.DeveloperType,
                    Status = user.Status,
                    StatusDescription = user.StatusDescription,
                    ThumbnailImageUrl = UserImage.Pick(user.IconUrl, user.CurrentAvatarThumbnailImageUrl, user.CurrentAvatarImageUrl),
                    Bio = user.Bio,
                    BioLinks = user.BioLinks,
                    Note = noteByUser.GetValueOrDefault(user.Id),
                    Memo = memos.GetValueOrDefault(user.Id),
                    JoinCount = seen.Count,
                    TimeTogetherMs = (long)seen.Where(v => v.LeftAt != null).Sum(v => (v.LeftAt!.Value - v.JoinedAt).TotalMilliseconds),
                    LastSeen = seen.Count > 0 ? seen.Max(v => v.LeftAt ?? v.JoinedAt) : null,
                    MutualCount = mutualCounts.GetValueOrDefault(user.Id),
                    MutualOptedOut = optedOut.Contains(user.Id),
                    LastActivity = user.LastActivity,
                    LastLogin = user.LastLogin,
                    DateJoined = user.DateJoined,
                    IsFavorite = favorites.Contains(user.Id),
                    NeedsLoad = user.LastFullFetchAt == null || user.ProfileFetchedAt == null,
                    NameHue = NameHue(user.Id),
                };
            }).ToArray(),
        });
    }

    private static int NameHue(string userId)
    {
        var hash = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(userId));
        return (hash[3] << 8) | hash[4];
    }

    /// <summary>Fetch a friend's full user object and profile from VRChat now: the Friend List's load missing entries.</summary>
    /// <response code="204">Fetched</response>
    /// <response code="409">No live session</response>
    /// <response code="502">VRChat refused</response>
    [HttpPost("{accountId}/friends/{userId}/load")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> LoadFriend([FromRoute] string accountId, [FromRoute] string userId, CancellationToken cancellationToken) =>
        RunAsync(accountId, async _ => await _sync.RefreshUserAsync(accountId, userId, cancellationToken));

    /// <summary>The account's friend log: friendships starting and ending, friend requests it sent through VRCX, and friends' name and trust changes while they were friends. Newest first, without the rows the account removed.</summary>
    /// <response code="200">The log</response>
    /// <response code="404">No such account</response>
    [HttpGet("{accountId}/friend-log")]
    [ProducesResponseType<FriendLogEntryResponse[]>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> FriendLog([FromRoute] string accountId, CancellationToken cancellationToken)
    {
        if (!await _db.Accounts.AnyAsync(a => a.UserId == accountId, cancellationToken)) return Problem(AccountError.AccountNotFound);

        var friendships = await _db.Friendships.AsNoTracking()
            .Where(f => f.OwnerUserId == accountId)
            .Select(f => new { f.Id, f.FriendUserId, f.FriendNumber, f.StartedAt, f.EndedAt, CurrentName = f.Friend.DisplayName })
            .ToListAsync(cancellationToken);
        var friendIds = friendships.Select(f => f.FriendUserId).Distinct().ToArray();
        var nameChanges = await _db.UserDisplayNameChanges.AsNoTracking().Where(c => friendIds.Contains(c.UserId)).OrderBy(c => c.ChangedAt).ToListAsync(cancellationToken);
        var trustChanges = await _db.UserTrustChanges.AsNoTracking().Where(c => friendIds.Contains(c.UserId)).ToListAsync(cancellationToken);
        var requests = await _db.FriendRequestEvents.AsNoTracking().Where(r => r.OwnerUserId == accountId).ToListAsync(cancellationToken);
        var hidden = (await _db.FriendLogHiddenEntries.AsNoTracking().Where(h => h.OwnerUserId == accountId).Select(h => h.EntryKey).ToListAsync(cancellationToken)).ToHashSet();

        var changesByUser = nameChanges.ToLookup(c => c.UserId);
        var trustByUser = trustChanges.ToLookup(c => c.UserId);
        string NameAt(string userId, string current, DateTimeOffset at) =>
            changesByUser[userId].FirstOrDefault(c => c.ChangedAt > at)?.PreviousDisplayName ?? current;

        var baseline = FriendLogBaseline(friendships.Select(f => f.StartedAt));

        var entries = new List<FriendLogEntryResponse>();
        foreach (var f in friendships)
        {
            if (f.StartedAt != baseline)
                entries.Add(new FriendLogEntryResponse
                {
                    Key = $"Friend:{f.Id}", Type = "Friend", CreatedAt = f.StartedAt, UserId = f.FriendUserId,
                    DisplayName = NameAt(f.FriendUserId, f.CurrentName, f.StartedAt), FriendNumber = f.FriendNumber,
                });
            if (f.EndedAt is { } ended)
                entries.Add(new FriendLogEntryResponse
                {
                    Key = $"Unfriend:{f.Id}", Type = "Unfriend", CreatedAt = ended, UserId = f.FriendUserId,
                    DisplayName = NameAt(f.FriendUserId, f.CurrentName, ended),
                });
            bool During(DateTimeOffset at) => at >= f.StartedAt && (f.EndedAt is null || at <= f.EndedAt);
            foreach (var c in changesByUser[f.FriendUserId].Where(c => During(c.ChangedAt)))
                entries.Add(new FriendLogEntryResponse
                {
                    Key = $"DisplayName:{c.Id}", Type = "DisplayName", CreatedAt = c.ChangedAt, UserId = f.FriendUserId,
                    DisplayName = c.DisplayName, PreviousDisplayName = c.PreviousDisplayName, FriendNumber = f.FriendNumber,
                });
            foreach (var c in trustByUser[f.FriendUserId].Where(c => During(c.ChangedAt) && !IsVeteranToggle(c.PreviousTrustLevel, c.TrustLevel)))
                entries.Add(new FriendLogEntryResponse
                {
                    Key = $"TrustLevel:{c.Id}", Type = "TrustLevel", CreatedAt = c.ChangedAt, UserId = f.FriendUserId,
                    DisplayName = NameAt(f.FriendUserId, f.CurrentName, c.ChangedAt), TrustLevel = c.TrustLevel, PreviousTrustLevel = c.PreviousTrustLevel, FriendNumber = f.FriendNumber,
                });
        }
        entries.AddRange(requests.Select(r => new FriendLogEntryResponse
        {
            Key = $"{r.Type}:{r.Id}", Type = r.Type, CreatedAt = r.CreatedAt, UserId = r.TargetUserId, DisplayName = r.DisplayName,
        }));

        return Ok(entries.Where(e => !hidden.Contains(e.Key)).OrderByDescending(e => e.CreatedAt).ThenByDescending(e => e.Key, StringComparer.Ordinal).ToArray());
    }

    // The original ignores a friend flipping between these two: it is a VRChat display setting.
    /// <summary>
    /// The first sync records everyone who was already a friend at one instant. Those are a
    /// starting point, not new friends, and the original logged nothing for them either.
    /// </summary>
    private static DateTimeOffset? FriendLogBaseline(IEnumerable<DateTimeOffset> startedAt)
    {
        var firstGroup = startedAt.GroupBy(t => t).OrderBy(g => g.Key).FirstOrDefault();
        return firstGroup is not null && firstGroup.Count() > 1 ? firstGroup.Key : null;
    }

    private async Task<DateTimeOffset?> FriendLogBaselineAsync(string accountId, CancellationToken cancellationToken) =>
        FriendLogBaseline(await _db.Friendships.AsNoTracking().Where(f => f.OwnerUserId == accountId).Select(f => f.StartedAt).ToListAsync(cancellationToken));

    private static bool IsVeteranToggle(string previous, string current) =>
        (previous, current) is ("Trusted User", "Veteran User") or ("Veteran User", "Trusted User");

    /// <summary>Remove a row from the account's friend log view. The history behind it stays in the database.</summary>
    /// <response code="204">Removed</response>
    /// <response code="404">No such account</response>
    [HttpDelete("{accountId}/friend-log/{key}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> HideFriendLogEntry([FromRoute] string accountId, [FromRoute] string key, CancellationToken cancellationToken)
    {
        if (!await _db.Accounts.AnyAsync(a => a.UserId == accountId, cancellationToken)) return Problem(AccountError.AccountNotFound);
        if (!await _db.FriendLogHiddenEntries.AnyAsync(h => h.OwnerUserId == accountId && h.EntryKey == key, cancellationToken))
        {
            _db.FriendLogHiddenEntries.Add(new FriendLogHiddenEntry { OwnerUserId = accountId, EntryKey = key, HiddenAt = DateTimeOffset.UtcNow });
            await _db.SaveChangesAsync(cancellationToken);
        }
        return NoContent();
    }
}
