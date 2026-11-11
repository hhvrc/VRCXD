using System.Net.Mime;
using System.Text.RegularExpressions;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenShock.Internal.Common.Problems;
using VRCX.API.Models.Response;
using VRCX.Common.Errors;
using VRCX.Common.Live;
using VRCX.Common.Services.Live;

namespace VRCX.API.Controller.Accounts;

public sealed record LaunchGameBody(string Url, bool DesktopMode, bool AllowSecondInstance);

public sealed record OpenInGameBody(string Url);

/// <summary>The instance the account's game is in by its game log, and who is there (the original's lastLocation.friendList).</summary>
public sealed record GameInstanceResponse(string? Location, IReadOnlyList<string> PlayerIds, DateTimeOffset? JoinedAt = null);

// The original's launchGame and tryOpenInstanceInVrc. The game runs where the desktop app runs,
// so these ask that app to do it over its live socket.
public sealed partial class AccountsController
{
    [GeneratedRegex(@"^vrchat://launch\?ref=vrcx\.app&id=[A-Za-z0-9_:~().\-]+(&shortName=[A-Za-z0-9_\-]+)?$")]
    private static partial Regex LaunchUrlPattern();

    /// <summary>Whether a desktop app for this account is connected, and whether VRChat is running there.</summary>
    /// <response code="200">The state</response>
    /// <summary>
    /// The game's current instance by the desktop app's game log, and everyone in it. The sidebar puts
    /// friends whose location VRChat hides under Same Instance with it, as the original does (upstream PR 1138).
    /// </summary>
    /// <response code="200">The instance, or a null location when the game is not in one</response>
    [HttpGet("{accountId}/game/instance")]
    [ProducesResponseType<GameInstanceResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> GetGameInstance([FromRoute] string accountId, [FromServices] VRCX.Common.Services.Automation.AccountAutomation automation, CancellationToken cancellationToken)
    {
        var location = await automation.GameLocationAsync(accountId, cancellationToken);
        if (location is null) return Ok(new GameInstanceResponse(null, []));
        var ids = await _db.InstanceVisits.AsNoTracking()
            .Where(v => v.InstanceLocation == location && v.LeftAt == null && v.Source == VRCX.Common.VrcxDb.VisitSource.GameLog && v.UserId != accountId)
            .Select(v => v.UserId).Distinct().ToListAsync(cancellationToken);
        // The original's lastLocation.date: when the game joined, for the wrist overlay's timer.
        var joinedAt = await _db.InstanceVisits.AsNoTracking()
            .Where(v => v.InstanceLocation == location && v.LeftAt == null && v.Source == VRCX.Common.VrcxDb.VisitSource.GameLog && v.UserId == accountId)
            .OrderByDescending(v => v.JoinedAt).Select(v => (DateTimeOffset?)v.JoinedAt).FirstOrDefaultAsync(cancellationToken);
        return Ok(new GameInstanceResponse(location, ids, joinedAt));
    }

    [HttpGet("{accountId}/desktop")]
    [ProducesResponseType<DesktopStatusResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> GetDesktopStatus([FromRoute] string accountId, [FromServices] DesktopClients desktops, CancellationToken cancellationToken)
    {
        var desktop = await FindDesktopAsync(accountId, desktops, cancellationToken);
        // The session times of the PC this account played on last (connected or not).
        var times = await _db.GameLogClients.AsNoTracking()
            .Where(c => c.AccountId == accountId || (desktop != null && c.ClientId == desktop.ClientId))
            .OrderByDescending(c => desktop != null && c.ClientId == desktop.ClientId).ThenByDescending(c => c.LastSeenAt)
            .Select(c => new { c.GameStartedAt, c.LastGameStartedAt, c.LastGameStoppedAt, c.GameNoVr })
            .FirstOrDefaultAsync(cancellationToken);
        var running = desktop?.GameRunning ?? false;
        return Ok(new DesktopStatusResponse
        {
            Connected = desktop is not null, ClientId = desktop?.ClientId, ClientName = desktop?.ClientName, GameRunning = running,
            GameStartedAt = running ? times?.GameStartedAt : null,
            LastGameStartedAt = times?.LastGameStartedAt, LastGameStoppedAt = times?.LastGameStoppedAt,
            GameNoVr = times?.GameNoVr ?? false,
            SteamVrRunning = desktop?.SteamVrRunning ?? false,
            HmdAfk = desktop?.HmdAfk ?? false,
            Capabilities = desktop?.Capabilities ?? [],
            Os = desktop?.Os,
            Folders = desktop?.Folders is { } f ? new DesktopFolders(f.AppData, f.Photos, f.Screenshots, f.Cache, f.UgcPhotos, f.CrashDumps) : null,
        });
    }

    /// <summary>Starts VRChat on this account's PC.</summary>
    /// <response code="204">Started</response>
    /// <response code="409">No desktop app is connected, or the game is running and a second copy was not asked for</response>
    /// <response code="502">The desktop app could not start it</response>
    /// <response code="504">The desktop app did not answer</response>
    [HttpPost("{accountId}/game/launch")]
    [Consumes(MediaTypeNames.Application.Json)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status400BadRequest, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status504GatewayTimeout, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> LaunchGame([FromRoute] string accountId, [FromBody] LaunchGameBody body, [FromServices] DesktopClients desktops, CancellationToken cancellationToken) =>
        DesktopActionAsync(accountId, body.Url, desktops, new ClientAction { LaunchVrchat = new LaunchVrchat { LaunchUrl = body.Url, DesktopMode = body.DesktopMode, AllowSecondInstance = body.AllowSecondInstance } }, cancellationToken);

    /// <summary>Opens an instance in the VRChat already running on this account's PC.</summary>
    /// <response code="204">VRChat took it</response>
    /// <response code="409">No desktop app is connected</response>
    /// <response code="502">VRChat did not take it</response>
    /// <response code="504">The desktop app did not answer</response>
    [HttpPost("{accountId}/game/open")]
    [Consumes(MediaTypeNames.Application.Json)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status400BadRequest, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status504GatewayTimeout, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> OpenInGame([FromRoute] string accountId, [FromBody] OpenInGameBody body, [FromServices] DesktopClients desktops, CancellationToken cancellationToken) =>
        DesktopActionAsync(accountId, body.Url, desktops, new ClientAction { OpenInstance = new OpenInstanceInGame { LaunchUrl = body.Url } }, cancellationToken);

    private async Task<IActionResult> DesktopActionAsync(string accountId, string url, DesktopClients desktops, ClientAction action, CancellationToken ct)
    {
        if (!LaunchUrlPattern().IsMatch(url)) return Problem(DesktopError.InvalidLaunchUrl);
        if (await FindDesktopAsync(accountId, desktops, ct) is not { } desktop) return Problem(DesktopError.NotConnected);

        var result = await desktops.RequestAsync(desktop.ClientId, action, ct);
        return result?.Status switch
        {
            null => Problem(DesktopError.NoAnswer),
            ActionStatus.Ok => NoContent(),
            ActionStatus.GameRunning => Problem(DesktopError.GameRunning),
            _ => Problem(DesktopError.ActionFailed(result.Error)),
        };
    }

    /// <summary>The PC this account last played on, if connected; else a connected app whose log has not named an account yet.</summary>
    private async Task<DesktopClient?> FindDesktopAsync(string accountId, DesktopClients desktops, CancellationToken ct)
    {
        var connected = desktops.ConnectedIds;
        if (connected.Count == 0) return null;
        var known = await _db.GameLogClients.AsNoTracking()
            .Where(c => connected.Contains(c.ClientId))
            .OrderByDescending(c => c.LastSeenAt)
            .Select(c => new { c.ClientId, c.AccountId })
            .ToListAsync(ct);
        var clientId = known.FirstOrDefault(c => c.AccountId == accountId)?.ClientId
            ?? known.FirstOrDefault(c => c.AccountId is null)?.ClientId
            ?? connected.FirstOrDefault(id => known.All(c => c.ClientId != id));
        return clientId is null ? null : desktops.Get(clientId);
    }
}
