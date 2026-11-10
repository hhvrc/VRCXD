using System.Net;
using VRCX.Testing;

namespace VRCX.API.IntegrationTests.Tests;

// A single 401 used to end an account's session for good, silently stopping collection.
public sealed class SessionLossTests
{
    [Test]
    public async Task One_401_while_the_session_still_works_keeps_it()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        app.Vrchat.Status("users/usr_flaky/mutuals", HttpStatusCode.Unauthorized, """{"error":{"message":"Missing Credentials","status_code":401}}""");

        await app.Api.GetAsync(app.AccountPath("users/usr_flaky/mutual-counts"));

        var health = await app.GetJsonAsync("/1/health");
        await Assert.That(health.GetProperty("entries").GetProperty("accounts").GetProperty("description").GetString()).DoesNotContain("session invalid");
        await using var db = app.CreateDbContext();
        await Assert.That(db.Accounts.Single(a => a.UserId == app.AccountId).SessionValid).IsTrue();
    }

    [Test]
    public async Task A_401_confirmed_by_asking_again_ends_the_session_and_health_says_so()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        app.Vrchat.Status("auth/user", HttpStatusCode.Unauthorized, """{"error":{"message":"Missing Credentials","status_code":401}}""");

        await app.Api.GetAsync(app.AccountPath("profile"));

        await using (var db = app.CreateDbContext())
            await Assert.That(db.Accounts.Single(a => a.UserId == app.AccountId).SessionValid).IsFalse();
        var health = await app.GetJsonAsync("/1/health");
        await Assert.That(health.GetProperty("status").GetString()).IsEqualTo("degraded");
        await Assert.That(health.GetProperty("entries").GetProperty("accounts").GetProperty("description").GetString()).Contains("session invalid");
    }
}
