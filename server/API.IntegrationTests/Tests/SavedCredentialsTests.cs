using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VRCX.Common.Live;
using VRCX.Common.Services.Sessions;
using VRCX.Common.VrcxDb;
using VRCX.Testing;
using VRCX.Testing.FakeVrchat;

namespace VRCX.API.IntegrationTests.Tests;

// The original's saved credentials, primary password and auto login (stores/auth.js,
// coordinators/authAutoLoginCoordinator.js): the password is kept encrypted, never returned, and
// used to log in again when the session expires.
public sealed class SavedCredentialsTests
{
    private const string Password = "test-saved-pass-1";
    private const string Primary = "test-primary-1";

    private static object LoginBody(FakeUser me, bool save, string? primary = null) =>
        new { username = (string?)me.Json["username"] ?? "me", password = Password, saveCredentials = save, primaryPassword = primary };

    private static async Task<string> LoginSavingAsync(VrcxScenario app)
    {
        var me = app.Vrchat.Me ?? app.Vrchat.SetMe();
        var syncs = app.Syncs.Started;
        var response = await app.Api.PostAsJsonAsync("/1/accounts/login", LoginBody(me, true));
        await VrcxScenario.EnsureSuccessAsync(response, "login");
        // The sync a login starts must be done before a test ends the session.
        await app.Syncs.WaitAsync(syncs, TimeSpan.FromSeconds(30));
        var body = await response.Content.ReadAsStringAsync();
        await Assert.That(body).DoesNotContain(Password);
        return me.Id;
    }

    private static string? BasicPassword(FakeRequest r)
    {
        if (r.Message.Headers.Authorization is not { Scheme: "Basic" } auth || auth.Parameter is null) return null;
        var pair = Encoding.UTF8.GetString(Convert.FromBase64String(auth.Parameter));
        return Uri.UnescapeDataString(pair[(pair.IndexOf(':') + 1)..]);
    }

    // VRChat with the session's cookies gone: auth/user without a password answers 401 until a
    // password login works again; what the password login answers is up to the test.
    private sealed class ExpiringSession
    {
        public bool Expired;
        public readonly List<string?> Passwords = [];
    }

    private static ExpiringSession ExpireSession(VrcxScenario app, Func<FakeRequest, HttpResponseMessage>? onPassword = null)
    {
        var session = new ExpiringSession { Expired = true };
        app.Vrchat.On("GET", "auth/user", r =>
        {
            var password = BasicPassword(r);
            if (password is null)
                return session.Expired ? FakeVrchatApi.Error(HttpStatusCode.Unauthorized, "Missing Credentials") : FakeVrchatApi.Ok(app.Vrchat.Me!.Json.DeepClone());
            lock (session.Passwords) session.Passwords.Add(password);
            if (onPassword is not null) return onPassword(r);
            session.Expired = false;
            return FakeVrchatApi.Ok(app.Vrchat.Me!.Json.DeepClone());
        });
        return session;
    }

    private static List<string?> Used(ExpiringSession session)
    {
        lock (session.Passwords) return session.Passwords.Distinct().ToList();
    }

    private static async Task<JsonElement> AccountJsonAsync(VrcxScenario app, string accountId)
    {
        var accounts = await app.GetJsonAsync("/1/accounts");
        return accounts.EnumerateArray().Single(a => a.GetProperty("userId").GetString() == accountId);
    }

    private static bool Contains(byte[] haystack, string needle) =>
        haystack.AsSpan().IndexOf(Encoding.UTF8.GetBytes(needle)) >= 0 || haystack.AsSpan().IndexOf(Encoding.Unicode.GetBytes(needle)) >= 0;

