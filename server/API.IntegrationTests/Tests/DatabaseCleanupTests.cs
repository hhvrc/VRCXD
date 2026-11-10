using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VRCX.API.Jobs;
using VRCX.Common.Services.Feed;
using VRCX.Common.VrcxDb;
using VRCX.Testing;

namespace VRCX.API.IntegrationTests.Tests;

// The original's Settings, Advanced, Database Cleanup: Purge Avatar Feed Data and the avatar
// auto-cleanup. Only the account's avatar feed rows go; with the defaults nothing is deleted.
public sealed class DatabaseCleanupTests
{
    private const string Friend = "usr_cleanup_friend";
    private const string Shared = "usr_cleanup_shared";
    private const string Stranger = "usr_cleanup_stranger";
    private const string Other = "usr_cleanup_other_account";

    private static async Task<VrcxContext> DbAsync(VrcxScenario app) =>
        await app.Services.GetRequiredService<IDbContextFactory<VrcxContext>>().CreateDbContextAsync();

    /// <summary>
    /// A friend with avatar changes 10, 60, 200, 400 and 800 days ago, plus a stranger's change and a
    /// friend shared with another account (both old), and a status and a bio change of the friend.
    /// </summary>
    private static async Task<VrcxScenario> StartAsync()
    {
        var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        var account = app.AccountId!;
        var now = DateTimeOffset.UtcNow;
        await using var db = await DbAsync(app);
        foreach (var id in new[] { Friend, Shared, Stranger, Other })
            db.Users.Add(new User { Id = id, DisplayName = id, FirstSeenAt = now, UpdatedAt = now });
        db.Accounts.Add(new Account { UserId = Other, LoggedInAt = now, SessionValid = false });
        db.Friendships.AddRange(
            new Friendship { OwnerUserId = account, FriendUserId = Friend, FriendNumber = 1, StartedAt = now.AddYears(-5), LastConfirmedAt = now },
            new Friendship { OwnerUserId = account, FriendUserId = Shared, FriendNumber = 2, StartedAt = now.AddYears(-5), LastConfirmedAt = now },
            new Friendship { OwnerUserId = Other, FriendUserId = Shared, FriendNumber = 1, StartedAt = now.AddYears(-5), LastConfirmedAt = now });
        foreach (var days in new[] { 10, 60, 200, 400, 800 })
            db.UserAvatarChanges.Add(new UserAvatarChange { UserId = Friend, ChangedAt = now.AddDays(-days), ImageUrl = $"https://img/{days}.png" });
        db.UserAvatarChanges.Add(new UserAvatarChange { UserId = Stranger, ChangedAt = now.AddDays(-800), ImageUrl = "https://img/stranger.png" });
        db.UserAvatarChanges.Add(new UserAvatarChange { UserId = Shared, ChangedAt = now.AddDays(-800), ImageUrl = "https://img/shared.png" });
        db.UserStatusChanges.Add(new UserStatusChange { UserId = Friend, ChangedAt = now.AddDays(-800), Status = "busy", PreviousStatus = "active" });
        db.UserBioChanges.Add(new UserBioChange { UserId = Friend, ChangedAt = now.AddDays(-800), Bio = "old" });
        await db.SaveChangesAsync();
        return app;
    }

    private static async Task<int[]> FriendAvatarAgesAsync(VrcxScenario app)
    {
        await using var db = await DbAsync(app);
        var now = DateTimeOffset.UtcNow;
        var at = await db.UserAvatarChanges.Where(c => c.UserId == Friend).Select(c => c.ChangedAt).ToListAsync();
        return at.Select(a => (int)Math.Round((now - a).TotalDays)).Order().ToArray();
    }

    /// <summary>Everything that is not the friend's avatar feed stays whatever ran.</summary>
    private static async Task AssertOthersKeptAsync(VrcxScenario app)
    {
        await using var db = await DbAsync(app);
        await Assert.That(await db.UserAvatarChanges.CountAsync(c => c.UserId == Stranger)).IsEqualTo(1);
        await Assert.That(await db.UserAvatarChanges.CountAsync(c => c.UserId == Shared)).IsEqualTo(1);
        await Assert.That(await db.UserStatusChanges.CountAsync(c => c.UserId == Friend)).IsEqualTo(1);
        await Assert.That(await db.UserBioChanges.CountAsync(c => c.UserId == Friend)).IsEqualTo(1);
    }

