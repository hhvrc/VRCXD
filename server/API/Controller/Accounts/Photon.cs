using System.Net.Mime;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using VRCX.Common.Services.Live.Photon;

namespace VRCX.API.Controller.Accounts;

public sealed partial class AccountsController
{
    /// <summary>
    /// The Photon lobby of the account's PC (the original's photon store): the players with their
    /// Photon ids, master and join details, who timed out, and the event table with the previous
    /// instance's. Empty while no Photon logger has run there.
    /// </summary>
    /// <response code="200">The lobby</response>
    [HttpGet("{accountId}/photon")]
    [ProducesResponseType<PhotonSnapshot>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [MapToApiVersion("1")]
    public IActionResult GetPhoton([FromRoute] string accountId, [FromServices] PhotonService photon) =>
        Ok(photon.Snapshot(accountId) ?? new PhotonSnapshot(false, null, 0, 0, [], [], [], []));
}
