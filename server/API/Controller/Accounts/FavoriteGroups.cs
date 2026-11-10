using System.Net.Mime;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenShock.Internal.Common.Problems;
using VRCX.API.Models.Response;
using VRCX.Common.Errors;
using VRCX.Common.VrcxDb;

namespace VRCX.API.Controller.Accounts;

public sealed record SaveFavoriteGroupBody(string? DisplayName, string? Visibility);

public sealed record LocalFavoriteGroupBody(FavoriteType Type, string Name);

public sealed record RenameLocalFavoriteGroupBody(FavoriteType Type, string Name, string NewName);

// The Favorites page's group panel: VRChat's groups, renamed, made visible or emptied there, and the
// local groups this server keeps.
public sealed partial class AccountsController
{
    /// <summary>Read every favorite from VRChat again.</summary>
    /// <response code="204">Refreshed</response>
    /// <response code="409">No live session</response>
    /// <response code="502">VRChat refused</response>
    [HttpPost("{accountId}/favorites/refresh")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> RefreshFavorites([FromRoute] string accountId, CancellationToken cancellationToken) =>
        RunAsync(accountId, _ => _sync.SyncFavoritesAsync(accountId, cancellationToken));

    /// <summary>Rename a VRChat favorite group or change its visibility.</summary>
    /// <response code="204">Saved</response>
    /// <response code="409">No live session</response>
    /// <response code="502">VRChat refused</response>
    [HttpPut("{accountId}/favorites/groups/{type}/{name}")]
    [Consumes(MediaTypeNames.Application.Json)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> SaveFavoriteGroup([FromRoute] string accountId, [FromRoute] string type, [FromRoute] string name, [FromBody] SaveFavoriteGroupBody body, CancellationToken cancellationToken) =>
        RunAsync(accountId, async client =>
        {
            var saved = await client.SaveFavoriteGroupAsync(type, name, accountId, body.DisplayName?.Trim(), body.Visibility, cancellationToken);
            var row = await _db.FavoriteGroups.FirstOrDefaultAsync(g => g.OwnerUserId == accountId && g.Type == type && g.Name == name, cancellationToken);
            if (row is null)
            {
                row = new FavoriteGroup { Id = saved.Id, OwnerUserId = accountId, Type = type, Name = name };
                _db.FavoriteGroups.Add(row);
            }
            row.DisplayName = saved.DisplayName ?? body.DisplayName ?? row.DisplayName;
            row.Visibility = saved.Visibility ?? body.Visibility ?? row.Visibility;
            row.FetchedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
        });

    /// <summary>Empty a VRChat favorite group, then read the favorites again.</summary>
    /// <response code="204">Emptied</response>
    /// <response code="409">No live session</response>
    /// <response code="502">VRChat refused</response>
    [HttpDelete("{accountId}/favorites/groups/{type}/{name}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> ClearFavoriteGroup([FromRoute] string accountId, [FromRoute] string type, [FromRoute] string name, CancellationToken cancellationToken) =>
        RunAsync(accountId, async client =>
        {
            await client.ClearFavoriteGroupAsync(type, name, accountId, cancellationToken);
            await _sync.SyncFavoritesAsync(accountId, cancellationToken);
        });

    /// <summary>The local groups of one kind, empty ones included, with how many each holds.</summary>
    /// <response code="200">The groups</response>
    [HttpGet("{accountId}/favorites/local/groups")]
    [ProducesResponseType<IReadOnlyList<LocalFavoriteGroupResponse>>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> ListLocalFavoriteGroups([FromRoute] string accountId, [FromQuery] FavoriteType type, CancellationToken cancellationToken) =>
        Ok(await LocalGroupsAsync(accountId, type, null, cancellationToken));

    /// <summary>Make an empty local group.</summary>
    /// <response code="204">Made, or it already existed</response>
    /// <response code="404">No such account</response>
    [HttpPost("{accountId}/favorites/local/groups")]
    [Consumes(MediaTypeNames.Application.Json)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> CreateLocalFavoriteGroup([FromRoute] string accountId, [FromBody] LocalFavoriteGroupBody body, CancellationToken cancellationToken)
    {
        if (!await _db.Accounts.AnyAsync(a => a.UserId == accountId, cancellationToken)) return Problem(AccountError.AccountNotFound);
        var name = body.Name.Trim();
        if (name.Length > 0 && !await _db.LocalFavoriteGroups.AnyAsync(g => g.OwnerUserId == accountId && g.Type == body.Type && g.Name == name, cancellationToken))
        {
            _db.LocalFavoriteGroups.Add(new LocalFavoriteGroup { OwnerUserId = accountId, Type = body.Type, Name = name, CreatedAt = DateTimeOffset.UtcNow });
            await _db.SaveChangesAsync(cancellationToken);
        }
        return NoContent();
    }

    /// <summary>Rename a local group and move its favorites with it.</summary>
    /// <response code="204">Renamed</response>
    [HttpPut("{accountId}/favorites/local/groups")]
    [Consumes(MediaTypeNames.Application.Json)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> RenameLocalFavoriteGroup([FromRoute] string accountId, [FromBody] RenameLocalFavoriteGroupBody body, CancellationToken cancellationToken)
    {
        var newName = body.NewName.Trim();
        if (newName.Length == 0 || newName == body.Name) return NoContent();
        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);
        var moving = await _db.LocalFavorites.Where(f => f.OwnerUserId == accountId && f.Type == body.Type && f.GroupName == body.Name).ToListAsync(cancellationToken);
        var present = (await _db.LocalFavorites.Where(f => f.OwnerUserId == accountId && f.Type == body.Type && f.GroupName == newName).Select(f => f.TargetId).ToListAsync(cancellationToken)).ToHashSet();
        foreach (var favorite in moving.Where(f => !present.Contains(f.TargetId)))
            _db.LocalFavorites.Add(new LocalFavorite { OwnerUserId = accountId, Type = body.Type, GroupName = newName, TargetId = favorite.TargetId, CreatedAt = favorite.CreatedAt });
        _db.LocalFavorites.RemoveRange(moving);
        await _db.LocalFavoriteGroups.Where(g => g.OwnerUserId == accountId && g.Type == body.Type && g.Name == body.Name).ExecuteDeleteAsync(cancellationToken);
        if (!await _db.LocalFavoriteGroups.AnyAsync(g => g.OwnerUserId == accountId && g.Type == body.Type && g.Name == newName, cancellationToken))
            _db.LocalFavoriteGroups.Add(new LocalFavoriteGroup { OwnerUserId = accountId, Type = body.Type, Name = newName, CreatedAt = DateTimeOffset.UtcNow });
        if (body.Type == FavoriteType.Friend) await RenameFavoriteFriendGroupAsync(accountId, body.Name, newName, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return NoContent();
    }

    /// <summary>Delete a local group and what is in it, as the original's delete does.</summary>
    /// <response code="204">Deleted</response>
    [HttpDelete("{accountId}/favorites/local/groups")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> DeleteLocalFavoriteGroup([FromRoute] string accountId, [FromQuery] FavoriteType type, [FromQuery] string name, CancellationToken cancellationToken)
    {
        await _db.LocalFavorites.Where(f => f.OwnerUserId == accountId && f.Type == type && f.GroupName == name).ExecuteDeleteAsync(cancellationToken);
        await _db.LocalFavoriteGroups.Where(g => g.OwnerUserId == accountId && g.Type == type && g.Name == name).ExecuteDeleteAsync(cancellationToken);
        if (type == FavoriteType.Friend)
        {
            await RenameFavoriteFriendGroupAsync(accountId, name, null, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
        }
        return NoContent();
    }

    /// <summary>The original's favoriteCoordinator keeps the Favorite Friends Groups setting in step when a local group is renamed or deleted.</summary>
    private async Task RenameFavoriteFriendGroupAsync(string accountId, string name, string? newName, CancellationToken ct)
    {
        var account = await _db.Accounts.FirstOrDefaultAsync(a => a.UserId == accountId, ct);
        var key = VRCX.Common.Services.FavoriteFriends.LocalPrefix + name;
        if (account is null || !account.Settings.FavoriteFriendGroups.Contains(key)) return;
        var groups = account.Settings.FavoriteFriendGroups.Where(g => g != key).ToList();
        if (newName is not null) groups.Insert(Array.IndexOf(account.Settings.FavoriteFriendGroups, key), VRCX.Common.Services.FavoriteFriends.LocalPrefix + newName);
        account.Settings = account.Settings with { FavoriteFriendGroups = groups.Distinct().ToArray() };
    }

    private async Task<List<LocalFavoriteGroupResponse>> LocalGroupsAsync(string accountId, FavoriteType type, string? targetId, CancellationToken ct)
    {
        var used = await _db.LocalFavorites.AsNoTracking()
            .Where(f => f.OwnerUserId == accountId && f.Type == type)
            .GroupBy(f => f.GroupName)
            .Select(g => new LocalFavoriteGroupResponse { Name = g.Key, Count = g.Count(), Contains = targetId != null && g.Any(f => f.TargetId == targetId) })
            .ToListAsync(ct);
        var made = await _db.LocalFavoriteGroups.AsNoTracking().Where(g => g.OwnerUserId == accountId && g.Type == type).Select(g => g.Name).ToListAsync(ct);
        var names = used.Select(g => g.Name).ToHashSet();
        return used.Concat(made.Where(n => !names.Contains(n)).Select(n => new LocalFavoriteGroupResponse { Name = n, Count = 0, Contains = false }))
            .OrderBy(g => g.Name, StringComparer.Ordinal).ToList();
    }
}
