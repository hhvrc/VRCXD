using VRCX.Testing;

namespace VRCX.API.IntegrationTests.Tests;

// Upstream issues 110 and 519: a game log row with only a display name opened nothing. As the
// original's lookupUser: a friend or stored user with that exact name, else VRChat's exact-name search.
public sealed class UserLookupTests
{
    private static async Task<string?> LookupAsync(VrcxScenario app, string name)
    {
        var found = await app.GetJsonAsync(app.AccountPath($"users/lookup?displayName={Uri.EscapeDataString(name)}"));
        return found.TryGetProperty("userId", out var id) ? id.GetString() : null;
    }

    [Test]
    public async Task A_display_name_finds_the_friend_then_vrchats_exact_match()
    {
        await using var app = await VrcxScenario.StartAsync();
        app.Vrchat.Friend("usr_pal", "Pal");
        await app.LoginAsync();
        app.Vrchat.Json("users", """[{"id":"usr_close","displayName":"Strangerr"},{"id":"usr_exact","displayName":"Stranger"}]""");

        await Assert.That(await LookupAsync(app, "Pal")).IsEqualTo("usr_pal");
        await Assert.That(await LookupAsync(app, "Stranger")).IsEqualTo("usr_exact");
        await Assert.That(await LookupAsync(app, "Nobody")).IsNull();
        await Assert.That(await LookupAsync(app, "ID:usr_x")).IsNull();
        bool exact;
        lock (app.Vrchat.Calls) exact = app.Vrchat.Calls.Any(c => c.Contains("fuzzy=false", StringComparison.Ordinal));
        await Assert.That(exact).IsTrue();
    }
}