    [Test]
    public async Task Saving_credentials_stores_the_password_encrypted_and_never_returns_it()
    {
        await using var app = await VrcxScenario.StartAsync();
        var accountId = await LoginSavingAsync(app);

        await using (var db = app.CreateDbContext())
        {
            var saved = await db.SavedCredentials.AsNoTracking().SingleAsync(c => c.AccountId == accountId);
            await Assert.That(saved.Protection).IsEqualTo(CredentialProtection.DataProtection);
            await Assert.That(Contains(saved.Secret, Password)).IsFalse();
            var account = await db.Accounts.AsNoTracking().SingleAsync(a => a.UserId == accountId);
            await Assert.That(JsonSerializer.Serialize(account.Cookies)).DoesNotContain(Password);
            await Assert.That(JsonSerializer.Serialize(account.Settings)).DoesNotContain(Password);
        }
        var listed = await app.Api.GetStringAsync("/1/accounts");
        await Assert.That(listed).DoesNotContain(Password);
        var json = await AccountJsonAsync(app, accountId);
        await Assert.That(json.GetProperty("hasSavedCredentials").GetBoolean()).IsTrue();
        await Assert.That(json.GetProperty("savedCredentialsNeedPrimaryPassword").GetBoolean()).IsFalse();
        await Assert.That(app.Logs.All.Any(l => l.ToString().Contains(Password, StringComparison.Ordinal))).IsFalse();

        // It still opens: the server can use it by itself.
        var store = app.Services.GetRequiredService<SavedCredentialStore>();
        await Assert.That(store.Open((await store.FindAsync(accountId, CancellationToken.None))!, null)).IsEqualTo(Password);
    }

    [Test]
    public async Task Without_save_credentials_no_password_is_kept()
    {
        await using var app = await VrcxScenario.StartAsync();
        var me = app.Vrchat.SetMe();
        await VrcxScenario.EnsureSuccessAsync(await app.Api.PostAsJsonAsync("/1/accounts/login", LoginBody(me, false)), "login");

        await using var db = app.CreateDbContext();
        await Assert.That(await db.SavedCredentials.AnyAsync()).IsFalse();
        await Assert.That((await AccountJsonAsync(app, me.Id)).GetProperty("hasSavedCredentials").GetBoolean()).IsFalse();
    }

    [Test]
    public async Task Two_factor_login_saves_the_password_once_the_code_verifies()
    {
        await using var app = await VrcxScenario.StartAsync();
        var me = app.Vrchat.SetMe();
        app.Vrchat.TwoFactorCode = "123456";
        var first = await (await app.Api.PostAsJsonAsync("/1/accounts/login", LoginBody(me, true))).Content.ReadFromJsonAsync<JsonElement>();
        await using (var db = app.CreateDbContext())
            await Assert.That(await db.SavedCredentials.AnyAsync()).IsFalse();

        var verified = await app.Api.PostAsJsonAsync("/1/accounts/login/2fa", new { pendingId = first.GetProperty("pendingId").GetString(), method = "totp", code = "123456" });
        await VrcxScenario.EnsureSuccessAsync(verified, "2fa");

        await using (var db = app.CreateDbContext())
            await Assert.That(await db.SavedCredentials.AnyAsync(c => c.AccountId == me.Id)).IsTrue();
    }

    [Test]
    public async Task An_expired_session_logs_in_again_with_the_saved_password()
    {
        await using var app = await VrcxScenario.StartAsync();
        var accountId = await LoginSavingAsync(app);
        await using var live = await app.ConnectLiveAsync();
        var session = ExpireSession(app);

        await app.Api.GetAsync($"/1/accounts/{accountId}/profile");

        var attempting = await live.WaitForAsync<AccountSessionChanged>(e => e.AutoLogin == "attempting");
        await Assert.That(attempting.AutoLoginDelay).IsEqualTo(0u);
        var success = await live.WaitForAsync<AccountSessionChanged>(e => e.AutoLogin == "success");
        await Assert.That(success.SessionValid).IsTrue();
        await Assert.That(success.AccountId).IsEqualTo(accountId);
        await Assert.That(Used(session)).IsEquivalentTo(new List<string?> { Password });
        await using var db = app.CreateDbContext();
        await Assert.That((await db.Accounts.AsNoTracking().SingleAsync(a => a.UserId == accountId)).SessionValid).IsTrue();
        await Assert.That(app.Services.GetRequiredService<IAccountSessionManager>().Get(accountId)).IsNotNull();
        await Assert.That(app.Logs.All.Any(l => l.ToString().Contains(Password, StringComparison.Ordinal))).IsFalse();
    }

