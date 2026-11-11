using System.Net.Mime;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenShock.Internal.Common.Problems;
using VRCX.Common.Errors;
using VRCX.Vrchat;
using VRCX.Vrchat.Models;

namespace VRCX.API.Controller.Accounts;

public sealed class CalendarGroup
{
    public string? Name { get; init; }
    public string? BannerUrl { get; init; }
}

/// <summary>A month of events, with the name and banner of each group that owns one.</summary>
public sealed class CalendarResponse
{
    public required IReadOnlyList<CalendarEvent> Events { get; init; }
    public required Dictionary<string, CalendarGroup> Groups { get; init; }
}

public sealed class FollowCalendarEventBody
{
    public required bool IsFollowing { get; set; }
}

// The Tools page's calendar and info push: live from VRChat, as in the original.
public sealed partial class AccountsController
{
    /// <summary>Group events for the month around a date.</summary>
    /// <param name="accountId">The account</param>
    /// <param name="scope">all (the account's groups), following or featured</param>
    /// <param name="date">The date VRChat picks the month from, in the viewer's local time</param>
    /// <param name="cancellationToken">Cancelled with the request</param>
    /// <response code="200">The events and their groups</response>
    /// <response code="409">No live session</response>
    [HttpGet("{accountId}/calendar/{scope:regex(^(all|following|featured)$)}")]
    [ProducesResponseType<CalendarResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> ListCalendarEvents([FromRoute] string accountId, [FromRoute] string scope, [FromQuery] string date, CancellationToken cancellationToken) =>
        ReadLiveAsync(accountId, async client =>
        {
            var events = await client.GetAllCalendarEventsAsync(scope == "all" ? "calendar" : $"calendar/{scope}", date, cancellationToken);
            var ownerIds = events.Select(e => e.OwnerId).Where(id => id.Length > 0).Distinct().ToList();
            var known = await _db.Groups.AsNoTracking().Where(g => ownerIds.Contains(g.Id) && !g.IsStub).ToDictionaryAsync(g => g.Id, g => new CalendarGroup { Name = g.Name, BannerUrl = g.BannerUrl }, cancellationToken);
            foreach (var missing in ownerIds.Where(id => !known.ContainsKey(id)))
            {
                try
                {
                    if (await _sync.RefreshGroupAsync(accountId, missing, cancellationToken) is { } group)
                        known[missing] = new CalendarGroup { Name = group.Name, BannerUrl = group.BannerUrl };
                }
                catch (VrchatApiException ex)
                {
                    _logger.LogDebug(ex, "Group {Group} of a calendar event could not be fetched", missing);
                }
            }
            return new CalendarResponse { Events = events, Groups = known };
        });

    /// <summary>Follow or stop following an event.</summary>
    /// <response code="200">The event as VRChat now has it</response>
    /// <response code="409">No live session</response>
    [HttpPost("{accountId}/calendar/{groupId}/{eventId}/follow")]
    [ProducesResponseType<CalendarEvent>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> FollowCalendarEvent([FromRoute] string accountId, [FromRoute] string groupId, [FromRoute] string eventId, [FromBody] FollowCalendarEventBody body, CancellationToken cancellationToken) =>
        ReadLiveAsync(accountId, client => client.FollowCalendarEventAsync(groupId, eventId, body.IsFollowing, cancellationToken));

    /// <summary>The event as an iCalendar file.</summary>
    /// <response code="200">The .ics file</response>
    /// <response code="409">No live session</response>
    [HttpGet("{accountId}/calendar/{groupId}/{eventId}/ics")]
    [Produces("text/calendar")]
    [ProducesResponseType<string>(StatusCodes.Status200OK, "text/calendar")]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> GetCalendarEventIcs([FromRoute] string accountId, [FromRoute] string groupId, [FromRoute] string eventId, CancellationToken cancellationToken)
    {
        if (_sessions.Get(accountId) is not { } client) return Problem(AccountError.NoLiveSession);
        try
        {
            return Content(await client.GetCalendarEventIcsAsync(groupId, eventId, cancellationToken), "text/calendar");
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
    }

    /// <summary>Opens the event's .ics with the calendar app on the account's PC (the original's Export to Calendar, OpenCalendarFile).</summary>
    /// <response code="204">Opened</response>
    /// <response code="409">No live session, or no PC that can</response>
    [HttpPost("{accountId}/calendar/{groupId}/{eventId}/open-on-pc")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> OpenCalendarEventOnPc([FromRoute] string accountId, [FromRoute] string groupId, [FromRoute] string eventId, [FromServices] VRCX.Common.Services.Live.DesktopClients desktops, CancellationToken cancellationToken)
    {
        if (_sessions.Get(accountId) is not { } client) return Problem(AccountError.NoLiveSession);
        string ics;
        try
        {
            ics = await client.GetCalendarEventIcsAsync(groupId, eventId, cancellationToken);
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
        return await AgentActionAsync(accountId, VRCX.Common.Live.Capability.Calendar, desktops, new VRCX.Common.Live.ClientAction { OpenCalendarFile = new VRCX.Common.Live.OpenCalendarFile { Ics = ics } }, cancellationToken);
    }

    /// <summary>VRChat's info push items: help articles, quick menu banners and loading screens. The shape varies by item, so it is passed through.</summary>
    /// <response code="200">The items</response>
    /// <response code="409">No live session</response>
    [HttpGet("{accountId}/info-push")]
    [ProducesResponseType<System.Text.Json.JsonElement>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> ListInfoPush([FromRoute] string accountId, CancellationToken cancellationToken) =>
        ReadLiveAsync(accountId, client => client.GetInfoPushAsync(cancellationToken));
}
