using System.Net.Mime;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VRCX.Common.Problems;
using VRCX.Common;
using VRCX.Common.Authentication;
using VRCX.Common.Errors;
using VRCX.Common.Features.Accounts;
using VRCX.Common.Features.QuickSearch;

namespace VRCX.API.Controller.QuickSearch;

/// <summary>
/// The original's Ctrl+K search. Everything is read by Common/Features/QuickSearch; the route stays
/// under /accounts, where it was while it was part of the accounts controller.
/// </summary>
[ApiController]
[Tags("Accounts")]
[ApiVersion("1")]
[Route("/{version:apiVersion}/accounts")]
[Authorize(AuthenticationSchemes = VrcxAuthSchemes.ApiToken)]
public sealed class QuickSearchController(AccountQueries accounts, QuickSearchQueries queries) : VrcxControllerBase
{
    /// <summary>Everything the quick search dialog matches against, read once when it opens.</summary>
    /// <response code="200">The index</response>
    /// <response code="404">No such account</response>
    [HttpGet("{accountId}/quick-search")]
    [ProducesResponseType<QuickSearchIndexResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<VrcxProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> GetQuickSearchIndex([FromRoute] string accountId, CancellationToken cancellationToken)
    {
        if (!await accounts.ExistsAsync(accountId, cancellationToken)) return Problem(AccountError.AccountNotFound);
        return Ok(await queries.IndexAsync(accountId, cancellationToken));
    }
}
