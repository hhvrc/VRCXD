using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using VRCX.Common.Features.Notifications;
using VRCX.Common.Live;
using VRCX.Common.VrcxDb;
using VRCX.Testing;
using VRCX.Testing.FakeVrchat;
using VRCX.Testing.Live;

namespace VRCX.API.IntegrationTests.Tests;

// The notifications topic on /1/ws (docs/live-topics.md): a subscription's snapshot is the first
// page GET notifications returns with includeHidden, and every way a notification changes (the
// pipeline, the Notifications page's actions, taking one off the page) reaches it as a delta,
// numbered without gaps, for that account only.
public sealed class NotificationsTopicTests
{
    /// <summary>Longer than the coalescing window: a delta that was coming has come by then.</summary>
    private static readonly TimeSpan Quiet = NotificationsTopic.DefaultCoalesceWindow * 4;

    private static async Task<(VrcxScenario App, FakeUser Bob, FakeNotification Invite)> StartAsync()
    {
        var app = await VrcxScenario.StartAsync();
        var bob = app.Vrchat.User("usr_bob", "Bob");
        var invite = app.Vrchat.Notification("invite", bob, n => n.Message("come over").Details(new JsonObject { ["worldName"] = "Lobby" }));
        await app.LoginAsync();
        return (app, bob, invite);
    }

    private static async Task<JsonElement[]> RestAsync(VrcxScenario app) =>
        (await app.GetJsonAsync(app.AccountPath("notifications?pageSize=500&includeHidden=true"))).GetProperty("items").EnumerateArray().ToArray();

    [Test]
    public async Task The_hello_lists_the_topic_and_the_snapshot_is_the_page_rest_returns()
    {
        var (app, bob, invite) = await StartAsync();
        await using var _ = app;
        await using var live = await app.ConnectLiveAsync();
        var hello = live.Received.First(m => m.MessageCase == ServerMessage.MessageOneofCase.Hello).Hello;
        await Assert.That(hello.Topics).Contains(NotificationsTopic.TopicName);

        var snapshot = await live.SubscribeSnapshotAsync(1, "notifications", app.AccountId);
        var rest = await RestAsync(app);

        await Assert.That(snapshot.Rev).IsEqualTo(1ul);
        await Assert.That(snapshot.Notifications.Notifications.Select(n => n.Id)).IsEquivalentTo(rest.Select(n => n.GetProperty("id").GetString()!), TUnit.Assertions.Enums.CollectionOrdering.Matching);
        var row = snapshot.Notifications.Notifications.Single(n => n.Id == invite.Id);
        var restRow = rest.Single(n => n.GetProperty("id").GetString() == invite.Id);
        await Assert.That(row.Type).IsEqualTo("invite");
        await Assert.That(row.Version).IsEqualTo(1);
        await Assert.That(row.SenderUserId).IsEqualTo(bob.Id);
        await Assert.That(row.SenderDisplayName).IsEqualTo(restRow.GetProperty("senderDisplayName").GetString());
        await Assert.That(row.Message).IsEqualTo("come over");
        await Assert.That(JsonDocument.Parse(row.DetailsJson).RootElement.GetProperty("worldName").GetString()).IsEqualTo("Lobby");
        await Assert.That(row.Seen).IsFalse();
        await Assert.That(row.Hidden).IsFalse();
        await Assert.That(row.CreatedAt.ToDateTimeOffset()).IsEqualTo(restRow.GetProperty("createdAt").GetDateTimeOffset());
    }

