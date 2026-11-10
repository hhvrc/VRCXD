using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using System.Text.Json;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using OpenShock.Internal.Common.Problems;

namespace VRCX.API.Controller.Accounts;

/// <summary>Who to hand a group over to.</summary>
public sealed class GroupTransferBody
{
    [Required] public required string TransferTargetId { get; set; }
}

// The original's GroupTransferDialog: checkTransferGroup, then transferGroup.
public sealed partial class AccountsController
{
    /// <summary>Whether a member can take the group over: VRChat's requirements, each met or not.</summary>
    /// <response code="200">VRChat's answer</response>
    /// <response code="409">No live session</response>
    [HttpGet("{accountId}/groups/{groupId}/transfer")]
    [ProducesResponseType<JsonElement>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> CheckGroupTransfer([FromRoute] string accountId, [FromRoute] string groupId, [FromQuery, Required] string transferTargetId, CancellationToken cancellationToken) =>
        ReadLiveAsync(accountId, client => client.GetGroupTransferabilityAsync(groupId, transferTargetId, cancellationToken));

    /// <summary>Ask a member to take the group over.</summary>
    /// <response code="200">VRChat's answer</response>
    /// <response code="409">No live session</response>
    [HttpPost("{accountId}/groups/{groupId}/transfer")]
    [Consumes(MediaTypeNames.Application.Json)]
    [ProducesResponseType<JsonElement>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> TransferGroup([FromRoute] string accountId, [FromRoute] string groupId, [FromBody] GroupTransferBody body, CancellationToken cancellationToken) =>
        ReadLiveAsync(accountId, client => client.TransferGroupAsync(groupId, body.TransferTargetId, cancellationToken));
}
