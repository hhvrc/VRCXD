using System.Net.Http.Json;
using System.Text.Json.Nodes;
using VRCX.Testing;

namespace VRCX.API.IntegrationTests.Tests;

// Profile badges and the mutual counts on the user page, the original's UserSummaryHeader.
public sealed class BadgeTests
{
    private static JsonNode Badges(bool hidden) => JsonNode.Parse($$"""
        [{"badgeId":"bdg_supporter","badgeName":"Supporter","badgeDescription":"Supported VRChat","badgeImageUrl":"https://assets.vrchat.com/badge.png","hidden":{{(hidden ? "true" : "false")}},"showcased":true,"assignedAt":"2025-01-02T03:04:05Z"}]
        """)!;

    [Test]
    public async Task A_refreshed_profile_stores_its_badges_on_the_user()
    {
        await using var app = await VrcxScenario.StartAsync();
        var friend = app.Vrchat.Friend("usr_badged", "Badged", u => u.Set("badges", Badges(false)));
        await app.LoginAsync();

        var user = await app.GetJsonAsync($"/1/users/{friend.Id}?refresh=true&account={app.AccountId}");

        var badge = user.GetProperty("badges")[0];
        await Assert.That(badge.GetProperty("badgeName").GetString()).IsEqualTo("Supporter");
        await Assert.That(badge.GetProperty("showcased").GetBoolean()).IsTrue();
    }

    // Upstream 7953963e: VRChat leaves the Supporter badge out of a VRC+ user's profile, the original adds it.
    [Test]
    public async Task A_vrc_plus_user_gets_the_supporter_badge_first_and_hidden()
    {
        await using var app = await VrcxScenario.StartAsync();
        var friend = app.Vrchat.Friend("usr_plus", "Plus", u => u.Set("badges", Badges(false)).Tags("system_supporter"));
        await app.LoginAsync();

        var user = await app.GetJsonAsync($"/1/users/{friend.Id}?refresh=true&account={app.AccountId}");
        var badges = user.GetProperty("badges");
        await Assert.That(badges.GetArrayLength()).IsEqualTo(2);
        var supporter = badges[0];
        await Assert.That(supporter.GetProperty("badgeId").GetString()).IsEqualTo("bdg_754f9935-0f97-49d8-b857-95afb9b673fa");
        await Assert.That(supporter.GetProperty("badgeName").GetString()).IsEqualTo("Supporter");
        await Assert.That(supporter.GetProperty("badgeDescription").GetString()).IsEqualTo("Supports VRChat through VRC+");
        await Assert.That(supporter.GetProperty("badgeImageUrl").GetString()).IsEqualTo("https://assets.vrchat.com/badges/fa/bdgai_583f6b13-91ab-4e1b-974e-ab91600b06cb.png");
        await Assert.That(supporter.GetProperty("hidden").GetBoolean()).IsTrue();
        await Assert.That(supporter.GetProperty("showcased").GetBoolean()).IsFalse();
        await Assert.That(badges[1].GetProperty("badgeId").GetString()).IsEqualTo("bdg_supporter");

        var profile = await app.GetJsonAsync(app.AccountPath($"users/{friend.Id}/public-profile"));
        await Assert.That(profile.GetProperty("badges")[0].GetProperty("badgeName").GetString()).IsEqualTo("Supporter");
    }

    [Test]
    public async Task The_supporter_badge_is_not_added_twice_or_to_users_without_vrc_plus()
    {
        await using var app = await VrcxScenario.StartAsync();
        var plus = app.Vrchat.Friend("usr_plus", "Plus", u => u.Tags("system_supporter"));
        var plain = app.Vrchat.Friend("usr_plain", "Plain", u => u.Set("badges", Badges(false)));
        app.Vrchat.Json($"profile/{plus.Id}", """
            {"id":"usr_plus","hasVrcPlus":true,"badges":[{"badgeId":"bdg_754f9935-0f97-49d8-b857-95afb9b673fa","badgeName":"Supporter","badgeDescription":"","badgeImageUrl":"","showcased":true,"hidden":false}]}
            """);
        await app.LoginAsync();

        var plusBadges = (await app.GetJsonAsync(app.AccountPath($"users/{plus.Id}/public-profile"))).GetProperty("badges");
        await Assert.That(plusBadges.GetArrayLength()).IsEqualTo(1);
        await Assert.That(plusBadges[0].GetProperty("showcased").GetBoolean()).IsTrue();
        var plainBadges = (await app.GetJsonAsync($"/1/users/{plain.Id}?refresh=true&account={app.AccountId}")).GetProperty("badges");
        await Assert.That(plainBadges.GetArrayLength()).IsEqualTo(1);
        await Assert.That(plainBadges[0].GetProperty("badgeId").GetString()).IsEqualTo("bdg_supporter");
    }

