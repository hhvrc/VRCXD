using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VRCX.Common.Authentication;
using VRCX.Common.Features.Accounts;
using VRCX.Common.Features.Desktop;

namespace VRCX.API.Controller.Desktop;

/// <summary>
/// The agent on the PC an account plays on, and the game there: what it relays (screenshots, the
/// cache, VRChat's config and registry, notifications, the wrist feed, the named pipe), launching
/// and joining, the Photon lobby, and the custom tags programs on the PC set. Finding the agent
/// and what this server keeps for it are Common/Features/Desktop. The routes stay under
/// /accounts, where they were while they were part of the accounts controller.
/// </summary>
[ApiController]
[Tags("Accounts")]
[ApiVersion("1")]
[Route("/{version:apiVersion}/accounts")]
[Authorize(AuthenticationSchemes = VrcxAuthSchemes.ApiToken)]
public sealed partial class AccountDesktopController(AccountSessionRunner runner, AccountQueries accounts, AccountDesktop desktop) : AccountForwardController(runner);
