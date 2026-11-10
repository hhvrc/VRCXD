using System.Text.Json;
using VRCX.Testing;

namespace VRCX.API.IntegrationTests.Tests;

// The original's groups tab reads its limits from VRChat's config constants.GROUPS.
public sealed class GroupLimitsTests
{
    [Test]
    public async Task Limits_come_from_the_config_with_defaults_for_missing_ones()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        app.Vrchat.Json("config", """{"constants":{"GROUPS":{"MAX_OWNED":7,"MAX_JOINED":120}}}""");
        var limits = await app.GetJsonAsync($"/1/accounts/{app.AccountId}/groups/limits");
        await Assert.That(limits.GetProperty("maxOwned").GetInt32()).IsEqualTo(7);
        await Assert.That(limits.GetProperty("maxJoined").GetInt32()).IsEqualTo(120);
        await Assert.That(limits.GetProperty("maxJoinedPlus").GetInt32()).IsEqualTo(200);
    }
}
