using System.Net.Mime;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenShock.Internal.Common.Problems;
using VRCX.API.Models.Response;
using VRCX.Common.Errors;
using VRCX.Common.Services.Ingestion;
using VRCX.Vrchat;
using VRCX.Vrchat.Models;

namespace VRCX.API.Controller.Users;

public sealed partial class UsersController
{
    /// <summary>Search VRChat for users by display name (through a logged-in account) and store what comes back.</summary>
    /// <param name="q">Display name fragment</param>
    /// <param name="account">Which logged-in account's session to search with</param>
    /// <param name="store"></param>
    /// <param name="cancellationToken"></param>
    /// <response code="200">Up to 20 results</response>
    /// <response code="409">No account is logged in</response>
    [HttpGet("search")]
    [ProducesResponseType<UserSearchResultResponse[]>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> Search([FromQuery] string q, [FromQuery] string? account, [FromServices] VrchatStore store, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(q)) return Ok(Array.Empty<UserSearchResultResponse>());
        var via = PickAccount(account, _sessions);
        if (via is null) return Problem(AccountError.NoAccounts);

        IReadOnlyList<Vrchat.Models.LimitedUserSearch> results;
        try
        {
            results = await _sessions.Require(via).SearchUsersAsync(q.Trim(), 20, 0, cancellationToken);
        }
        catch (VrchatApiException ex)
        {
            return VrchatProblem(ex);
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var result in results) await store.UpsertSearchResultAsync(_db, result, now, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);

        var friendIds = await _db.Friendships.Where(f => f.OwnerUserId == via && f.EndedAt == null).Select(f => f.FriendUserId).ToHashSetAsync(cancellationToken);
        return Ok(results.Select(r => new UserSearchResultResponse
        {
            Id = r.Id, DisplayName = r.DisplayName, Status = r.Status, StatusDescription = r.StatusDescription,
            TrustLevel = TrustLevel.FromTags(r.Tags), ThumbnailImageUrl = r.CurrentAvatarThumbnailImageUrl,
            IsFriend = friendIds.Contains(r.Id),
        }).ToArray());
    }
}
