using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VRCX.Common.VrcxDb;

namespace VRCX.API.IntegrationTests.Tests;

// The group pop-up and the avatar picture lookup, with VRChat faked.
public sealed class GroupActionsTests
{
    private const string Account = "usr_groups_account";

    [ClassDataSource<IsolatedHost>(Shared = SharedType.None)]
    public required IsolatedHost Factory { get; init; }

    private async Task<HttpClient> LoginAsync()
    {
        Factory.Vrchat.Json("auth/user", $$"""{"id":"{{Account}}","displayName":"Groups","username":"groups"}""");
        var client = Factory.CreateAuthenticatedClient();
        var login = await client.PostAsJsonAsync("/1/accounts/login", new { username = "groups", password = "pw" });
        await Assert.That(login.StatusCode).IsEqualTo(HttpStatusCode.OK);
        return client;
    }

    [Test]
    public async Task Details_show_the_group_and_its_instances()
    {
        const string group = "grp_details";
        Factory.Vrchat
            .Json($"groups/{group}", """{"id":"grp_details","name":"Live","memberCount":7}""")
            .Json($"users/{Account}/instances/groups/{group}", """{"instances":[{"location":"wrld_a:1~group(grp_details)"}]}""");
        using var client = await LoginAsync();

        var details = await client.GetFromJsonAsync<JsonElement>($"/1/accounts/{Account}/groups/{group}/details");

        await Assert.That(details.GetProperty("group").GetProperty("memberCount").GetInt32()).IsEqualTo(7);
        await Assert.That(details.GetProperty("instances").GetProperty("instances").GetArrayLength()).IsEqualTo(1);
        await Assert.That(details.GetProperty("joinCount").GetInt32()).IsEqualTo(0);
    }

    [Test]
    public async Task Hidden_instances_still_give_the_group()
    {
        const string group = "grp_hidden";
        Factory.Vrchat
            .Json($"groups/{group}", """{"id":"grp_hidden","name":"Live"}""")
            .Status($"users/{Account}/instances/groups/{group}", HttpStatusCode.Forbidden, """{"error":{"message":"Forbidden","status_code":403}}""");
        using var client = await LoginAsync();

        var response = await client.GetAsync($"/1/accounts/{Account}/groups/{group}/details");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task A_group_vrchat_does_not_have_is_not_found()
    {
        const string group = "grp_missing";
        Factory.Vrchat.Status($"groups/{group}", HttpStatusCode.NotFound, """{"error":{"message":"Not found","status_code":404}}""");
        using var client = await LoginAsync();

        var response = await client.GetAsync($"/1/accounts/{Account}/groups/{group}/details");

        await Assert.That(response.IsSuccessStatusCode).IsFalse();
    }

    [Test]
    public async Task Posts_pass_through_and_leaving_reaches_vrchat()
    {
        const string group = "grp_posts";
        Factory.Vrchat
            .Json($"groups/{group}/posts", """{"posts":[{"id":"gpost_1","title":"Hello"}]}""")
            .Json($"groups/{group}/leave", "{}");
        using var client = await LoginAsync();

        var posts = await client.GetFromJsonAsync<JsonElement>($"/1/accounts/{Account}/groups/{group}/posts");
        var left = await client.PostAsync($"/1/accounts/{Account}/groups/{group}/leave", null);

        await Assert.That(posts.GetProperty("posts")[0].GetProperty("title").GetString()).IsEqualTo("Hello");
        await Assert.That(left.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(Factory.Vrchat.Calls).Contains($"POST groups/{group}/leave");
    }

    [Test]
    public async Task A_picture_the_server_has_seen_finds_its_avatar()
    {
        const string avatar = "avtr_lookup";
        const string file = "file_0a1b2c3d-1111-2222-3333-444455556666";
        await using (var db = await Factory.Services.GetRequiredService<IDbContextFactory<VrcxContext>>().CreateDbContextAsync())
        {
            db.Avatars.Add(new Avatar { Id = avatar, Name = "Seen", FirstSeenAt = DateTimeOffset.UtcNow, ImageUrl = $"https://api.vrchat.cloud/api/1/file/{file}/1/file" });
            await db.SaveChangesAsync();
        }
        Factory.Vrchat.Json($"file/{file}", """{"id":"x","ownerId":"usr_owner","name":"Avatar - Seen - Image - 2024","tags":[]}""");
        using var client = await LoginAsync();

        var found = await client.GetFromJsonAsync<JsonElement>($"/1/accounts/{Account}/avatars/lookup?imageUrl={Uri.EscapeDataString($"https://api.vrchat.cloud/api/1/image/{file}/1/256")}&userId=usr_other");

        await Assert.That(found.GetProperty("avatarId").GetString()).IsEqualTo(avatar);
    }
}
