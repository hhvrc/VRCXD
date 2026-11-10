using VRCX.Testing;

namespace VRCX.API.IntegrationTests.Tests;

// Upstream issue 674: the favorite worlds' Players sort needs each world's current player count.
public sealed class FavoriteWorldOccupantsTests
{
    [Test]
    public async Task Favorite_and_local_worlds_report_their_players()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        app.Vrchat.World("wrld_fav_busy", "Busy", w => w.Set("occupants", 42));
        app.Vrchat.World("wrld_local_only", "Local", w => w.Set("occupants", 7));
        app.Vrchat.Favorite("world", "wrld_fav_busy", "worlds1");

        var occupants = await app.GetJsonAsync(app.AccountPath("favorites/world-occupants?ids=wrld_local_only&ids=wrld_missing&ids=usr_not_a_world"));

        await Assert.That(occupants.GetProperty("wrld_fav_busy").GetInt32()).IsEqualTo(42);
        await Assert.That(occupants.GetProperty("wrld_local_only").GetInt32()).IsEqualTo(7);
        await Assert.That(occupants.TryGetProperty("wrld_missing", out _)).IsFalse();
        await Assert.That(occupants.TryGetProperty("usr_not_a_world", out _)).IsFalse();
    }
}
