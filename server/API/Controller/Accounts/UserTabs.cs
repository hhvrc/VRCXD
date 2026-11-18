using VRCX.Common.Errors;
using System.Net.Mime;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenShock.Internal.Common.Problems;
using VRCX.API.Models.Response;
using VRCX.API.Services;
using VRCX.Common.Services;
using VRCX.Common.Features.Friends;
using VRCX.Vrchat.Models;

namespace VRCX.API.Controller.Accounts;

public sealed record UserLookupResponse(string? UserId);

public sealed partial class AccountsController
{
    /// <summary>The friends this account shares with a user, live from VRChat.</summary>
    /// <response code="200">The friends</response>
    /// <response code="409">No live session</response>
    [HttpGet("{accountId}/users/{userId}/mutuals")]
    [ProducesResponseType<LimitedUserSearch[]>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> ListUserMutuals([FromRoute] string accountId, [FromRoute] string userId, CancellationToken cancellationToken) =>
        ReadLiveAsync(accountId, client => client.GetAllMutualFriendsAsync(userId, cancellationToken));

    /// <summary>
    /// How often the account's game log saw a user join and how long they were together: the
    /// original user dialog's Join Count and Time Together, counted as the Friend List does.
    /// </summary>
    /// <response code="200">The totals</response>
    /// <response code="404">No such account</response>
    [HttpGet("{accountId}/users/{userId}/together")]
    [ProducesResponseType<TimeTogetherResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> GetTimeTogether([FromRoute] string accountId, [FromRoute] string userId, [FromServices] FriendQueries friends, CancellationToken cancellationToken)
    {
        if (!await _db.Accounts.AnyAsync(a => a.UserId == accountId, cancellationToken)) return Problem(AccountError.AccountNotFound);
        return Ok(await friends.TimeTogetherAsync(accountId, userId, cancellationToken));
    }

    /// <summary>A user's public profile live from VRChat: bio, badges, and the profile theme and
    /// background the user dialog shows (the original's updateUserDialogProfile).</summary>
    /// <response code="200">The profile, or null when VRChat has none</response>
    /// <response code="409">No live session</response>
    [HttpGet("{accountId}/users/{userId}/public-profile")]
    [ProducesResponseType<PublicProfile>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> GetUserPublicProfile([FromRoute] string accountId, [FromRoute] string userId, [FromServices] VrchatReadCache cache, CancellationToken cancellationToken) =>
        ReadLiveAsync(accountId, client => cache.GetOrFetchAsync(VrchatReadCache.Key(accountId, "public-profile", userId), VrchatReadCache.DialogTtl,
            () => client.GetPublicProfileAsync(userId, cancellationToken)));

    /// <summary>How many friends and groups this account shares with a user, live from VRChat.</summary>
    /// <response code="200">The counts</response>
    /// <response code="409">No live session</response>
    [HttpGet("{accountId}/users/{userId}/mutual-counts")]
    [ProducesResponseType<MutualCounts>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> GetUserMutualCounts([FromRoute] string accountId, [FromRoute] string userId, [FromServices] VrchatReadCache cache, CancellationToken cancellationToken) =>
        ReadLiveAsync(accountId, client => cache.GetOrFetchAsync(VrchatReadCache.Key(accountId, "mutual-counts", userId), VrchatReadCache.DialogTtl,
            () => client.GetMutualCountsAsync(userId, cancellationToken)));

    /// <summary>The groups a user is in, as far as this account may see, live from VRChat.</summary>
    /// <response code="200">The groups</response>
    /// <response code="409">No live session</response>
    /// <summary>
    /// The original's lookupUser, for rows that only have a display name: a friend or stored user with
    /// exactly that name, else VRChat's exact-name search. A null id when nobody matches (upstream issues 110 and 519).
    /// </summary>
    /// <response code="200">The user id, or null</response>
    [HttpGet("{accountId}/users/lookup")]
    [ProducesResponseType<UserLookupResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> LookupUser([FromRoute] string accountId, [FromQuery] string displayName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(displayName) || displayName.StartsWith("ID:", StringComparison.Ordinal)) return Ok(new UserLookupResponse(null));
        var friend = await _db.Friendships.AsNoTracking()
            .Where(f => f.OwnerUserId == accountId && f.EndedAt == null && f.Friend.DisplayName == displayName)
            .Select(f => f.FriendUserId).FirstOrDefaultAsync(cancellationToken);
        if (friend is not null) return Ok(new UserLookupResponse(friend));
        var known = await _db.Users.AsNoTracking()
            .Where(u => u.DisplayName == displayName && !u.IsStub)
            .OrderByDescending(u => u.UpdatedAt).Select(u => u.Id).FirstOrDefaultAsync(cancellationToken);
        if (known is not null) return Ok(new UserLookupResponse(known));
        if (_sessions.Get(accountId) is not { } client) return Ok(new UserLookupResponse(null));
        try
        {
            var found = await client.SearchUsersAsync(displayName, 10, 0, cancellationToken, fuzzy: false);
            return Ok(new UserLookupResponse(found.FirstOrDefault(u => u.DisplayName == displayName)?.Id));
        }
        catch (VRCX.Vrchat.VrchatApiException ex) when (ex is not VRCX.Vrchat.VrchatUnauthorizedException)
        {
            return Ok(new UserLookupResponse(null));
        }
    }

    [HttpGet("{accountId}/users/{userId}/groups")]
    [ProducesResponseType<LimitedUserGroup[]>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> ListUserGroups([FromRoute] string accountId, [FromRoute] string userId, [FromServices] VrchatReadCache cache, CancellationToken cancellationToken) =>
        ReadLiveAsync(accountId, client => cache.GetOrFetchAsync(VrchatReadCache.Key(accountId, "user-groups", userId), VrchatReadCache.DialogTtl,
            () => client.GetUserGroupsWithRepresentationAsync(userId, cancellationToken)));

    /// <summary>The worlds a user made, live from VRChat: public ones, or all of them for the account itself.</summary>
    /// <response code="200">The worlds</response>
    /// <response code="409">No live session</response>
    [HttpGet("{accountId}/users/{userId}/worlds")]
    [ProducesResponseType<World[]>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> ListUserWorlds([FromRoute] string accountId, [FromRoute] string userId, CancellationToken cancellationToken) =>
        ReadLiveAsync(accountId, client => client.GetWorldsByAuthorAsync(userId, isSelf: userId == accountId, cancellationToken));

    /// <summary>A user's favorite world lists, live from VRChat.</summary>
    /// <response code="200">The lists</response>
    /// <response code="409">No live session</response>
    [HttpGet("{accountId}/users/{userId}/favorite-worlds")]
    [ProducesResponseType<UserFavoriteWorldsResponse[]>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> ListUserFavoriteWorlds([FromRoute] string accountId, [FromRoute] string userId, CancellationToken cancellationToken) =>
        ReadLiveAsync(accountId, async client => (await client.GetUserFavoriteWorldsAsync(userId, cancellationToken))
            .Select(list => new UserFavoriteWorldsResponse { Name = list.Group.DisplayName, Visibility = list.Group.Visibility, Worlds = list.Worlds })
            .ToList());

    /// <summary>The avatars by a user that this server has seen. VRChat only lists an account's own.</summary>
    /// <response code="200">The avatars</response>
    [HttpGet("{accountId}/users/{userId}/avatars")]
    [ProducesResponseType<AvatarResponse[]>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> ListUserAvatars([FromRoute] string accountId, [FromRoute] string userId, CancellationToken cancellationToken)
    {
        var avatars = await _db.Avatars.AsNoTracking().Where(a => a.AuthorId == userId).OrderBy(a => a.Name).ToListAsync(cancellationToken);
        return Ok(avatars.Select(AvatarResponse.From));
    }

    /// <summary>The avatars this account bought, from VRChat, stored as they come: the Avatars tab's Purchased group.</summary>
    /// <response code="200">The avatars</response>
    /// <response code="409">No live session</response>
    [HttpGet("{accountId}/avatars/licensed")]
    [ProducesResponseType<AvatarResponse[]>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> ListLicensedAvatars([FromRoute] string accountId, [FromServices] Common.Services.Ingestion.VrchatStore store, CancellationToken cancellationToken) =>
        ReadLiveAsync(accountId, async client =>
        {
            var now = DateTimeOffset.UtcNow;
            var stored = new List<AvatarResponse>();
            foreach (var api in await client.GetLicensedAvatarsAsync(cancellationToken))
                stored.Add(AvatarResponse.From(await store.UpsertAvatarAsync(_db, api, now, cancellationToken)));
            await _db.SaveChangesAsync(cancellationToken);
            return stored;
        });
}
