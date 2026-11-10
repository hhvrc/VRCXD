using System.Net.Mime;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenShock.Internal.Common.Problems;
using VRCX.API.Models.Requests;
using VRCX.API.Models.Response;
using VRCX.Common.Errors;
using VRCX.Common.VrcxDb;

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
    public async Task<IActionResult> SetMemo([FromRoute] string userId, [FromBody] MemoRequest body, CancellationToken cancellationToken)
    {
        if (!await _db.Users.AnyAsync(u => u.Id == userId, cancellationToken)) return Problem(UserError.UserNotFound);
        var memo = await _db.UserMemos.FirstOrDefaultAsync(m => m.UserId == userId, cancellationToken);
        if (memo is null)
        {
            memo = new UserMemo { UserId = userId, Memo = body.Memo };
            _db.UserMemos.Add(memo);
        }
        memo.Memo = body.Memo;
        memo.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return Ok(new MemoResponse { UserId = memo.UserId, Memo = memo.Memo, UpdatedAt = memo.UpdatedAt });
    }

    /// <summary>Remove the memo on this user.</summary>
    /// <response code="204">Removed (or there was none)</response>
    [HttpDelete("{userId}/memo")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> DeleteMemo([FromRoute] string userId, CancellationToken cancellationToken)
    {
        await _db.UserMemos.Where(m => m.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        return NoContent();
    }
}
