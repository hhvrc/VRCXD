using VRCX.Common.VrcxDb;
using System.Net.Mime;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenShock.Internal.Common.Problems;
using VRCX.API.Models.Response;
using VRCX.Common.Errors;
using VRCX.Common.Services;
using VRCX.Common.Services.Queries;
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
    public async Task<IActionResult> ListFriends([FromRoute] string accountId, [FromServices] FriendQueries friends, CancellationToken cancellationToken)
    {
        if (!await _db.Accounts.AnyAsync(a => a.UserId == accountId, cancellationToken)) return Problem(AccountError.AccountNotFound);
        return Ok(await friends.ListFriendsAsync(accountId, cancellationToken));
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
                    NameHue = FriendQueries.NameHue(user.Id),
                };
            }).ToArray(),
        });
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
    public async Task<IActionResult> FriendLog([FromRoute] string accountId, [FromServices] FriendQueries friends, CancellationToken cancellationToken)
    {
        if (!await _db.Accounts.AnyAsync(a => a.UserId == accountId, cancellationToken)) return Problem(AccountError.AccountNotFound);
        return Ok(await friends.FriendLogAsync(accountId, cancellationToken));
    }

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
