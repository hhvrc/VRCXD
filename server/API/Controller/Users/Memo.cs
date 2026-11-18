using System.Net.Mime;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenShock.Internal.Common.Problems;
using VRCX.API.Models.Requests;
using VRCX.API.Models.Response;
using VRCX.Common.Errors;
using VRCX.Common.Services;

namespace VRCX.API.Controller.Users;

// VRCX's own memo on a user: local data, VRChat never sees it.
public sealed partial class UsersController
{
    /// <summary>The memo kept on this user, if any.</summary>
    /// <response code="200">The memo</response>
    /// <response code="404">No memo (or no such user)</response>
    [HttpGet("{userId}/memo")]
    [ProducesResponseType<MemoResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> GetMemo([FromRoute] string userId, CancellationToken cancellationToken)
    {
        var memo = await _db.UserMemos.AsNoTracking().FirstOrDefaultAsync(m => m.UserId == userId, cancellationToken);
        if (memo is null) return Problem(UserError.MemoNotFound);
        return Ok(new MemoResponse { UserId = memo.UserId, Memo = memo.Memo, UpdatedAt = memo.UpdatedAt });
    }

    /// <summary>Write (or replace) the memo on this user.</summary>
    /// <response code="200">The memo as stored</response>
    /// <response code="404">Never seen this user</response>
    [HttpPut("{userId}/memo")]
    [Consumes(MediaTypeNames.Application.Json)]
    [ProducesResponseType<MemoResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> SetMemo([FromRoute] string userId, [FromBody] MemoRequest body, [FromServices] MemoStore memos, CancellationToken cancellationToken)
    {
        var memo = await memos.SetUserMemoAsync(userId, body.Memo, cancellationToken);
        if (!memo.Found) return Problem(UserError.UserNotFound);
        return Ok(new MemoResponse { UserId = userId, Memo = memo.Memo, UpdatedAt = memo.UpdatedAt!.Value });
    }

    /// <summary>Remove the memo on this user.</summary>
    /// <response code="204">Removed (or there was none)</response>
    [HttpDelete("{userId}/memo")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> DeleteMemo([FromRoute] string userId, [FromServices] MemoStore memos, CancellationToken cancellationToken)
    {
        await memos.DeleteUserMemoAsync(userId, cancellationToken);
        return NoContent();
    }
}
