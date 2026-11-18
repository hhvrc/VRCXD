using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VRCX.Common.Authentication;
using VRCX.Common.Features.Accounts;
using VRCX.Common.Features.Profile;

namespace VRCX.API.Controller.Profile;

/// <summary>
/// The account's own profile: the original's edit dialogs for the bio, links, pronouns, languages
/// and profile themes. Forwards to VRChat with the account's own session and keeps the stored copy
/// in step through Common/Features/Profile. The routes stay under /accounts, where they were while
/// they were part of the accounts controller.
/// </summary>
[ApiController]
[Tags("Accounts")]
[ApiVersion("1")]
[Route("/{version:apiVersion}/accounts")]
[Authorize(AuthenticationSchemes = VrcxAuthSchemes.ApiToken)]
public sealed partial class AccountProfileController(AccountSessionRunner runner, ProfileStore profileStore) : AccountForwardController(runner);
