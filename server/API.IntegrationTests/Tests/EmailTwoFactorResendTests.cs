using System.Net.Http.Json;
using System.Text.Json;
using VRCX.Testing;

namespace VRCX.API.IntegrationTests.Tests;

// Upstream issues 445 and 732: there was no way to have VRChat send the email code again. As the
// original's resendEmail2fa, logging in again from a fresh session does that, and gives a new pending id.
public sealed class EmailTwoFactorResendTests
{
    [Test]
    public async Task Logging_in_again_during_email_2fa_asks_vrchat_again_with_a_new_pending_id()
    {
        await using var app = await VrcxScenario.StartAsync();
        var me = app.Vrchat.SetMe();
        app.Vrchat.TwoFactorCode = "123456";
        app.Vrchat.TwoFactorMethods = ["emailOtp"];
        var body = new { username = (string?)me.Json["username"] ?? "me", password = "test-password" };
        int AuthCalls()
        {
            lock (app.Vrchat.Calls) return app.Vrchat.Calls.Count(c => c.StartsWith("GET auth/user", StringComparison.Ordinal) && !c.StartsWith("GET auth/user/", StringComparison.Ordinal));
        }

        var first = await (await app.Api.PostAsJsonAsync("/1/accounts/login", body)).Content.ReadFromJsonAsync<JsonElement>();
        var calls = AuthCalls();
        var second = await (await app.Api.PostAsJsonAsync("/1/accounts/login", body)).Content.ReadFromJsonAsync<JsonElement>();

        await Assert.That(first.GetProperty("status").GetString()).IsEqualTo("needsTwoFactor");
        await Assert.That(second.GetProperty("status").GetString()).IsEqualTo("needsTwoFactor");
        await Assert.That(second.GetProperty("methods").EnumerateArray().Select(m => m.GetString())).Contains("emailOtp");
        await Assert.That(second.GetProperty("pendingId").GetString()).IsNotEqualTo(first.GetProperty("pendingId").GetString());
        await Assert.That(AuthCalls()).IsGreaterThan(calls);
    }
}
