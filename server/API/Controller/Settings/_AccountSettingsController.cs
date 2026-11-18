using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VRCX.Common.Authentication;
using VRCX.Common.Features.Accounts;
using VRCX.Common.Features.Desktop;

namespace VRCX.API.Controller.Settings;

/// <summary>
/// The per-account app settings. Stored through Common/Features/Accounts; a save is also pushed to
/// the agent on the account's PC and to what follows the settings. The routes stay under /accounts,
/// where they were while they were part of the accounts controller.
/// </summary>
[ApiController]
[Tags("Accounts")]
[ApiVersion("1")]
[Route("/{version:apiVersion}/accounts")]
[Authorize(AuthenticationSchemes = VrcxAuthSchemes.ApiToken)]
public sealed partial class AccountSettingsController(AccountSettingsStore settings, AccountDesktop desktop) : VRCX.Common.VrcxControllerBase;
