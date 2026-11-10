using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VRCX.Common.Services.Ingestion;
using VRCX.Testing;
using VRCX.Vrchat;

namespace VRCX.API.IntegrationTests.Tests;

// The original's sanitize steps on everything it applies (sanitizeUserJson, sanitizeEntityJson,
// sanitizeNotificationJson): VRChat's look-alike symbols back to ASCII, and no emojis in a status.
public sealed class SanitizedTextTests
{
    [Test]
    public async Task A_status_description_loses_its_look_alikes_and_emojis()
    {
        await using var app = await VrcxScenario.StartAsync();
        app.Vrchat.Friend("usr_status", "Status Friend", u => u.Status("active", "hello？ 🎉 world＠home"));
        await app.LoginAsync();
        await app.Jobs.FriendSyncAsync();

        var friend = (await app.GetJsonAsync(app.AccountPath("friends"))).EnumerateArray().Single();
        await Assert.That(friend.GetProperty("statusDescription").GetString()).IsEqualTo("hello? world@home");
    }

    [Test]
    public async Task A_status_stored_before_the_cleanup_is_not_a_status_change()
    {
        await using var app = await VrcxScenario.StartAsync();
        var friend = app.Vrchat.Friend("usr_old", "Old Status", u => u.Status("active", "party 🎉 time"));
        await app.LoginAsync();
        await app.Jobs.FriendSyncAsync();
        await using (var db = app.CreateDbContext())
            await db.Users.Where(u => u.Id == "usr_old").ExecuteUpdateAsync(s => s.SetProperty(u => u.StatusDescription, "party 🎉 time"));

        await app.Pipeline.FriendUpdateAsync(friend);
        await app.Jobs.FriendSyncAsync();

        await using var check = app.CreateDbContext();
        await Assert.That(await check.UserStatusChanges.CountAsync(c => c.UserId == "usr_old")).IsEqualTo(0);
        await Assert.That(await check.Users.Where(u => u.Id == "usr_old").Select(u => u.StatusDescription).SingleAsync()).IsEqualTo("party time");
    }

    [Test]
    public async Task A_world_name_and_description_lose_their_look_alikes()
    {
        await using var app = await VrcxScenario.StartAsync();
        app.Vrchat.World("wrld_symbols", "Club（VR）", w => w.Set("description", "Come inǃ Open 24⁄7"));
        await app.LoginAsync();

        var world = await app.GetJsonAsync("/1/worlds/wrld_symbols?refresh=true");
        await Assert.That(world.GetProperty("name").GetString()).IsEqualTo("Club(VR)");
        await Assert.That(world.GetProperty("description").GetString()).IsEqualTo("Come in! Open 24/7");
    }

    [Test]
    public async Task A_group_name_description_rules_and_roles_lose_their_look_alikes()
    {
        await using var app = await VrcxScenario.StartAsync();
        var api = JsonSerializer.Deserialize<Vrchat.Models.Group>("""
            {"id":"grp_symbols","name":"Friends＆Co","shortCode":"FCO","discriminator":"0000","description":"We meet at 8˸00","rules":"Be niceǃ","ownerId":"usr_owner",
             "roles":[{"id":"grol_1","groupId":"grp_symbols","name":"Mods＋","description":"Keep order［always］"}]}
            """, VrchatJson.Options)!;
        await using (var db = app.CreateDbContext())
        {
            await app.Services.GetRequiredService<VrchatStore>().UpsertGroupAsync(db, api, app.Time.GetUtcNow(), CancellationToken.None);
            await db.SaveChangesAsync();
        }

        await using var check = app.CreateDbContext();
        var group = await check.Groups.SingleAsync(g => g.Id == "grp_symbols");
        await Assert.That((group.Name, group.Description, group.Rules)).IsEqualTo(("Friends&Co", "We meet at 8:00", "Be nice!"));
        var role = await check.GroupRoles.SingleAsync(r => r.Id == "grol_1");
        await Assert.That((role.Name, role.Description)).IsEqualTo(("Mods+", "Keep order[always]"));
    }

    [Test]
    public async Task A_notification_message_and_title_lose_their_look_alikes()
    {
        await using var app = await VrcxScenario.StartAsync();
        var bob = app.Vrchat.User("usr_bob", "Bob");
        var invite = app.Vrchat.Notification("invite", bob, n => n.Message("Come join？"));
        await app.LoginAsync();

        var row = (await app.GetJsonAsync(app.AccountPath("notifications?pageSize=500"))).GetProperty("items").EnumerateArray()
            .Single(n => n.GetProperty("id").GetString() == invite.Id);
        await Assert.That(row.GetProperty("message").GetString()).IsEqualTo("Come join?");

        var v2 = JsonSerializer.Deserialize<Vrchat.Models.NotificationV2>("""
            {"id":"not_v2","type":"group.announcement","title":"News＠Club","message":"Party（tonight）","senderUserId":"usr_bob"}
            """, VrchatJson.Options)!;
        await using (var db = app.CreateDbContext())
        {
            await app.Services.GetRequiredService<VrchatStore>().UpsertNotificationAsync(db, app.AccountId!, v2, app.Time.GetUtcNow(), CancellationToken.None);
            await db.SaveChangesAsync();
        }
        await using var check = app.CreateDbContext();
        var stored = await check.Notifications.SingleAsync(n => n.Id == "not_v2");
        await Assert.That((stored.Title, stored.Message)).IsEqualTo(("News@Club", "Party(tonight)"));
    }

    [Test]
    public async Task A_user_note_loses_its_look_alikes()
    {
        await using var app = await VrcxScenario.StartAsync();
        app.Vrchat.User("usr_noted", "Noted");
        app.Vrchat.UserNote("usr_noted", "met at club＃1");
        await app.LoginAsync();

        await using var check = app.CreateDbContext();
        var note = await check.UserNotes.Where(n => n.TargetUserId == "usr_noted").Select(n => n.Note).SingleAsync();
        await Assert.That(note).IsEqualTo("met at club#1");
    }

    [Test]
    public async Task An_avatar_and_an_avatar_name_read_from_its_image_lose_their_look_alikes()
    {
        await using var app = await VrcxScenario.StartAsync();
        app.Vrchat.Json("file/file_cat", """{"id":"file_cat","name":"Avatar - Cool（Cat） - Image - v1","ownerId":"usr_owner","tags":[],"versions":[{"version":0}]}""");
        await app.LoginAsync();

        var info = await app.Services.GetRequiredService<IAvatarImageResolver>().ResolveAsync("https://api.vrchat.cloud/api/1/file/file_cat/1/file", CancellationToken.None);
        await Assert.That(info!.AvatarName).IsEqualTo("Cool(Cat)");

        var api = JsonSerializer.Deserialize<Vrchat.Models.Avatar>("""
            {"id":"avtr_symbols","name":"Fox＋","description":"Made by A＆B","authorId":"usr_owner","authorName":"Owner"}
            """, VrchatJson.Options)!;
        await using (var db = app.CreateDbContext())
        {
            await app.Services.GetRequiredService<VrchatStore>().UpsertAvatarAsync(db, api, app.Time.GetUtcNow(), CancellationToken.None);
            await db.SaveChangesAsync();
        }
        await using var check = app.CreateDbContext();
        var avatar = await check.Avatars.SingleAsync(a => a.Id == "avtr_symbols");
        await Assert.That((avatar.Name, avatar.Description)).IsEqualTo(("Fox+", "Made by A&B"));
    }
}