    [Test]
    public async Task A_notification_arriving_and_being_seen_are_whole_row_deltas_numbered_one_after_another()
    {
        var (app, bob, _) = await StartAsync();
        await using var __ = app;
        await using var live = await app.ConnectLiveAsync();
        await live.SubscribeSnapshotAsync(1, "notifications", app.AccountId);

        var request = app.Vrchat.Notification("requestInvite", bob, n => n.Message("can I join"));
        await app.Pipeline.NotificationAsync(request);
        var arrived = await live.DeltaAsync(1);
        await Assert.That(arrived.Rev).IsEqualTo(2ul);
        var row = arrived.Notifications.Upserts.Single();
        await Assert.That((row.Id, row.Type, row.Message, row.Seen)).IsEqualTo((request.Id, "requestInvite", "can I join", false));
        await Assert.That(arrived.Notifications.RemovedIds).IsEmpty();

        await app.Pipeline.SeeNotificationAsync(request.Id);
        var seen = await live.DeltaAsync(1, d => d.Rev == 3);
        await Assert.That(seen.Notifications.Upserts.Single().Id).IsEqualTo(request.Id);
        await Assert.That(seen.Notifications.Upserts.Single().Seen).IsTrue();
    }

    [Test]
    public async Task Declining_marks_the_row_hidden_and_taking_it_off_the_page_removes_it()
    {
        var (app, _, invite) = await StartAsync();
        await using var __ = app;
        await using var live = await app.ConnectLiveAsync();
        await live.SubscribeSnapshotAsync(1, "notifications", app.AccountId);

        await VrcxScenario.EnsureSuccessAsync(await app.Api.PostAsync(app.AccountPath($"notifications/{invite.Id}/hide"), null), "decline");
        var hidden = await live.DeltaAsync(1);
        await Assert.That(hidden.Notifications.Upserts.Single().Id).IsEqualTo(invite.Id);
        await Assert.That(hidden.Notifications.Upserts.Single().Hidden).IsTrue();

        await Assert.That((await app.Api.DeleteAsync(app.AccountPath($"notifications/{invite.Id}"))).StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        var removed = await live.DeltaAsync(1, d => d.Rev == 3);
        await Assert.That(removed.Notifications.RemovedIds).IsEquivalentTo(new[] { invite.Id });
        await Assert.That(removed.Notifications.Upserts).IsEmpty();
        await Assert.That((await RestAsync(app)).Select(n => n.GetProperty("id").GetString())).DoesNotContain(invite.Id);
    }

    [Test]
    public async Task An_answered_friend_request_leaves_the_list_as_it_leaves_rests()
    {
        await using var app = await VrcxScenario.StartAsync();
        var bob = app.Vrchat.User("usr_bob", "Bob");
        var request = app.Vrchat.Notification("friendRequest", bob);
        await app.LoginAsync();
        await using var live = await app.ConnectLiveAsync();
        var snapshot = await live.SubscribeSnapshotAsync(1, "notifications", app.AccountId);
        await Assert.That(snapshot.Notifications.Notifications.Select(n => n.Id)).Contains(request.Id);

        // Answered on another device: VRChat no longer lists it, and the refresh hides it in one statement.
        app.Vrchat.RemoveNotification(request.Id);
        await VrcxScenario.EnsureSuccessAsync(await app.Api.PostAsync(app.AccountPath("notifications/refresh"), null), "refresh");

        var delta = await live.DeltaAsync(1, d => d.Notifications.RemovedIds.Contains(request.Id));
        await Assert.That(delta.Notifications.Upserts.Select(n => n.Id)).DoesNotContain(request.Id);
        await Assert.That((await RestAsync(app)).Select(n => n.GetProperty("id").GetString())).DoesNotContain(request.Id);
    }

    [Test]
    public async Task A_sender_changing_their_name_sends_the_rows_they_sent_with_the_new_name()
    {
        var (app, bob, invite) = await StartAsync();
        await using var __ = app;
        var carol = app.Vrchat.User("usr_carol", "Carol");
        var other = app.Vrchat.Notification("invite", carol);
        await app.Pipeline.NotificationAsync(other);
        await using var live = await app.ConnectLiveAsync();
        var snapshot = await live.SubscribeSnapshotAsync(1, "notifications", app.AccountId);
        await Assert.That(snapshot.Notifications.Notifications.Single(n => n.Id == invite.Id).SenderDisplayName).IsEqualTo("Bob");
        await Assert.That(snapshot.Notifications.Notifications.Select(n => n.Id)).Contains(other.Id);

        await using (var db = app.CreateDbContext())
        {
            var user = await db.Users.FindAsync(bob.Id);
            user!.DisplayName = "Bobby";
            await db.SaveChangesAsync();
        }

        var renamed = await live.DeltaAsync(1);
        // Bob's row only: Carol's shows nothing new.
        await Assert.That(renamed.Notifications.Upserts.Select(n => (n.Id, n.SenderDisplayName))).IsEquivalentTo(new[] { (invite.Id, "Bobby") });
        var rest = (await RestAsync(app)).Single(n => n.GetProperty("id").GetString() == invite.Id);
        await Assert.That(rest.GetProperty("senderDisplayName").GetString()).IsEqualTo("Bobby");

        // A user who sent nothing changes their name: nothing is sent.
        await using (var db = app.CreateDbContext())
        {
            db.Users.Add(new User { Id = "usr_stranger", DisplayName = "Stranger" });
            await db.SaveChangesAsync();
            (await db.Users.FindAsync("usr_stranger"))!.DisplayName = "Stranger2";
            await db.SaveChangesAsync();
        }
        await live.AssertNoDeltaAsync(1, Quiet);
    }

    [Test]
    public async Task Changes_within_the_window_are_one_delta_and_a_change_nobody_can_see_is_none()
    {
        var (app, bob, invite) = await StartAsync();
        await using var __ = app;
        app.Services.GetRequiredService<NotificationsTopic>().CoalesceWindow = TimeSpan.FromSeconds(1);
        await using var live = await app.ConnectLiveAsync();
        await live.SubscribeSnapshotAsync(1, "notifications", app.AccountId);

        var first = app.Vrchat.Notification("requestInvite", bob);
        var second = app.Vrchat.Notification("requestInvite", bob);
        await app.Pipeline.NotificationAsync(first);
        await app.Pipeline.NotificationAsync(second);
        await app.Pipeline.SeeNotificationAsync(first.Id);

        var delta = await live.DeltaAsync(1);
        await Assert.That(delta.Rev).IsEqualTo(2ul);
        await Assert.That(delta.Notifications.Upserts.Select(n => n.Id)).IsEquivalentTo(new[] { first.Id, second.Id });
        await Assert.That(delta.Notifications.Upserts.Single(n => n.Id == first.Id).Seen).IsTrue();

        // The same row written again with the same values: read again, found equal, not sent.
        await using (var db = app.CreateDbContext())
        {
            var row = await db.Notifications.FindAsync(app.AccountId, invite.Id);
            row!.FetchedAt = row.FetchedAt.AddSeconds(1);
            await db.SaveChangesAsync();
        }
        await live.AssertNoDeltaAsync(1, TimeSpan.FromSeconds(2));
    }

    [Test]
    public async Task Another_accounts_notifications_do_not_reach_this_subscription_and_unsubscribing_drops_the_feed()
    {
        var (app, _, _) = await StartAsync();
        await using var __ = app;
        var topic = app.Services.GetRequiredService<NotificationsTopic>();
        await using var live = await app.ConnectLiveAsync();
        await live.SubscribeSnapshotAsync(1, "notifications", app.AccountId);
        await Assert.That(topic.FeedCount).IsEqualTo(1);

        const string other = "usr_other_account";
        await using (var db = app.CreateDbContext())
        {
            var now = DateTimeOffset.UtcNow;
            db.Users.Add(new User { Id = other, DisplayName = "Other" });
            db.Accounts.Add(new Account { UserId = other, LoggedInAt = now });
            db.Notifications.Add(new Notification { Id = "not_theirs", OwnerUserId = other, Type = "invite", Version = 1, CreatedAt = now, FetchedAt = now });
            await db.SaveChangesAsync();
        }
        await live.AssertNoDeltaAsync(1, Quiet);

        await live.UnsubscribeAsync(1);
        await live.PingAsync();
        await Assert.That(topic.FeedCount).IsEqualTo(0);
    }
}
