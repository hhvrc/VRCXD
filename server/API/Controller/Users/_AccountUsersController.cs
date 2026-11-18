using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VRCX.Common.Authentication;
using VRCX.Common.Features.Accounts;
using VRCX.Common.Features.Avatars;
using VRCX.Common.Features.Content;
using VRCX.Common.Features.Notifications;
using VRCX.Common.Features.Profile;
using VRCX.Common.Features.Users;

namespace VRCX.API.Controller.Users;

/// <summary>
/// The user pop-up for an account: its tabs (mutuals, groups, worlds, avatars), the rest of its
/// action menu (invites, boops, friend requests, badges) and the invite photos. Mostly live reads
/// and writes against VRChat with the account's own session; what is stored here goes through the
/// features it belongs to. The routes stay under /accounts, where they were while they were part
/// of the accounts controller.
/// </summary>
[ApiController]
[Tags("Accounts")]
[ApiVersion("1")]
[Route("/{version:apiVersion}/accounts")]
[Authorize(AuthenticationSchemes = VrcxAuthSchemes.ApiToken)]
public sealed partial class AccountUsersController(
    AccountSessionRunner runner, AccountQueries accounts, UserQueries userQueries, UserNotes notes, ContentQueries content, OwnAvatars ownAvatars,
    NotificationActions notificationActions, ProfileStore profileStore) : AccountForwardController(runner);
