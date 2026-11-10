using VRCX.Common.Services.Presence;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VRCX.Common.Services.Automation;
using VRCX.Common.Services.Ingestion;
using VRCX.Common.VrcxDb;
using VRCX.Vrchat;
using VrchatNotification = VRCX.Vrchat.Models.Notification;

namespace VRCX.API.IntegrationTests.Tests;

// Auto Status and Invites, against the fake VRChat. The background loop does not run in tests,
// so each check calls what it calls.
public sealed class AutomationTests
{
    private const string Account = "usr_auto";
    private const string Friend = "usr_autofriend";
    private const string Location = "wrld_auto:123";
    private const string Client = "client_auto";

    [ClassDataSource<IsolatedHost>(Shared = SharedType.None)]
    public required IsolatedHost Factory { get; init; }

    private AccountAutomation Automation => Factory.Services.GetRequiredService<AccountAutomation>();
    private GameClientRegistry Clients => Factory.Services.GetRequiredService<GameClientRegistry>();

    private static string Me(string status) =>
        $$"""{"id":"{{Account}}","displayName":"Auto","username":"auto","status":"{{status}}","tags":[],"onlineFriends":[],"activeFriends":[],"friendGroupNames":[]}""";

    private int CallCount(string call)
    {
        lock (Factory.Vrchat.Calls) return Factory.Vrchat.Calls.Count(c => c == call);
    }

