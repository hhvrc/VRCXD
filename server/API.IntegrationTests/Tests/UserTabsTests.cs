using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VRCX.Common.VrcxDb;

namespace VRCX.API.IntegrationTests.Tests;

// The tabs of the user pop-up, with VRChat faked.
public sealed class UserTabsTests
{
    private const string Account = "usr_tabs_account";
    private const string Other = "usr_tabs_other";

    [ClassDataSource<IsolatedHost>(Shared = SharedType.None)]
    public required IsolatedHost Factory { get; init; }

    private async Task<HttpClient> LoginAsync()
    {
        Factory.Vrchat.Json("auth/user", $$"""{"id":"{{Account}}","displayName":"Tabs","username":"tabs"}""");
        var client = Factory.CreateAuthenticatedClient();
        var login = await client.PostAsJsonAsync("/1/accounts/login", new { username = "tabs", password = "pw" });
        await Assert.That(login.StatusCode).IsEqualTo(HttpStatusCode.OK);
        return client;
    }

    [Test]
    public async Task Mutuals_groups_and_worlds_come_live_from_vrchat_and_other_users_only_show_public_worlds()
    {
        Factory.Vrchat
            .Json($"users/{Other}/mutuals/friends?n=100&offset=0", """[{"id":"usr_tabs_mutual","displayName":"Mutual"}]""")
            .Json($"users/{Other}/groups", """[{"groupId":"grp_tabs","name":"Group","memberCount":3}]""")
            .Json($"worlds?user=friends&userId={Other}&releaseStatus=public&sort=updated&order=descending&n=100&offset=0", """[{"id":"wrld_tabs","name":"World","authorId":"usr_tabs_other"}]""")
            .Json($"worlds?user=me&releaseStatus=all&sort=updated&order=descending&n=100&offset=0", """[{"id":"wrld_mine","name":"Mine","authorId":"usr_tabs_account"}]""");
        using var client = await LoginAsync();
        var path = $"/1/accounts/{Account}/users";

        var mutuals = await client.GetFromJsonAsync<JsonElement>($"{path}/{Other}/mutuals");
        await Assert.That(mutuals[0].GetProperty("displayName").GetString()).IsEqualTo("Mutual");
        var groups = await client.GetFromJsonAsync<JsonElement>($"{path}/{Other}/groups");
        await Assert.That(groups[0].GetProperty("memberCount").GetInt32()).IsEqualTo(3);
        var worlds = await client.GetFromJsonAsync<JsonElement>($"{path}/{Other}/worlds");
        await Assert.That(worlds[0].GetProperty("id").GetString()).IsEqualTo("wrld_tabs");
        var own = await client.GetFromJsonAsync<JsonElement>($"{path}/{Account}/worlds");
        await Assert.That(own[0].GetProperty("id").GetString()).IsEqualTo("wrld_mine");
    }

    [Test]
    public async Task A_user_who_hides_their_mutuals_gives_an_error_and_not_a_crash()
    {
        Factory.Vrchat.Status($"users/{Other}_hidden/mutuals/friends?n=100&offset=0", HttpStatusCode.Forbidden, """{"error":{"message":"Forbidden","status_code":403}}""");
        using var client = await LoginAsync();

        var response = await client.GetAsync($"/1/accounts/{Account}/users/{Other}_hidden/mutuals");

        await Assert.That(response.IsSuccessStatusCode).IsFalse();
        await Assert.That(response.StatusCode).IsNotEqualTo(HttpStatusCode.InternalServerError);
    }

    [Test]
    public async Task Favorite_worlds_come_per_world_list_and_a_refused_list_is_left_out()
    {
        const string owner = "usr_tabs_favorites";
        Factory.Vrchat
            .Json($"favorite/groups?ownerId={owner}&n=100&offset=0", """[{"id":"fvgrp_1","type":"world","name":"worlds1","displayName":"One","visibility":"public"},{"id":"fvgrp_2","type":"world","name":"worlds2","displayName":"Two","visibility":"public"},{"id":"fvgrp_3","type":"avatar","name":"avatars1","displayName":"Skip","visibility":"public"}]""")
            .Json($"worlds/favorites?ownerId={owner}&n=100&offset=0&userId={owner}&tag=worlds1", """[{"id":"wrld_fav","name":"Fav","authorId":"usr_x"}]""")
            .Status($"worlds/favorites?ownerId={owner}&n=100&offset=0&userId={owner}&tag=worlds2", HttpStatusCode.Forbidden, """{"error":{"message":"Forbidden","status_code":403}}""");
        using var client = await LoginAsync();

        var lists = await client.GetFromJsonAsync<JsonElement>($"/1/accounts/{Account}/users/{owner}/favorite-worlds");

        await Assert.That(lists.GetArrayLength()).IsEqualTo(1);
        await Assert.That(lists[0].GetProperty("name").GetString()).IsEqualTo("One");
        await Assert.That(lists[0].GetProperty("worlds")[0].GetProperty("id").GetString()).IsEqualTo("wrld_fav");
    }

    [Test]
    public async Task Avatars_are_the_stored_ones_by_that_author()
    {
        await using (var db = await Factory.Services.GetRequiredService<IDbContextFactory<VrcxContext>>().CreateDbContextAsync())
        {
            var now = DateTimeOffset.UtcNow;
            db.Users.Add(new User { Id = Other + "_author", DisplayName = "Author", FirstSeenAt = now, UpdatedAt = now });
            db.Avatars.Add(new Avatar { Id = "avtr_tabs_a", Name = "By them", AuthorId = Other + "_author", FirstSeenAt = now });
            db.Avatars.Add(new Avatar { Id = "avtr_tabs_b", Name = "By nobody", FirstSeenAt = now });
            await db.SaveChangesAsync();
        }
        using var client = Factory.CreateAuthenticatedClient();

        var avatars = await client.GetFromJsonAsync<JsonElement>($"/1/accounts/{Account}/users/{Other}_author/avatars");

        await Assert.That(avatars.GetArrayLength()).IsEqualTo(1);
        await Assert.That(avatars[0].GetProperty("id").GetString()).IsEqualTo("avtr_tabs_a");
    }
}
