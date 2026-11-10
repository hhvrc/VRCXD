using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VRCX.Common.VrcxDb;

namespace VRCX.API.IntegrationTests.Tests;

// The Favorites page's local avatar history and the invalid local avatar check.
public sealed class AvatarHistoryTests
{
    private const string Account = "usr_history_account";

    [ClassDataSource<IsolatedHost>(Shared = SharedType.None)]
    public required IsolatedHost Factory { get; init; }

    private async Task<HttpClient> LoginAsync()
    {
        Factory.Vrchat.Json("auth/user", $$"""{"id":"{{Account}}","displayName":"History","username":"history"}""");
        var client = Factory.CreateAuthenticatedClient();
        var login = await client.PostAsJsonAsync("/1/accounts/login", new { username = "history", password = "pw" });
        await Assert.That(login.StatusCode).IsEqualTo(HttpStatusCode.OK);
        return client;
    }

    [Test]
    public async Task History_lists_others_avatars_newest_first_and_clearing_keeps_usage()
    {
        using var client = await LoginAsync();
        var now = DateTimeOffset.UtcNow;
        await using (var db = await Factory.Services.GetRequiredService<IDbContextFactory<VrcxContext>>().CreateDbContextAsync())
        {
            if (await db.Users.FindAsync("usr_other") is null) db.Users.Add(new User { Id = "usr_other", DisplayName = "Other" });
            db.Avatars.AddRange(
                new Avatar { Id = "avtr_hist_old", Name = "Old", AuthorId = "usr_other", AuthorName = "Other", FirstSeenAt = now },
                new Avatar { Id = "avtr_hist_new", Name = "New", AuthorId = "usr_other", AuthorName = "Other", FirstSeenAt = now },
                new Avatar { Id = "avtr_hist_own", Name = "Own", AuthorId = Account, FirstSeenAt = now });
            db.AvatarUsages.AddRange(
                new AvatarUsage { OwnerUserId = Account, AvatarId = "avtr_hist_old", LastUsedAt = now.AddHours(-2) },
                new AvatarUsage { OwnerUserId = Account, AvatarId = "avtr_hist_new", LastUsedAt = now.AddHours(-1) },
                new AvatarUsage { OwnerUserId = Account, AvatarId = "avtr_hist_own", LastUsedAt = now });
            await db.SaveChangesAsync();
        }

        var history = await client.GetFromJsonAsync<JsonElement>($"/1/accounts/{Account}/avatar-history");
        await Assert.That(history.GetArrayLength()).IsEqualTo(2);
        await Assert.That(history[0].GetProperty("id").GetString()).IsEqualTo("avtr_hist_new");

        var cleared = await client.DeleteAsync($"/1/accounts/{Account}/avatar-history");
        var after = await client.GetFromJsonAsync<JsonElement>($"/1/accounts/{Account}/avatar-history");

        await Assert.That(cleared.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(after.GetArrayLength()).IsEqualTo(0);
        await using var check = await Factory.Services.GetRequiredService<IDbContextFactory<VrcxContext>>().CreateDbContextAsync();
        await Assert.That(await check.AvatarUsages.CountAsync(u => u.OwnerUserId == Account)).IsEqualTo(3);
    }

    [Test]
    public async Task An_avatar_vrchat_refuses_counts_as_gone()
    {
        Factory.Vrchat
            .Json("avatars/avtr_alive", """{"id":"avtr_alive","name":"Alive","authorId":"usr_other"}""")
            .Status("avatars/avtr_gone", HttpStatusCode.NotFound, """{"error":{"message":"Not found","status_code":404}}""");
        using var client = await LoginAsync();

        var alive = await client.GetFromJsonAsync<JsonElement>($"/1/accounts/{Account}/avatars/avtr_alive/exists");
        var gone = await client.GetFromJsonAsync<JsonElement>($"/1/accounts/{Account}/avatars/avtr_gone/exists");

        await Assert.That(alive.GetProperty("exists").GetBoolean()).IsTrue();
        await Assert.That(gone.GetProperty("exists").GetBoolean()).IsFalse();
    }
}