    private async Task<HttpClient> SetUpAsync()
    {
        Factory.Vrchat
            .Json("auth/user", Me("active"))
            .Json("auth/user/friends?offline=false&n=100&offset=0", $$"""[{"id":"{{Friend}}","displayName":"AutoFriend","location":"offline","tags":[],"status":"active"}]""")
            .Json("auth/user/friends?offline=true&n=100&offset=0", "[]")
            .Json($"users/{Account}/groups", "[]")
            .Json("favorite/groups?n=100", "[]")
            .Json("favorites?n=100&offset=0", "[]")
            .Json("auth/user/playermoderations", "[]")
            .Json("userNotes?n=100&offset=0", "[]")
            .Json("auth/user/notifications?n=100&offset=0", "[]")
            .Json("notifications?n=100&offset=0", "[]")
            .Json("worlds/wrld_auto", """{"id":"wrld_auto","name":"Auto World","tags":[]}""");
        var client = Factory.CreateAuthenticatedClient();
        var login = await client.PostAsJsonAsync("/1/accounts/login", new { username = "auto", password = "pw" });
        await Assert.That(login.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That((await client.PostAsync($"/1/accounts/{Account}/sync", null)).StatusCode).IsEqualTo(HttpStatusCode.NoContent);

        await using var db = await Factory.Services.GetRequiredService<IDbContextFactory<VrcxContext>>().CreateDbContextAsync();
        var store = Factory.Services.GetRequiredService<VrchatStore>();
        await store.EnsureInstanceAsync(db, VrchatLocation.Parse(Location), DateTimeOffset.UtcNow, CancellationToken.None);
        await db.Database.ExecuteSqlAsync($"DELETE FROM stored_instance_visits WHERE instance_location = {Location}");
        await db.Favorites.Where(f => f.OwnerUserId == Account).ExecuteDeleteAsync();
        await db.GameLogClients.Where(c => c.ClientId == Client).ExecuteDeleteAsync();
        db.GameLogClients.Add(new GameLogClient { ClientId = Client, AccountId = Account, FirstSeenAt = DateTimeOffset.UtcNow, LastSeenAt = DateTimeOffset.UtcNow });
        db.InstanceVisits.Add(new InstanceVisit { UserId = Account, InstanceLocation = Location, JoinedAt = DateTimeOffset.UtcNow, Source = VisitSource.GameLog });
        await db.SaveChangesAsync();
        await Factory.Services.GetRequiredService<IGameClientState>().SetAsync(Client, new GameClientLive(Location, DateTimeOffset.UtcNow), CancellationToken.None);
        return client;
    }

    private async Task AddFriendVisitAsync()
    {
        await using var db = await Factory.Services.GetRequiredService<IDbContextFactory<VrcxContext>>().CreateDbContextAsync();
        db.InstanceVisits.Add(new InstanceVisit { UserId = Friend, InstanceLocation = Location, JoinedAt = DateTimeOffset.UtcNow, Source = VisitSource.GameLog });
        await db.SaveChangesAsync();
    }

    [Test]
    public async Task Status_follows_who_shares_the_instance()
    {
        using var client = await SetUpAsync();
        var settings = $"/1/accounts/{Account}/settings";
        var ct = CancellationToken.None;
        var put = $"PUT users/{Account}";
        var before = CallCount(put);

        // Off by default, whatever the instance looks like.
        await Assert.That(await Automation.UpdateStatusAsync(Account, ct)).IsNull();

        await client.PutAsJsonAsync(settings, new { autoStateChangeEnabled = true });
        // No desktop app connected means no game.
        await Assert.That(await Automation.UpdateStatusAsync(Account, ct)).IsNull();

        Clients.Connected(Client);
        try
        {
            Factory.Vrchat.Json($"users/{Account}", Me("join me"));
            await Assert.That(await Automation.UpdateStatusAsync(Account, ct)).IsEqualTo("join me");
            await Assert.That(CallCount(put)).IsEqualTo(before + 1);

            // Already right: nothing more is sent.
            await Assert.That(await Automation.UpdateStatusAsync(Account, ct)).IsNull();
            await Assert.That(CallCount(put)).IsEqualTo(before + 1);

            await AddFriendVisitAsync();
            Factory.Vrchat.Json($"users/{Account}", Me("busy"));
            await Assert.That(await Automation.UpdateStatusAsync(Account, ct)).IsEqualTo("busy");

            // Only friends in a chosen favorite group count, and this friend is in none.
            await client.PutAsJsonAsync(settings, new { autoStateChangeEnabled = true, autoStateChangeNoFriends = true, autoStateChangeGroups = new[] { "group_0" } });
            Factory.Vrchat.Json($"users/{Account}", Me("join me"));
            await Assert.That(await Automation.UpdateStatusAsync(Account, ct)).IsEqualTo("join me");

            // Outside the chosen instance types nothing changes.
            await client.PutAsJsonAsync(settings, new { autoStateChangeEnabled = true, autoStateChangeAloneStatus = "ask me", autoStateChangeInstanceTypes = new[] { "invite" } });
            await Assert.That(await Automation.UpdateStatusAsync(Account, ct)).IsNull();
        }
        finally
        {
            Clients.Disconnected(Client);
            await client.PutAsJsonAsync(settings, new { });
        }
    }

    [Test]
    public async Task Invite_requests_are_answered_for_favorites()
    {
        using var client = await SetUpAsync();
        var settings = $"/1/accounts/{Account}/settings";
        var ct = CancellationToken.None;
        var request = new VrchatNotification { Id = "not_auto", Type = "requestInvite", SenderUserId = Friend, SenderUsername = "AutoFriend" };
        var invite = $"POST invite/{Friend}";
        var before = CallCount(invite);
        Factory.Vrchat
            .Json($"invite/{Friend}", """{"id":"not_sent"}""")
            .Json("auth/user/notifications/not_auto/hide", """{"id":"not_auto"}""");

        Clients.Connected(Client);
        try
        {
            await Assert.That(await Automation.AnswerInviteRequestAsync(Account, request, ct)).IsFalse();

            await client.PutAsJsonAsync(settings, new { autoAcceptInviteRequests = "All Favorites" });
            // Not a favorite yet.
            await Assert.That(await Automation.AnswerInviteRequestAsync(Account, request, ct)).IsFalse();

            await using (var db = await Factory.Services.GetRequiredService<IDbContextFactory<VrcxContext>>().CreateDbContextAsync())
            {
                db.Favorites.Add(new Favorite { Id = "fvrt_auto", OwnerUserId = Account, Type = FavoriteType.Friend, TargetId = Friend, GroupName = "group_0", FirstSeenAt = DateTimeOffset.UtcNow, FetchedAt = DateTimeOffset.UtcNow });
                await db.SaveChangesAsync();
            }

            await client.PutAsJsonAsync(settings, new { autoAcceptInviteRequests = "All Favorites", autoAcceptInviteStatuses = new[] { "ask me" } });
            await Assert.That(await Automation.AnswerInviteRequestAsync(Account, request, ct)).IsFalse();

            await client.PutAsJsonAsync(settings, new { autoAcceptInviteRequests = "Selected Favorites", autoAcceptInviteGroups = new[] { "group_1" } });
            await Assert.That(await Automation.AnswerInviteRequestAsync(Account, request, ct)).IsFalse();
            await Assert.That(CallCount(invite)).IsEqualTo(before);

            await client.PutAsJsonAsync(settings, new { autoAcceptInviteRequests = "Selected Favorites", autoAcceptInviteGroups = new[] { "group_0" } });
            await Assert.That(await Automation.AnswerInviteRequestAsync(Account, request, ct)).IsTrue();
            await Assert.That(CallCount(invite)).IsEqualTo(before + 1);
            await Assert.That(CallCount("PUT auth/user/notifications/not_auto/hide")).IsGreaterThanOrEqualTo(1);
        }
        finally
        {
            Clients.Disconnected(Client);
            await client.PutAsJsonAsync(settings, new { });
        }
    }

    [Test]
    public async Task Invite_requests_count_local_favorite_groups_and_the_originals_keys()
    {
        using var client = await SetUpAsync();
        var settings = $"/1/accounts/{Account}/settings";
        var ct = CancellationToken.None;
        var request = new VrchatNotification { Id = "not_local", Type = "requestInvite", SenderUserId = Friend, SenderUsername = "AutoFriend" };
        var invite = $"POST invite/{Friend}";
        Factory.Vrchat
            .Json($"invite/{Friend}", """{"id":"not_sent"}""")
            .Json("auth/user/notifications/not_local/hide", """{"id":"not_local"}""");
        await using (var db = await Factory.Services.GetRequiredService<IDbContextFactory<VrcxContext>>().CreateDbContextAsync())
        {
            db.LocalFavorites.Add(new LocalFavorite { OwnerUserId = Account, Type = FavoriteType.Friend, GroupName = "Close", TargetId = Friend, CreatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }

        Clients.Connected(Client);
        try
        {
            var before = CallCount(invite);
            // A VRChat group the friend is not in, by the original's key.
            await client.PutAsJsonAsync(settings, new { autoAcceptInviteRequests = "Selected Favorites", autoAcceptInviteGroups = new[] { "friend:group_2" } });
            await Assert.That(await Automation.AnswerInviteRequestAsync(Account, request, ct)).IsFalse();
            await Assert.That(CallCount(invite)).IsEqualTo(before);

            // Only in a local group: All Favorites and that local group both count, as in the original.
            await client.PutAsJsonAsync(settings, new { autoAcceptInviteRequests = "All Favorites" });
            await Assert.That(await Automation.AnswerInviteRequestAsync(Account, request, ct)).IsTrue();
            await client.PutAsJsonAsync(settings, new { autoAcceptInviteRequests = "Selected Favorites", autoAcceptInviteGroups = new[] { "local:Close" } });
            await Assert.That(await Automation.AnswerInviteRequestAsync(Account, request, ct)).IsTrue();
            await Assert.That(CallCount(invite)).IsEqualTo(before + 2);
        }
        finally
        {
            Clients.Disconnected(Client);
            await client.PutAsJsonAsync(settings, new { });
        }
    }
}
