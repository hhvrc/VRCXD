using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace VRCX.API.IntegrationTests.Tests;

// The favorite dialog, with VRChat faked.
public sealed class FavoriteTests
{
    private const string Account = "usr_favorites_account";

    [ClassDataSource<IsolatedHost>(Shared = SharedType.None)]
    public required IsolatedHost Factory { get; init; }

    private async Task<HttpClient> LoginAsync()
    {
        Factory.Vrchat
            .Json("auth/user", $$"""{"id":"{{Account}}","displayName":"Favorites","username":"favorites","tags":["system_supporter"]}""")
            .Json("auth/user/favoritelimits", """{"maxFavoriteGroups":{"avatar":2,"friend":1,"world":1,"vrcPlusWorld":1},"maxFavoritesPerGroup":{"avatar":50,"friend":150,"world":100,"vrcPlusWorld":100}}""");
        var client = Factory.CreateAuthenticatedClient();
        var loggingIn = DateTimeOffset.UtcNow;
        var login = await client.PostAsJsonAsync("/1/accounts/login", new { username = "favorites", password = "pw" });
        await Assert.That(login.StatusCode).IsEqualTo(HttpStatusCode.OK);

        // The login's background sync replaces the stored favorites with the fake's empty list, so a test has to act after it.
        for (var i = 0; i < 100; i++)
        {
            var accounts = await client.GetFromJsonAsync<JsonElement>("/1/accounts");
            var account = accounts.EnumerateArray().First(a => a.GetProperty("userId").GetString() == Account);
            if (account.TryGetProperty("lastFullSyncAt", out var synced) && synced.ValueKind == JsonValueKind.String && synced.GetDateTimeOffset() >= loggingIn) return client;
            await Task.Delay(50);
        }
        throw new TimeoutException("the login sync never finished");
    }

    [Test]
    public async Task Adding_shows_in_the_group_count_and_removing_clears_it()
    {
        const string world = "wrld_favorite_add";
        Factory.Vrchat
            .Json("favorites", $$"""{"id":"fvrt_add","type":"world","favoriteId":"{{world}}","tags":["worlds1"]}""")
            .Json($"favorites/{world}", """{"success":{"message":"ok","status_code":200}}""");
        using var client = await LoginAsync();

        var added = await client.PostAsJsonAsync($"/1/accounts/{Account}/favorites", new { type = "world", targetId = world, group = "worlds1" });
        await Assert.That(added.StatusCode).IsEqualTo(HttpStatusCode.NoContent);

        var picker = await client.GetFromJsonAsync<JsonElement>($"/1/accounts/{Account}/favorites/picker?type=world&targetId={world}");
        var groups = picker.GetProperty("groups");
        await Assert.That(groups.GetArrayLength()).IsEqualTo(2);
        await Assert.That(groups[0].GetProperty("name").GetString()).IsEqualTo("worlds1");
        await Assert.That(groups[0].GetProperty("count").GetInt32()).IsEqualTo(1);
        await Assert.That(groups[1].GetProperty("displayName").GetString()).IsEqualTo("VRC+ Group 1");
        await Assert.That(picker.GetProperty("current").GetProperty("name").GetString()).IsEqualTo("worlds1");
        await Assert.That(picker.GetProperty("isSupporter").GetBoolean()).IsTrue();
        await Assert.That(await client.GetFromJsonAsync<bool>($"/1/accounts/{Account}/favorites/status?targetId={world}")).IsTrue();

        var removed = await client.DeleteAsync($"/1/accounts/{Account}/favorites/{world}");
        await Assert.That(removed.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(Factory.Vrchat.Calls).Contains($"DELETE favorites/{world}");
        await Assert.That(await client.GetFromJsonAsync<bool>($"/1/accounts/{Account}/favorites/status?targetId={world}")).IsFalse();
    }

    [Test]
    public async Task A_vrc_plus_world_group_counts_as_a_world()
    {
        const string world = "wrld_favorite_plus";
        Factory.Vrchat.Json("favorites", $$"""{"id":"fvrt_plus","type":"vrcPlusWorld","favoriteId":"{{world}}","tags":["vrcPlusWorlds1"]}""");
        using var client = await LoginAsync();

        await client.PostAsJsonAsync($"/1/accounts/{Account}/favorites", new { type = "vrcPlusWorld", targetId = world, group = "vrcPlusWorlds1" });

        var picker = await client.GetFromJsonAsync<JsonElement>($"/1/accounts/{Account}/favorites/picker?type=world&targetId={world}");
        await Assert.That(picker.GetProperty("current").GetProperty("displayName").GetString()).IsEqualTo("VRC+ Group 1");
    }

    [Test]
    public async Task A_refused_favorite_is_not_stored()
    {
        const string world = "wrld_favorite_refused";
        Factory.Vrchat.Status("favorites", HttpStatusCode.BadRequest, """{"error":{"message":"Full","status_code":400}}""");
        using var client = await LoginAsync();

        var added = await client.PostAsJsonAsync($"/1/accounts/{Account}/favorites", new { type = "world", targetId = world, group = "worlds1" });

        await Assert.That(added.IsSuccessStatusCode).IsFalse();
        await Assert.That(await client.GetFromJsonAsync<bool>($"/1/accounts/{Account}/favorites/status?targetId={world}")).IsFalse();
    }

    [Test]
    public async Task Local_groups_need_no_vrchat_and_can_be_emptied()
    {
        const string avatar = "avtr_favorite_local";
        using var client = await LoginAsync();

        var added = await client.PostAsJsonAsync($"/1/accounts/{Account}/favorites/local", new { type = "avatar", targetId = avatar, group = "Nice" });
        await Assert.That(added.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        var again = await client.PostAsJsonAsync($"/1/accounts/{Account}/favorites/local", new { type = "avatar", targetId = avatar, group = "Nice" });
        await Assert.That(again.StatusCode).IsEqualTo(HttpStatusCode.NoContent);

        var picker = await client.GetFromJsonAsync<JsonElement>($"/1/accounts/{Account}/favorites/picker?type=avatar&targetId={avatar}");
        var local = picker.GetProperty("local")[0];
        await Assert.That(local.GetProperty("name").GetString()).IsEqualTo("Nice");
        await Assert.That(local.GetProperty("count").GetInt32()).IsEqualTo(1);
        await Assert.That(local.GetProperty("contains").GetBoolean()).IsTrue();
        var list = await client.GetFromJsonAsync<JsonElement>($"/1/accounts/{Account}/favorites/local?type=avatar");
        await Assert.That(list.EnumerateArray().Any(f => f.GetProperty("targetId").GetString() == avatar)).IsTrue();

        await client.DeleteAsync($"/1/accounts/{Account}/favorites/local?type=avatar&targetId={avatar}&group=Nice");
        await Assert.That(await client.GetFromJsonAsync<bool>($"/1/accounts/{Account}/favorites/status?targetId={avatar}")).IsFalse();
    }
}
