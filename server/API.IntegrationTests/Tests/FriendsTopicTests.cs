using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using VRCX.Common.Features.Friends;
using VRCX.Common.Live;
using VRCX.Common.Live.Topics;
using VRCX.Common.Services.Ingestion;
using VRCX.Common.Services.Sync;
using VRCX.Common.VrcxDb;
using VRCX.Testing;
using VRCX.Testing.FakeVrchat;
using VRCX.Testing.Live;

namespace VRCX.API.IntegrationTests.Tests;

// The friends topic on /1/ws (docs/live-topics.md): a subscription's snapshot is GET friends, and
// every way a friend changes (the pipeline, the friend sync, the pending offline, the Unfriend
// endpoint) reaches it as a delta, numbered without gaps, for that account only.
public sealed class FriendsTopicTests
{
    private const string Lobby = "wrld_topic:1~region(eu)";
    private const string Elsewhere = "wrld_topic:2~region(eu)";
    /// <summary>Longer than the coalescing window: a delta that was coming has come by then.</summary>
    private static readonly TimeSpan Quiet = FriendsTopic.DefaultCoalesceWindow * 4;

    private static async Task<(VrcxScenario App, FakeUser Alice, FakeUser Bob)> StartAsync()
    {
        var app = await VrcxScenario.StartAsync();
        var alice = app.Vrchat.Friend("usr_alice", "Alice", u => u.OnlineAt(Lobby).Status("join me", "hi"));
        var bob = app.Vrchat.Friend("usr_bob", "Bob", u => u.Offline());
        await app.LoginAsync();
        return (app, alice, bob);
    }

    private static FriendEntry Row(TopicSnapshot snapshot, string userId) => snapshot.Friends.Friends.Single(f => f.UserId == userId);

    [Test]
    public async Task The_hello_lists_the_topic_and_the_snapshot_is_the_friend_list_rest_returns()
    {
        var (app, _, _) = await StartAsync();
        await using var _ = app;
        await using var live = await app.ConnectLiveAsync();
        var hello = live.Received.First(m => m.MessageCase == ServerMessage.MessageOneofCase.Hello).Hello;
        await Assert.That(hello.Topics).Contains(FriendsTopic.TopicName);

        var snapshot = await live.SubscribeSnapshotAsync(1, "friends", app.AccountId);
        var rest = await app.GetJsonAsync(app.AccountPath("friends"));

        await Assert.That(snapshot.Rev).IsEqualTo(1ul);
        await Assert.That(snapshot.Friends.Friends.Select(f => f.UserId)).IsEquivalentTo(rest.EnumerateArray().Select(f => f.GetProperty("userId").GetString()!), TUnit.Assertions.Enums.CollectionOrdering.Matching);
        var alice = Row(snapshot, "usr_alice");
        var restAlice = rest.EnumerateArray().Single(f => f.GetProperty("userId").GetString() == "usr_alice");
        await Assert.That(alice.State).IsEqualTo("online");
        await Assert.That(alice.Location).IsEqualTo(Lobby);
        await Assert.That(alice.Status).IsEqualTo("join me");
        await Assert.That(alice.StatusDescription).IsEqualTo("hi");
        await Assert.That(alice.DisplayName).IsEqualTo(restAlice.GetProperty("displayName").GetString());
        await Assert.That(alice.TrustLevel).IsEqualTo(restAlice.GetProperty("trustLevel").GetString());
        await Assert.That(alice.FriendNumber).IsEqualTo(restAlice.GetProperty("friendNumber").GetInt32());
        await Assert.That(alice.LocationSince.ToDateTimeOffset()).IsEqualTo(restAlice.GetProperty("locationSince").GetDateTimeOffset());
        await Assert.That(Row(snapshot, "usr_bob").State).IsEqualTo("offline");
    }

