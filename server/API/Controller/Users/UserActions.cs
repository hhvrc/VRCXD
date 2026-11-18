using System.Net.Mime;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenShock.Internal.Common.Problems;
using VRCX.API.Models.Response;
using VRCX.Common.Services.Ingestion;
using VRCX.Vrchat;
using VRCX.Vrchat.Models;

namespace VRCX.API.Controller.Users;

public sealed record BoopBody(string? EmojiId);

public sealed record UpdateBadgeBody(bool Hidden, bool Showcased);

public sealed record SendInviteBody(string Location, string WorldName, int? MessageSlot = null);
public sealed record RequestInviteBody(int? RequestSlot = null);

// The rest of the user pop-up's action menu, done on VRChat with the account's own session.
public sealed partial class AccountUsersController
{
    /// <summary>Hide or showcase one of the account's own badges, the original's updateBadge, and keep the stored profile in step.</summary>
    /// <response code="200">The badge as VRChat now has it</response>
    /// <response code="409">No live session</response>
    /// <response code="502">VRChat refused</response>
    [HttpPut("{accountId}/badges/{badgeId}")]
    [ProducesResponseType<Badge>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> UpdateBadge([FromRoute] string accountId, [FromRoute] string badgeId, [FromBody] UpdateBadgeBody body, CancellationToken cancellationToken) =>
        ReadLiveAsync(accountId, async client =>
        {
            // Hiding a badge takes it out of the showcase, and showcasing it shows it, as the original does.
            var showcased = body.Showcased && !body.Hidden;
            var badge = await client.UpdateBadgeAsync(accountId, badgeId, body.Hidden, showcased, cancellationToken);
            await profileStore.BadgeChangedAsync(accountId, badgeId, badge, cancellationToken);
            return badge;
        });

    /// <summary>Where this account stands with a user: friend state live from VRChat, its note from the last sync.</summary>
    /// <response code="200">The relationship</response>
    /// <response code="409">No live session</response>
    [HttpGet("{accountId}/users/{userId}/relationship")]
    [ProducesResponseType<UserRelationshipResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> GetUserRelationship([FromRoute] string accountId, [FromRoute] string userId, CancellationToken cancellationToken) =>
        ReadLiveAsync(accountId, async client =>
        {
            var status = await client.GetFriendStatusAsync(userId, cancellationToken);
            var note = await notes.NoteAsync(accountId, userId, cancellationToken);
            return new UserRelationshipResponse
            {
                IsFriend = status?.IsFriend ?? false,
                IncomingRequest = status?.IncomingRequest ?? false,
                OutgoingRequest = status?.OutgoingRequest ?? false,
                Note = note ?? ""
            };
        });

    /// <summary>Ask a user for an invite.</summary>
    /// <response code="204">Sent</response>
    /// <response code="409">No live session</response>
    /// <response code="502">VRChat refused</response>
    [HttpPost("{accountId}/invite-requests/{userId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> SendRequestInvite([FromRoute] string accountId, [FromRoute] string userId, [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] RequestInviteBody? body, CancellationToken cancellationToken) =>
        RunAsync(accountId, client => client.SendRequestInviteAsync(userId, body?.RequestSlot, cancellationToken));

    /// <summary>Invite a user to an instance.</summary>
    /// <response code="204">Sent</response>
    /// <response code="409">No live session</response>
    /// <response code="502">VRChat refused</response>
    [HttpPost("{accountId}/invites/{userId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> SendInvite([FromRoute] string accountId, [FromRoute] string userId, [FromBody] SendInviteBody body, CancellationToken cancellationToken) =>
        RunAsync(accountId, client => client.SendInstanceInviteAsync(userId, body.Location, body.WorldName, body.MessageSlot, cancellationToken));

    /// <summary>Boop a user, optionally with an emoji.</summary>
    /// <response code="204">Sent</response>
    /// <response code="409">No live session</response>
    /// <response code="502">VRChat refused</response>
    [HttpPost("{accountId}/users/{userId}/boop")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> SendBoop([FromRoute] string accountId, [FromRoute] string userId, [FromBody] BoopBody body, CancellationToken cancellationToken) =>
        Done(await notificationActions.SendBoopAsync(accountId, userId, body.EmojiId, cancellationToken));

    /// <summary>Accept a user's friend request. Without a stored notification this sends a request back, as the original does.</summary>
    /// <response code="204">Accepted</response>
    /// <response code="409">No live session</response>
    /// <response code="502">VRChat refused</response>
    [HttpPost("{accountId}/friend-requests/{userId}/accept")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> AcceptFriendRequest([FromRoute] string accountId, [FromRoute] string userId, CancellationToken cancellationToken) =>
        Done(await notificationActions.AcceptFriendRequestFromAsync(accountId, userId, cancellationToken));

    /// <summary>Decline a user's friend request by hiding its notification.</summary>
    /// <response code="204">Declined</response>
    /// <response code="409">No live session</response>
    /// <response code="502">VRChat refused</response>
    [HttpPost("{accountId}/friend-requests/{userId}/decline")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> DeclineFriendRequest([FromRoute] string accountId, [FromRoute] string userId, CancellationToken cancellationToken) =>
        Done(await notificationActions.DeclineFriendRequestFromAsync(accountId, userId, cancellationToken));
}
