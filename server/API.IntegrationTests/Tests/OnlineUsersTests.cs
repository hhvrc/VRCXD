using VRCX.Testing;

namespace VRCX.API.IntegrationTests.Tests;

// Upstream issue 122: the original's Online Users row in Settings, Advanced, from GET visits and
// GET instances/count.
public sealed class OnlineUsersTests
{
    [Test]
    public async Task Online_users_and_active_instances_are_returned()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        app.Vrchat.Json("visits", "12345").Json("instances/count", """{"instances":678}""");

        var counts = await app.GetJsonAsync(app.AccountPath("online-users"));

        await Assert.That(counts.GetProperty("users").GetInt64()).IsEqualTo(12345);
        await Assert.That(counts.GetProperty("instances").GetInt64()).IsEqualTo(678);
    }
}
