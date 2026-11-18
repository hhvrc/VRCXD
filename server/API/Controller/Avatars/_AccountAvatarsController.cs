using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VRCX.Common.Authentication;
using VRCX.Common.Features.Accounts;
using VRCX.Common.Features.Avatars;
using VRCX.Common.Services.Sync;

namespace VRCX.API.Controller.Avatars;

/// <summary>
/// An account's avatars: the My Avatars page, and the avatar pop-up's actions. The pop-up's actions
/// forward to VRChat with the account's own session; what is this server's own (the stored
/// uploads, VRCX's tags, time worn) is done by Common/Features/Avatars. The routes stay under
/// /accounts, where they were while they were part of the accounts controller.
/// </summary>
[ApiController]
[Tags("Accounts")]
[ApiVersion("1")]
[Route("/{version:apiVersion}/accounts")]
[Authorize(AuthenticationSchemes = VrcxAuthSchemes.ApiToken)]
public sealed partial class AccountAvatarsController(AccountSessionRunner runner, IAccountSyncService _sync, OwnAvatars ownAvatars) : AccountForwardController(runner);
