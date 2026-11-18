using System.Net.Mime;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenShock.Internal.Common.Problems;
using VRCX.API.Models.Response;
using VRCX.Common.Errors;
using VRCX.Common.Services;

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
    public async Task<IActionResult> SetMemo([FromRoute] string worldId, [FromBody] WorldMemoRequest body, [FromServices] MemoStore memos, CancellationToken cancellationToken)
    {
        var memo = await memos.SetWorldMemoAsync(worldId, body.Memo, cancellationToken);
        if (!memo.Found) return Problem(WorldError.WorldNotFound);
        return Ok(new WorldMemoResponse { WorldId = worldId, Memo = memo.Memo, UpdatedAt = memo.UpdatedAt });
    }
}
