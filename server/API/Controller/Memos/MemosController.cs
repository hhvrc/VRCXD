using System.Net.Mime;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VRCX.Common.Problems;
using VRCX.API.Models.Requests;
using VRCX.API.Models.Response;
using VRCX.Common;
using VRCX.Common.Authentication;
using VRCX.Common.Errors;
using VRCX.Common.Features.Memos;

namespace VRCX.API.Controller.Memos;

public sealed record WorldMemoRequest(string Memo);

public sealed record AvatarMemoRequest(string Memo);

/// <summary>
/// VRCX's own memos on users, worlds and avatars: local data, VRChat never sees it. Everything is
/// done by Common/Features/Memos; the routes stay under /users, /worlds and /avatars, where they
/// were while they were part of those controllers.
/// </summary>
[ApiController]
[ApiVersion("1")]
[Route("/{version:apiVersion}")]
[Authorize(AuthenticationSchemes = VrcxAuthSchemes.ApiToken)]
public sealed class MemosController(MemoStore memos) : VrcxControllerBase
{
    /// <summary>The memo kept on this user, if any.</summary>
    /// <response code="200">The memo</response>
    /// <response code="404">No memo (or no such user)</response>
    [HttpGet("users/{userId}/memo")]
    [Tags("Users")]
    [ProducesResponseType<MemoResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<VrcxProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> GetUserMemo([FromRoute] string userId, CancellationToken cancellationToken)
    {
        var memo = await memos.GetUserMemoAsync(userId, cancellationToken);
        if (memo is null) return Problem(UserError.MemoNotFound);
        return Ok(new MemoResponse { UserId = userId, Memo = memo.Memo, UpdatedAt = memo.UpdatedAt });
    }

    /// <summary>Write (or replace) the memo on this user.</summary>
    /// <response code="200">The memo as stored</response>
    /// <response code="404">Never seen this user</response>
    [HttpPut("users/{userId}/memo")]
    [Tags("Users")]
    [Consumes(MediaTypeNames.Application.Json)]
    [ProducesResponseType<MemoResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<VrcxProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> SetUserMemo([FromRoute] string userId, [FromBody] MemoRequest body, CancellationToken cancellationToken)
    {
        var memo = await memos.SetUserMemoAsync(userId, body.Memo, cancellationToken);
        if (!memo.Found) return Problem(UserError.UserNotFound);
        return Ok(new MemoResponse { UserId = userId, Memo = memo.Memo, UpdatedAt = memo.UpdatedAt!.Value });
    }

    /// <summary>Remove the memo on this user.</summary>
    /// <response code="204">Removed (or there was none)</response>
    [HttpDelete("users/{userId}/memo")]
    [Tags("Users")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> DeleteUserMemo([FromRoute] string userId, CancellationToken cancellationToken)
    {
        await memos.DeleteUserMemoAsync(userId, cancellationToken);
        return NoContent();
    }

    /// <summary>The memo kept on this world. Empty when there is none.</summary>
    /// <response code="200">The memo</response>
    [HttpGet("worlds/{worldId}/memo")]
    [Tags("Worlds")]
    [ProducesResponseType<WorldMemoResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> GetWorldMemo([FromRoute] string worldId, CancellationToken cancellationToken)
    {
        var memo = await memos.GetWorldMemoAsync(worldId, cancellationToken);
        return Ok(new WorldMemoResponse { WorldId = worldId, Memo = memo?.Memo ?? "", UpdatedAt = memo?.UpdatedAt });
    }

    /// <summary>Write the memo on this world. An empty memo removes it.</summary>
    /// <response code="200">The memo as stored</response>
    /// <response code="404">Never seen this world</response>
    [HttpPut("worlds/{worldId}/memo")]
    [Tags("Worlds")]
    [Consumes(MediaTypeNames.Application.Json)]
    [ProducesResponseType<WorldMemoResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<VrcxProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> SetWorldMemo([FromRoute] string worldId, [FromBody] WorldMemoRequest body, CancellationToken cancellationToken)
    {
        var memo = await memos.SetWorldMemoAsync(worldId, body.Memo, cancellationToken);
        if (!memo.Found) return Problem(WorldError.WorldNotFound);
        return Ok(new WorldMemoResponse { WorldId = worldId, Memo = memo.Memo, UpdatedAt = memo.UpdatedAt });
    }

    /// <summary>The memo kept on this avatar. Empty when there is none.</summary>
    /// <response code="200">The memo</response>
    [HttpGet("avatars/{avatarId}/memo")]
    [Tags("Avatars")]
    [ProducesResponseType<AvatarMemoResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> GetAvatarMemo([FromRoute] string avatarId, CancellationToken cancellationToken)
    {
        var memo = await memos.GetAvatarMemoAsync(avatarId, cancellationToken);
        return Ok(new AvatarMemoResponse { AvatarId = avatarId, Memo = memo?.Memo ?? "", UpdatedAt = memo?.UpdatedAt });
    }

    /// <summary>Write the memo on this avatar. An empty memo removes it.</summary>
    /// <response code="200">The memo as stored</response>
    /// <response code="404">Never seen this avatar</response>
    [HttpPut("avatars/{avatarId}/memo")]
    [Tags("Avatars")]
    [Consumes(MediaTypeNames.Application.Json)]
    [ProducesResponseType<AvatarMemoResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<VrcxProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> SetAvatarMemo([FromRoute] string avatarId, [FromBody] AvatarMemoRequest body, CancellationToken cancellationToken)
    {
        var memo = await memos.SetAvatarMemoAsync(avatarId, body.Memo, cancellationToken);
        if (!memo.Found) return Problem(AvatarError.AvatarNotFound);
        return Ok(new AvatarMemoResponse { AvatarId = avatarId, Memo = memo.Memo, UpdatedAt = memo.UpdatedAt });
    }
}
