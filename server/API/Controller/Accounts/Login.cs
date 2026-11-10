using System.Net.Mime;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenShock.Internal.Common.Problems;
using VRCX.API.Models.Requests;
using VRCX.API.Models.Response;
using VRCX.Common.Errors;
using VRCX.Common.Services.Sessions;
using VRCX.Vrchat;

namespace VRCX.API.Controller.Accounts;

public sealed partial class AccountsController
{
    /// <summary>
    /// Log a VRChat account into this server. Answers needsTwoFactor with a pendingId when the account has 2FA.
    /// </summary>
    /// <response code="200">Logged in, or a second factor is needed (see status)</response>
    /// <response code="401">VRChat rejected the credentials</response>
    /// <response code="502">VRChat could not be reached</response>
    [HttpPost("login")]
    [Consumes(MediaTypeNames.Application.Json)]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status401Unauthorized, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> Login([FromBody] LoginRequest body, CancellationToken cancellationToken)
    {
        var vrchatOptions = HttpContext.RequestServices.GetService<VRCX.Common.Options.VrchatOptions>() ?? new VRCX.Common.Options.VrchatOptions();
        if (!VrchatEndpoint.TryParse(body.Endpoint, body.Websocket, vrchatOptions.EndpointHosts, out var endpoint, out var endpointError))
            return Problem(new OpenShockProblem("Login.InvalidEndpoint", "The custom endpoint is not allowed", System.Net.HttpStatusCode.BadRequest, endpointError));

        // The original's Save Credentials: with the primary password on, the password is encrypted
        // with it, and a wrong one stops the login before VRChat is asked.
        SealedPassword? sealedPassword = null;
        if (body.SaveCredentials)
        {
            string? primaryPassword = null;
            if (await _credentials.PrimaryPasswordEnabledAsync(cancellationToken))
            {
                if (string.IsNullOrEmpty(body.PrimaryPassword)) return Problem(SavedLoginError.PrimaryPasswordRequired);
                if (!await _credentials.CheckPrimaryPasswordAsync(body.PrimaryPassword, cancellationToken)) return Problem(SavedLoginError.IncorrectPrimaryPassword);
                primaryPassword = body.PrimaryPassword;
            }
            sealedPassword = _credentials.Seal(body.Password, primaryPassword);
        }

        LoginOutcome outcome;
        try
        {
            outcome = await _sessions.LoginAsync(body.Username, body.Password, endpoint.IsDefault ? null : endpoint, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or VrchatApiException)
        {
            _logger.LogWarning(ex, "VRChat login failed to complete");
            return Problem(LoginError.VrchatUnavailable(ex.Message));
        }

        if (sealedPassword is not null)
        {
            if (outcome is LoginOutcome.LoggedIn saved) await _credentials.SaveAsync(saved.UserId, body.Username, sealedPassword, cancellationToken);
            else if (outcome is LoginOutcome.NeedsTwoFactor pending) _credentials.Hold(pending.PendingId, body.Username, sealedPassword);
        }
        if (outcome is LoginOutcome.LoggedIn loggedIn) StartInitialSync(loggedIn.UserId);
        return outcome switch
        {
            LoginOutcome.LoggedIn ok => Ok(new LoginResponse { Status = "loggedIn", UserId = ok.UserId, DisplayName = ok.DisplayName }),
            LoginOutcome.NeedsTwoFactor two => Ok(new LoginResponse { Status = "needsTwoFactor", PendingId = two.PendingId, Methods = two.Methods }),
            LoginOutcome.Rejected rejected => Problem(LoginError.InvalidCredentials(rejected.Reason)),
            _ => throw new InvalidOperationException($"Unexpected login outcome {outcome.GetType().Name}"),
        };
    }

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
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status400BadRequest, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status401Unauthorized, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> CompleteTwoFactor([FromBody] TwoFactorRequest body, CancellationToken cancellationToken)
    {
        var method = body.Method.ToLowerInvariant() switch
        {
            "totp" => TwoFactorMethod.Totp,
            "otp" or "recovery" => TwoFactorMethod.RecoveryCode,
            "emailotp" or "email" => TwoFactorMethod.EmailOtp,
            _ => (TwoFactorMethod?)null,
        };
        if (method is null) return Problem(LoginError.UnknownMethod);

        LoginOutcome outcome;
        try
        {
            outcome = await _sessions.CompleteTwoFactorAsync(body.PendingId, method.Value, body.Code, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or VrchatApiException)
        {
            _logger.LogWarning(ex, "VRChat two-factor verification failed to complete");
            return Problem(LoginError.VrchatUnavailable(ex.Message));
        }

        if (outcome is LoginOutcome.LoggedIn verified)
        {
            await _credentials.CommitHeldAsync(body.PendingId, verified.UserId, cancellationToken);
            StartInitialSync(verified.UserId);
        }
        return outcome switch
        {
            LoginOutcome.LoggedIn ok => Ok(new LoginResponse { Status = "loggedIn", UserId = ok.UserId, DisplayName = ok.DisplayName }),
            LoginOutcome.UnknownAttempt => Problem(LoginError.UnknownAttempt),
            LoginOutcome.Rejected => Problem(LoginError.CodeRejected),
            _ => throw new InvalidOperationException($"Unexpected login outcome {outcome.GetType().Name}"),
        };
    }

    /// <summary>
    /// The original's QR login: a code for another device signed in to VRChat to approve. Show the
    /// verification address as a QR code, then poll /accounts/login/device/{pendingId}.
    /// </summary>
    /// <response code="200">The code is waiting for approval</response>
    /// <response code="502">VRChat could not be reached</response>
    [HttpPost("login/device")]
    [ProducesResponseType<DeviceLoginResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> StartDeviceLogin(CancellationToken cancellationToken)
    {
        try
        {
            var (pendingId, code) = await _sessions.StartDeviceLoginAsync(cancellationToken);
            return Ok(new DeviceLoginResponse { PendingId = pendingId, VerificationUrl = code.VerificationUrlComplete, Interval = code.Interval, ExpiresIn = code.ExpiresIn });
        }
        catch (Exception ex) when (ex is HttpRequestException or VrchatApiException)
        {
            _logger.LogWarning(ex, "VRChat QR login failed to start");
            return Problem(LoginError.VrchatUnavailable(ex.Message));
        }
    }

    /// <summary>Whether a QR login has been approved: pending until it is, then loggedIn.</summary>
    /// <response code="200">pending or loggedIn</response>
    /// <response code="404">Unknown or abandoned pendingId</response>
    /// <response code="502">VRChat refused the code (expired) or could not be reached</response>
    [HttpPost("login/device/{pendingId}")]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> PollDeviceLogin([FromRoute] string pendingId, CancellationToken cancellationToken)
    {
        LoginOutcome? outcome;
        try
        {
            outcome = await _sessions.PollDeviceLoginAsync(pendingId, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or VrchatApiException)
        {
            _sessions.AbandonPending(pendingId);
            _logger.LogWarning(ex, "VRChat QR login failed");
            return Problem(LoginError.VrchatUnavailable(ex.Message));
        }

        if (outcome is LoginOutcome.LoggedIn loggedIn) StartInitialSync(loggedIn.UserId);
        return outcome switch
        {
            null => Ok(new LoginResponse { Status = "pending" }),
            LoginOutcome.LoggedIn ok => Ok(new LoginResponse { Status = "loggedIn", UserId = ok.UserId, DisplayName = ok.DisplayName }),
            LoginOutcome.UnknownAttempt => Problem(LoginError.UnknownAttempt),
            _ => throw new InvalidOperationException($"Unexpected login outcome {outcome.GetType().Name}"),
        };
    }

    /// <summary>Gives up on a QR login.</summary>
    /// <response code="204">Abandoned (or already gone)</response>
    [HttpDelete("login/device/{pendingId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [MapToApiVersion("1")]
    public IActionResult AbandonDeviceLogin([FromRoute] string pendingId)
    {
        _sessions.AbandonPending(pendingId);
        return NoContent();
    }

    /// <summary>The original's Forgot Password: VRChat mails a password recovery link to the address.</summary>
    /// <response code="204">Requested</response>
    /// <response code="502">VRChat refused or could not be reached</response>
    [HttpPost("login/password-reset")]
    [Consumes(MediaTypeNames.Application.Json)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> RequestPasswordReset([FromBody] PasswordResetRequest body, CancellationToken cancellationToken)
    {
        try
        {
            await _sessions.RequestPasswordResetAsync(body.Email.Trim(), cancellationToken);
            return NoContent();
        }
        catch (Exception ex) when (ex is HttpRequestException or VrchatApiException)
        {
            _logger.LogWarning(ex, "VRChat password reset failed");
            return Problem(LoginError.VrchatUnavailable(ex.Message));
        }
    }

    // A fresh login should not sit empty until the next Hangfire tick: pull everything now, in
    // the background, with the same error handling the jobs have.
    private void StartInitialSync(string accountId)
    {
        var sync = _sync;
        var sessions = _sessions;
        var logger = _logger;
        _ = Task.Run(async () =>
        {
            try
            {
                await sync.SyncEverythingAsync(accountId, CancellationToken.None);
            }
            catch (VrchatUnauthorizedException)
            {
                await sessions.InvalidateAsync(accountId, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Initial sync after login failed for {Account}", accountId);
            }
        });
    }

    /// <summary>
    /// Log an account out: drops its VRChat session here and on VRChat's side. Its history stays.
    /// </summary>
    /// <response code="204">Logged out</response>
    /// <response code="404">No such account</response>
    [HttpDelete("{accountId}/session")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> Logout([FromRoute] string accountId, CancellationToken cancellationToken)
    {
        if (!await _db.Accounts.AnyAsync(a => a.UserId == accountId, cancellationToken)) return Problem(AccountError.AccountNotFound);
        await _sessions.LogoutAsync(accountId, cancellationToken);
        return NoContent();
    }
}
