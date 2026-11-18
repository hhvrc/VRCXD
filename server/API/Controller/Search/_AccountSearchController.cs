using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VRCX.Common.Authentication;
using VRCX.Common.Features.Accounts;
using VRCX.Common.Features.Search;

namespace VRCX.API.Controller.Search;

/// <summary>
/// The Search page. Each tab asks VRChat through the account's session and gets VRChat's answer
/// back as it came, the way the original reads it. What is kept here (the users found, this
/// install's id for the avatar databases) goes through Common/Features/Search. The routes stay
/// under /accounts, where they were while they were part of the accounts controller.
/// </summary>
[ApiController]
[Tags("Accounts")]
[ApiVersion("1")]
[Route("/{version:apiVersion}/accounts")]
[Authorize(AuthenticationSchemes = VrcxAuthSchemes.ApiToken)]
public sealed partial class AccountSearchController(AccountSessionRunner runner, SearchStore searchStore) : AccountForwardController(runner);
