using Microsoft.AspNetCore.DataProtection;
using System.Collections.Concurrent;
using System.Net;
using Microsoft.EntityFrameworkCore;
using VRCX.Common.Services.Ingestion;
using VRCX.Common.Services.Live;
using VRCX.Common.VrcxDb;
using VRCX.Vrchat;
using VRCX.Vrchat.Models;

namespace VRCX.Common.Services.Sessions;

/// <summary>Builds the VRChat clients the session manager hands out. Integration tests swap in a fake VRChat here.</summary>
public interface IVrchatClientFactory
{
    VrchatApiClient Create(CookieContainer? cookies = null, VrchatEndpoint? endpoint = null);
}

/// <summary>Every client shares <see cref="VrchatRateLimiter.Shared"/>: VRChat limits per IP, so accounts must not each get a budget.</summary>
public sealed class VrchatClientFactory : IVrchatClientFactory
{
    public VrchatApiClient Create(CookieContainer? cookies = null, VrchatEndpoint? endpoint = null) =>
        VrchatApiClient.Create(cookies, rateLimiter: VrchatRateLimiter.Shared, endpoint: endpoint);
}

/// <summary>
/// One <see cref="VrchatApiClient"/> per logged-in account, kept for the life of the process;
/// cookies are persisted on the accounts row so a restart resumes every session without a
/// login. Logins waiting on a 2FA code sit in a pending slot keyed by a throwaway id until the
/// code verifies and we learn which account it is.
/// </summary>
public interface IAccountSessionManager
{
    /// <summary>Accounts with a live client. What the background services iterate.</summary>
    IReadOnlyCollection<string> ActiveAccountIds { get; }
    VrchatApiClient? Get(string accountId);
    VrchatApiClient Require(string accountId);
    Task<LoginOutcome> LoginAsync(string username, string password, CancellationToken ct) => LoginAsync(username, password, null, ct);
    /// <summary>Password login against a custom endpoint (the original's development endpoint option); null is VRChat's.</summary>
    Task<LoginOutcome> LoginAsync(string username, string password, VrchatEndpoint? endpoint, CancellationToken ct);
    Task<LoginOutcome> CompleteTwoFactorAsync(string pendingId, TwoFactorMethod method, string code, CancellationToken ct);
    void AbandonPending(string pendingId);
    /// <summary>The original's QR login: a code waiting, under a pendingId, for another device to approve it.</summary>
    Task<(string PendingId, DeviceCodeGrant Code)> StartDeviceLoginAsync(CancellationToken ct);
    /// <summary>Null while the code waits for approval; LoggedIn once approved; UnknownAttempt for an unknown pendingId.</summary>
    Task<LoginOutcome?> PollDeviceLoginAsync(string pendingId, CancellationToken ct);
    /// <summary>Asks VRChat to mail a password recovery link, on a throwaway client.</summary>
    Task RequestPasswordResetAsync(string email, CancellationToken ct);
    /// <summary>Re-saves the cookie jar (VRChat rotates cookies occasionally). Cheap; call after sync cycles.</summary>
    Task PersistCookiesAsync(string accountId, CancellationToken ct);
    /// <summary>Loads a client for every valid account this process has none for yet. Safe to call repeatedly.</summary>
    Task<int> ResumeAllAsync(CancellationToken ct);
    /// <summary>VRChat said 401: drop the client and mark the account so nothing else keeps hammering it.</summary>
    Task InvalidateAsync(string accountId, CancellationToken ct);
    Task LogoutAsync(string accountId, CancellationToken ct);
    /// <summary>
    /// The original's auto login: logs in again with the account's saved password after its session
    /// expired. <see cref="InvalidateAsync"/> starts it in the background; returns what happened.
    /// </summary>
    Task<AutoLoginOutcome> AutoLoginAsync(string accountId, CancellationToken ct);
}

public enum AutoLoginOutcome
{
    /// <summary>One is already running for the account.</summary>
    Busy,
    NoSavedCredentials,
    /// <summary>The saved password needs the primary password, which disables auto login.</summary>
    PrimaryPassword,
    /// <summary>Three attempts within the past hour already.</summary>
    TooManyAttempts,
    Success,
    /// <summary>VRChat wants a 2FA code: the user has to log in.</summary>
    NeedsTwoFactor,
    /// <summary>VRChat refused the saved password, so it was removed.</summary>
    InvalidCredentials,
    Failed,
}

