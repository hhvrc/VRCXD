using System.Net.Mime;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using OpenShock.Internal.Common.Problems;
using VRCX.Common.Live;
using VRCX.Common.Services.Live;

namespace VRCX.API.Controller.Accounts;

/// <summary>One wrist overlay row: the time as the page shows it, the name and what happened.</summary>
public sealed record AgentWristRow(string Time, string Name, string Text, bool Friend, bool Favorite, string? Colour = null);

/// <summary>
/// The wrist overlay's feed (the original's wristFeedUpdate), newest first, with its bottom bar: the
/// player count, when the game joined the instance and started (Unix ms, 0 when hidden), whether the
/// PC adds its uptime and CPU load, minimalFeed, the friends online and the labels.
/// </summary>
public sealed record AgentWristFeedBody(
    IReadOnlyList<AgentWristRow> Rows,
    bool Background,
    bool HideDevices,
    string? Status,
    long LocationSinceMs = 0,
    long OnlineSinceMs = 0,
    bool PcUptime = false,
    bool CpuUsage = false,
    bool Minimal = true,
    int OnlineFriends = 0,
    string? TimerLabel = null,
    string? PlayersLabel = null,
    string? CpuLabel = null,
    string? OnlineLabel = null,
    bool Hour12 = false);

public sealed partial class AccountsController
{
    /// <summary>Hands the wrist feed to the account's PC, whose SteamVR wrist overlay draws it.</summary>
    /// <response code="204">Sent</response>
    /// <response code="409">No PC that can</response>
    [HttpPost("{accountId}/agent/wrist-feed")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> AgentWristFeed([FromRoute] string accountId, [FromBody] AgentWristFeedBody body, [FromServices] DesktopClients desktops, CancellationToken cancellationToken)
    {
        var feed = new WristFeed
        {
            Background = body.Background, HideDevices = body.HideDevices, Status = body.Status ?? "",
            LocationSinceMs = body.LocationSinceMs, OnlineSinceMs = body.OnlineSinceMs, PcUptime = body.PcUptime, CpuUsage = body.CpuUsage,
            Minimal = body.Minimal, OnlineFriends = body.OnlineFriends,
            TimerLabel = body.TimerLabel ?? "", PlayersLabel = body.PlayersLabel ?? "", CpuLabel = body.CpuLabel ?? "", OnlineLabel = body.OnlineLabel ?? "", Hour12 = body.Hour12,
        };
        // The original's maxEntries.
        feed.Rows.AddRange(body.Rows.Take(25).Select(r => new WristRow { Time = r.Time, Name = r.Name, Text = r.Text, Friend = r.Friend, Favorite = r.Favorite, Colour = r.Colour ?? "" }));
        return AgentActionAsync(accountId, Capability.Openvr, desktops, new ClientAction { UpdateWristFeed = feed }, cancellationToken);
    }
}
