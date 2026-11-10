using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VRCX.Common.VrcxDb;

namespace VRCX.API.IntegrationTests.Tests;

// The avatar pop-up, with VRChat faked.
public sealed class AvatarActionsTests
{
    private const string Account = "usr_avatars_account";

    [ClassDataSource<IsolatedHost>(Shared = SharedType.None)]
    public required IsolatedHost Factory { get; init; }

    private async Task<HttpClient> LoginAsync()
    {
        Factory.Vrchat.Json("auth/user", $$"""{"id":"{{Account}}","displayName":"Avatars","username":"avatars"}""");
        var client = Factory.CreateAuthenticatedClient();
        var login = await client.PostAsJsonAsync("/1/accounts/login", new { username = "avatars", password = "pw" });
        await Assert.That(login.StatusCode).IsEqualTo(HttpStatusCode.OK);
        return client;
    }

    [Test]
    public async Task Details_show_the_live_avatar_and_whether_it_is_blocked()
    {
        const string avatar = "avtr_details";
        Factory.Vrchat
            .Json($"avatars/{avatar}", """{"id":"avtr_details","name":"Live","authorId":"usr_x","version":3}""")
            .Json("auth/user/avatarmoderations", """[{"avatarModerationType":"block","targetAvatarId":"avtr_details"}]""");
        using var client = await LoginAsync();

        var details = await client.GetFromJsonAsync<JsonElement>($"/1/accounts/{Account}/avatars/{avatar}/details");

        await Assert.That(details.GetProperty("avatar").GetProperty("version").GetInt32()).IsEqualTo(3);
        await Assert.That(details.GetProperty("isBlocked").GetBoolean()).IsTrue();
    }

    [Test]
    public async Task An_avatar_vrchat_will_not_show_still_gives_details()
    {
        const string avatar = "avtr_hidden";
        Factory.Vrchat
            .Status($"avatars/{avatar}", HttpStatusCode.Forbidden, """{"error":{"message":"Forbidden","status_code":403}}""")
            .Json("auth/user/avatarmoderations", "[]");
        using var client = await LoginAsync();

        var details = await client.GetFromJsonAsync<JsonElement>($"/1/accounts/{Account}/avatars/{avatar}/details");

        await Assert.That(details.GetProperty("isBlocked").GetBoolean()).IsFalse();
    }

    [Test]
    public async Task Blocking_and_unblocking_reach_vrchat()
    {
        const string avatar = "avtr_block";
        Factory.Vrchat.Json("auth/user/avatarmoderations", """{"avatarModerationType":"block","targetAvatarId":"avtr_block"}""");
        using var client = await LoginAsync();

        var blocked = await client.PostAsync($"/1/accounts/{Account}/avatars/{avatar}/block", null);
        var unblocked = await client.DeleteAsync($"/1/accounts/{Account}/avatars/{avatar}/block");

        await Assert.That(blocked.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(Factory.Vrchat.Calls).Contains("POST auth/user/avatarmoderations");
        await Assert.That(Factory.Vrchat.Calls).Contains($"DELETE auth/user/avatarmoderations?targetAvatarId={avatar}&avatarModerationType=block");
        await Assert.That(unblocked.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
    }

    [Test]
    public async Task An_avatar_memo_is_kept_and_an_empty_one_removes_it()
    {
        const string avatar = "avtr_memo";
        using var client = Factory.CreateAuthenticatedClient();
        await using (var db = await Factory.Services.GetRequiredService<IDbContextFactory<VrcxContext>>().CreateDbContextAsync())
        {
            db.Avatars.Add(new Avatar { Id = avatar, Name = "Memo", FirstSeenAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }

        await client.PutAsJsonAsync($"/1/avatars/{avatar}/memo", new { memo = "nice" });
        var read = await client.GetFromJsonAsync<JsonElement>($"/1/avatars/{avatar}/memo");
        await Assert.That(read.GetProperty("memo").GetString()).IsEqualTo("nice");

        await client.PutAsJsonAsync($"/1/avatars/{avatar}/memo", new { memo = " " });
        read = await client.GetFromJsonAsync<JsonElement>($"/1/avatars/{avatar}/memo");
        await Assert.That(read.GetProperty("memo").GetString()).IsEqualTo("");
    }
}
