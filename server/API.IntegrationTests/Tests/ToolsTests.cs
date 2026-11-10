using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace VRCX.API.IntegrationTests.Tests;

// The Tools page's writes, checked against the fake VRChat: what the server sends has to be
// the same call the original app made.
public sealed class ToolsTests
{
    [ClassDataSource<IsolatedHost>(Shared = SharedType.None)]
    public required IsolatedHost Factory { get; init; }

    private async Task<HttpClient> LoginAsync()
    {
        Factory.Vrchat
            .Json("auth/user", """{"id":"usr_tools","displayName":"Tools","username":"tools","status":"active","tags":[],"onlineFriends":[],"activeFriends":[],"friendGroupNames":[]}""")
            .Json("auth/user/friends?offline=false&n=100&offset=0", """[{"id":"usr_f","displayName":"Friend","location":"offline","tags":[],"bio":"dc: friend#1","currentAvatarImageUrl":"https://img/full"}]""")
            .Json("auth/user/friends?offline=true&n=100&offset=0", "[]")
            .Json("users/usr_tools/groups", "[]")
            .Json("favorite/groups?n=100", "[]")
            .Json("favorites?n=100&offset=0", "[]")
            .Json("auth/user/playermoderations", "[]")
            .Json("userNotes?n=100&offset=0", "[]")
            .Json("auth/user/notifications?n=100&offset=0", "[]")
            .Json("notifications?n=100&offset=0", "[]")
            .Json("logout", "{}");
        var client = Factory.CreateAuthenticatedClient();
        var login = await client.PostAsJsonAsync("/1/accounts/login", new { username = "tools", password = "pw" });
        await Assert.That(login.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var sync = await client.PostAsync("/1/accounts/usr_tools/sync", null);
        await Assert.That(sync.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        return client;
    }

    [Test]
    public async Task Group_actions_make_the_originals_calls()
    {
        Factory.Vrchat
            .Json("users/usr_tools/groups/invited", """[{"id":"gmem_1","groupId":"grp_1","name":"One","shortCode":"ONE","discriminator":"1234","memberCount":5}]""")
            .Json("groups/grp_1/join", """{"membershipStatus":"requested"}""")
            .Json("groups/grp_1/block", "{}")
            .Json("groups/grp_1/requests", "{}")
            .Json("groups/grp_1/members/usr_tools", "{}");
        using var client = await LoginAsync();
        const string groups = "/1/accounts/usr_tools/groups";

        var invited = await client.GetFromJsonAsync<JsonElement>($"{groups}/invited");
        await Assert.That(invited[0].GetProperty("groupId").GetString()).IsEqualTo("grp_1");
        await Assert.That(invited[0].GetProperty("memberCount").GetInt32()).IsEqualTo(5);

        var joined = await (await client.PostAsync($"{groups}/grp_1/join", null)).Content.ReadFromJsonAsync<JsonElement>();
        await Assert.That(joined.GetProperty("membershipStatus").GetString()).IsEqualTo("requested");
        await Assert.That((await client.PostAsync($"{groups}/grp_1/block", null)).StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That((await client.DeleteAsync($"{groups}/grp_1/request")).StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That((await client.DeleteAsync($"{groups}/grp_1/membership")).StatusCode).IsEqualTo(HttpStatusCode.NoContent);

        await Assert.That(Factory.Vrchat.Calls).Contains("POST groups/grp_1/join");
        await Assert.That(Factory.Vrchat.Calls).Contains("POST groups/grp_1/block");
        await Assert.That(Factory.Vrchat.Calls).Contains("DELETE groups/grp_1/requests");
        await Assert.That(Factory.Vrchat.Calls).Contains("DELETE groups/grp_1/members/usr_tools");
    }

    [Test]
    public async Task Other_group_states_are_refused_by_the_route()
    {
        using var client = await LoginAsync();
        var response = await client.GetAsync("/1/accounts/usr_tools/groups/members");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Editing_an_invite_message_returns_every_slot()
    {
        Factory.Vrchat.Json("message/usr_tools/requestResponse/3", """[{"id":"m0","slot":0,"message":"a","messageType":"requestResponse"},{"id":"m3","slot":3,"message":"new","messageType":"requestResponse","updatedAt":"2026-11-01T10:00:00Z"}]""");
        using var client = await LoginAsync();

        var response = await client.PutAsJsonAsync("/1/accounts/usr_tools/invite-messages/requestResponse/3", new { message = "new" });
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var slots = await response.Content.ReadFromJsonAsync<JsonElement>();
        await Assert.That(slots.GetArrayLength()).IsEqualTo(2);
        await Assert.That(slots[1].GetProperty("message").GetString()).IsEqualTo("new");
        await Assert.That(Factory.Vrchat.Calls).Contains("PUT message/usr_tools/requestResponse/3");

        var tooLong = await client.PutAsJsonAsync("/1/accounts/usr_tools/invite-messages/message/0", new { message = new string('x', 65) });
        await Assert.That(tooLong.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task Saving_a_note_updates_what_the_export_reads()
    {
        Factory.Vrchat.Json("userNotes", """{"id":"unt_1","userId":"usr_tools","targetUserId":"usr_f","note":"hello"}""");
        using var client = await LoginAsync();

        var before = await client.GetFromJsonAsync<JsonElement>("/1/accounts/usr_tools/tools/friends");
        var row = before.EnumerateArray().Single(f => f.GetProperty("userId").GetString() == "usr_f");
        await Assert.That(row.GetProperty("bio").GetString()).IsEqualTo("dc: friend#1");
        // VRChat sends friends without a thumbnail: the full image stands in, as in the original.
        await Assert.That(row.GetProperty("thumbnailImageUrl").GetString()).IsEqualTo("https://img/full");

        var saved = await client.PutAsJsonAsync("/1/accounts/usr_tools/user-notes/usr_f", new { note = "hello" });
        await Assert.That(saved.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(Factory.Vrchat.Calls).Contains("POST userNotes");

        var after = await client.GetFromJsonAsync<JsonElement>("/1/accounts/usr_tools/tools/friends");
        row = after.EnumerateArray().Single(f => f.GetProperty("userId").GetString() == "usr_f");
        await Assert.That(row.GetProperty("note").GetString()).IsEqualTo("hello");
    }

    [Test]
    public async Task Report_labels_come_from_the_config()
    {
        Factory.Vrchat.Json("config", """{"reportOptions":{"user":{},"world":{}},"reportCategories":{"bio":{"text":"Biography","title":"x"}},"reportReasons":{"spam":{"text":"Spam"}}}""");
        using var client = await LoginAsync();

        var options = await client.GetFromJsonAsync<JsonElement>("/1/accounts/usr_tools/moderation-reports/options");
        await Assert.That(options.GetProperty("types").GetArrayLength()).IsEqualTo(2);
        await Assert.That(options.GetProperty("categories").GetProperty("bio").GetString()).IsEqualTo("Biography");
        await Assert.That(options.GetProperty("reasons").GetProperty("spam").GetString()).IsEqualTo("Spam");
    }

    [Test]
    public async Task Calendar_lists_pages_and_follows()
    {
        Factory.Vrchat
            .Json("calendar", """{"results":[{"id":"cal_1","ownerId":"grp_c","title":"Party","startsAt":"2026-11-01T20:00:00Z","endsAt":"2026-11-01T21:00:00Z","userInterest":{"isFollowing":false}}],"hasNext":false}""")
            .Json("groups/grp_c?includeRoles=true", """{"id":"grp_c","name":"Club","shortCode":"CLUB","discriminator":"0001","bannerUrl":"https://img/banner"}""")
            .Json("groups/grp_c/roles", "[]")
            .Json("calendar/grp_c/cal_1/follow", """{"id":"cal_1","ownerId":"grp_c","title":"Party","userInterest":{"isFollowing":true}}""")
            .Json("calendar/grp_c/cal_1.ics", "BEGIN:VCALENDAR");
        using var client = await LoginAsync();

        var month = await client.GetFromJsonAsync<JsonElement>("/1/accounts/usr_tools/calendar/all?date=2026-11-01T10:00:00Z");
        await Assert.That(month.GetProperty("events")[0].GetProperty("title").GetString()).IsEqualTo("Party");
        await Assert.That(month.GetProperty("groups").GetProperty("grp_c").GetProperty("name").GetString()).IsEqualTo("Club");

        var followed = await client.PostAsJsonAsync("/1/accounts/usr_tools/calendar/grp_c/cal_1/follow", new { isFollowing = true });
        await Assert.That(followed.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var body = await followed.Content.ReadFromJsonAsync<JsonElement>();
        await Assert.That(body.GetProperty("userInterest").GetProperty("isFollowing").GetBoolean()).IsTrue();
        await Assert.That(Factory.Vrchat.Calls).Contains("POST calendar/grp_c/cal_1/follow");

        var ics = await client.GetAsync("/1/accounts/usr_tools/calendar/grp_c/cal_1/ics");
        await Assert.That(ics.Content.Headers.ContentType?.MediaType).IsEqualTo("text/calendar");
        await Assert.That(await ics.Content.ReadAsStringAsync()).IsEqualTo("BEGIN:VCALENDAR");
    }

    [Test]
    public async Task Without_a_session_the_tools_answer_409()
    {
        using var client = await LoginAsync();
        await client.DeleteAsync("/1/accounts/usr_tools/session");
        var response = await client.GetAsync("/1/accounts/usr_tools/purchases");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
    }
}
