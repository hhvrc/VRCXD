using System.Net.Mime;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenShock.Internal.Common.Problems;
using VRCX.Common.Errors;
using VRCX.Common.Features.Feed;
using VRCX.Common.VrcxDb;

namespace VRCX.API.Controller.Settings;

// The per-account app settings the original kept in its config: what the server should ask
// the desktop app to do with screenshots and a quitting game, and what to log.
public sealed record YouTubeKeyTestBody(string? ApiKey);
public sealed record YouTubeKeyTestResponse(bool Valid);

public sealed partial class AccountSettingsController
{
    /// <summary>The account's app settings.</summary>
    /// <response code="200">The settings</response>
    /// <response code="404">No such account</response>
    [HttpGet("{accountId}/settings")]
    [ProducesResponseType<AccountSettings>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> GetSettings([FromRoute] string accountId, CancellationToken cancellationToken)
    {
        return await settings.GetAsync(accountId, cancellationToken) is { } stored ? Ok(stored) : Problem(AccountError.AccountNotFound);
    }

    /// <summary>
    /// The original's YouTube API dialog test: looks up one known video with the key, whether or not
    /// the YouTube API is turned on (upstream PR 1450). Nothing is stored.
    /// </summary>
    /// <response code="200">Whether YouTube accepted the key</response>
    [HttpPost("{accountId}/settings/youtube-key-test")]
    [Consumes(MediaTypeNames.Application.Json)]
    [ProducesResponseType<YouTubeKeyTestResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> TestYouTubeKey([FromRoute] string accountId, [FromBody] YouTubeKeyTestBody body, [FromServices] VRCX.Common.Services.Live.YouTubeLookup youTube, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.ApiKey)) return Ok(new YouTubeKeyTestResponse(false));
        var video = await youTube.LookupAsync("dQw4w9WgXcQ", body.ApiKey.Trim(), cancellationToken);
        return Ok(new YouTubeKeyTestResponse(video is not null));
    }

    /// <summary>Replaces the account's app settings. Takes effect on the next log line the desktop app ships.</summary>
    /// <response code="200">The stored settings</response>
    /// <response code="404">No such account</response>
    [HttpPut("{accountId}/settings")]
    [ProducesResponseType<AccountSettings>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> PutSettings([FromRoute] string accountId, [FromBody] AccountSettings body, [FromServices] VRCX.Common.Services.Live.AgentSettingsPush agentSettings, [FromServices] VRCX.Common.Services.Live.DiscordPresenceService discordPresence, [FromServices] VRCX.Common.Services.Live.Photon.PhotonService photon, CancellationToken cancellationToken)
    {
        var saved = await settings.SaveAsync(accountId, body, cancellationToken);
        if (saved is null) return Problem(AccountError.AccountNotFound);
        await agentSettings.SendAsync(await desktop.FindAsync(accountId, cancellationToken), saved, cancellationToken);
        discordPresence.SettingsChanged(accountId);
        photon.SettingsChanged(accountId);
        return Ok(saved);
    }
}
