using System.Net.Mime;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenShock.Internal.Common.Problems;
using VRCX.API.Models.Response;
using VRCX.Common.Errors;
using VRCX.Common.VrcxDb;

namespace VRCX.API.Controller.Worlds;

public sealed record WorldMemoRequest(string Memo);

public sealed partial class WorldsController
{
    /// <summary>The memo kept on this world. Empty when there is none.</summary>
    /// <response code="200">The memo</response>
    [HttpGet("{worldId}/memo")]
    [ProducesResponseType<WorldMemoResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> GetMemo([FromRoute] string worldId, CancellationToken cancellationToken)
    {
        var memo = await _db.WorldMemos.AsNoTracking().FirstOrDefaultAsync(m => m.WorldId == worldId, cancellationToken);
        return Ok(new WorldMemoResponse { WorldId = worldId, Memo = memo?.Memo ?? "", UpdatedAt = memo?.UpdatedAt });
    }

    /// <summary>Write the memo on this world. An empty memo removes it.</summary>
    /// <response code="200">The memo as stored</response>
    /// <response code="404">Never seen this world</response>
    [HttpPut("{worldId}/memo")]
    [Consumes(MediaTypeNames.Application.Json)]
    [ProducesResponseType<WorldMemoResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> SetMemo([FromRoute] string worldId, [FromBody] WorldMemoRequest body, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.Memo))
        {
            await _db.WorldMemos.Where(m => m.WorldId == worldId).ExecuteDeleteAsync(cancellationToken);
            return Ok(new WorldMemoResponse { WorldId = worldId, Memo = "", UpdatedAt = null });
        }
        if (!await _db.Worlds.AnyAsync(w => w.Id == worldId, cancellationToken)) return Problem(WorldError.WorldNotFound);
        var memo = await _db.WorldMemos.FirstOrDefaultAsync(m => m.WorldId == worldId, cancellationToken);
        if (memo is null)
        {
            memo = new WorldMemo { WorldId = worldId, Memo = body.Memo };
            _db.WorldMemos.Add(memo);
        }
        memo.Memo = body.Memo;
        memo.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return Ok(new WorldMemoResponse { WorldId = worldId, Memo = memo.Memo, UpdatedAt = memo.UpdatedAt });
    }
}
