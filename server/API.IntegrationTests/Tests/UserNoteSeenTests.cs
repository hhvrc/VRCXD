using Microsoft.EntityFrameworkCore;
using VRCX.Testing;

namespace VRCX.API.IntegrationTests.Tests;

// The original's checkNote: the note a user object carries keeps the stored note current.
public sealed class UserNoteSeenTests
{
    private static async Task<string?> StoredNoteAsync(VrcxScenario app, string userId)
    {
        await using var db = app.CreateDbContext();
        return await db.UserNotes.Where(n => n.TargetUserId == userId).Select(n => (string?)n.Note).SingleOrDefaultAsync();
    }

    [Test]
    public async Task A_friend_update_with_a_changed_note_updates_it_and_an_empty_one_deletes_it()
    {
        await using var app = await VrcxScenario.StartAsync();
        var friend = app.Vrchat.Friend("usr_noted", "Noted");
        app.Vrchat.UserNote("usr_noted", "first");
        await app.LoginAsync();
        await Assert.That(await StoredNoteAsync(app, "usr_noted")).IsEqualTo("first");

        friend.Set("note", "second＃1");
        await app.Pipeline.FriendUpdateAsync(friend);
        await Assert.That(await StoredNoteAsync(app, "usr_noted")).IsEqualTo("second#1");

        friend.Set("note", "");
        await app.Pipeline.FriendUpdateAsync(friend);
        await Assert.That(await StoredNoteAsync(app, "usr_noted")).IsNull();
    }

    [Test]
    public async Task A_refreshed_user_with_a_new_note_reads_the_latest_notes()
    {
        await using var app = await VrcxScenario.StartAsync();
        var friend = app.Vrchat.Friend("usr_new", "New");
        await app.LoginAsync();
        await Assert.That(await StoredNoteAsync(app, "usr_new")).IsNull();

        app.Vrchat.UserNote("usr_new", "written in game");
        friend.Set("note", "written in game");
        await app.GetJsonAsync($"/1/users/{friend.Id}?refresh=true&account={app.AccountId}");

        await Assert.That(await StoredNoteAsync(app, "usr_new")).IsEqualTo("written in game");
    }
}
