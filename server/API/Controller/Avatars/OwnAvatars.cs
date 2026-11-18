using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using Microsoft.AspNetCore.Mvc;
using Asp.Versioning;
using VRCX.Common.Problems;
using VRCX.API.Models.Response;
using VRCX.Common.Errors;
using VRCX.Common.Features.Avatars;

namespace VRCX.API.Controller.Avatars;

/// <summary>One of the account's own avatars: the stored avatar plus VRCX's tags, time worn and whether it is on.</summary>
public sealed class OwnAvatarResponse
{
    public required AvatarResponse Avatar { get; init; }
    public required OwnAvatarTag[] Tags { get; init; }
    /// <summary>Milliseconds worn, including the open session.</summary>
    public required long TimeSpentMs { get; init; }
    public required bool IsCurrent { get; init; }
}

public sealed class UpdateAvatarRequest
{
    [StringLength(64, MinimumLength = 1)]
    public string? Name { get; set; }
    [StringLength(256)]
    public string? Description { get; set; }
    /// <summary>public or private</summary>
    [RegularExpression("^(public|private)$")]
    public string? ReleaseStatus { get; set; }
}

// The My Avatars page: the account's uploads (pulled from VRChat once and on Refresh, like the
// original), VRCX's own tags on them, time worn, and the edits the original offered. Done by
// Common/Features/Avatars/OwnAvatars.
public sealed partial class AccountAvatarsController
{
    /// <summary>
    /// The account's own avatars. With a live session they are fetched from VRChat (GET avatars?user=me,
    /// every page) and stored when asked to refresh or not fetched in the last 10 minutes; otherwise the
    /// stored ones are returned.
    /// </summary>
    /// <response code="200">The avatars, newest update first</response>
    /// <response code="404">No such account</response>
    [HttpGet("{accountId}/avatars")]
    [ProducesResponseType<OwnAvatarResponse[]>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<VrcxProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> ListOwnAvatars([FromRoute] string accountId, CancellationToken cancellationToken, [FromQuery] bool refresh = false)
    {
        var avatars = await ownAvatars.ListAsync(accountId, refresh, cancellationToken);
        if (avatars is null) return Problem(AccountError.AccountNotFound);
        return Ok(avatars.Select(a => new OwnAvatarResponse { Avatar = AvatarResponse.From(a.Avatar), Tags = a.Tags, TimeSpentMs = a.TimeSpentMs, IsCurrent = a.IsCurrent }).ToList());
    }

    /// <summary>Wear the avatar (PUT avatars/{id}/select on VRChat).</summary>
    /// <response code="204">Selected</response>
    /// <response code="404">No such account</response>
    /// <response code="409">No live session</response>
    [HttpPost("{accountId}/avatars/{avatarId}/select")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<VrcxProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<VrcxProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> SelectAvatar([FromRoute] string accountId, [FromRoute] string avatarId, CancellationToken cancellationToken) =>
        await ownAvatars.SelectAsync(accountId, avatarId, cancellationToken) is { } problem ? Problem(problem) : NoContent();

    /// <summary>Rename, change the description or the visibility of one of the account's avatars (PUT avatars/{id} on VRChat).</summary>
    /// <response code="200">The avatar as VRChat now has it</response>
    /// <response code="404">No such account</response>
    /// <response code="409">No live session</response>
    [HttpPatch("{accountId}/avatars/{avatarId}")]
    [ProducesResponseType<AvatarResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<VrcxProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<VrcxProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> UpdateAvatar([FromRoute] string accountId, [FromRoute] string avatarId, [FromBody] UpdateAvatarRequest body, CancellationToken cancellationToken)
    {
        var saved = await ownAvatars.UpdateAsync(accountId, avatarId, body.Name, body.Description, body.ReleaseStatus, cancellationToken);
        return Answer(saved, avatar => Ok(AvatarResponse.From(avatar)));
    }

    /// <summary>Ask VRChat to build the avatar's impostor.</summary>
    /// <response code="202">Queued</response>
    /// <response code="404">No such account</response>
    /// <response code="409">No live session</response>
    [HttpPost("{accountId}/avatars/{avatarId}/impostor")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType<VrcxProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<VrcxProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> CreateImpostor([FromRoute] string accountId, [FromRoute] string avatarId, CancellationToken cancellationToken) =>
        await ownAvatars.CreateImpostorAsync(accountId, avatarId, cancellationToken) is { } problem ? Problem(problem) : Accepted();

    /// <summary>Replace VRCX's tags on an avatar.</summary>
    /// <response code="200">The stored tags</response>
    /// <response code="404">No such account or avatar</response>
    [HttpPut("{accountId}/avatars/{avatarId}/tags")]
    [ProducesResponseType<OwnAvatarTag[]>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<VrcxProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> PutAvatarTags([FromRoute] string accountId, [FromRoute] string avatarId, [FromBody] OwnAvatarTag[] body, CancellationToken cancellationToken)
    {
        return Answer(await ownAvatars.PutTagsAsync(accountId, avatarId, body, cancellationToken));
    }

    /// <summary>Which avatar a wearer's picture shows, the original's showAvatarAuthorDialog: their own current avatar, one the server has seen with that image, and otherwise only who owns it.</summary>
    /// <response code="200">What could be found. Either field can be missing.</response>
    /// <response code="404">No such account</response>
    [HttpGet("{accountId}/avatars/lookup")]
    [ProducesResponseType<AvatarLookupResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<VrcxProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> LookupAvatar([FromRoute] string accountId, [FromQuery] string imageUrl, [FromQuery] string? userId, CancellationToken cancellationToken) =>
        await ownAvatars.LookupAsync(accountId, imageUrl, userId, cancellationToken) is { } found ? Ok(found) : Problem(AccountError.AccountNotFound);
}