    [Test]
    public async Task The_auto_login_delay_setting_goes_with_the_attempt()
    {
        await using var app = await VrcxScenario.StartAsync();
        var accountId = await LoginSavingAsync(app);
        var settings = await app.GetJsonAsync($"/1/accounts/{accountId}/settings");
        var patched = JsonNode.Parse(settings.GetRawText())!.AsObject();
        patched["autoLoginDelayEnabled"] = true;
        patched["autoLoginDelaySeconds"] = 25;
        var stored = await (await app.Api.PutAsJsonAsync($"/1/accounts/{accountId}/settings", patched)).Content.ReadFromJsonAsync<JsonElement>();
        await Assert.That(stored.GetProperty("autoLoginDelaySeconds").GetInt32()).IsEqualTo(10);
        patched["autoLoginDelaySeconds"] = 3;
        await VrcxScenario.EnsureSuccessAsync(await app.Api.PutAsJsonAsync($"/1/accounts/{accountId}/settings", patched), "settings");
        await using var live = await app.ConnectLiveAsync();
        ExpireSession(app);

        await app.Api.GetAsync($"/1/accounts/{accountId}/profile");

        var attempting = await live.WaitForAsync<AccountSessionChanged>(e => e.AutoLogin == "attempting");
        await Assert.That(attempting.AutoLoginDelay).IsEqualTo(3u);
        await live.WaitForAsync<AccountSessionChanged>(e => e.AutoLogin == "success");
    }

    [Test]
    public async Task A_refused_saved_password_is_removed()
    {
        await using var app = await VrcxScenario.StartAsync();
        var accountId = await LoginSavingAsync(app);
        await using var live = await app.ConnectLiveAsync();
        ExpireSession(app, _ => FakeVrchatApi.Error(HttpStatusCode.Unauthorized, "Invalid Username/Email or Password"));

        await app.Api.GetAsync($"/1/accounts/{accountId}/profile");

        var invalid = await live.WaitForAsync<AccountSessionChanged>(e => e.AutoLogin == "invalid");
        await Assert.That(invalid.SessionValid).IsFalse();
        await using var db = app.CreateDbContext();
        await Assert.That(await db.SavedCredentials.AnyAsync(c => c.AccountId == accountId)).IsFalse();
        await Assert.That((await db.Accounts.AsNoTracking().SingleAsync(a => a.UserId == accountId)).SessionValid).IsFalse();
    }

    [Test]
    public async Task Two_factor_still_needs_the_user()
    {
        await using var app = await VrcxScenario.StartAsync();
        var accountId = await LoginSavingAsync(app);
        await using var live = await app.ConnectLiveAsync();
        ExpireSession(app, _ => FakeVrchatApi.Ok(new JsonObject { ["requiresTwoFactorAuth"] = new JsonArray("totp", "otp") }));

        await app.Api.GetAsync($"/1/accounts/{accountId}/profile");

        var failed = await live.WaitForAsync<AccountSessionChanged>(e => e.AutoLogin == "failed");
        await Assert.That(failed.SessionValid).IsFalse();
        await using var db = app.CreateDbContext();
        // The password is fine: it stays for the user's own login with the code.
        await Assert.That(await db.SavedCredentials.AnyAsync(c => c.AccountId == accountId)).IsTrue();
        await Assert.That((await db.Accounts.AsNoTracking().SingleAsync(a => a.UserId == accountId)).SessionValid).IsFalse();

        var relogin = await (await app.Api.PostAsJsonAsync($"/1/accounts/{accountId}/relogin", new { })).Content.ReadFromJsonAsync<JsonElement>();
        await Assert.That(relogin.GetProperty("status").GetString()).IsEqualTo("needsTwoFactor");
    }

