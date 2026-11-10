using System.Net.Http.Json;
using System.Text.Json;
using VRCX.Common.VrcxDb;
using VRCX.Testing;

namespace VRCX.API.IntegrationTests.Tests;

// The original's local favorites Refresh, and upstream PR 1525's unavailable avatar detection.
public sealed class LocalFavoriteRefreshTests
{
    [Test]
    public async Task Refresh_stores_what_vrchat_has_and_reports_what_is_gone()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        var now = DateTimeOffset.UtcNow;
        await using (var db = app.CreateDbContext())
        {
            db.Avatars.Add(new Avatar { Id = "avtr_refresh", Name = "Old Name", FirstSeenAt = now });
            await db.SaveChangesAsync();
        }
        app.Vrchat.Avatar("avtr_refresh", "New Name", a => a.Author("usr_maker", "Maker"));

        async Task<bool> Refresh(string id)
        {
            using var response = await app.Api.PostAsync(app.AccountPath($"favorites/local/refresh?type=Avatar&targetId={id}"), null);
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("available").GetBoolean();
        }

        await Assert.That(await Refresh("avtr_refresh")).IsTrue();
        await Assert.That(await Refresh("avtr_gone")).IsFalse();
        await using (var db = app.CreateDbContext())
        {
            var avatar = await db.Avatars.FindAsync("avtr_refresh");
            await Assert.That(avatar!.Name).IsEqualTo("New Name");
        }
    }
}
