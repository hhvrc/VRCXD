using System.Net.Mime;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenShock.Internal.Common.Problems;
using VRCX.API.Models.Response;
using VRCX.Common.Services.Ingestion;
using VRCX.Vrchat;
using VRCX.Vrchat.Models;

namespace VRCX.API.Controller.Accounts;

public sealed record BoopBody(string? EmojiId);

public sealed record UpdateBadgeBody(bool Hidden, bool Showcased);

public sealed record SendInviteBody(string Location, string WorldName, int? MessageSlot = null);
public sealed record RequestInviteBody(int? RequestSlot = null);

// The rest of the user pop-up's action menu, done on VRChat with the account's own session.
public sealed partial class AccountsController
{
    private const string ModerationTypes = "^(block|mute|muteChat|interactOff)$";
    // VRChat also reports the opposite kinds, such as an explicit unmute, and any of them can be removed.
    private const string AllModerationTypes = "^(block|unblock|mute|unmute|interactOn|interactOff|muteChat|unmuteChat)$";

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
            if (await _db.Users.FindAsync([accountId], cancellationToken) is { } me)
            {
                me.Badges = me.Badges.Select(b => b.BadgeId == badgeId ? b with { Hidden = badge.Hidden, Showcased = badge.Showcased, UpdatedAt = badge.UpdatedAt ?? b.UpdatedAt } : b).ToList();
                await _db.SaveChangesAsync(cancellationToken);
            }
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
            var note = await _db.UserNotes.AsNoTracking()
                .Where(n => n.OwnerUserId == accountId && n.TargetUserId == userId)
                .Select(n => n.Note)
                .FirstOrDefaultAsync(cancellationToken);
            return new UserRelationshipResponse
            {
                IsFriend = status?.IsFriend ?? false,
                IncomingRequest = status?.IncomingRequest ?? false,
                OutgoingRequest = status?.OutgoingRequest ?? false,
                Note = note ?? ""
            };
        });

    /// <summary>Block, mute, disable the chatbox of, or disable avatar interaction with a user.</summary>
    /// <response code="204">Applied</response>
    /// <response code="409">No live session</response>
    /// <response code="502">VRChat refused</response>
    [HttpPost("{accountId}/moderations/{userId}/{type:regex(" + ModerationTypes + ")}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> SendModeration([FromRoute] string accountId, [FromRoute] string userId, [FromRoute] string type, CancellationToken cancellationToken) =>
        RunAsync(accountId, async client =>
        {
            var sent = await client.SendPlayerModerationAsync(userId, type, cancellationToken);
            var row = await _db.PlayerModerations.FirstOrDefaultAsync(m => m.Id == sent.Id, cancellationToken);
            if (row is null)
            {
                // A user the server has never stored can't be a row yet; the next sync adds them both.
                if (!await _db.Users.AnyAsync(u => u.Id == userId, cancellationToken)) return;
                row = new global::VRCX.Common.VrcxDb.PlayerModeration { Id = sent.Id, OwnerUserId = accountId, TargetUserId = userId, Type = type };
                _db.PlayerModerations.Add(row);
            }
            row.CreatedAt = sent.Created;
            row.FetchedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
        });

    /// <summary>Undo a block, mute, disabled chatbox or disabled avatar interaction.</summary>
    /// <response code="204">Removed</response>
    /// <response code="409">No live session</response>
    /// <response code="502">VRChat refused</response>
    [HttpDelete("{accountId}/moderations/{userId}/{type:regex(" + AllModerationTypes + ")}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> DeleteModeration([FromRoute] string accountId, [FromRoute] string userId, [FromRoute] string type, CancellationToken cancellationToken) =>
        RunAsync(accountId, async client =>
        {
            await client.DeletePlayerModerationAsync(userId, type, cancellationToken);
            await _db.PlayerModerations
                .Where(m => m.OwnerUserId == accountId && m.TargetUserId == userId && m.Type == type)
                .ExecuteDeleteAsync(cancellationToken);
        });

    /// <summary>Remove every moderation of one kind from the account.</summary>
    /// <response code="204">Removed</response>
    /// <response code="409">No live session</response>
    /// <response code="502">VRChat refused</response>
    [HttpDelete("{accountId}/moderations/{type:regex(" + AllModerationTypes + ")}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> ClearModerations([FromRoute] string accountId, [FromRoute] string type, CancellationToken cancellationToken) =>
        RunAsync(accountId, async client =>
        {
            await client.DeletePlayerModerationsOfTypeAsync(type, cancellationToken);
            await _db.PlayerModerations.Where(m => m.OwnerUserId == accountId && m.Type == type).ExecuteDeleteAsync(cancellationToken);
        });

    /// <summary>Fetch the account's moderations from VRChat now instead of waiting for the next sync.</summary>
    /// <response code="204">Refreshed</response>
    /// <response code="409">No live session</response>
    /// <response code="502">VRChat refused</response>
    [HttpPost("{accountId}/moderations/refresh")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> RefreshModerations([FromRoute] string accountId, [FromServices] VrchatStore store, CancellationToken cancellationToken) =>
        RunAsync(accountId, async client =>
        {
            var moderations = await client.GetPlayerModerationsAsync(cancellationToken);
            await store.SyncPlayerModerationsAsync(_db, accountId, moderations, DateTimeOffset.UtcNow, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
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
    public Task<IActionResult> SendBoop([FromRoute] string accountId, [FromRoute] string userId, [FromBody] BoopBody body, [FromServices] VrchatStore store, CancellationToken cancellationToken) =>
        RunAsync(accountId, async client =>
        {
            // As the original's SendBoopDialog: booping back hides the boops this user sent that are
            // still open, before sending.
            var now = DateTimeOffset.UtcNow;
            var link = $"user:{userId}";
            var open = await _db.Notifications
                .Where(n => n.OwnerUserId == accountId && n.Type == "boop" && n.Link == link && !n.Hidden && (n.ExpiresAt == null || n.ExpiresAt > now))
                .Select(n => n.Id)
                .ToListAsync(cancellationToken);
            foreach (var id in open)
            {
                try { await client.HideNotificationV2Async(id, cancellationToken); }
                catch (VrchatApiException ex) when (ex is not VrchatUnauthorizedException) { }
                await store.MarkNotificationAsync(_db, accountId, id, seen: null, hidden: true, cancellationToken);
            }
            if (open.Count > 0) await _db.SaveChangesAsync(cancellationToken);
            await client.SendBoopAsync(userId, string.IsNullOrEmpty(body.EmojiId) ? null : body.EmojiId, cancellationToken);
        });

    /// <summary>Accept a user's friend request. Without a stored notification this sends a request back, as the original does.</summary>
    /// <response code="204">Accepted</response>
    /// <response code="409">No live session</response>
    /// <response code="502">VRChat refused</response>
    [HttpPost("{accountId}/friend-requests/{userId}/accept")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> AcceptFriendRequest([FromRoute] string accountId, [FromRoute] string userId, CancellationToken cancellationToken) =>
        RunAsync(accountId, async client =>
        {
            var request = await FindFriendRequestAsync(accountId, userId, cancellationToken);
            if (request is null)
            {
                await client.SendFriendRequestAsync(userId, cancellationToken);
                return;
            }
            await client.AcceptFriendRequestNotificationAsync(request.Id, cancellationToken);
            request.Hidden = true;
            await _db.SaveChangesAsync(cancellationToken);
        });

    /// <summary>Decline a user's friend request by hiding its notification.</summary>
    /// <response code="204">Declined</response>
    /// <response code="409">No live session</response>
    /// <response code="502">VRChat refused</response>
    [HttpPost("{accountId}/friend-requests/{userId}/decline")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> DeclineFriendRequest([FromRoute] string accountId, [FromRoute] string userId, CancellationToken cancellationToken) =>
        RunAsync(accountId, async client =>
        {
            var request = await FindFriendRequestAsync(accountId, userId, cancellationToken);
            if (request is null)
            {
                await client.CancelFriendRequestAsync(userId, cancellationToken);
                return;
            }
            await client.HideNotificationAsync(request.Id, cancellationToken);
            request.Hidden = true;
            await _db.SaveChangesAsync(cancellationToken);
        });

    private Task<global::VRCX.Common.VrcxDb.Notification?> FindFriendRequestAsync(string accountId, string userId, CancellationToken cancellationToken) =>
        _db.Notifications
            .Where(n => n.OwnerUserId == accountId && n.SenderUserId == userId && n.Type == "friendRequest" && !n.Hidden)
            .OrderByDescending(n => n.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
}