    [Test]
    public async Task Auto_login_stops_after_three_attempts_within_an_hour()
    {
        await using var app = await VrcxScenario.StartAsync();
        var accountId = await LoginSavingAsync(app);
        ExpireSession(app, _ => FakeVrchatApi.Error(HttpStatusCode.BadRequest, "nope"));
        var sessions = app.Services.GetRequiredService<IAccountSessionManager>();

        var outcomes = new List<AutoLoginOutcome>();
        for (var i = 0; i < 4; i++) outcomes.Add(await sessions.AutoLoginAsync(accountId, CancellationToken.None));
        app.Time.Advance(TimeSpan.FromMinutes(61));
        outcomes.Add(await sessions.AutoLoginAsync(accountId, CancellationToken.None));

        await Assert.That(outcomes).IsEquivalentTo(new List<AutoLoginOutcome>
        {
            AutoLoginOutcome.Failed, AutoLoginOutcome.Failed, AutoLoginOutcome.Failed, AutoLoginOutcome.TooManyAttempts, AutoLoginOutcome.Failed,
        });
    }

    [Test]
    public async Task Clicking_a_saved_account_logs_in_with_its_password()
    {
        await using var app = await VrcxScenario.StartAsync();
        var accountId = await LoginSavingAsync(app);
        await VrcxScenario.EnsureSuccessAsync(await app.Api.DeleteAsync($"/1/accounts/{accountId}/session"), "logout");
        await Assert.That((await AccountJsonAsync(app, accountId)).GetProperty("hasSavedCredentials").GetBoolean()).IsTrue();
        var session = ExpireSession(app);

        var response = await app.Api.PostAsJsonAsync($"/1/accounts/{accountId}/relogin", new { });
        await VrcxScenario.EnsureSuccessAsync(response, "relogin");

        await Assert.That((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString()).IsEqualTo("loggedIn");
        await Assert.That(Used(session)).IsEquivalentTo(new List<string?> { Password });
        await Assert.That((await AccountJsonAsync(app, accountId)).GetProperty("sessionValid").GetBoolean()).IsTrue();
    }

    [Test]
    public async Task A_saved_account_click_vrchat_refuses_removes_it()
    {
        await using var app = await VrcxScenario.StartAsync();
        var accountId = await LoginSavingAsync(app);
        ExpireSession(app, _ => FakeVrchatApi.Error(HttpStatusCode.Unauthorized, "Invalid Username/Email or Password"));

        var response = await app.Api.PostAsJsonAsync($"/1/accounts/{accountId}/relogin", new { });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("type").GetString()).IsEqualTo("SavedLogin.Invalid");
        await Assert.That((await AccountJsonAsync(app, accountId)).GetProperty("hasSavedCredentials").GetBoolean()).IsFalse();
    }

