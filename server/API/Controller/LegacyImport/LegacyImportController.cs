using System.Net.Mime;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VRCX.Common;
using VRCX.Common.Authentication;
using VRCX.Common.Features.LegacyImport;
using VRCX.Common.Problems;

namespace VRCX.API.Controller.LegacyImport;

public sealed record LegacyImportStartRequest(long Size);

/// <summary>
/// Importing an original VRCX database (VRCX.sqlite3) from the Settings page: the file goes up in
/// parts, then a dry run reports what it holds, then the import. Everything is done by
/// Common/Features/LegacyImport. The import touches Postgres only and makes no VRChat call.
/// </summary>
[ApiController]
[ApiVersion("1")]
[Route("/{version:apiVersion}/legacy-import")]
[Authorize(AuthenticationSchemes = VrcxAuthSchemes.ApiToken)]
[Tags("Legacy import")]
public sealed class LegacyImportController(LegacyImportRuns runs) : VrcxControllerBase
{
    /// <summary>The upload, the step running and the last report.</summary>
    [HttpGet]
    [ProducesResponseType<LegacyImportStatus>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [MapToApiVersion("1")]
    public LegacyImportStatus GetStatus() => runs.Status();

    /// <summary>Starts a new upload of a file this many bytes long, replacing the last one.</summary>
    /// <response code="200">The new upload; send its parts to PUT /legacy-import/{uploadId}</response>
    /// <response code="400">An empty file</response>
    /// <response code="409">A dry run or import is running</response>
    /// <response code="413">Larger than VRCX:LegacyImport:MaxSize</response>
    /// <response code="507">Not enough free disk space</response>
    [HttpPost]
    [Consumes(MediaTypeNames.Application.Json)]
    [ProducesResponseType<LegacyImportStatus>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<VrcxProblem>(StatusCodes.Status400BadRequest, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<VrcxProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<VrcxProblem>(StatusCodes.Status413PayloadTooLarge, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<VrcxProblem>(StatusCodes.Status507InsufficientStorage, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> Start([FromBody] LegacyImportStartRequest body, CancellationToken cancellationToken) =>
        Answer(await runs.StartAsync(body.Size, cancellationToken));

    /// <summary>
    /// One part of the file, as the raw body, starting at <paramref name="offset"/> (the bytes
    /// received so far). At most 64 MiB a part.
    /// </summary>
    /// <response code="200">The upload with what has been received</response>
    /// <response code="404">No such upload</response>
    /// <response code="409">The offset is not where the file ends; the detail says where it does</response>
    /// <response code="415">The file does not start like an SQLite database</response>
    [HttpPut("{uploadId}")]
    [RequestSizeLimit(LegacyImportRuns.MaxChunk)]
    [ProducesResponseType<LegacyImportStatus>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<VrcxProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<VrcxProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<VrcxProblem>(StatusCodes.Status415UnsupportedMediaType, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> Append([FromRoute] string uploadId, [FromQuery] long offset, CancellationToken cancellationToken) =>
        Answer(await runs.AppendAsync(uploadId, offset, Request.Body, cancellationToken));

    /// <summary>Reads the uploaded file and reports what would be imported, in the background; the "legacy_import" topic and GET carry the report.</summary>
    /// <response code="200">The check has started</response>
    /// <response code="404">No such upload</response>
    /// <response code="409">The file is not all there, or a run is already going</response>
    [HttpPost("{uploadId}/dry-run")]
    [ProducesResponseType<LegacyImportStatus>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<VrcxProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<VrcxProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public IActionResult DryRun([FromRoute] string uploadId, [FromQuery] bool replaceHistory = false) => Answer(runs.Check(uploadId, replaceHistory));

    /// <summary>
    /// Imports the uploaded file in the background and deletes it after. Safe to repeat. With
    /// replaceHistory, this server's feeds, name and trust changes, friend requests and game log up
    /// to the file's newest row of each are deleted first, and the file's rows take their place.
    /// </summary>
    /// <response code="200">The import has started</response>
    /// <response code="404">No such upload</response>
    /// <response code="409">The file is not all there (or was imported already), or a run is already going</response>
    [HttpPost("{uploadId}/import")]
    [ProducesResponseType<LegacyImportStatus>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<VrcxProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<VrcxProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public IActionResult Import([FromRoute] string uploadId, [FromQuery] bool replaceHistory = false) => Answer(runs.Import(uploadId, replaceHistory));

    /// <summary>Forgets the upload and deletes the file.</summary>
    /// <response code="204">Gone</response>
    /// <response code="409">A dry run or import is running</response>
    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<VrcxProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> Discard(CancellationToken cancellationToken) =>
        await runs.DiscardAsync(cancellationToken) is { } problem ? Problem(problem) : NoContent();
}
