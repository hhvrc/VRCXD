using System.Net.Mime;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VRCX.Common.Problems;
using VRCX.API.Models.Requests;
using VRCX.API.Models.Response;
using VRCX.Common;
using VRCX.Common.Authentication;
using VRCX.Common.Errors;
using VRCX.Common.Features.Accounts;
using VRCX.Common.Services.Sessions;
using VRCX.Common.Services.Sync;

namespace VRCX.API.Controller.Accounts;

/// <summary>
/// The VRChat accounts logged into this server: the list, logging in and out, the saved passwords
/// and a full sync on request. Done by Common/Features/Accounts. What an account does with its
/// friends, groups, worlds and the rest is in the controllers named after those.
/// </summary>
[ApiController]
[Tags("Accounts")]
[ApiVersion("1")]
[Route("/{version:apiVersion}/accounts")]
[Authorize(AuthenticationSchemes = VrcxAuthSchemes.ApiToken)]
public sealed class AccountsController(AccountSessionRunner runner, AccountQueries accounts, AccountLogin login, IAccountSyncService sync) : AccountForwardController(runner)
{
    /// <summary>
    /// Every VRChat account that has logged into this server, with its session and sync state.
    /// </summary>
    /// <response code="200">The accounts</response>
    [HttpGet]
    [ProducesResponseType<AccountResponse[]>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [MapToApiVersion("1")]
    public async Task<AccountResponse[]> ListAccounts(CancellationToken cancellationToken) =>
        (await accounts.ListDetailsAsync(cancellationToken)).Select(a => new AccountResponse
        {
            UserId = a.UserId,
            DisplayName = a.DisplayName,
            Username = a.Username,
            SessionValid = a.SessionValid,
            ApiEndpoint = a.ApiEndpoint,
            PipelineEndpoint = a.PipelineEndpoint,
            Connected = Runner.HasSession(a.UserId),
            LoggedInAt = a.LoggedInAt,
            LastFriendSyncAt = a.LastFriendSyncAt,
            LastFullSyncAt = a.LastFullSyncAt,
            HomeLocation = a.HomeLocation,
            ThumbnailImageUrl = a.ThumbnailImageUrl,
            IconFrame = a.IconFrame,
            HasSavedCredentials = a.HasSavedCredentials,
            SavedCredentialsNeedPrimaryPassword = a.SavedCredentialsNeedPrimaryPassword,
        }).ToArray();

    /// <summary>
    /// Pull everything VRChat knows about the account right now (friends, groups, favorites, moderations, notes, notifications).
    /// </summary>
    /// <response code="204">Synced</response>
    /// <response code="404">No such account</response>
    /// <response code="409">The account has no live VRChat session</response>
    [HttpPost("{accountId}/sync")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<VrcxProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<VrcxProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> Sync([FromRoute] string accountId, CancellationToken cancellationToken)
    {
        if (!await accounts.ExistsAsync(accountId, cancellationToken)) return Problem(AccountError.AccountNotFound);
        return await RunAsync(accountId, _ => sync.SyncEverythingAsync(accountId, cancellationToken));
    }

    /// <summary>
    /// Log a VRChat account into this server. Answers needsTwoFactor with a pendingId when the account has 2FA.
    /// </summary>
    /// <response code="200">Logged in, or a second factor is needed (see status)</response>
    /// <response code="401">VRChat rejected the credentials</response>
    /// <response code="502">VRChat could not be reached</response>
    [HttpPost("login")]
    [Consumes(MediaTypeNames.Application.Json)]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<VrcxProblem>(StatusCodes.Status401Unauthorized, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<VrcxProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> Login([FromBody] LoginRequest body, CancellationToken cancellationToken) =>
        Answer(await login.LoginAsync(body.Username, body.Password, body.Endpoint, body.Websocket, body.SaveCredentials, body.PrimaryPassword, cancellationToken),
            rejected => LoginError.InvalidCredentials(rejected.Reason));

    /// <summary>
    /// Finish a login that answered needsTwoFactor.
    /// </summary>
    /// <response code="200">Logged in</response>
    /// <response code="400">Unknown method</response>
    /// <response code="401">VRChat rejected the code</response>
    /// <response code="404">Unknown or expired pendingId</response>
    [HttpPost("login/2fa")]
    [Consumes(MediaTypeNames.Application.Json)]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<VrcxProblem>(StatusCodes.Status400BadRequest, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<VrcxProblem>(StatusCodes.Status401Unauthorized, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<VrcxProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> CompleteTwoFactor([FromBody] TwoFactorRequest body, CancellationToken cancellationToken) =>
        Answer(await login.CompleteTwoFactorAsync(body.PendingId, body.Method, body.Code, cancellationToken), _ => LoginError.CodeRejected);

    /// <summary>
    /// The original's QR login: a code for another device signed in to VRChat to approve. Show the
    /// verification address as a QR code, then poll /accounts/login/device/{pendingId}.
    /// </summary>
    /// <response code="200">The code is waiting for approval</response>
    /// <response code="502">VRChat could not be reached</response>
    [HttpPost("login/device")]
    [ProducesResponseType<DeviceLoginResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<VrcxProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> StartDeviceLogin(CancellationToken cancellationToken)
    {
        return Answer(await login.StartDeviceLoginAsync(cancellationToken), started =>
            Ok(new DeviceLoginResponse { PendingId = started.PendingId, VerificationUrl = started.Code.VerificationUrlComplete, Interval = started.Code.Interval, ExpiresIn = started.Code.ExpiresIn }));
    }

    /// <summary>Whether a QR login has been approved: pending until it is, then loggedIn.</summary>
    /// <response code="200">pending or loggedIn</response>
    /// <response code="404">Unknown or abandoned pendingId</response>
    /// <response code="502">VRChat refused the code (expired) or could not be reached</response>
    [HttpPost("login/device/{pendingId}")]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<VrcxProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<VrcxProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> PollDeviceLogin([FromRoute] string pendingId, CancellationToken cancellationToken) =>
        Answer(await login.PollDeviceLoginAsync(pendingId, cancellationToken), rejected => LoginError.InvalidCredentials(rejected.Reason));

    /// <summary>Gives up on a QR login.</summary>
    /// <response code="204">Abandoned (or already gone)</response>
    [HttpDelete("login/device/{pendingId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [MapToApiVersion("1")]
    public IActionResult AbandonDeviceLogin([FromRoute] string pendingId)
    {
        login.AbandonDeviceLogin(pendingId);
        return NoContent();
    }

    /// <summary>The original's Forgot Password: VRChat mails a password recovery link to the address.</summary>
    /// <response code="204">Requested</response>
    /// <response code="502">VRChat refused or could not be reached</response>
    [HttpPost("login/password-reset")]
    [Consumes(MediaTypeNames.Application.Json)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<VrcxProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> RequestPasswordReset([FromBody] PasswordResetRequest body, CancellationToken cancellationToken) =>
        Done(await login.RequestPasswordResetAsync(body.Email, cancellationToken));

    /// <summary>
    /// Log an account out: drops its VRChat session here and on VRChat's side. Its history stays.
    /// </summary>
    /// <response code="204">Logged out</response>
    /// <response code="404">No such account</response>
    [HttpDelete("{accountId}/session")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<VrcxProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> Logout([FromRoute] string accountId, CancellationToken cancellationToken) =>
        Done(await login.LogoutAsync(accountId, cancellationToken));

    /// <summary>
    /// The original's saved account click (relogin): logs in with the account's saved password.
    /// Answers needsTwoFactor as a password login does. A password VRChat refuses is removed.
    /// </summary>
    /// <response code="200">Logged in, or a second factor is needed (see status)</response>
    /// <response code="401">VRChat refused the saved password (it was removed) or another 401</response>
    /// <response code="403">The primary password is missing or wrong</response>
    /// <response code="404">No password is saved for the account</response>
    /// <response code="502">VRChat could not be reached</response>
    [HttpPost("{accountId}/relogin")]
    [Consumes(MediaTypeNames.Application.Json)]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<VrcxProblem>(StatusCodes.Status401Unauthorized, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<VrcxProblem>(StatusCodes.Status403Forbidden, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<VrcxProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<VrcxProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> Relogin([FromRoute] string accountId, [FromBody] ReloginRequest body, CancellationToken cancellationToken) =>
        Answer(await login.ReloginAsync(accountId, body.PrimaryPassword, cancellationToken), rejected => LoginError.InvalidCredentials(rejected.Reason));

    /// <summary>
    /// The original's saved account delete: forgets the saved password and logs the session out. Its
    /// history stays. With no saved account left the primary password turns off.
    /// </summary>
    /// <response code="204">Removed</response>
    /// <response code="404">No such account</response>
    [HttpDelete("{accountId}/saved-login")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<VrcxProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> DeleteSavedLogin([FromRoute] string accountId, CancellationToken cancellationToken) =>
        Done(await login.DeleteSavedLoginAsync(accountId, cancellationToken));

    /// <summary>Whether new saved passwords are encrypted with the primary password.</summary>
    /// <response code="200">The state</response>
    [HttpGet("primary-password")]
    [ProducesResponseType<PrimaryPasswordResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [MapToApiVersion("1")]
    public async Task<PrimaryPasswordResponse> GetPrimaryPassword(CancellationToken cancellationToken) =>
        new() { Enabled = await login.PrimaryPasswordEnabledAsync(cancellationToken) };

    /// <summary>
    /// The original's Primary Password setting. On: every saved password is encrypted with it, and
    /// the server can no longer log in again by itself. Off: needs it, and decrypts them again.
    /// </summary>
    /// <response code="200">The new state</response>
    /// <response code="403">Turning it off with a wrong primary password; nothing changed</response>
    /// <response code="409">Turning it on while it is on</response>
    [HttpPut("primary-password")]
    [Consumes(MediaTypeNames.Application.Json)]
    [ProducesResponseType<PrimaryPasswordResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<VrcxProblem>(StatusCodes.Status403Forbidden, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<VrcxProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> PutPrimaryPassword([FromBody] PrimaryPasswordRequest body, CancellationToken cancellationToken) =>
        await login.SetPrimaryPasswordAsync(body.Enabled, body.Password, cancellationToken) is { } problem
            ? Problem(problem)
            : Ok(new PrimaryPasswordResponse { Enabled = body.Enabled });

    /// <summary>
    /// A login step as its response: the problem; pending while a QR login waits; logged in; a second
    /// factor needed; an unknown attempt; or rejected, which each step words its own way.
    /// </summary>
    private IActionResult Answer(Result<LoginOutcome> attempt, Func<LoginOutcome.Rejected, VrcxProblem> rejected) => attempt switch
    {
        VrcxProblem problem => Problem(problem),
        LoginOutcome.Pending => Ok(new LoginResponse { Status = "pending" }),
        LoginOutcome.LoggedIn ok => Ok(new LoginResponse { Status = "loggedIn", UserId = ok.UserId, DisplayName = ok.DisplayName }),
        LoginOutcome.NeedsTwoFactor two => Ok(new LoginResponse { Status = "needsTwoFactor", PendingId = two.PendingId, Methods = two.Methods }),
        LoginOutcome.UnknownAttempt => Problem(LoginError.UnknownAttempt),
        LoginOutcome.Rejected refused => Problem(rejected(refused)),
    };
}
