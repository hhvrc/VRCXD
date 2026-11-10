using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using VRCX.Testing;
using VRCX.Testing.FakeVrchat;

namespace VRCX.API.IntegrationTests.Tests;

// The login page's QR login (VRChat's device code) and Forgot Password.
public sealed class DeviceLoginTests
{
    [Test]
    public async Task A_QR_login_waits_for_approval_then_logs_the_account_in()
    {
        await using var app = await VrcxScenario.StartAsync();
        app.Vrchat.SetMe();
        app.Vrchat.Json("config", """{"clientApiKey":"x"}""");
        app.Vrchat.Json("POST", "auth/device/code", """{"deviceCode":"dev-123","verificationUrlComplete":"https://vrchat.com/device?code=ABCD","interval":5,"expiresIn":300}""");
        var approved = false;
        app.Vrchat.On("POST", "auth/device/status", r => r.Json?["deviceCode"]?.GetValue<string>() != "dev-123"
            ? FakeVrchatApi.Error(HttpStatusCode.BadRequest, "deviceCode is required")
            : approved
            ? FakeVrchatApi.Response(HttpStatusCode.OK, """{"user":{"id":"usr_me"}}""")
            : FakeVrchatApi.Error(HttpStatusCode.BadRequest, "authorization_pending"));

        var start = await app.Api.PostAsync("/1/accounts/login/device", null);
        await VrcxScenario.EnsureSuccessAsync(start, "start");
        var code = await start.Content.ReadFromJsonAsync<JsonElement>();
        await Assert.That(code.GetProperty("verificationUrl").GetString()).IsEqualTo("https://vrchat.com/device?code=ABCD");
        await Assert.That(code.GetProperty("interval").GetInt32()).IsEqualTo(5);
        var pendingId = code.GetProperty("pendingId").GetString()!;

        var waiting = await app.Api.PostAsync($"/1/accounts/login/device/{pendingId}", null);
        await Assert.That((await waiting.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString()).IsEqualTo("pending");

        approved = true;
        var done = await app.Api.PostAsync($"/1/accounts/login/device/{pendingId}", null);
        var body = await done.Content.ReadFromJsonAsync<JsonElement>();
        await Assert.That(body.GetProperty("status").GetString()).IsEqualTo("loggedIn");
        await Assert.That(body.GetProperty("userId").GetString()).IsEqualTo("usr_me");

        var accounts = await app.Api.GetFromJsonAsync<JsonElement>("/1/accounts");
        await Assert.That(accounts.EnumerateArray().Any(a => a.GetProperty("userId").GetString() == "usr_me" && a.GetProperty("connected").GetBoolean())).IsTrue();
        // Used up: the same pendingId is unknown now.
        var again = await app.Api.PostAsync($"/1/accounts/login/device/{pendingId}", null);
        await Assert.That(again.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task An_abandoned_or_expired_QR_login_is_gone()
    {
        await using var app = await VrcxScenario.StartAsync();
        app.Vrchat.Json("config", "{}");
        app.Vrchat.Json("POST", "auth/device/code", """{"deviceCode":"dev-1","verificationUrlComplete":"https://vrchat.com/device?code=A","interval":5,"expiresIn":300}""");
        app.Vrchat.Fail("POST", "auth/device/status", HttpStatusCode.BadRequest, "expired_token");

        var first = (await (await app.Api.PostAsync("/1/accounts/login/device", null)).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("pendingId").GetString();
        var expired = await app.Api.PostAsync($"/1/accounts/login/device/{first}", null);
        await Assert.That(expired.StatusCode).IsEqualTo(HttpStatusCode.BadGateway);
        await Assert.That((await app.Api.PostAsync($"/1/accounts/login/device/{first}", null)).StatusCode).IsEqualTo(HttpStatusCode.NotFound);

        var second = (await (await app.Api.PostAsync("/1/accounts/login/device", null)).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("pendingId").GetString();
        await Assert.That((await app.Api.DeleteAsync($"/1/accounts/login/device/{second}")).StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That((await app.Api.PostAsync($"/1/accounts/login/device/{second}", null)).StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Forgot_password_asks_VRChat_to_mail_the_address()
    {
        await using var app = await VrcxScenario.StartAsync();
        app.Vrchat.Json("PUT", "auth/password", """{"success":{"message":"ok"}}""");

        var reset = await app.Api.PostAsJsonAsync("/1/accounts/login/password-reset", new { email = " me@example.com " });

        await Assert.That(reset.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(app.Vrchat.AssertCalled("PUT", "auth/password").Json?["email"]?.GetValue<string>()).IsEqualTo("me@example.com");
        var bad = await app.Api.PostAsJsonAsync("/1/accounts/login/password-reset", new { email = "not-an-address" });
        await Assert.That(bad.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }
}
