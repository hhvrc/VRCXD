using VRCX.Testing;

namespace VRCX.API.IntegrationTests.Tests;

// Upstream issues 1611 and 1758: My Avatars paged through every own avatar on each visit and ran into
// VRChat's rate limit. Like the original, they are fetched once and again on Refresh.
public sealed class OwnAvatarsCacheTests
{
    [Test]
    public async Task Own_avatars_are_not_refetched_on_every_visit()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        int Fetches()
        {
            lock (app.Vrchat.Calls) return app.Vrchat.Calls.Count(c => c.StartsWith("GET avatars", StringComparison.Ordinal));
        }

        (await app.Api.GetAsync(app.AccountPath("avatars"))).EnsureSuccessStatusCode();
        var first = Fetches();
        await Assert.That(first).IsGreaterThan(0);

        (await app.Api.GetAsync(app.AccountPath("avatars"))).EnsureSuccessStatusCode();
        await Assert.That(Fetches()).IsEqualTo(first);

        (await app.Api.GetAsync(app.AccountPath("avatars?refresh=true"))).EnsureSuccessStatusCode();
        await Assert.That(Fetches()).IsGreaterThan(first);
    }
}
