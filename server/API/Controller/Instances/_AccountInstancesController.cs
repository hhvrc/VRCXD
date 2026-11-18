using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VRCX.Common.Authentication;
using VRCX.Common.Features.Accounts;
using VRCX.Common.Features.Instances;

namespace VRCX.API.Controller.Instances;

/// <summary>
/// What an account does with VRChat's instances: creating and inviting, the live state of one, its
/// queue, announcing and closing. Almost all of it forwards to VRChat with the account's own
/// session; what is stored here beside it is read through Common/Features/Instances. The routes
/// stay under /accounts, where they were while they were part of the accounts controller.
/// </summary>
[ApiController]
[Tags("Accounts")]
[ApiVersion("1")]
[Route("/{version:apiVersion}/accounts")]
[Authorize(AuthenticationSchemes = VrcxAuthSchemes.ApiToken)]
public sealed partial class AccountInstancesController(AccountSessionRunner runner, InstanceQueries instanceQueries) : AccountForwardController(runner);
