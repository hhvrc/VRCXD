using System.Text.Json;
using System.Net.Mime;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VRCX.Common.Problems;
using VRCX.API.Models.Response;
using VRCX.API.Services;
using VRCX.Common.Services.Ingestion;
using VRCX.Vrchat;
using VRCX.Vrchat.Models;
using VRCX.Common.Services;

namespace VRCX.API.Controller.Profile;

public sealed record SaveMeBody(string? Status, string? StatusDescription, string? Pronouns = null);

public sealed record SaveMeOptionsBody(bool? AllowAvatarCopying = null, bool? IsBoopingEnabled = null, bool? HasSharedConnectionsOptOut = null, bool? HasDiscordFriendsOptOut = null);

public sealed record SaveClientConfigBody(bool AccessReduceDecorAnim);

public sealed class MeResponse
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string Status { get; init; }
    public required string StatusDescription { get; init; }
    public required IReadOnlyList<string> StatusHistory { get; init; }
    public required bool IsBoopingEnabled { get; init; }
    public string? ImageUrl { get; init; }
    public string? Location { get; init; }
    public string? TravelingToLocation { get; init; }
    public string? Platform { get; init; }
    /// <summary>Where the game log puts the account now, when the game is running.</summary>
    public string? CurrentLocation { get; init; }
    /// <summary>VRC+ supporters can attach a picture to invites.</summary>
    public bool IsVrcPlus { get; init; }
    /// <summary>The account hides shared connections, so the user page asks for no mutual counts.</summary>
    public bool HasSharedConnectionsOptOut { get; init; }
    /// <summary>The account hides its Discord connections.</summary>
    public bool HasDiscordFriendsOptOut { get; init; }
    /// <summary>Others may clone the account's avatar.</summary>
    public bool AllowAvatarCopying { get; init; }
    /// <summary>The account's home world, which the Activity tab can leave out of most visited worlds.</summary>
    public string? HomeLocation { get; init; }
    /// <summary>The avatar others see when the current one cannot be shown, for "Show Fallback Avatar Details".</summary>
    public string? FallbackAvatar { get; init; }
    /// <summary>The avatar worn now, so Select Avatar can be disabled for it.</summary>
    public string? CurrentAvatar { get; init; }
    /// <summary>The icon frame around the account's picture, which the sidebar's own row shows.</summary>
    public string? IconFrame { get; init; }
    /// <summary>VRChat's account standing (good, or a warning), shown on the account's own page.</summary>
    public string? AccountStanding { get; init; }

    public static MeResponse From(CurrentUser u, string? currentLocation = null) => new()
    {
        Id = u.Id,
        DisplayName = u.DisplayName,
        Status = u.Status,
        StatusDescription = u.StatusDescription,
        StatusHistory = u.StatusHistory,
        IsBoopingEnabled = u.IsBoopingEnabled,
        ImageUrl = UserImage.Pick(string.IsNullOrEmpty(u.IconUrl) ? u.UserIcon : u.IconUrl, u.ProfilePicOverrideThumbnail, u.CurrentAvatarThumbnailImageUrl),
        Location = u.Location,
        TravelingToLocation = u.TravelingToLocation,
        Platform = u.Presence?.Platform ?? u.LastPlatform,
        CurrentLocation = currentLocation,
        IsVrcPlus = u.Tags.Contains("system_supporter"),
        HasSharedConnectionsOptOut = u.HasSharedConnectionsOptOut,
        HasDiscordFriendsOptOut = u.HasDiscordFriendsOptOut,
        AllowAvatarCopying = u.AllowAvatarCopying,
        HomeLocation = u.HomeLocation,
        FallbackAvatar = string.IsNullOrEmpty(u.FallbackAvatar) ? null : u.FallbackAvatar,
        CurrentAvatar = string.IsNullOrEmpty(u.CurrentAvatar) ? null : u.CurrentAvatar,
        IconFrame = string.IsNullOrEmpty(u.IconFrame) ? null : u.IconFrame,
        AccountStanding = string.IsNullOrEmpty(u.AccountStanding) ? null : u.AccountStanding,
    };
}

