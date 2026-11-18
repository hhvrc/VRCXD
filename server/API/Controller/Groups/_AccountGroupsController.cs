using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VRCX.Common.Authentication;
using VRCX.Common.Features.Accounts;
using VRCX.Common.Features.Groups;

namespace VRCX.API.Controller.Groups;

/// <summary>
/// What an account does with VRChat's groups: the group pop-up, posts, events, galleries, roles,
/// moderation, editing and transfer. Almost all of it forwards to VRChat with the account's own
/// session; what is this server's own is read through Common/Features/Groups. The routes stay
/// under /accounts, where they were while they were part of the accounts controller.
/// </summary>
[ApiController]
[Tags("Accounts")]
[ApiVersion("1")]
[Route("/{version:apiVersion}/accounts")]
[Authorize(AuthenticationSchemes = VrcxAuthSchemes.ApiToken)]
public sealed partial class AccountGroupsController(AccountSessionRunner runner, GroupQueries groupQueries) : AccountForwardController(runner);
