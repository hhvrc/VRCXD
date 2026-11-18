using System.Net.Mime;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VRCX.Common.Problems;
using VRCX.Common;
using VRCX.Common.Authentication;
using VRCX.Common.Errors;
using VRCX.Common.Features.Accounts;
using VRCX.Common.Features.Favorites;
using VRCX.Common.VrcxDb;

namespace VRCX.API.Controller.Favorites;

public sealed record AddFavoriteBody(string Type, string TargetId, string Group);

public sealed record LocalFavoriteBody(FavoriteType Type, string TargetId, string Group);

public sealed record SaveFavoriteGroupBody(string? DisplayName, string? Visibility);

public sealed record LocalFavoriteGroupBody(FavoriteType Type, string Name);

public sealed record RenameLocalFavoriteGroupBody(FavoriteType Type, string Name, string NewName);

/// <summary>
/// The account's favorites: the lists, the original's ChooseFavoriteGroupDialog (VRChat's favorite
/// groups, and the local ones only VRCX knows of) and the Favorites page's group panel. Everything
/// is done by Common/Features/Favorites; the routes stay under /accounts, where they were while
/// they were part of the accounts controller.
/// </summary>
[ApiController]
[Tags("Accounts")]
[ApiVersion("1")]
[Route("/{version:apiVersion}/accounts")]
[Authorize(AuthenticationSchemes = VrcxAuthSchemes.ApiToken)]
public sealed class FavoritesController(AccountQueries accounts, FavoriteQueries queries, FavoriteActions actions) : VrcxControllerBase
{
    /// <summary>The account's VRChat favorites of one kind, with their groups.</summary>
    /// <param name="accountId"></param>
    /// <param name="type">friend, world or avatar</param>
    /// <param name="cancellationToken"></param>
    /// <response code="200">Groups and favorites of that kind</response>
    /// <response code="404">No such account</response>
    [HttpGet("{accountId}/favorites")]
    [ProducesResponseType<FavoritesResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<VrcxProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> ListFavorites([FromRoute] string accountId, [FromQuery] FavoriteType type, CancellationToken cancellationToken)
    {
        if (!await accounts.ExistsAsync(accountId, cancellationToken)) return Problem(AccountError.AccountNotFound);
        return Ok(await queries.FavoritesAsync(accountId, type, cancellationToken));
    }

    /// <summary>Whether something is a favorite at all, on VRChat or locally.</summary>
    /// <response code="200">True or false</response>
    [HttpGet("{accountId}/favorites/status")]
    [ProducesResponseType<bool>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> GetFavoriteStatus([FromRoute] string accountId, [FromQuery] string targetId, CancellationToken cancellationToken) =>
        Ok(await queries.IsFavoriteAsync(accountId, targetId, cancellationToken));

    /// <summary>What the favorite dialog offers for one thing: every group slot with its count, and the local groups.</summary>
    /// <response code="200">The choices</response>
    /// <response code="409">No live session</response>
    [HttpGet("{accountId}/favorites/picker")]
    [ProducesResponseType<FavoritePickerResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<VrcxProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<VrcxProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> GetFavoritePicker([FromRoute] string accountId, [FromQuery] FavoriteType type, [FromQuery] string? targetId, CancellationToken cancellationToken) =>
        Answer(await actions.PickerAsync(accountId, type, targetId, cancellationToken));

    /// <summary>Add a favorite to one of VRChat's groups.</summary>
    /// <response code="204">Added</response>
    /// <response code="409">No live session</response>
    /// <response code="502">VRChat refused</response>
    [HttpPost("{accountId}/favorites")]
    [Consumes(MediaTypeNames.Application.Json)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<VrcxProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<VrcxProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> AddFavorite([FromRoute] string accountId, [FromBody] AddFavoriteBody body, CancellationToken cancellationToken) =>
        Done(await actions.AddAsync(accountId, body.Type, body.TargetId, body.Group, cancellationToken));

    /// <summary>Take something out of VRChat's favorites.</summary>
    /// <response code="204">Removed</response>
    /// <response code="409">No live session</response>
    /// <response code="502">VRChat refused</response>
    [HttpDelete("{accountId}/favorites/{targetId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<VrcxProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<VrcxProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> RemoveFavorite([FromRoute] string accountId, [FromRoute] string targetId, CancellationToken cancellationToken) =>
        Done(await actions.RemoveAsync(accountId, targetId, cancellationToken));

    /// <summary>
    /// How many players are in each favorite world right now, for the original's Players sort (upstream
    /// issue 674): VRChat's favorite worlds in one call, plus any of <paramref name="ids"/> (local
    /// favorites, at most 100) fetched one by one. Momentary counts, so nothing is stored.
    /// </summary>
    /// <response code="200">World id to occupants</response>
    /// <response code="409">No live session</response>
    [HttpGet("{accountId}/favorites/world-occupants")]
    [ProducesResponseType<Dictionary<string, int>>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<VrcxProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<VrcxProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> GetFavoriteWorldOccupants([FromRoute] string accountId, [FromQuery] string[]? ids, CancellationToken cancellationToken) =>
        Answer(await actions.WorldOccupantsAsync(accountId, ids, cancellationToken));

    /// <summary>Everything in the local favorite groups of one kind.</summary>
    /// <response code="200">The favorites</response>
    [HttpGet("{accountId}/favorites/local")]
    [ProducesResponseType<IReadOnlyList<LocalFavoriteResponse>>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> ListLocalFavorites([FromRoute] string accountId, [FromQuery] FavoriteType type, CancellationToken cancellationToken) =>
        Ok(await queries.LocalFavoritesAsync(accountId, type, cancellationToken));

    /// <summary>Put something in a local group, which only this server knows about. The group appears when first used.</summary>
    /// <response code="204">Added</response>
    /// <response code="404">No such account</response>
    [HttpPost("{accountId}/favorites/local")]
    [Consumes(MediaTypeNames.Application.Json)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<VrcxProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> AddLocalFavorite([FromRoute] string accountId, [FromBody] LocalFavoriteBody body, CancellationToken cancellationToken)
    {
        if (!await accounts.ExistsAsync(accountId, cancellationToken)) return Problem(AccountError.AccountNotFound);
        await actions.AddLocalAsync(accountId, body.Type, body.TargetId, body.Group, cancellationToken);
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
    [ProducesResponseType<VrcxProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> RefreshLocalFavorite([FromRoute] string accountId, [FromQuery] FavoriteType type, [FromQuery] string targetId, CancellationToken cancellationToken) =>
        Answer(await actions.RefreshLocalAsync(accountId, type, targetId, cancellationToken));

    /// <summary>Take something out of a local group.</summary>
    /// <response code="204">Removed</response>
    [HttpDelete("{accountId}/favorites/local")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> RemoveLocalFavorite([FromRoute] string accountId, [FromQuery] FavoriteType type, [FromQuery] string targetId, [FromQuery] string group, CancellationToken cancellationToken)
    {
        await actions.RemoveLocalAsync(accountId, type, targetId, group, cancellationToken);
        return NoContent();
    }

    /// <summary>Read every favorite from VRChat again.</summary>
    /// <response code="204">Refreshed</response>
    /// <response code="409">No live session</response>
    /// <response code="502">VRChat refused</response>
    [HttpPost("{accountId}/favorites/refresh")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<VrcxProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<VrcxProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> RefreshFavorites([FromRoute] string accountId, CancellationToken cancellationToken) =>
        Done(await actions.RefreshAsync(accountId, cancellationToken));

    /// <summary>Rename a VRChat favorite group or change its visibility.</summary>
    /// <response code="204">Saved</response>
    /// <response code="409">No live session</response>
    /// <response code="502">VRChat refused</response>
    [HttpPut("{accountId}/favorites/groups/{type}/{name}")]
    [Consumes(MediaTypeNames.Application.Json)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<VrcxProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<VrcxProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> SaveFavoriteGroup([FromRoute] string accountId, [FromRoute] string type, [FromRoute] string name, [FromBody] SaveFavoriteGroupBody body, CancellationToken cancellationToken) =>
        Done(await actions.SaveGroupAsync(accountId, type, name, body.DisplayName, body.Visibility, cancellationToken));

    /// <summary>Empty a VRChat favorite group, then read the favorites again.</summary>
    /// <response code="204">Emptied</response>
    /// <response code="409">No live session</response>
    /// <response code="502">VRChat refused</response>
    [HttpDelete("{accountId}/favorites/groups/{type}/{name}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<VrcxProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<VrcxProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> ClearFavoriteGroup([FromRoute] string accountId, [FromRoute] string type, [FromRoute] string name, CancellationToken cancellationToken) =>
        Done(await actions.ClearGroupAsync(accountId, type, name, cancellationToken));

    /// <summary>The local groups of one kind, empty ones included, with how many each holds.</summary>
    /// <response code="200">The groups</response>
    [HttpGet("{accountId}/favorites/local/groups")]
    [ProducesResponseType<IReadOnlyList<LocalFavoriteGroupResponse>>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> ListLocalFavoriteGroups([FromRoute] string accountId, [FromQuery] FavoriteType type, CancellationToken cancellationToken) =>
        Ok(await queries.LocalGroupsAsync(accountId, type, null, cancellationToken));

    /// <summary>Make an empty local group.</summary>
    /// <response code="204">Made, or it already existed</response>
    /// <response code="404">No such account</response>
    [HttpPost("{accountId}/favorites/local/groups")]
    [Consumes(MediaTypeNames.Application.Json)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<VrcxProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> CreateLocalFavoriteGroup([FromRoute] string accountId, [FromBody] LocalFavoriteGroupBody body, CancellationToken cancellationToken)
    {
        if (!await accounts.ExistsAsync(accountId, cancellationToken)) return Problem(AccountError.AccountNotFound);
        await actions.CreateLocalGroupAsync(accountId, body.Type, body.Name, cancellationToken);
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
        await actions.RenameLocalGroupAsync(accountId, body.Type, body.Name, body.NewName, cancellationToken);
        return NoContent();
    }

    /// <summary>Delete a local group and what is in it, as the original's delete does.</summary>
    /// <response code="204">Deleted</response>
    [HttpDelete("{accountId}/favorites/local/groups")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> DeleteLocalFavoriteGroup([FromRoute] string accountId, [FromQuery] FavoriteType type, [FromQuery] string name, CancellationToken cancellationToken)
    {
        await actions.DeleteLocalGroupAsync(accountId, type, name, cancellationToken);
        return NoContent();
    }

    private IActionResult Done(VrcxProblem? problem) => problem is null ? NoContent() : Problem(problem);

}
