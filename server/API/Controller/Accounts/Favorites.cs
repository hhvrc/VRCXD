using System.Net.Mime;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenShock.Internal.Common.Problems;
using VRCX.API.Models.Response;
using VRCX.Common.Errors;
using VRCX.Common.VrcxDb;
using VRCX.Vrchat.Models;

namespace VRCX.API.Controller.Accounts;

public sealed record LocalFavoriteRefreshResponse(bool Available);

public sealed record AddFavoriteBody(string Type, string TargetId, string Group);
public sealed record LocalFavoriteBody(FavoriteType Type, string TargetId, string Group);

// The original's ChooseFavoriteGroupDialog: VRChat's favorite groups, and the local ones only VRCX knows of.
public sealed partial class AccountsController
{
    /// <summary>Whether something is a favorite at all, on VRChat or locally.</summary>
    /// <response code="200">True or false</response>
    [HttpGet("{accountId}/favorites/status")]
    [ProducesResponseType<bool>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> GetFavoriteStatus([FromRoute] string accountId, [FromQuery] string targetId, CancellationToken cancellationToken) =>
        Ok(await _db.Favorites.AnyAsync(f => f.OwnerUserId == accountId && f.TargetId == targetId, cancellationToken)
            || await _db.LocalFavorites.AnyAsync(f => f.OwnerUserId == accountId && f.TargetId == targetId, cancellationToken));

    /// <summary>What the favorite dialog offers for one thing: every group slot with its count, and the local groups.</summary>
    /// <response code="200">The choices</response>
    /// <response code="409">No live session</response>
    [HttpGet("{accountId}/favorites/picker")]
    [ProducesResponseType<FavoritePickerResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> GetFavoritePicker([FromRoute] string accountId, [FromQuery] FavoriteType type, [FromQuery] string? targetId, CancellationToken cancellationToken)
    {
        var stored = await _db.FavoriteGroups.AsNoTracking().Where(g => g.OwnerUserId == accountId).ToListAsync(cancellationToken);
        var counts = await _db.Favorites.AsNoTracking()
            .Where(f => f.OwnerUserId == accountId && f.Type == type && f.GroupName != null)
            .GroupBy(f => f.GroupName!)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
        var currentGroup = await _db.Favorites.AsNoTracking()
            .Where(f => f.OwnerUserId == accountId && f.Type == type && f.TargetId == targetId)
            .Select(f => f.GroupName)
            .FirstOrDefaultAsync(cancellationToken);
        var isFavorite = await _db.Favorites.AnyAsync(f => f.OwnerUserId == accountId && f.Type == type && f.TargetId == targetId, cancellationToken);
        var locals = await LocalGroupsAsync(accountId, type, targetId, cancellationToken);

        return await ReadLiveAsync(accountId, async client =>
        {
            var limits = await client.GetFavoriteLimitsAsync(cancellationToken);
            var me = await client.GetCurrentUserAsync(cancellationToken);
            var slots = FavoriteSlots(type, limits).Select(slot =>
            {
                var named = stored.FirstOrDefault(g => g.Name == slot.Name);
                return new FavoriteSlotResponse
                {
                    Type = slot.Type,
                    Name = slot.Name,
                    DisplayName = string.IsNullOrEmpty(named?.DisplayName) ? slot.DisplayName : named.DisplayName,
                    Capacity = slot.Capacity,
                    Count = counts.GetValueOrDefault(slot.Name),
                    Visibility = named?.Visibility ?? "private",
                };
            }).ToList();
            return new FavoritePickerResponse
            {
                Groups = slots,
                Current = isFavorite ? slots.FirstOrDefault(s => s.Name == currentGroup) ?? new FavoriteSlotResponse { Type = FavoriteTypeName(type), Name = currentGroup ?? "", DisplayName = currentGroup ?? "Favorites", Capacity = 0, Count = 0 } : null,
                Local = locals,
                IsSupporter = me.Tags.Contains("system_supporter"),
            };
        });
    }

    /// <summary>Add a favorite to one of VRChat's groups.</summary>
    /// <response code="204">Added</response>
    /// <response code="409">No live session</response>
    /// <response code="502">VRChat refused</response>
    [HttpPost("{accountId}/favorites")]
    [Consumes(MediaTypeNames.Application.Json)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> AddFavorite([FromRoute] string accountId, [FromBody] AddFavoriteBody body, CancellationToken cancellationToken) =>
        RunAsync(accountId, _ => _sync.AddFavoriteAsync(accountId, body.Type, body.TargetId, body.Group, cancellationToken));

    /// <summary>Take something out of VRChat's favorites.</summary>
    /// <response code="204">Removed</response>
    /// <response code="409">No live session</response>
    /// <response code="502">VRChat refused</response>
    [HttpDelete("{accountId}/favorites/{targetId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> RemoveFavorite([FromRoute] string accountId, [FromRoute] string targetId, CancellationToken cancellationToken) =>
        RunAsync(accountId, _ => _sync.RemoveFavoriteAsync(accountId, targetId, cancellationToken));

    /// <summary>
    /// How many players are in each favorite world right now, for the original's Players sort (upstream
    /// issue 674): VRChat's favorite worlds in one call, plus any of <paramref name="ids"/> (local
    /// favorites, at most 100) fetched one by one. Momentary counts, so nothing is stored.
    /// </summary>
    /// <response code="200">World id to occupants</response>
    /// <response code="409">No live session</response>
    [HttpGet("{accountId}/favorites/world-occupants")]
    [ProducesResponseType<Dictionary<string, int>>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> GetFavoriteWorldOccupants([FromRoute] string accountId, [FromQuery] string[]? ids, CancellationToken cancellationToken) =>
        ReadLiveAsync(accountId, async client =>
        {
            var occupants = new Dictionary<string, int>();
            foreach (var world in await client.GetFavoriteWorldsAsync(cancellationToken)) occupants[world.Id] = world.Occupants;
            foreach (var id in (ids ?? []).Where(id => id.StartsWith("wrld_", StringComparison.Ordinal) && !occupants.ContainsKey(id)).Distinct().Take(100))
            {
                try
                {
                    if (await client.GetWorldAsync(id, cancellationToken) is { } world) occupants[world.Id] = world.Occupants;
                }
                catch (VRCX.Vrchat.VrchatApiException ex) when (ex is not VRCX.Vrchat.VrchatUnauthorizedException)
                {
                    // A deleted or private world has no count; it sorts last.
                }
            }
            return occupants;
        });

    /// <summary>Everything in the local favorite groups of one kind.</summary>
    /// <response code="200">The favorites</response>
    [HttpGet("{accountId}/favorites/local")]
    [ProducesResponseType<IReadOnlyList<LocalFavoriteResponse>>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> ListLocalFavorites([FromRoute] string accountId, [FromQuery] FavoriteType type, CancellationToken cancellationToken)
    {
        var rows = await _db.LocalFavorites.AsNoTracking()
            .Where(f => f.OwnerUserId == accountId && f.Type == type)
            .OrderBy(f => f.GroupName).ThenBy(f => f.CreatedAt)
            .ToListAsync(cancellationToken);
        var names = await FavoriteTargetsAsync(type, rows.Select(f => f.TargetId).ToList(), cancellationToken);
        return Ok(rows.Select(f => new LocalFavoriteResponse
        {
            GroupName = f.GroupName,
            TargetId = f.TargetId,
            TargetName = names.TryGetValue(f.TargetId, out var n) ? n.Name : f.TargetId,
            ThumbnailImageUrl = n?.Thumbnail,
            AuthorName = n?.AuthorName,
            AuthorId = n?.AuthorId,
            ReleaseStatus = n?.ReleaseStatus,
            Tags = n?.Tags,
        }).ToList());
    }

    /// <summary>Put something in a local group, which only this server knows about. The group appears when first used.</summary>
    /// <response code="204">Added</response>
    /// <response code="404">No such account</response>
    [HttpPost("{accountId}/favorites/local")]
    [Consumes(MediaTypeNames.Application.Json)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> AddLocalFavorite([FromRoute] string accountId, [FromBody] LocalFavoriteBody body, CancellationToken cancellationToken)
    {
        if (!await _db.Accounts.AnyAsync(a => a.UserId == accountId, cancellationToken)) return Problem(AccountError.AccountNotFound);
        await _sync.AddLocalFavoriteAsync(accountId, body.Type, body.TargetId, body.Group.Trim(), cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Fetch one local favorite from VRChat again and store it, the original's local Refresh, which the
    /// client runs one per second. Unavailable when VRChat no longer shows it (upstream PR 1525).
    /// </summary>
    /// <response code="200">Whether VRChat still has it</response>
    /// <response code="409">No live session</response>
    [HttpPost("{accountId}/favorites/local/refresh")]
    [ProducesResponseType<LocalFavoriteRefreshResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> RefreshLocalFavorite([FromRoute] string accountId, [FromQuery] FavoriteType type, [FromQuery] string targetId, [FromServices] Common.Services.Ingestion.VrchatStore store, CancellationToken cancellationToken) =>
        ReadLiveAsync(accountId, async client =>
        {
            var now = DateTimeOffset.UtcNow;
            try
            {
                switch (type)
                {
                    case FavoriteType.World when await client.GetWorldAsync(targetId, cancellationToken) is { } world:
                        await store.UpsertWorldAsync(_db, world, now, cancellationToken);
                        break;
                    case FavoriteType.Avatar when await client.GetAvatarAsync(targetId, cancellationToken) is { } avatar:
                        await store.UpsertAvatarAsync(_db, avatar, now, cancellationToken);
                        break;
                    case FavoriteType.Friend when await client.GetUserAsync(targetId, cancellationToken) is { } user:
                        await store.UpsertUserAsync(_db, user, now, cancellationToken);
                        break;
                    default:
                        return new LocalFavoriteRefreshResponse(false);
                }
            }
            catch (VRCX.Vrchat.VrchatApiException ex) when (ex is not VRCX.Vrchat.VrchatUnauthorizedException)
            {
                return new LocalFavoriteRefreshResponse(false);
            }
            await _db.SaveChangesAsync(cancellationToken);
            return new LocalFavoriteRefreshResponse(true);
        });

    /// <summary>Take something out of a local group.</summary>
    /// <response code="204">Removed</response>
    [HttpDelete("{accountId}/favorites/local")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> RemoveLocalFavorite([FromRoute] string accountId, [FromQuery] FavoriteType type, [FromQuery] string targetId, [FromQuery] string group, CancellationToken cancellationToken)
    {
        await _db.LocalFavorites.Where(f => f.OwnerUserId == accountId && f.Type == type && f.TargetId == targetId && f.GroupName == group).ExecuteDeleteAsync(cancellationToken);
        return NoContent();
    }

    private sealed record FavoriteTarget(string Name, string? Thumbnail, string? AuthorName = null, string[]? Tags = null, string? AuthorId = null, string? ReleaseStatus = null);

    private async Task<Dictionary<string, FavoriteTarget>> FavoriteTargetsAsync(FavoriteType type, List<string> ids, CancellationToken ct) => type switch
    {
        FavoriteType.Friend => await _db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => new FavoriteTarget(u.DisplayName, u.CurrentAvatarThumbnailImageUrl), ct),
        FavoriteType.World => await _db.Worlds.AsNoTracking().Where(w => ids.Contains(w.Id)).ToDictionaryAsync(w => w.Id, w => new FavoriteTarget(w.Name, w.ThumbnailImageUrl, w.AuthorName, w.Tags, w.AuthorId, w.ReleaseStatus), ct),
        _ => await _db.Avatars.AsNoTracking().Where(a => ids.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => new FavoriteTarget(a.Name, a.ThumbnailImageUrl, a.AuthorName, null, a.AuthorId, a.ReleaseStatus), ct),
    };

    private static string FavoriteTypeName(FavoriteType type) => type switch { FavoriteType.Friend => "friend", FavoriteType.World => "world", _ => "avatar" };

    // VRChat only lists the groups that were renamed, so the slots come from the limits, as in the original's buildFavoriteGroups.
    private static IEnumerable<(string Type, string Name, string DisplayName, int Capacity)> FavoriteSlots(FavoriteType type, FavoriteLimits limits)
    {
        if (type == FavoriteType.Friend)
        {
            for (var i = 0; i < limits.MaxFavoriteGroups.Friend; i++) yield return ("friend", $"group_{i}", $"Group {i + 1}", limits.MaxFavoritesPerGroup.Friend);
        }
        else if (type == FavoriteType.World)
        {
            for (var i = 0; i < limits.MaxFavoriteGroups.World; i++) yield return ("world", $"worlds{i + 1}", $"Group {i + 1}", limits.MaxFavoritesPerGroup.World);
            for (var i = 0; i < limits.MaxFavoriteGroups.VrcPlusWorld; i++) yield return ("vrcPlusWorld", $"vrcPlusWorlds{i + 1}", $"VRC+ Group {i + 1}", limits.MaxFavoritesPerGroup.VrcPlusWorld);
        }
        else
        {
            for (var i = 0; i < limits.MaxFavoriteGroups.Avatar; i++) yield return ("avatar", $"avatars{i + 1}", $"Group {i + 1}", limits.MaxFavoritesPerGroup.Avatar);
        }
    }
}
