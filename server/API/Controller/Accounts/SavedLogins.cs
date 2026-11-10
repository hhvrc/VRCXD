using System.Net;
using System.Net.Mime;
using System.Security.Cryptography;
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

/// <summary>The saved login problems: the original's toasts for them are picked by type.</summary>
public static class SavedLoginError
{
    public static OpenShockProblem NotFound => new("SavedLogin.NotFound", "No password is saved for this account", HttpStatusCode.NotFound);
    public static OpenShockProblem Invalid => new("SavedLogin.Invalid", "Saved credentials are no longer valid", HttpStatusCode.Unauthorized);
    public static OpenShockProblem PrimaryPasswordRequired => new("SavedLogin.PrimaryPasswordRequired", "The primary password is required", HttpStatusCode.Forbidden);
    public static OpenShockProblem IncorrectPrimaryPassword => new("SavedLogin.IncorrectPrimaryPassword", "Incorrect primary password", HttpStatusCode.Forbidden);
    public static OpenShockProblem PrimaryPasswordAlreadyEnabled => new("SavedLogin.PrimaryPasswordAlreadyEnabled", "The primary password is already on", HttpStatusCode.Conflict);
}

public sealed partial class AccountsController
{
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
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status401Unauthorized, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status403Forbidden, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> Relogin([FromRoute] string accountId, [FromBody] ReloginRequest body, CancellationToken cancellationToken)
    {
        var saved = await _credentials.FindAsync(accountId, cancellationToken);
        if (saved is null) return Problem(SavedLoginError.NotFound);
        var account = await _db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.UserId == accountId, cancellationToken);
        if (account is null) return Problem(SavedLoginError.NotFound);

        string password;
        try
        {
            password = _credentials.Open(saved, body.PrimaryPassword);
        }
        catch (PrimaryPasswordException)
        {
            return Problem(string.IsNullOrEmpty(body.PrimaryPassword) ? SavedLoginError.PrimaryPasswordRequired : SavedLoginError.IncorrectPrimaryPassword);
        }
        catch (CryptographicException ex)
        {
            // The keys are gone (a new keys directory): the password cannot be read again.
            _logger.LogWarning(ex, "The saved password for {AccountId} cannot be decrypted", accountId);
            await _credentials.DeleteAsync(accountId, cancellationToken);
            return Problem(SavedLoginError.Invalid);
        }

        var endpoint = account.ApiEndpoint is null ? null : new VrchatEndpoint(new Uri(account.ApiEndpoint), new Uri(account.PipelineEndpoint ?? VrchatEndpoint.Default.Pipeline.ToString()));
        LoginOutcome outcome;
        try
        {
            outcome = await _sessions.LoginAsync(saved.Username, password, endpoint, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or VrchatApiException)
        {
            _logger.LogWarning(ex, "VRChat login with saved credentials failed to complete");
            return Problem(LoginError.VrchatUnavailable(ex.Message));
        }

        if (outcome is LoginOutcome.LoggedIn loggedIn) StartInitialSync(loggedIn.UserId);
        switch (outcome)
        {
            case LoginOutcome.LoggedIn ok:
                return Ok(new LoginResponse { Status = "loggedIn", UserId = ok.UserId, DisplayName = ok.DisplayName });
            case LoginOutcome.NeedsTwoFactor two:
                return Ok(new LoginResponse { Status = "needsTwoFactor", PendingId = two.PendingId, Methods = two.Methods });
            case LoginOutcome.Rejected rejected when rejected.Reason.Contains("Invalid Username/Email or Password", StringComparison.Ordinal):
                await _credentials.DeleteAsync(accountId, cancellationToken);
                return Problem(SavedLoginError.Invalid);
            case LoginOutcome.Rejected rejected:
                return Problem(LoginError.InvalidCredentials(rejected.Reason));
            default:
                throw new InvalidOperationException($"Unexpected login outcome {outcome.GetType().Name}");
        }
    }

    /// <summary>
    /// The original's saved account delete: forgets the saved password and logs the session out. Its
    /// history stays. With no saved account left the primary password turns off.
    /// </summary>
    /// <response code="204">Removed</response>
    /// <response code="404">No such account</response>
    [HttpDelete("{accountId}/saved-login")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> DeleteSavedLogin([FromRoute] string accountId, CancellationToken cancellationToken)
    {
        if (!await _db.Accounts.AnyAsync(a => a.UserId == accountId, cancellationToken)) return Problem(AccountError.AccountNotFound);
        await _credentials.DeleteAsync(accountId, cancellationToken);
        await _sessions.LogoutAsync(accountId, cancellationToken);
        return NoContent();
    }

    /// <summary>Whether new saved passwords are encrypted with the primary password.</summary>
    /// <response code="200">The state</response>
    [HttpGet("primary-password")]
    [ProducesResponseType<PrimaryPasswordResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [MapToApiVersion("1")]
    public async Task<PrimaryPasswordResponse> GetPrimaryPassword(CancellationToken cancellationToken) =>
        new() { Enabled = await _credentials.PrimaryPasswordEnabledAsync(cancellationToken) };

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
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status403Forbidden, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> PutPrimaryPassword([FromBody] PrimaryPasswordRequest body, CancellationToken cancellationToken)
    {
        var enabled = await _credentials.PrimaryPasswordEnabledAsync(cancellationToken);
        if (body.Enabled && enabled) return Problem(SavedLoginError.PrimaryPasswordAlreadyEnabled);
        if (!body.Enabled && !enabled) return Ok(new PrimaryPasswordResponse { Enabled = false });
        try
        {
            await _credentials.SetPrimaryPasswordAsync(body.Enabled, body.Password, cancellationToken);
        }
        catch (PrimaryPasswordException)
        {
            return Problem(SavedLoginError.IncorrectPrimaryPassword);
        }
        return Ok(new PrimaryPasswordResponse { Enabled = body.Enabled });
    }
}
