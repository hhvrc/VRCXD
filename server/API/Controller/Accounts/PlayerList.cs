using System.Net.Mime;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenShock.Internal.Common.Problems;
using VRCX.API.Models.Response;
using VRCX.Common.Errors;
using VRCX.Common.Services.Presence;
using VRCX.Common.VrcxDb;

namespace VRCX.API.Controller.Accounts;

// The Player List page: the instance the account is in right now, and everyone the game log says is there.
public sealed partial class AccountsController
{
    /// <summary>The account's current instance and who is in it.</summary>
    /// <response code="200">The instance, or empty fields when the account is not in one</response>
    /// <response code="404">No such account</response>
    [HttpGet("{accountId}/player-list")]
    [ProducesResponseType<PlayerListResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> GetPlayerList([FromRoute] string accountId, [FromServices] IPresenceCache presence, CancellationToken cancellationToken)
    {
        if (!await _db.Accounts.AnyAsync(a => a.UserId == accountId, cancellationToken)) return Problem(AccountError.AccountNotFound);

        var stay = await _db.InstanceVisits.AsNoTracking()
            .Where(v => v.UserId == accountId && v.LeftAt == null && v.Source == VisitSource.GameLog)
            .OrderByDescending(v => v.JoinedAt)
            .Select(v => new { v.InstanceLocation, v.JoinedAt })
            .FirstOrDefaultAsync(cancellationToken);
        var own = (await presence.GetManyAsync([accountId], cancellationToken)).GetValueOrDefault(accountId);
        var arrived = own?.LocationSince ?? own?.PresenceChangedAt;

        // A stay stays open when the game exits without logging the leave. VRChat saying the account
        // moved later wins over it: without this the page shows a room left hours ago.
        var stayCurrent = stay is not null && (own?.CurrentInstanceLocation is null || own.CurrentInstanceLocation == stay.InstanceLocation || arrived is null || arrived <= stay.JoinedAt);
        var location = stayCurrent ? stay!.InstanceLocation : own?.CurrentInstanceLocation;
        if (location is null) return Ok(new PlayerListResponse { Players = [] });

        var world = await _db.Instances.AsNoTracking().Where(i => i.Location == location).Select(i => i.World).FirstOrDefaultAsync(cancellationToken);
        var home = await _db.Accounts.AsNoTracking().Where(a => a.UserId == accountId).Select(a => a.HomeLocation).FirstOrDefaultAsync(cancellationToken);

        var visits = await _db.InstanceVisits.AsNoTracking()
            .Where(v => v.InstanceLocation == location && v.LeftAt == null && (!stayCurrent || v.Source == VisitSource.GameLog))
            .OrderBy(v => v.JoinedAt)
            .Select(v => new { v.JoinedAt, v.User })
            .ToListAsync(cancellationToken);
        var players = visits.DistinctBy(v => v.User.Id).ToList();
        var ids = players.Select(p => p.User.Id).ToArray();

        var (friends, _) = await FriendSetsAsync(accountId, cancellationToken);
        var notes = (await _db.UserNotes.AsNoTracking().Where(n => n.OwnerUserId == accountId && ids.Contains(n.TargetUserId))
            .Select(n => new { n.TargetUserId, n.Note }).ToListAsync(cancellationToken)).GroupBy(n => n.TargetUserId).ToDictionary(g => g.Key, g => g.First().Note);
        var memos = await _db.UserMemos.AsNoTracking().Where(m => ids.Contains(m.UserId)).ToDictionaryAsync(m => m.UserId, m => m.Memo, cancellationToken);
        var moderations = (await _db.PlayerModerations.AsNoTracking().Where(m => m.OwnerUserId == accountId && ids.Contains(m.TargetUserId))
            .Select(m => new { m.TargetUserId, m.Type }).ToListAsync(cancellationToken)).ToLookup(m => m.TargetUserId, m => m.Type);
        var present = await presence.GetManyAsync(ids, cancellationToken);

        return Ok(new PlayerListResponse
        {
            Location = location,
            JoinedAt = stayCurrent ? stay!.JoinedAt : arrived,
            World = world is null ? null : new PlayerListWorld
            {
                Id = world.Id, Name = world.Name, Description = world.Description, AuthorId = world.AuthorId, AuthorName = world.AuthorName,
                ImageUrl = world.ImageUrl, ThumbnailImageUrl = world.ThumbnailImageUrl, ReleaseStatus = world.ReleaseStatus, Tags = world.Tags, Platforms = world.Platforms,
                Capacity = world.Capacity, RecommendedCapacity = world.RecommendedCapacity, CreatedAt = world.CreatedAt, UpdatedAt = world.UpdatedAt,
                IsHome = home is not null && home.StartsWith(world.Id, StringComparison.Ordinal),
            },
            Players = players.Select(p => new PlayerListPlayer
            {
                UserId = p.User.Id,
                DisplayName = p.User.DisplayName,
                JoinedAt = p.JoinedAt,
                TrustLevel = p.User.TrustLevel,
                Tags = p.User.Tags,
                DeveloperType = p.User.DeveloperType,
                Status = p.User.Status,
                StatusDescription = p.User.StatusDescription,
                ThumbnailImageUrl = UserImage.Pick(p.User.IconUrl, p.User.CurrentAvatarThumbnailImageUrl, p.User.CurrentAvatarImageUrl),
                BioLinks = p.User.BioLinks,
                Note = notes.GetValueOrDefault(p.User.Id),
                Memo = memos.GetValueOrDefault(p.User.Id),
                Platform = present.GetValueOrDefault(p.User.Id)?.Platform ?? p.User.LastPlatform,
                IsFriend = friends.Contains(p.User.Id),
                IsBlocked = moderations[p.User.Id].Contains("block"),
                IsMuted = moderations[p.User.Id].Contains("mute"),
                IsAvatarInteractionDisabled = moderations[p.User.Id].Contains("interactOff"),
                AgeVerified = p.User.AgeVerificationStatus == "18+",
            }).ToArray(),
        });
    }
}