    [Test]
    public async Task The_primary_password_encrypts_saved_passwords_and_is_asked_for()
    {
        await using var app = await VrcxScenario.StartAsync();
        var accountId = await LoginSavingAsync(app);

        var on = await app.Api.PutAsJsonAsync("/1/accounts/primary-password", new { enabled = true, password = Primary });
        await VrcxScenario.EnsureSuccessAsync(on, "primary on");
        await Assert.That((await app.GetJsonAsync("/1/accounts/primary-password")).GetProperty("enabled").GetBoolean()).IsTrue();
        await using (var db = app.CreateDbContext())
        {
            var saved = await db.SavedCredentials.AsNoTracking().SingleAsync(c => c.AccountId == accountId);
            await Assert.That(saved.Protection).IsEqualTo(CredentialProtection.PrimaryPassword);
            await Assert.That(Contains(saved.Secret, Password)).IsFalse();
            await Assert.That(Contains(saved.Secret, Primary)).IsFalse();
            var settingsText = string.Join("\n", await db.ServerSettings.AsNoTracking().Select(s => s.Key + "=" + s.Value).ToListAsync());
            await Assert.That(settingsText).DoesNotContain(Primary);
        }
        await Assert.That((await AccountJsonAsync(app, accountId)).GetProperty("savedCredentialsNeedPrimaryPassword").GetBoolean()).IsTrue();

        // The server cannot use it by itself any more.
        var sessions = app.Services.GetRequiredService<IAccountSessionManager>();
        await Assert.That(await sessions.AutoLoginAsync(accountId, CancellationToken.None)).IsEqualTo(AutoLoginOutcome.PrimaryPassword);

        ExpireSession(app);
        var missing = await app.Api.PostAsJsonAsync($"/1/accounts/{accountId}/relogin", new { });
        await Assert.That((await missing.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("type").GetString()).IsEqualTo("SavedLogin.PrimaryPasswordRequired");
        var wrong = await app.Api.PostAsJsonAsync($"/1/accounts/{accountId}/relogin", new { primaryPassword = "test-wrong-1" });
        await Assert.That(wrong.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        await Assert.That((await wrong.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("type").GetString()).IsEqualTo("SavedLogin.IncorrectPrimaryPassword");
        var right = await app.Api.PostAsJsonAsync($"/1/accounts/{accountId}/relogin", new { primaryPassword = Primary });
        await VrcxScenario.EnsureSuccessAsync(right, "relogin with primary");

        // Off needs it; a wrong one changes nothing.
        var offWrong = await app.Api.PutAsJsonAsync("/1/accounts/primary-password", new { enabled = false, password = "test-wrong-1" });
        await Assert.That(offWrong.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        await Assert.That((await app.GetJsonAsync("/1/accounts/primary-password")).GetProperty("enabled").GetBoolean()).IsTrue();
        await VrcxScenario.EnsureSuccessAsync(await app.Api.PutAsJsonAsync("/1/accounts/primary-password", new { enabled = false, password = Primary }), "primary off");
        await using (var db = app.CreateDbContext())
            await Assert.That((await db.SavedCredentials.AsNoTracking().SingleAsync(c => c.AccountId == accountId)).Protection).IsEqualTo(CredentialProtection.DataProtection);
        await Assert.That(app.Logs.All.Any(l => l.ToString().Contains(Primary, StringComparison.Ordinal) || l.ToString().Contains(Password, StringComparison.Ordinal))).IsFalse();
    }

    [Test]
    public async Task Saving_while_the_primary_password_is_on_needs_it()
    {
        await using var app = await VrcxScenario.StartAsync();
        var accountId = await LoginSavingAsync(app);
        await VrcxScenario.EnsureSuccessAsync(await app.Api.PutAsJsonAsync("/1/accounts/primary-password", new { enabled = true, password = Primary }), "primary on");
        var me = app.Vrchat.Me!;

        var missing = await app.Api.PostAsJsonAsync("/1/accounts/login", LoginBody(me, true));
        await Assert.That((await missing.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("type").GetString()).IsEqualTo("SavedLogin.PrimaryPasswordRequired");
        var wrong = await app.Api.PostAsJsonAsync("/1/accounts/login", LoginBody(me, true, "test-wrong-1"));
        await Assert.That((await wrong.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("type").GetString()).IsEqualTo("SavedLogin.IncorrectPrimaryPassword");
        var right = await app.Api.PostAsJsonAsync("/1/accounts/login", LoginBody(me, true, Primary));
        await VrcxScenario.EnsureSuccessAsync(right, "login with primary");

        var store = app.Services.GetRequiredService<SavedCredentialStore>();
        var saved = (await store.FindAsync(accountId, CancellationToken.None))!;
        await Assert.That(saved.Protection).IsEqualTo(CredentialProtection.PrimaryPassword);
        await Assert.That(store.Open(saved, Primary)).IsEqualTo(Password);
    }

    [Test]
    public async Task Removing_the_last_saved_login_turns_the_primary_password_off()
    {
        await using var app = await VrcxScenario.StartAsync();
        var accountId = await LoginSavingAsync(app);
        await VrcxScenario.EnsureSuccessAsync(await app.Api.PutAsJsonAsync("/1/accounts/primary-password", new { enabled = true, password = Primary }), "primary on");

        await VrcxScenario.EnsureSuccessAsync(await app.Api.DeleteAsync($"/1/accounts/{accountId}/saved-login"), "remove");

        var account = await AccountJsonAsync(app, accountId);
        await Assert.That(account.GetProperty("hasSavedCredentials").GetBoolean()).IsFalse();
        await Assert.That(account.GetProperty("sessionValid").GetBoolean()).IsFalse();
        await Assert.That((await app.GetJsonAsync("/1/accounts/primary-password")).GetProperty("enabled").GetBoolean()).IsFalse();
    }
}