    [Test]
    public async Task Hiding_an_own_badge_takes_it_out_of_the_showcase_on_vrchat_and_in_the_store()
    {
        await using var app = await VrcxScenario.StartAsync();
        app.Vrchat.SetMe(configure: u => u.Set("badges", Badges(false)));
        await app.LoginAsync();
        await app.GetJsonAsync($"/1/users/{app.AccountId}?refresh=true&account={app.AccountId}");

        var response = await app.Api.PutAsJsonAsync(app.AccountPath("badges/bdg_supporter"), new { hidden = true, showcased = true });
        await Assert.That(response.IsSuccessStatusCode).IsTrue();

        var sent = JsonNode.Parse(app.Vrchat.Bodies[$"PUT users/{app.AccountId}/badges/bdg_supporter"])!;
        await Assert.That((bool)sent["hidden"]!).IsTrue();
        await Assert.That((bool)sent["showcased"]!).IsFalse();
        var user = await app.GetJsonAsync($"/1/users/{app.AccountId}");
        var badge = user.GetProperty("badges")[0];
        await Assert.That(badge.GetProperty("hidden").GetBoolean()).IsTrue();
        await Assert.That(badge.GetProperty("showcased").GetBoolean()).IsFalse();
    }

    [Test]
    public async Task Mutual_counts_come_live_from_vrchat()
    {
        await using var app = await VrcxScenario.StartAsync();
        var friend = app.Vrchat.Friend("usr_mutual", "Mutual");
        app.Vrchat.Json($"users/{friend.Id}/mutuals", """{"friends":4,"groups":2}""");
        await app.LoginAsync();

        var counts = await app.GetJsonAsync(app.AccountPath($"users/{friend.Id}/mutual-counts"));

        await Assert.That(counts.GetProperty("friends").GetInt32()).IsEqualTo(4);
        await Assert.That(counts.GetProperty("groups").GetInt32()).IsEqualTo(2);
    }

    [Test]
    public async Task The_public_profile_carries_the_theme_and_background_live_from_vrchat()
    {
        await using var app = await VrcxScenario.StartAsync();
        var friend = app.Vrchat.Friend("usr_themed", "Themed");
        app.Vrchat.Json($"profile/{friend.Id}", """
            {"id":"usr_themed","bio":"hi","themeIconColor":"6ae3f9","themeButtonColor":"#ff0000","themeSubtextColor":"aaaaaa",
             "backgroundType":"gradient","backgroundGradientTop":"112233","backgroundGradientBottom":"445566","backgroundTextureId":"koi","isEconomyCreator":true}
            """);
        await app.LoginAsync();

        var profile = await app.GetJsonAsync(app.AccountPath($"users/{friend.Id}/public-profile"));

        await Assert.That(profile.GetProperty("themeIconColor").GetString()).IsEqualTo("6ae3f9");
        await Assert.That(profile.GetProperty("backgroundType").GetString()).IsEqualTo("gradient");
        await Assert.That(profile.GetProperty("backgroundGradientBottom").GetString()).IsEqualTo("445566");
        await Assert.That(profile.GetProperty("backgroundTextureId").GetString()).IsEqualTo("koi");
        // Upstream issue 1846: the economy creator check.
        await Assert.That(profile.GetProperty("isEconomyCreator").GetBoolean()).IsTrue();
    }
}