    [Test]
    public async Task Nothing_is_deleted_by_default()
    {
        await using var app = await StartAsync();
        var settings = await app.GetJsonAsync($"/1/accounts/{app.AccountId}/settings");
        await Assert.That(settings.GetProperty("avatarAutoCleanup").GetString()).IsEqualTo("Off");

        // The hourly job and a direct run both do nothing while the setting is Off.
        await Assert.That(await app.Services.GetRequiredService<AvatarFeedCleanup>().RunAutoCleanupForAllAsync(CancellationToken.None)).IsEqualTo(0);
        await ActivatorUtilities.CreateInstance<AvatarAutoCleanupJob>(app.Services).Execute();

        // A purge request that names neither days nor all is refused.
        var empty = await app.Api.PostAsJsonAsync($"/1/accounts/{app.AccountId}/database/purge-avatar-feed", new { });
        await Assert.That(empty.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        var zero = await app.Api.PostAsJsonAsync($"/1/accounts/{app.AccountId}/database/purge-avatar-feed", new { olderThanDays = 0 });
        await Assert.That(zero.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);

        await Assert.That(await FriendAvatarAgesAsync(app)).IsEquivalentTo([10, 60, 200, 400, 800]);
        await AssertOthersKeptAsync(app);
    }

    [Test]
    public async Task Purge_older_than_deletes_only_the_older_avatar_feed_rows()
    {
        await using var app = await StartAsync();
        var response = await app.Api.PostAsJsonAsync($"/1/accounts/{app.AccountId}/database/purge-avatar-feed", new { olderThanDays = 365 });
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        await Assert.That(body.GetProperty("deleted").GetInt32()).IsEqualTo(2);

        await Assert.That(await FriendAvatarAgesAsync(app)).IsEquivalentTo([10, 60, 200]);
        await AssertOthersKeptAsync(app);
    }

    [Test]
    public async Task Purge_all_deletes_every_avatar_feed_row()
    {
        await using var app = await StartAsync();
        var response = await app.Api.PostAsJsonAsync($"/1/accounts/{app.AccountId}/database/purge-avatar-feed", new { all = true });
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        await Assert.That(await FriendAvatarAgesAsync(app)).IsEmpty();
        await AssertOthersKeptAsync(app);
    }

    [Test]
    public async Task Auto_cleanup_deletes_rows_older_than_the_setting_at_most_once_a_week()
    {
        await using var app = await StartAsync();
        var account = app.AccountId!;
        var settings = await app.GetJsonAsync($"/1/accounts/{account}/settings");
        var next = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(settings.GetRawText())!;
        next["avatarAutoCleanup"] = JsonSerializer.SerializeToElement("90");
        // The client cannot set when it last ran.
        next["lastAvatarCleanupAt"] = JsonSerializer.SerializeToElement(DateTimeOffset.UtcNow);
        var put = await app.Api.PutAsJsonAsync($"/1/accounts/{account}/settings", next);
        await Assert.That(put.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var stored = await put.Content.ReadFromJsonAsync<JsonElement>();
        await Assert.That(stored.GetProperty("avatarAutoCleanup").GetString()).IsEqualTo("90");
        await Assert.That(stored.TryGetProperty("lastAvatarCleanupAt", out var last) && last.ValueKind != JsonValueKind.Null).IsFalse();

        await ActivatorUtilities.CreateInstance<AvatarAutoCleanupJob>(app.Services).Execute();
        await Assert.That(await FriendAvatarAgesAsync(app)).IsEquivalentTo([10, 60]);
        await AssertOthersKeptAsync(app);

        // Within 7 days of the last run, a longer setting... and new old rows are left alone.
        var cleanup = app.Services.GetRequiredService<AvatarFeedCleanup>();
        await using (var db = await DbAsync(app))
        {
            db.UserAvatarChanges.Add(new UserAvatarChange { UserId = Friend, ChangedAt = DateTimeOffset.UtcNow.AddDays(-500), ImageUrl = "https://img/late.png" });
            await db.SaveChangesAsync();
        }
        await Assert.That(await cleanup.RunAutoCleanupAsync(account, CancellationToken.None)).IsNull();
        await Assert.That(await FriendAvatarAgesAsync(app)).IsEquivalentTo([10, 60, 500]);

        // A week on, it runs again.
        await using (var db = await DbAsync(app))
        {
            var row = await db.Accounts.FirstAsync(a => a.UserId == account);
            row.Settings = row.Settings with { LastAvatarCleanupAt = DateTimeOffset.UtcNow.AddDays(-8) };
            await db.SaveChangesAsync();
        }
        await Assert.That(await cleanup.RunAutoCleanupAsync(account, CancellationToken.None)).IsEqualTo(1);
        await Assert.That(await FriendAvatarAgesAsync(app)).IsEquivalentTo([10, 60]);
    }

    [Test]
    public async Task An_unknown_auto_cleanup_value_is_stored_as_off()
    {
        await using var app = await StartAsync();
        var account = app.AccountId!;
        var settings = await app.GetJsonAsync($"/1/accounts/{account}/settings");
        var next = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(settings.GetRawText())!;
        next["avatarAutoCleanup"] = JsonSerializer.SerializeToElement("1");
        var stored = await (await app.Api.PutAsJsonAsync($"/1/accounts/{account}/settings", next)).Content.ReadFromJsonAsync<JsonElement>();
        await Assert.That(stored.GetProperty("avatarAutoCleanup").GetString()).IsEqualTo("Off");

        await ActivatorUtilities.CreateInstance<AvatarAutoCleanupJob>(app.Services).Execute();
        await Assert.That(await FriendAvatarAgesAsync(app)).IsEquivalentTo([10, 60, 200, 400, 800]);
    }
}
