using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VRCX.Common.Authentication;
using VRCX.Common.Features.Accounts;
using VRCX.Common.Features.Desktop;
using VRCX.Common.Features.Friends;
using VRCX.Common.Features.Gallery;
using VRCX.Common.Features.Groups;
using VRCX.Common.Features.Users;
using VRCX.Common.Services.Sync;

namespace VRCX.API.Controller.Tools;

/// <summary>
/// The Tools page: the original's group, invite message, report, purchase and export tools, the
/// gallery and inventory, and the calendar. These are live reads and writes against VRChat with
/// the account's own session; the few things kept here (notes, print favorites, the friend
/// export) go through their features. The routes stay under /accounts, where they were while they
/// were part of the accounts controller.
/// </summary>
[ApiController]
[Tags("Accounts")]
[ApiVersion("1")]
[Route("/{version:apiVersion}/accounts")]
[Authorize(AuthenticationSchemes = VrcxAuthSchemes.ApiToken)]
public sealed partial class AccountToolsController(
    AccountSessionRunner runner, AccountQueries accounts, FriendQueries friends, UserNotes notes, PrintFavorites printFavorites, GroupQueries groupQueries,
    AccountDesktop desktop, IAccountSyncService _sync, ILogger<AccountToolsController> _logger) : AccountForwardController(runner);
