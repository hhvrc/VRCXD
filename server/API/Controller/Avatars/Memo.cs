using System.Net.Mime;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenShock.Internal.Common.Problems;
using VRCX.API.Models.Response;
using VRCX.Common.Errors;
using VRCX.Common.Services;

namespace VRCX.API.Controller.Avatars;

public sealed record AvatarMemoRequest(string Memo);

public sealed partial class AvatarsController
{
    /// <summary>The memo kept on this avatar. Empty when there is none.</summary>
    /// <response code="200">The memo</response>
    [HttpGet("{avatarId}/memo")]
    [ProducesResponseType<AvatarMemoResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> GetMemo([FromRoute] string avatarId, CancellationToken cancellationToken)
    {
        var memo = await _db.AvatarMemos.AsNoTracking().FirstOrDefaultAsync(m => m.AvatarId == avatarId, cancellationToken);
        return Ok(new AvatarMemoResponse { AvatarId = avatarId, Memo = memo?.Memo ?? "", UpdatedAt = memo?.UpdatedAt });
    }

    /// <summary>Write the memo on this avatar. An empty memo removes it.</summary>
    /// <response code="200">The memo as stored</response>
    /// <response code="404">Never seen this avatar</response>
    [HttpPut("{avatarId}/memo")]
    [Consumes(MediaTypeNames.Application.Json)]
    [ProducesResponseType<AvatarMemoResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> SetMemo([FromRoute] string avatarId, [FromBody] AvatarMemoRequest body, [FromServices] MemoStore memos, CancellationToken cancellationToken)
    {
        var memo = await memos.SetAvatarMemoAsync(avatarId, body.Memo, cancellationToken);
        if (!memo.Found) return Problem(AvatarError.AvatarNotFound);
        return Ok(new AvatarMemoResponse { AvatarId = avatarId, Memo = memo.Memo, UpdatedAt = memo.UpdatedAt });
    }
}