    [Test]
    public async Task Each_pipeline_friend_event_is_a_whole_row_delta_numbered_one_after_another()
    {
        var (app, alice, bob) = await StartAsync();
        await using var _ = app;
        await using var live = await app.ConnectLiveAsync();
        await live.SubscribeSnapshotAsync(1, "friends", app.AccountId);

        // friend-location
        await app.Pipeline.FriendLocationAsync(alice, Elsewhere);
        var moved = await live.DeltaAsync(1);
        await Assert.That(moved.Rev).IsEqualTo(2ul);
        await Assert.That(moved.Friends.Upserts.Single().Location).IsEqualTo(Elsewhere);
        await Assert.That(moved.Friends.Upserts.Single().DisplayName).IsEqualTo("Alice");

        // friend-online
        await app.Pipeline.FriendOnlineAsync(bob, Lobby);
        var online = await live.DeltaAsync(1);
        await Assert.That(online.Rev).IsEqualTo(3ul);
        var bobRow = online.Friends.Upserts.Single();
        await Assert.That(bobRow.UserId == "usr_bob" && bobRow.State == "online" && bobRow.Location == Lobby).IsTrue();

        // friend-update: a new name and status
        alice.Name("Alicia").Status("busy", "later");
        await app.Pipeline.FriendUpdateAsync(alice);
        var updated = await live.DeltaAsync(1);
        await Assert.That(updated.Rev).IsEqualTo(4ul);
        var alicia = updated.Friends.Upserts.Single();
        await Assert.That(alicia.DisplayName == "Alicia" && alicia.Status == "busy" && alicia.StatusDescription == "later").IsTrue();

        // friend-add
        var carol = app.Vrchat.User("usr_carol", "Carol");
        await app.Pipeline.FriendAddAsync(carol);
        var added = await live.DeltaAsync(1);
        await Assert.That(added.Rev).IsEqualTo(5ul);
        await Assert.That(added.Friends.Upserts.Single().UserId).IsEqualTo("usr_carol");
        await Assert.That(added.Friends.Upserts.Single().FriendNumber).IsGreaterThan(Row(await live.SubscribeSnapshotAsync(9, "friends", app.AccountId), "usr_bob").FriendNumber);

        // friend-delete
        await app.Pipeline.FriendDeleteAsync(alice);
        var removed = await live.DeltaAsync(1);
        await Assert.That(removed.Rev).IsEqualTo(6ul);
        await Assert.That(removed.Friends.RemovedUserIds).IsEquivalentTo(new[] { "usr_alice" });
        await Assert.That(removed.Friends.Upserts).IsEmpty();

        // The same changes again change nothing a row shows: no delta.
        await app.Pipeline.FriendUpdateAsync(carol);
        await live.AssertNoDeltaAsync(1, Quiet);
        await Assert.That(live.Deltas(1).Select(d => d.Rev)).IsEquivalentTo(new ulong[] { 2, 3, 4, 5, 6 }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task A_burst_for_one_friend_is_one_delta_with_where_they_ended_up()
    {
        var (app, alice, _) = await StartAsync();
        await using var _ = app;
        app.Services.GetRequiredService<FriendsTopic>().CoalesceWindow = TimeSpan.FromSeconds(1);
        await using var live = await app.ConnectLiveAsync();
        await live.SubscribeSnapshotAsync(1, "friends", app.AccountId);

        for (var i = 0; i < 5; i++) await app.Pipeline.FriendLocationAsync(alice, $"wrld_burst:{i}~region(eu)");

        var delta = await live.DeltaAsync(1);
        await Assert.That(delta.Friends.Upserts.Single().Location).IsEqualTo("wrld_burst:4~region(eu)");
        await live.AssertNoDeltaAsync(1, TimeSpan.FromSeconds(1.5));
        await Assert.That(live.Deltas(1).Count).IsEqualTo(1);
    }

    [Test]
    public async Task A_friend_reported_offline_is_a_pending_offline_delta_then_an_offline_one()
    {
        var (app, alice, _) = await StartAsync();
        await using var _ = app;
        await using var live = await app.ConnectLiveAsync();
        await live.SubscribeSnapshotAsync(1, "friends", app.AccountId);

        // The original's pendingOffline: still listed online for 170 s, which nothing announced before topics.
        await app.Pipeline.FriendOfflineAsync(alice);
        var pending = (await live.DeltaAsync(1)).Friends.Upserts.Single();
        await Assert.That(pending.PendingOffline).IsTrue();
        await Assert.That(pending.State).IsEqualTo("online");

        app.Time.Advance(PresenceDebouncer.Delay + TimeSpan.FromSeconds(1));
        await Assert.That(await app.Jobs.SweepPendingPresenceAsync()).IsEqualTo(1);
        var offline = (await live.DeltaAsync(1)).Friends.Upserts.Single();
        await Assert.That(offline.PendingOffline).IsFalse();
        await Assert.That(offline.State).IsEqualTo("offline");
    }

    [Test]
    public async Task A_friend_coming_back_inside_the_grace_period_drops_the_pending_offline()
    {
        var (app, alice, _) = await StartAsync();
        await using var _ = app;
        await using var live = await app.ConnectLiveAsync();
        await live.SubscribeSnapshotAsync(1, "friends", app.AccountId);

        await app.Pipeline.FriendOfflineAsync(alice);
        await Assert.That((await live.DeltaAsync(1)).Friends.Upserts.Single().PendingOffline).IsTrue();
        await app.Pipeline.FriendOnlineAsync(alice, Lobby);
        var back = (await live.DeltaAsync(1)).Friends.Upserts.Single();
        await Assert.That(back.PendingOffline).IsFalse();
        await Assert.That(back.State).IsEqualTo("online");
    }

    [Test]
    public async Task The_friend_sync_sends_what_it_changed()
    {
        var (app, alice, bob) = await StartAsync();
        await using var _ = app;
        await using var live = await app.ConnectLiveAsync();
        await live.SubscribeSnapshotAsync(1, "friends", app.AccountId);

        alice.Status("busy", "synced");
        app.Vrchat.Unfriend(bob.Id);
        app.Vrchat.Friend("usr_dave", "Dave");
        await app.Services.GetRequiredService<IAccountSyncService>().SyncFriendsAsync(app.AccountId!, CancellationToken.None);

        // One unit of work, one delta: Alice's new status, Dave in, Bob out.
        var delta = await live.DeltaAsync(1);
        await Assert.That(delta.Friends.Upserts.Select(u => u.UserId)).IsEquivalentTo(new[] { "usr_alice", "usr_dave" });
        await Assert.That(delta.Friends.Upserts.Single(u => u.UserId == "usr_alice").Status).IsEqualTo("busy");
        await Assert.That(delta.Friends.RemovedUserIds).IsEquivalentTo(new[] { "usr_bob" });

        // A sync that finds nothing new sends nothing.
        await app.Services.GetRequiredService<IAccountSyncService>().SyncFriendsAsync(app.AccountId!, CancellationToken.None);
        await live.AssertNoDeltaAsync(1, Quiet);
    }

    [Test]
    public async Task Unfriending_through_the_api_is_a_removal()
    {
        var (app, _, bob) = await StartAsync();
        await using var _ = app;
        await using var live = await app.ConnectLiveAsync();
        await live.SubscribeSnapshotAsync(1, "friends", app.AccountId);

        var response = await app.Api.DeleteAsync(app.AccountPath($"friends/{bob.Id}"));
        await VrcxScenario.EnsureSuccessAsync(response, "unfriend");

        var delta = await live.DeltaAsync(1);
        await Assert.That(delta.Friends.RemovedUserIds).IsEquivalentTo(new[] { "usr_bob" });
    }

    [Test]
    public async Task A_memo_is_the_rows_nickname()
    {
        var (app, _, bob) = await StartAsync();
        await using var _ = app;
        await using var live = await app.ConnectLiveAsync();
        await live.SubscribeSnapshotAsync(1, "friends", app.AccountId);

        await VrcxScenario.EnsureSuccessAsync(await app.Api.PutAsync($"/1/users/{bob.Id}/memo", JsonContent("""{"memo":"Bobby\nmet at the lobby"}""")), "memo");
        await Assert.That((await live.DeltaAsync(1)).Friends.Upserts.Single().Nickname).IsEqualTo("Bobby");
        await VrcxScenario.EnsureSuccessAsync(await app.Api.DeleteAsync($"/1/users/{bob.Id}/memo"), "memo delete");
        await Assert.That((await live.DeltaAsync(1)).Friends.Upserts.Single().Nickname).IsEqualTo("");
    }

    [Test]
    public async Task Unsubscribing_stops_the_deltas_and_the_last_one_out_drops_the_feed()
    {
        var (app, alice, _) = await StartAsync();
        await using var _ = app;
        var topic = app.Services.GetRequiredService<FriendsTopic>();
        await using var live = await app.ConnectLiveAsync();
        await live.SubscribeSnapshotAsync(1, "friends", app.AccountId);
        await live.SubscribeSnapshotAsync(2, "friends", app.AccountId);
        await Assert.That(topic.FeedCount).IsEqualTo(1);

        await live.UnsubscribeAsync(1);
        await app.Pipeline.FriendLocationAsync(alice, Elsewhere);
        await live.DeltaAsync(2);
        await live.AssertNoDeltaAsync(1, Quiet);

        await live.UnsubscribeAsync(2);
        await live.PingAsync();
        await Assert.That(topic.FeedCount).IsEqualTo(0);
    }

    [Test]
    public async Task A_closed_connection_takes_its_subscriptions_with_it()
    {
        var (app, _, _) = await StartAsync();
        await using var _ = app;
        var topic = app.Services.GetRequiredService<FriendsTopic>();
        var live = await app.ConnectLiveAsync();
        await live.SubscribeSnapshotAsync(1, "friends", app.AccountId);
        await live.DisposeAsync();

        var until = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (topic.FeedCount > 0 && DateTime.UtcNow < until) await Task.Delay(50);
        await Assert.That(topic.FeedCount).IsEqualTo(0);
    }

    [Test]
    public async Task Another_accounts_friends_send_it_nothing()
    {
        var (app, alice, _) = await StartAsync();
        await using var _ = app;
        const string other = "usr_topic_other";
        await using (var db = app.CreateDbContext())
        {
            var now = DateTimeOffset.UtcNow;
            db.Users.Add(new User { Id = other, DisplayName = "Other" });
            db.Users.Add(new User { Id = "usr_theirs", DisplayName = "Theirs" });
            db.Accounts.Add(new Account { UserId = other, LoggedInAt = now });
            db.Friendships.Add(new Friendship { OwnerUserId = other, FriendUserId = "usr_theirs", FriendNumber = 1, StartedAt = now, LastConfirmedAt = now });
            // Alice is a friend of both accounts.
            db.Friendships.Add(new Friendship { OwnerUserId = other, FriendUserId = alice.Id, FriendNumber = 2, StartedAt = now, LastConfirmedAt = now });
            await db.SaveChangesAsync();
        }
        await using var live = await app.ConnectLiveAsync();
        await live.SubscribeSnapshotAsync(1, "friends", app.AccountId);
        var theirs = await live.SubscribeSnapshotAsync(2, "friends", other);
        await Assert.That(theirs.Friends.Friends.Select(f => f.UserId)).IsEquivalentTo(new[] { "usr_theirs", "usr_alice" });

        // A friend of only this account, added to it and moving: nothing for the other one.
        var carol = app.Vrchat.User("usr_carol", "Carol");
        await app.Pipeline.FriendAddAsync(carol);
        await app.Pipeline.FriendOnlineAsync(carol, Lobby);
        await live.DeltaAsync(1, d => d.Friends.Upserts.Any(u => u.UserId == "usr_carol" && u.State == "online"));
        await live.AssertNoDeltaAsync(2, Quiet);

        // A friend of both: both hear it.
        await app.Pipeline.FriendLocationAsync(alice, Elsewhere);
        await live.DeltaAsync(1, d => d.Friends.Upserts.Any(u => u.UserId == "usr_alice"));
        await live.DeltaAsync(2, d => d.Friends.Upserts.Any(u => u.UserId == "usr_alice" && u.Location == Elsewhere));
    }

    [Test]
    public async Task A_reconnect_subscribes_again_and_gets_a_fresh_snapshot()
    {
        var (app, alice, _) = await StartAsync();
        await using var _ = app;
        var first = await app.ConnectLiveAsync();
        await first.SubscribeSnapshotAsync(1, "friends", app.AccountId);
        await first.DisposeAsync();

        // Missed while away.
        await app.Pipeline.FriendLocationAsync(alice, Elsewhere);

        await using var again = await app.ConnectLiveAsync();
        var snapshot = await again.SubscribeSnapshotAsync(1, "friends", app.AccountId);
        await Assert.That(snapshot.Rev).IsEqualTo(1ul);
        await Assert.That(Row(snapshot, "usr_alice").Location).IsEqualTo(Elsewhere);
    }

    [Test]
    public async Task Unknown_topics_and_accounts_are_closed_with_a_reason()
    {
        var (app, _, _) = await StartAsync();
        await using var _ = app;
        await using var live = await app.ConnectLiveAsync();

        await live.SubscribeAsync(1, "no-such-topic", app.AccountId);
        await Assert.That((await live.ClosedAsync(1)).Reason).IsEqualTo(TopicCloseReason.UnknownTopic);
        await live.SubscribeAsync(2, "friends", "usr_not_logged_in");
        await Assert.That((await live.ClosedAsync(2)).Reason).IsEqualTo(TopicCloseReason.UnknownAccount);
        await live.SubscribeAsync(3, "friends", null);
        await Assert.That((await live.ClosedAsync(3)).Reason).IsEqualTo(TopicCloseReason.BadRequest);
    }

    [Test]
    public async Task A_connection_too_far_behind_has_the_subscription_closed_for_overflow_not_deltas_dropped()
    {
        var (app, _, _) = await StartAsync();
        await using var _ = app;
        var hub = app.Services.GetRequiredService<TopicHub>();
        await using var live = await app.ConnectLiveAsync();

        // Nothing may wait: even the snapshot is too much for this connection.
        hub.MaxQueued = 0;
        await live.SubscribeAsync(1, "friends", app.AccountId);
        await Assert.That((await live.ClosedAsync(1)).Reason).IsEqualTo(TopicCloseReason.Overflow);
        await Assert.That(live.Received.Any(m => m.MessageCase == ServerMessage.MessageOneofCase.TopicSnapshot)).IsFalse();

        // Caught up again, the client's resubscribe starts over with a snapshot.
        hub.MaxQueued = 256;
        var snapshot = await live.SubscribeSnapshotAsync(2, "friends", app.AccountId);
        await Assert.That(snapshot.Friends.Friends).IsNotEmpty();
    }

    [Test]
    public async Task A_connection_that_cannot_queue_more_closures_is_aborted()
    {
        var (app, _, _) = await StartAsync();
        await using var _ = app;
        var hub = app.Services.GetRequiredService<TopicHub>();
        hub.MaxQueued = 0;
        await using var session = hub.OpenSession();

        session.Subscribe(new Subscribe { SubId = 1, Topic = "no-such-topic" });
        session.Subscribe(new Subscribe { SubId = 2, Topic = "no-such-topic" });

        await Assert.That(session.Closing.IsCancellationRequested).IsTrue();
    }

    private static StringContent JsonContent(string json) => new(json, System.Text.Encoding.UTF8, "application/json");
}