public sealed class GroupInstanceResponse
{
    public required string Location { get; init; }
    public required string GroupId { get; init; }
    public required string GroupName { get; init; }
    public string? GroupIconUrl { get; init; }
    public required int UserCount { get; init; }
    public required int Capacity { get; init; }
    public required bool AgeGate { get; init; }
}

// The original's right-hand sidebar: the account's own row with its status menu, and the
// Groups tab of open instances in the account's groups.
public sealed partial class AccountProfileController
{
    /// <summary>
    /// The account's own profile as VRChat has it, with its status history. It is the auth/user last
    /// read (by the friend sync, a login or an edit) and changed by the pipeline since, while that
    /// is under a minute old; older, or with refresh=true, it is read from VRChat first.
    /// </summary>
    /// <param name="accountId">usr_…</param>
    /// <param name="refresh">Read auth/user from VRChat whatever the age of the stored copy</param>
    /// <param name="presence">Where the account is</param>
    /// <param name="automation">Where the account's game is</param>
    /// <param name="currentUsers">The stored copy</param>
    /// <param name="cancellationToken"></param>
    /// <response code="200">The profile</response>
    /// <response code="409">No live session</response>
    [HttpGet("{accountId}/me")]
    [ProducesResponseType<MeResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<VrcxProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> GetMe([FromRoute] string accountId, [FromQuery] bool refresh, [FromServices] VRCX.Common.Services.Presence.IPresenceCache presence, [FromServices] VRCX.Common.Services.Automation.AccountAutomation automation, [FromServices] VRCX.Common.Services.Sessions.CurrentUserCache currentUsers, CancellationToken cancellationToken)
    {
        // The original's runUpdateCurrentUserLocationFlow: while the game runs, its log says where the
        // account is, ahead of what VRChat reports.
        async Task<string?> CurrentLocationAsync()
        {
            var game = await automation.GameLocationAsync(accountId, cancellationToken);
            var own = await presence.GetManyAsync([accountId], cancellationToken);
            return game ?? (own.TryGetValue(accountId, out var mine) ? mine.CurrentInstanceLocation : null);
        }

        if (!refresh && Runner.HasSession(accountId) && currentUsers.Get(accountId) is { } stored)
            return Ok(MeResponse.From(stored, await CurrentLocationAsync()));

        return await FetchAsync(accountId, async client =>
        {
            var me = await client.GetCurrentUserAsync(cancellationToken);
            await profileStore.StoreAsync(me, cancellationToken);
            return MeResponse.From(me, await CurrentLocationAsync());
        });
    }

    /// <summary>Change the account's status, status text or pronouns on VRChat.</summary>
    /// <response code="200">The profile after the change</response>
    /// <response code="409">No live session</response>
    [HttpPut("{accountId}/me")]
    [Consumes(MediaTypeNames.Application.Json)]
    [ProducesResponseType<MeResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<VrcxProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> SaveMe([FromRoute] string accountId, [FromBody] SaveMeBody body, CancellationToken cancellationToken) =>
        FetchAsync(accountId, async client =>
        {
            var me = await client.SaveCurrentUserAsync(accountId, body.Status, body.StatusDescription, body.Pronouns, cancellationToken);
            await profileStore.StoreAsync(me, cancellationToken);
            return MeResponse.From(me);
        });

    /// <summary>
    /// Turn the account's own options on or off, as the original's user page toggles do (Avatar
    /// Cloning, Booping, Show Mutual Friends, Show Discord Connections). Only the fields given change.
    /// </summary>
    /// <response code="200">The profile after the change</response>
    /// <response code="409">No live session</response>
    [HttpPut("{accountId}/me/options")]
    [Consumes(MediaTypeNames.Application.Json)]
    [ProducesResponseType<MeResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<VrcxProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> SaveMeOptions([FromRoute] string accountId, [FromBody] SaveMeOptionsBody body, CancellationToken cancellationToken) =>
        FetchAsync(accountId, async client =>
        {
            var changes = new Dictionary<string, bool>();
            if (body.AllowAvatarCopying is { } copying) changes["allowAvatarCopying"] = copying;
            if (body.IsBoopingEnabled is { } booping) changes["isBoopingEnabled"] = booping;
            if (body.HasSharedConnectionsOptOut is { } shared) changes["hasSharedConnectionsOptOut"] = shared;
            if (body.HasDiscordFriendsOptOut is { } discord) changes["hasDiscordFriendsOptOut"] = discord;
            var me = changes.Count > 0 ? await client.UpdateCurrentUserFlagsAsync(accountId, changes, cancellationToken) : await client.GetCurrentUserAsync(cancellationToken);
            await profileStore.StoreAsync(me, cancellationToken);
            return MeResponse.From(me);
        });

    /// <summary>The account's VRChat client options, for Reduced Profile Animations (accessReduceDecorAnim).</summary>
    /// <response code="200">VRChat's clientConfig</response>
    /// <response code="409">No live session</response>
    [HttpGet("{accountId}/client-config")]
    [ProducesResponseType<JsonElement>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<VrcxProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> GetClientConfig([FromRoute] string accountId, CancellationToken cancellationToken) =>
        FetchAsync(accountId, async client => await client.GetClientConfigAsync(accountId, cancellationToken));

    /// <summary>Turn Reduced Profile Animations on or off.</summary>
    /// <response code="200">VRChat's clientConfig after the change</response>
    /// <response code="409">No live session</response>
    [HttpPut("{accountId}/client-config")]
    [Consumes(MediaTypeNames.Application.Json)]
    [ProducesResponseType<JsonElement>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<VrcxProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> SaveClientConfig([FromRoute] string accountId, [FromBody] SaveClientConfigBody body, CancellationToken cancellationToken) =>
        FetchAsync(accountId, async client => await client.SaveClientConfigAsync(accountId, new Dictionary<string, bool> { ["accessReduceDecorAnim"] = body.AccessReduceDecorAnim }, cancellationToken));

    /// <summary>Open instances in the account's groups, as the Groups tab lists them.</summary>
    /// <response code="200">The instances</response>
    /// <response code="409">No live session</response>
    [HttpGet("{accountId}/group-instances")]
    [ProducesResponseType<IReadOnlyList<GroupInstanceResponse>>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<VrcxProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> ListGroupInstances([FromRoute] string accountId, [FromServices] VrchatReadCache cache, CancellationToken cancellationToken) =>
        FetchAsync(accountId, async client =>
        {
            var instances = await cache.GetOrFetchAsync(VrchatReadCache.Key(accountId, "group-instances", ""), VrchatReadCache.DialogTtl,
                () => client.GetUserGroupInstancesAsync(accountId, cancellationToken));
            var groupIds = instances.Select(i => i.OwnerIdString).OfType<string>().Distinct().ToList();
            // Groups this server has never seen are read once and kept, as the original does.
            var groups = await groupStore.KnownOrFetchAsync(groupIds, id => client.GetGroupAsync(id, false, cancellationToken), cancellationToken);
            return instances
                .Where(i => i.OwnerIdString is not null && groups.ContainsKey(i.OwnerIdString))
                .Select(i =>
                {
                    var group = groups[i.OwnerIdString!];
                    return new GroupInstanceResponse
                    {
                        Location = i.Location,
                        GroupId = group.Id,
                        GroupName = group.Name,
                        GroupIconUrl = group.IconUrl,
                        UserCount = i.UserCount,
                        Capacity = i.Capacity,
                        AgeGate = i.AgeGate == true || i.Location.Contains("~ageGate", StringComparison.Ordinal),
                    };
                })
                .ToList();
        });
}
