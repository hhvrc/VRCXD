using System.Net.Http.Json;
using VRCX.Common.Services;
using VRCX.Common.VrcxDb;
using VRCX.Testing;

namespace VRCX.API.IntegrationTests.Tests;

// The Social tab's Favorite Friends Groups (the original's localFavoriteFriendsGroups): which favorite
// groups make someone a favorite friend, and the setting following a local group's rename and delete.
public sealed class FavoriteFriendGroupsTests
{
    [Test]
    public async Task Selected_groups_decide_who_is_a_favorite_friend()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        var me = app.AccountId!;
        var now = DateTimeOffset.UtcNow;
        await using (var db = app.CreateDbContext())
        {
            foreach (var id in new[] { "usr_g0", "usr_g1", "usr_local" })
                if (!db.Users.Any(u => u.Id == id)) db.Users.Add(new User { Id = id, DisplayName = id });
            db.Favorites.Add(new Favorite { Id = "fvrt_g0", OwnerUserId = me, Type = FavoriteType.Friend, TargetId = "usr_g0", GroupName = "group_0", FirstSeenAt = now, FetchedAt = now });
            db.Favorites.Add(new Favorite { Id = "fvrt_g1", OwnerUserId = me, Type = FavoriteType.Friend, TargetId = "usr_g1", GroupName = "group_1", FirstSeenAt = now, FetchedAt = now });
            db.LocalFavorites.Add(new LocalFavorite { OwnerUserId = me, Type = FavoriteType.Friend, GroupName = "Close", TargetId = "usr_local", CreatedAt = now });
            await db.SaveChangesAsync();
        }

        async Task<string[]> Favorites()
        {
            await using var db = app.CreateDbContext();
            return [.. (await FavoriteFriends.IdSetAsync(db, me, CancellationToken.None)).Order()];
        }
        var settings = $"/1/accounts/{me}/settings";

        // Nothing selected: every group.
        await Assert.That(await Favorites()).IsEquivalentTo(new[] { "usr_g0", "usr_g1", "usr_local" });

        await app.Api.PutAsJsonAsync(settings, new { favoriteFriendGroups = new[] { "friend:group_1" } });
        await Assert.That(await Favorites()).IsEquivalentTo(new[] { "usr_g1" });

        // Only local groups picked: VRChat's groups all still count, as in the original.
        await app.Api.PutAsJsonAsync(settings, new { favoriteFriendGroups = new[] { "local:Close" } });
        await Assert.That(await Favorites()).IsEquivalentTo(new[] { "usr_g0", "usr_g1", "usr_local" });

        await app.Api.PutAsJsonAsync(settings, new { favoriteFriendGroups = new[] { "friend:group_0", "local:Close" } });
        await Assert.That(await Favorites()).IsEquivalentTo(new[] { "usr_g0", "usr_local" });

        // Renaming the local group keeps it selected under its new name.
        (await app.Api.PutAsJsonAsync($"/1/accounts/{me}/favorites/local/groups", new { type = "Friend", name = "Close", newName = "Closest" })).EnsureSuccessStatusCode();
        var stored = await app.Api.GetFromJsonAsync<AccountSettings>(settings, System.Text.Json.JsonSerializerOptions.Web);
        await Assert.That(stored!.FavoriteFriendGroups).IsEquivalentTo(new[] { "friend:group_0", "local:Closest" });
        await Assert.That(await Favorites()).IsEquivalentTo(new[] { "usr_g0", "usr_local" });

        (await app.Api.DeleteAsync($"/1/accounts/{me}/favorites/local/groups?type=Friend&name=Closest")).EnsureSuccessStatusCode();
        stored = await app.Api.GetFromJsonAsync<AccountSettings>(settings, System.Text.Json.JsonSerializerOptions.Web);
        await Assert.That(stored!.FavoriteFriendGroups).IsEquivalentTo(new[] { "friend:group_0" });
        await Assert.That(await Favorites()).IsEquivalentTo(new[] { "usr_g0" });
    }
}
