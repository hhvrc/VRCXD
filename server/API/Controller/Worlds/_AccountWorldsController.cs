using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VRCX.Common.Authentication;
using VRCX.Common.Features.Accounts;
using VRCX.Common.Features.Worlds;
using VRCX.Common.Services.Sync;

namespace VRCX.API.Controller.Worlds;

/// <summary>
/// The world pop-up for an account: what only VRChat knows right now, and the actions its menu
/// offers. Almost all of it forwards to VRChat with the account's own session; what is this
/// server's own (the account's history in a world, its home) goes through Common/Features/Worlds.
/// The routes stay under /accounts, where they were while they were part of the accounts controller.
/// </summary>
[ApiController]
[Tags("Accounts")]
[ApiVersion("1")]
[Route("/{version:apiVersion}/accounts")]
[Authorize(AuthenticationSchemes = VrcxAuthSchemes.ApiToken)]
public sealed partial class AccountWorldsController(AccountSessionRunner runner, IAccountSyncService _sync, AccountWorlds worlds) : AccountForwardController(runner);
