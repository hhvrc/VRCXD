using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using OpenShock.Internal.Common.Problems;
using VRCX.Common.Errors;
using VRCX.Common.Options;
using VRCX.Common.Services.Sessions;
using VRCX.Common.Services.Sync;
using VRCX.Common.VrcxDb;
using VRCX.Vrchat;
using VRCX.Vrchat.Models;

namespace VRCX.Common.Features.Accounts;

/// <summary>How a login step ended: the outcome to answer with, or the problem. Neither means it is still pending (a QR login waiting for approval).</summary>
public readonly record struct LoginAttempt(LoginOutcome? Outcome, OpenShockProblem? Problem);

/// <summary>A QR login waiting for approval, or the problem starting it.</summary>
public readonly record struct DeviceLoginStart(string PendingId, DeviceCodeGrant? Code, OpenShockProblem? Problem);

/// <summary>
/// Logging VRChat accounts in and out of this server: the password login with its second factor,
/// the original's QR login, logging in again with a saved password, the saved passwords and the
/// primary password that protects them. A fresh login starts a full sync in the background.
/// </summary>
public sealed class AccountLogin(
    VrcxContext db, IAccountSessionManager sessions, SavedCredentialStore credentials, IAccountSyncService sync, IServiceProvider services, ILogger<AccountLogin> logger)
{
    /// <summary>
    /// Logs an account in with its password. The outcome is logged in, needs a second factor, or
    /// rejected. With <paramref name="saveCredentials"/> the password is kept for logging in again.
    /// </summary>
    public async Task<LoginAttempt> LoginAsync(
        string username, string password, string? endpointAddress, string? websocketAddress, bool saveCredentials, string? primaryPassword, CancellationToken cancellationToken)
    {
        var vrchatOptions = services.GetService<VrchatOptions>() ?? new VrchatOptions();
        if (!VrchatEndpoint.TryParse(endpointAddress, websocketAddress, vrchatOptions.EndpointHosts, out var endpoint, out var endpointError))
            return Failed(new OpenShockProblem("Login.InvalidEndpoint", "The custom endpoint is not allowed", System.Net.HttpStatusCode.BadRequest, endpointError));

        // The original's Save Credentials: with the primary password on, the password is encrypted
        // with it, and a wrong one stops the login before VRChat is asked.
        SealedPassword? sealedPassword = null;
        if (saveCredentials)
        {
            string? sealWith = null;
            if (await credentials.PrimaryPasswordEnabledAsync(cancellationToken))
            {
                if (string.IsNullOrEmpty(primaryPassword)) return Failed(SavedLoginError.PrimaryPasswordRequired);
                if (!await credentials.CheckPrimaryPasswordAsync(primaryPassword, cancellationToken)) return Failed(SavedLoginError.IncorrectPrimaryPassword);
                sealWith = primaryPassword;
            }
            sealedPassword = credentials.Seal(password, sealWith);
        }

        LoginOutcome outcome;
        try
        {
            outcome = await sessions.LoginAsync(username, password, endpoint.IsDefault ? null : endpoint, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or VrchatApiException)
        {
            logger.LogWarning(ex, "VRChat login failed to complete");
            return Failed(LoginError.VrchatUnavailable(ex.Message));
        }

        if (sealedPassword is not null)
        {
            if (outcome is LoginOutcome.LoggedIn saved) await credentials.SaveAsync(saved.UserId, username, sealedPassword, cancellationToken);
            else if (outcome is LoginOutcome.NeedsTwoFactor pending) credentials.Hold(pending.PendingId, username, sealedPassword);
        }
        if (outcome is LoginOutcome.LoggedIn loggedIn) StartInitialSync(loggedIn.UserId);
        return new LoginAttempt(outcome, null);
    }

    /// <summary>Finishes a login that needed a second factor. <paramref name="methodName"/> is totp, otp (or recovery) or emailOtp (or email).</summary>
    public async Task<LoginAttempt> CompleteTwoFactorAsync(string pendingId, string methodName, string code, CancellationToken cancellationToken)
    {
        var method = methodName.ToLowerInvariant() switch
        {
            "totp" => TwoFactorMethod.Totp,
            "otp" or "recovery" => TwoFactorMethod.RecoveryCode,
            "emailotp" or "email" => TwoFactorMethod.EmailOtp,
            _ => (TwoFactorMethod?)null,
        };
        if (method is null) return Failed(LoginError.UnknownMethod);

        LoginOutcome outcome;
        try
        {
            outcome = await sessions.CompleteTwoFactorAsync(pendingId, method.Value, code, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or VrchatApiException)
        {
            logger.LogWarning(ex, "VRChat two-factor verification failed to complete");
            return Failed(LoginError.VrchatUnavailable(ex.Message));
        }

        if (outcome is LoginOutcome.LoggedIn verified)
        {
            await credentials.CommitHeldAsync(pendingId, verified.UserId, cancellationToken);
            StartInitialSync(verified.UserId);
        }
        return new LoginAttempt(outcome, null);
    }

    /// <summary>The original's QR login: a code for another device signed in to VRChat to approve.</summary>
    public async Task<DeviceLoginStart> StartDeviceLoginAsync(CancellationToken cancellationToken)
    {
        try
        {
            var (pendingId, code) = await sessions.StartDeviceLoginAsync(cancellationToken);
            return new DeviceLoginStart(pendingId, code, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or VrchatApiException)
        {
            logger.LogWarning(ex, "VRChat QR login failed to start");
            return new DeviceLoginStart("", null, LoginError.VrchatUnavailable(ex.Message));
        }
    }

    /// <summary>Whether a QR login has been approved: no outcome until it is, then logged in.</summary>
    public async Task<LoginAttempt> PollDeviceLoginAsync(string pendingId, CancellationToken cancellationToken)
    {
        LoginOutcome? outcome;
        try
        {
            outcome = await sessions.PollDeviceLoginAsync(pendingId, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or VrchatApiException)
        {
            sessions.AbandonPending(pendingId);
            logger.LogWarning(ex, "VRChat QR login failed");
            return Failed(LoginError.VrchatUnavailable(ex.Message));
        }

        if (outcome is LoginOutcome.LoggedIn loggedIn) StartInitialSync(loggedIn.UserId);
        return new LoginAttempt(outcome, null);
    }

    /// <summary>Gives up on a QR login.</summary>
    public void AbandonDeviceLogin(string pendingId) => sessions.AbandonPending(pendingId);

    /// <summary>The original's Forgot Password: VRChat mails a password recovery link to the address.</summary>
    public async Task<OpenShockProblem?> RequestPasswordResetAsync(string email, CancellationToken cancellationToken)
    {
        try
        {
            await sessions.RequestPasswordResetAsync(email.Trim(), cancellationToken);
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or VrchatApiException)
        {
            logger.LogWarning(ex, "VRChat password reset failed");
            return LoginError.VrchatUnavailable(ex.Message);
        }
    }

    /// <summary>Logs an account out: drops its VRChat session here and on VRChat's side. Its history stays.</summary>
    public async Task<OpenShockProblem?> LogoutAsync(string accountId, CancellationToken cancellationToken)
    {
        if (!await db.Accounts.AnyAsync(a => a.UserId == accountId, cancellationToken)) return AccountError.AccountNotFound;
        await sessions.LogoutAsync(accountId, cancellationToken);
        return null;
    }

    /// <summary>
    /// The original's saved account click (relogin): logs in with the account's saved password. A
    /// password VRChat refuses is removed, and so is one that can no longer be decrypted.
    /// </summary>
    public async Task<LoginAttempt> ReloginAsync(string accountId, string? primaryPassword, CancellationToken cancellationToken)
    {
        var saved = await credentials.FindAsync(accountId, cancellationToken);
        if (saved is null) return Failed(SavedLoginError.NotFound);
        var account = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.UserId == accountId, cancellationToken);
        if (account is null) return Failed(SavedLoginError.NotFound);

        string password;
        try
        {
            password = credentials.Open(saved, primaryPassword);
        }
        catch (PrimaryPasswordException)
        {
            return Failed(string.IsNullOrEmpty(primaryPassword) ? SavedLoginError.PrimaryPasswordRequired : SavedLoginError.IncorrectPrimaryPassword);
        }
        catch (CryptographicException ex)
        {
            // The keys are gone (a new keys directory): the password cannot be read again.
            logger.LogWarning(ex, "The saved password for {AccountId} cannot be decrypted", accountId);
            await credentials.DeleteAsync(accountId, cancellationToken);
            return Failed(SavedLoginError.Invalid);
        }

        var endpoint = account.ApiEndpoint is null ? null : new VrchatEndpoint(new Uri(account.ApiEndpoint), new Uri(account.PipelineEndpoint ?? VrchatEndpoint.Default.Pipeline.ToString()));
        LoginOutcome outcome;
        try
        {
            outcome = await sessions.LoginAsync(saved.Username, password, endpoint, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or VrchatApiException)
        {
            logger.LogWarning(ex, "VRChat login with saved credentials failed to complete");
            return Failed(LoginError.VrchatUnavailable(ex.Message));
        }

        if (outcome is LoginOutcome.LoggedIn loggedIn) StartInitialSync(loggedIn.UserId);
        if (outcome is LoginOutcome.Rejected rejected && rejected.Reason.Contains("Invalid Username/Email or Password", StringComparison.Ordinal))
        {
            await credentials.DeleteAsync(accountId, cancellationToken);
            return Failed(SavedLoginError.Invalid);
        }
        return new LoginAttempt(outcome, null);
    }

    /// <summary>
    /// The original's saved account delete: forgets the saved password and logs the session out. Its
    /// history stays. With no saved account left the primary password turns off.
    /// </summary>
    public async Task<OpenShockProblem?> DeleteSavedLoginAsync(string accountId, CancellationToken cancellationToken)
    {
        if (!await db.Accounts.AnyAsync(a => a.UserId == accountId, cancellationToken)) return AccountError.AccountNotFound;
        await credentials.DeleteAsync(accountId, cancellationToken);
        await sessions.LogoutAsync(accountId, cancellationToken);
        return null;
    }

    /// <summary>Whether new saved passwords are encrypted with the primary password.</summary>
    public Task<bool> PrimaryPasswordEnabledAsync(CancellationToken cancellationToken) => credentials.PrimaryPasswordEnabledAsync(cancellationToken);

    /// <summary>
    /// The original's Primary Password setting. On: every saved password is encrypted with it, and
    /// the server can no longer log in again by itself. Off: needs it, and decrypts them again.
    /// Answers the problem, or null when the setting is now <paramref name="enabled"/>.
    /// </summary>
    public async Task<OpenShockProblem?> SetPrimaryPasswordAsync(bool enabled, string password, CancellationToken cancellationToken)
    {
        var current = await credentials.PrimaryPasswordEnabledAsync(cancellationToken);
        if (enabled && current) return SavedLoginError.PrimaryPasswordAlreadyEnabled;
        if (!enabled && !current) return null;
        try
        {
            await credentials.SetPrimaryPasswordAsync(enabled, password, cancellationToken);
        }
        catch (PrimaryPasswordException)
        {
            return SavedLoginError.IncorrectPrimaryPassword;
        }
        return null;
    }

    private static LoginAttempt Failed(OpenShockProblem problem) => new(null, problem);

    // A fresh login should not sit empty until the next Hangfire tick: pull everything now, in
    // the background, with the same error handling the jobs have.
    private void StartInitialSync(string accountId)
    {
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
}
