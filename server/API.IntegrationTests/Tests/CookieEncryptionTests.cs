using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VRCX.Common.Services.Sessions;
using VRCX.Testing;

namespace VRCX.API.IntegrationTests.Tests;

// The VRChat session cookies sign in as the account without password or 2FA; a database dump alone
// must not hand them out.
public sealed class CookieEncryptionTests
{
    [Test]
    public async Task Stored_session_cookies_are_encrypted()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        var sessions = app.Services.GetRequiredService<IAccountSessionManager>();
        sessions.Get(app.AccountId!)!.Cookies.Add(new System.Net.Cookie("auth", "authcookie_secret", "/", "api.vrchat.cloud"));
        await sessions.PersistCookiesAsync(app.AccountId!, CancellationToken.None);

        await using var db = app.CreateDbContext();
        var cookies = (await db.Accounts.AsNoTracking().SingleAsync(a => a.UserId == app.AccountId)).Cookies;
        await Assert.That(cookies).IsNotEmpty();
        foreach (var cookie in cookies)
        {
            await Assert.That(cookie.Value).StartsWith("dp1:");
            await Assert.That(cookie.Value).DoesNotContain("authcookie_secret");
        }
    }
}
