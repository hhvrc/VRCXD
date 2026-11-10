using System.Net.Mime;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenShock.Internal.Common.Problems;
using VRCX.Common.Errors;
using VRCX.Common.VrcxDb;
using VRCX.Vrchat;

namespace VRCX.API.Controller.Accounts;

// Friend requests and unfriending, done on VRChat with the account's own session. The
// friendships table follows through the pipeline's friend-add / friend-delete events.
public sealed partial class AccountsController
{
    /// <summary>Send a friend request from this account.</summary>
    /// <response code="204">Sent</response>
    /// <response code="409">The account has no live VRChat session</response>
    /// <response code="502">VRChat refused</response>
    [HttpPost("{accountId}/friend-requests/{userId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> SendFriendRequest([FromRoute] string accountId, [FromRoute] string userId, CancellationToken cancellationToken) =>
        RunAsync(accountId, async client =>
        {
            await client.SendFriendRequestAsync(userId, cancellationToken);
            await LogFriendRequestAsync(accountId, userId, "FriendRequest", cancellationToken);
        });

    /// <summary>Withdraw a friend request this account sent.</summary>
    /// <response code="204">Withdrawn</response>
    [HttpDelete("{accountId}/friend-requests/{userId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> CancelFriendRequest([FromRoute] string accountId, [FromRoute] string userId, CancellationToken cancellationToken) =>
        RunAsync(accountId, async client =>
        {
            await client.CancelFriendRequestAsync(userId, cancellationToken);
            await LogFriendRequestAsync(accountId, userId, "CancelFriendRequest", cancellationToken);
        });

    /// <summary>Unfriend on VRChat.</summary>
    /// <response code="204">Unfriended</response>
    [HttpDelete("{accountId}/friends/{userId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> Unfriend([FromRoute] string accountId, [FromRoute] string userId, CancellationToken cancellationToken) =>
        RunAsync(accountId, async client =>
        {
            await client.UnfriendAsync(userId, cancellationToken);
            var now = DateTimeOffset.UtcNow;
            await _db.Friendships.Where(f => f.OwnerUserId == accountId && f.FriendUserId == userId && f.EndedAt == null)
                .ExecuteUpdateAsync(set => set.SetProperty(f => f.EndedAt, now), cancellationToken);
        });

    private async Task LogFriendRequestAsync(string accountId, string userId, string type, CancellationToken cancellationToken)
    {
        var name = await _db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.DisplayName).FirstOrDefaultAsync(cancellationToken);
        _db.FriendRequestEvents.Add(new FriendRequestEvent { OwnerUserId = accountId, TargetUserId = userId, DisplayName = name ?? userId, Type = type, CreatedAt = DateTimeOffset.UtcNow });
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task<IActionResult> RunAsync(string accountId, Func<VrchatApiClient, Task> action)
    {
        var client = _sessions.Get(accountId);
        if (client is null) return Problem(AccountError.NoLiveSession);
        try
        {
            await action(client);
        }
        catch (VrchatUnauthorizedException)
        {
            await _sessions.InvalidateAsync(accountId, HttpContext.RequestAborted);
            return Problem(VrchatError.SessionExpired);
        }
        catch (VrchatApiException ex)
        {
            return VrchatProblem(ex);
        }
        return NoContent();
    }
}
