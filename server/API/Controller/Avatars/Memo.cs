using System.Net.Mime;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenShock.Internal.Common.Problems;
using VRCX.API.Models.Response;
using VRCX.Common.Errors;
using VRCX.Common.VrcxDb;

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
    public async Task<IActionResult> SetMemo([FromRoute] string avatarId, [FromBody] AvatarMemoRequest body, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.Memo))
        {
            await _db.AvatarMemos.Where(m => m.AvatarId == avatarId).ExecuteDeleteAsync(cancellationToken);
            return Ok(new AvatarMemoResponse { AvatarId = avatarId, Memo = "", UpdatedAt = null });
        }
        if (!await _db.Avatars.AnyAsync(a => a.Id == avatarId, cancellationToken)) return Problem(AvatarError.AvatarNotFound);
        var memo = await _db.AvatarMemos.FirstOrDefaultAsync(m => m.AvatarId == avatarId, cancellationToken);
        if (memo is null)
        {
            memo = new AvatarMemo { AvatarId = avatarId, Memo = body.Memo };
            _db.AvatarMemos.Add(memo);
        }
        memo.Memo = body.Memo;
        memo.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return Ok(new AvatarMemoResponse { AvatarId = avatarId, Memo = memo.Memo, UpdatedAt = memo.UpdatedAt });
    }
}