/// <inheritdoc />
public sealed class AccountSessionManager(
    IDbContextFactory<VrcxContext> dbFactory,
    VrchatStore store,
    IVrchatClientFactory clientFactory,
    ILiveEventBus bus,
    Microsoft.AspNetCore.DataProtection.IDataProtectionProvider protection,
    SavedCredentialStore credentials,
    TimeProvider time,
    ILogger<AccountSessionManager> logger) : IAccountSessionManager, IDisposable
{
    private readonly CancellationTokenSource _stopping = new();

    // Session cookies sign in as the account without password or 2FA, so they are stored encrypted.
    private readonly Microsoft.AspNetCore.DataProtection.IDataProtector _cookies = protection.CreateProtector("VRCX.SessionCookies");
    private const string EncryptedPrefix = "dp1:";

    private readonly ConcurrentDictionary<string, VrchatApiClient> _byAccount = new();
    private readonly ConcurrentDictionary<string, VrchatApiClient> _pending = new();

    public IReadOnlyCollection<string> ActiveAccountIds => _byAccount.Keys.ToList();

    public VrchatApiClient? Get(string accountId) => _byAccount.GetValueOrDefault(accountId);

    public VrchatApiClient Require(string accountId) =>
        Get(accountId) ?? throw new InvalidOperationException($"No VRChat session for {accountId}");

    // ---- login ------------------------------------------------------------

    public Task<LoginOutcome> LoginAsync(string username, string password, VrchatEndpoint? endpoint, CancellationToken ct) =>
        LoginAsync(username, password, endpoint, "", ct);

    private async Task<LoginOutcome> LoginAsync(string username, string password, VrchatEndpoint? endpoint, string autoLogin, CancellationToken ct)
    {
        var client = clientFactory.Create(endpoint: endpoint);
        LoginResult result;
        try
        {
            result = await client.LoginAsync(username, password, ct);
        }
        catch
        {
            client.Dispose();
            throw;
        }

        switch (result)
        {
            case LoginResult.LoggedIn ok:
                await AdoptAsync(client, ok.User, username, ct, autoLogin);
                return new LoginOutcome.LoggedIn(ok.User.Id, ok.User.DisplayName);
            case LoginResult.RequiresTwoFactor twoFactor:
                var pendingId = Guid.NewGuid().ToString("N");
                _pending[pendingId] = client;
                return new LoginOutcome.NeedsTwoFactor(pendingId, twoFactor.Methods);
            case LoginResult.InvalidCredentials bad:
                client.Dispose();
                return new LoginOutcome.Rejected(bad.Message ?? "Invalid username or password");
            default:
                client.Dispose();
                throw new InvalidOperationException($"Unexpected login result {result.GetType().Name}");
        }
    }

    public async Task<LoginOutcome> CompleteTwoFactorAsync(string pendingId, TwoFactorMethod method, string code, CancellationToken ct)
    {
        if (!_pending.TryGetValue(pendingId, out var client))
            return new LoginOutcome.UnknownAttempt();

        if (!await client.VerifyTwoFactorAsync(method, code, ct))
            return new LoginOutcome.Rejected("The code was not accepted");

        var user = await client.GetCurrentUserAsync(ct);
        if (user.RequiresTwoFactorAuth is { Count: > 0 })
            return new LoginOutcome.Rejected("VRChat still wants a second factor after verifying");

        _pending.TryRemove(pendingId, out _);
        await AdoptAsync(client, user, null, ct);
        return new LoginOutcome.LoggedIn(user.Id, user.DisplayName);
    }

    private readonly ConcurrentDictionary<string, string> _deviceCodes = new();

    public async Task<(string PendingId, DeviceCodeGrant Code)> StartDeviceLoginAsync(CancellationToken ct)
    {
        var client = clientFactory.Create();
        DeviceCodeGrant code;
        try
        {
            code = await client.StartDeviceLoginAsync(ct);
        }
        catch
        {
            client.Dispose();
            throw;
        }
        var pendingId = Guid.NewGuid().ToString("N");
        _pending[pendingId] = client;
        _deviceCodes[pendingId] = code.DeviceCode;
        return (pendingId, code);
    }

    public async Task<LoginOutcome?> PollDeviceLoginAsync(string pendingId, CancellationToken ct)
    {
        if (!_pending.TryGetValue(pendingId, out var client) || !_deviceCodes.TryGetValue(pendingId, out var deviceCode))
            return new LoginOutcome.UnknownAttempt();
        if (!await client.PollDeviceLoginAsync(deviceCode, ct)) return null;

        var user = await client.GetCurrentUserAsync(ct);
        _pending.TryRemove(pendingId, out _);
        _deviceCodes.TryRemove(pendingId, out _);
        await AdoptAsync(client, user, null, ct);
        return new LoginOutcome.LoggedIn(user.Id, user.DisplayName);
    }

    public async Task RequestPasswordResetAsync(string email, CancellationToken ct)
    {
        using var client = clientFactory.Create();
        await client.RequestPasswordResetAsync(email, ct);
    }

    public void AbandonPending(string pendingId)
    {
        _deviceCodes.TryRemove(pendingId, out _);
        if (_pending.TryRemove(pendingId, out var client)) client.Dispose();
    }

    private async Task AdoptAsync(VrchatApiClient client, CurrentUser user, string? username, CancellationToken ct, string autoLogin = "")
    {
        if (_byAccount.TryRemove(user.Id, out var previous) && !ReferenceEquals(previous, client)) previous.Dispose();
        _byAccount[user.Id] = client;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await store.UpsertCurrentUserAsync(db, user, ct);
        var account = await db.Accounts.FindAsync([user.Id], ct);
        if (account is null)
        {
            account = new Account { UserId = user.Id, LoggedInAt = DateTimeOffset.UtcNow };
            db.Accounts.Add(account);
        }
        account.Username = username ?? account.Username ?? user.Username;
        account.SessionValid = true;
        account.LoggedInAt = DateTimeOffset.UtcNow;
        account.Cookies = Snapshot(client.Cookies);
        account.ApiEndpoint = client.Endpoint.IsDefault ? null : client.Endpoint.Api.ToString();
        account.PipelineEndpoint = client.Endpoint.IsDefault ? null : client.Endpoint.Pipeline.ToString();
        await db.SaveChangesAsync(ct);
        logger.LogInformation("VRChat session for {DisplayName} ({UserId}) is live", user.DisplayName, user.Id);
        bus.Publish(LiveEvents.Account(user.Id, user.DisplayName, true, autoLogin));
    }

    // ---- persistence ------------------------------------------------------

    public async Task PersistCookiesAsync(string accountId, CancellationToken ct)
    {
        if (!_byAccount.TryGetValue(accountId, out var client)) return;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var account = await db.Accounts.FindAsync([accountId], ct);
        if (account is null) return;
        account.Cookies = Snapshot(client.Cookies);
        await db.SaveChangesAsync(ct);
    }

    public async Task<int> ResumeAllAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var accounts = await db.Accounts.AsNoTracking().Where(a => a.SessionValid).ToListAsync(ct);
        var resumed = 0;
        foreach (var account in accounts)
        {
            if (_byAccount.ContainsKey(account.UserId)) continue;
            var jar = new CookieContainer();
            foreach (var cookie in account.Cookies)
            {
                try
                {
                    jar.Add(new Cookie(cookie.Name, Reveal(cookie.Value), cookie.Path, cookie.Domain));
                }
                catch (CookieException ex)
                {
                    logger.LogWarning(ex, "Skipping a malformed persisted cookie for {AccountId}", account.UserId);
                }
                catch (System.Security.Cryptography.CryptographicException ex)
                {
                    // The keys are gone (a new keys directory): the account has to log in again.
                    logger.LogWarning(ex, "A stored cookie for {AccountId} cannot be decrypted with the current keys", account.UserId);
                }
            }
            var endpoint = account.ApiEndpoint is null ? null : new VrchatEndpoint(new Uri(account.ApiEndpoint), new Uri(account.PipelineEndpoint ?? VrchatEndpoint.Default.Pipeline.ToString()));
            if (!_byAccount.TryAdd(account.UserId, clientFactory.Create(jar, endpoint))) continue;
            resumed++;
            // Stored before cookies were encrypted: store them again, encrypted.
            if (account.Cookies.Any(c => !c.Value.StartsWith(EncryptedPrefix, StringComparison.Ordinal)))
                await PersistCookiesAsync(account.UserId, ct);
        }
        return resumed;
    }

    public async Task InvalidateAsync(string accountId, CancellationToken ct)
    {
        // One 401 is not proof: VRChat answers it now and then during an outage, and an invalid
        // session is never resumed, so collection would stop until someone logs in again. Only a
        // second 401 from asking who we are ends it; any other answer, or none, keeps the session.
        if (_byAccount.TryGetValue(accountId, out var live))
        {
            try
            {
                await Task.Delay(ConfirmDelay, ct);
                await live.GetCurrentUserAsync(ct);
                logger.LogWarning("VRChat answered 401 for {AccountId}, but the session still works; keeping it", accountId);
                return;
            }
            catch (VrchatUnauthorizedException)
            {
                // Confirmed below.
            }
            catch (Exception ex) when (ex is VrchatApiException or HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "VRChat answered 401 for {AccountId} and could not be asked again; keeping the session", accountId);
                return;
            }
        }
        if (_byAccount.TryRemove(accountId, out var client)) client.Dispose();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.Accounts.Where(a => a.UserId == accountId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.SessionValid, false), ct);
        logger.LogWarning("VRChat session for {AccountId} is no longer valid - log in again", accountId);
        bus.Publish(LiveEvents.Account(accountId, "", false));
        // As the original's 401 handler: try the saved password, off the caller's request.
        var stopping = _stopping.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await AutoLoginAsync(accountId, stopping);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !stopping.IsCancellationRequested)
            {
                logger.LogError(ex, "Automatic login for {AccountId} failed", accountId);
            }
            catch (OperationCanceledException)
            {
                // Shutting down.
            }
        }, CancellationToken.None);
    }

    private readonly ConcurrentDictionary<string, byte> _autoLoggingIn = new();
    private readonly ConcurrentDictionary<string, List<DateTimeOffset>> _autoLoginAttempts = new();

    /// <summary>The unit of the auto-login delay setting.</summary>
    /// <remarks>Settable so tests need not wait.</remarks>
    public static TimeSpan AutoLoginDelayUnit { get; set; } = TimeSpan.FromSeconds(1);

    public async Task<AutoLoginOutcome> AutoLoginAsync(string accountId, CancellationToken ct)
    {
        if (!_autoLoggingIn.TryAdd(accountId, 0))
        {
            logger.LogWarning("Already attempting auto login for {AccountId}, skipping", accountId);
            return AutoLoginOutcome.Busy;
        }
        try
        {
            var saved = await credentials.FindAsync(accountId, ct);
            if (saved is null) return AutoLoginOutcome.NoSavedCredentials;
            if (saved.Protection != CredentialProtection.DataProtection)
            {
                logger.LogWarning("Primary password is enabled, this disables auto login for {AccountId}", accountId);
                return AutoLoginOutcome.PrimaryPassword;
            }

            var now = time.GetUtcNow();
            var attempts = _autoLoginAttempts.GetOrAdd(accountId, _ => []);
            lock (attempts)
            {
                attempts.RemoveAll(at => at <= now.AddHours(-1));
                if (attempts.Count >= 3)
                {
                    logger.LogError("More than 3 auto login attempts within the past hour for {AccountId}, not attempting auto login", accountId);
                    return AutoLoginOutcome.TooManyAttempts;
                }
                attempts.Add(now);
            }

            Account? account;
            await using (var db = await dbFactory.CreateDbContextAsync(ct))
                account = await db.Accounts.AsNoTracking().Include(a => a.User).FirstOrDefaultAsync(a => a.UserId == accountId, ct);
            if (account is null) return AutoLoginOutcome.NoSavedCredentials;
            var displayName = account.User?.DisplayName ?? "";
            var delay = account.Settings.AutoLoginDelayEnabled ? Math.Clamp(account.Settings.AutoLoginDelaySeconds, 0, 10) : 0;
            bus.Publish(LiveEvents.Account(accountId, displayName, false, "attempting", delay));
            if (delay > 0) await Task.Delay(AutoLoginDelayUnit * delay, ct);

            string password;
            try
            {
                password = credentials.Open(saved, null);
            }
            catch (System.Security.Cryptography.CryptographicException ex)
            {
                // The keys are gone (a new keys directory): the password cannot be read again.
                logger.LogWarning(ex, "The saved password for {AccountId} cannot be decrypted with the current keys", accountId);
                await credentials.DeleteAsync(accountId, ct);
                bus.Publish(LiveEvents.Account(accountId, displayName, false, "invalid"));
                return AutoLoginOutcome.InvalidCredentials;
            }

            var endpoint = account.ApiEndpoint is null ? null : new VrchatEndpoint(new Uri(account.ApiEndpoint), new Uri(account.PipelineEndpoint ?? VrchatEndpoint.Default.Pipeline.ToString()));
            LoginOutcome outcome;
            try
            {
                logger.LogInformation("Attempting automatic login for {AccountId}", accountId);
                outcome = await LoginAsync(saved.Username, password, endpoint, "success", ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or VrchatApiException || ex is TaskCanceledException && !ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Automatic login for {AccountId} could not reach VRChat", accountId);
                bus.Publish(LiveEvents.Account(accountId, displayName, false, "failed"));
                return AutoLoginOutcome.Failed;
            }

            switch (outcome)
            {
                case LoginOutcome.LoggedIn:
                    logger.LogInformation("Automatically logged in {AccountId}", accountId);
                    return AutoLoginOutcome.Success;
                case LoginOutcome.NeedsTwoFactor twoFactor:
                    AbandonPending(twoFactor.PendingId);
                    bus.Publish(LiveEvents.Account(accountId, displayName, false, "failed"));
                    return AutoLoginOutcome.NeedsTwoFactor;
                case LoginOutcome.Rejected rejected when rejected.Reason.Contains("Invalid Username/Email or Password", StringComparison.Ordinal):
                    await credentials.DeleteAsync(accountId, ct);
                    bus.Publish(LiveEvents.Account(accountId, displayName, false, "invalid"));
                    return AutoLoginOutcome.InvalidCredentials;
                default:
                    bus.Publish(LiveEvents.Account(accountId, displayName, false, "failed"));
                    return AutoLoginOutcome.Failed;
            }
        }
        finally
        {
            _autoLoggingIn.TryRemove(accountId, out _);
        }
    }

    /// <summary>The wait before checking a 401 again.</summary>
    /// <remarks>Settable so tests need not wait.</remarks>
    public static TimeSpan ConfirmDelay { get; set; } = TimeSpan.FromSeconds(5);

    public async Task LogoutAsync(string accountId, CancellationToken ct)
    {
        // As the original's runLogoutFlow; the saved password stays.
        _autoLoginAttempts.TryRemove(accountId, out _);
        if (_byAccount.TryRemove(accountId, out var client))
        {
            try { await client.LogoutAsync(ct); }
            catch (Exception ex) when (ex is VrchatApiException or HttpRequestException) { logger.LogWarning(ex, "VRChat logout for {AccountId} failed; dropping the session anyway", accountId); }
            client.Dispose();
        }
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var account = await db.Accounts.FindAsync([accountId], ct);
        if (account is null) return;
        account.SessionValid = false;
        account.Cookies = [];
        await db.SaveChangesAsync(ct);
    }

    /// <summary>A stored cookie value in clear; one stored before encryption is read as it is.</summary>
    private string Reveal(string stored) =>
        stored.StartsWith(EncryptedPrefix, StringComparison.Ordinal) ? _cookies.Unprotect(stored[EncryptedPrefix.Length..]) : stored;

    private List<StoredCookie> Snapshot(CookieContainer jar) =>
        jar.GetAllCookies()
            .Select(c => new StoredCookie
            {
                Name = c.Name,
                Value = EncryptedPrefix + _cookies.Protect(c.Value),
                Path = c.Path,
                Domain = c.Domain,
                Expires = c.Expires == DateTime.MinValue ? null : new DateTimeOffset(c.Expires.ToUniversalTime(), TimeSpan.Zero),
            })
            .ToList();

    public void Dispose()
    {
        _stopping.Cancel();
        foreach (var client in _byAccount.Values) client.Dispose();
        foreach (var client in _pending.Values) client.Dispose();
    }
}

public abstract record LoginOutcome
{
    public sealed record LoggedIn(string UserId, string DisplayName) : LoginOutcome;
    public sealed record NeedsTwoFactor(string PendingId, IReadOnlyList<string> Methods) : LoginOutcome;
    /// <summary>VRChat said no: bad credentials or a bad code.</summary>
    public sealed record Rejected(string Reason) : LoginOutcome;
    /// <summary>The pendingId is not one we are waiting on.</summary>
    public sealed record UnknownAttempt : LoginOutcome;
}
