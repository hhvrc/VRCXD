using System.Net.Http.Json;
using System.Text.Json;
using VRCX.Testing;

namespace VRCX.API.IntegrationTests.Tests;

// The original's InstanceActionBar: VRChat's instance as it sends it, with the owning group's
// permissions, and Close and Rename sent as the original sends them.
public sealed class LiveInstanceTests
{
    private const string Location = "wrld_x:1~group(grp_g)~groupAccessType(public)~region(eu)";

    [Test]
    public async Task A_group_instance_comes_with_the_groups_permissions()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        app.Vrchat
            .Json("instances/wrld_x:1~group(grp_g)~groupAccessType(public)~region(eu)",
                """{"location":"wrld_x:1","ownerId":"grp_g","userCount":3,"capacity":32,"platforms":{"standalonewindows":2,"android":1},"contentSettings":{"emoji":false}}""")
            .Json($"users/{app.AccountId}/groups/permissions", """{"grp_g":["group-instance-moderate"]}""");

        var live = await app.Api.GetFromJsonAsync<JsonElement>($"/1/accounts/{app.AccountId}/instances/live?location={Uri.EscapeDataString(Location)}");
        await Assert.That(live.GetProperty("instance").GetProperty("userCount").GetInt32()).IsEqualTo(3);
        await Assert.That(live.GetProperty("instance").GetProperty("contentSettings").GetProperty("emoji").GetBoolean()).IsFalse();
        await Assert.That(live.GetProperty("groupPermissions")[0].GetString()).IsEqualTo("group-instance-moderate");
    }

    // Upstream 2929696b: the tooltip shows what the creator set.
    [Test]
    public async Task The_creators_description_category_vibes_and_languages_come_through()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        app.Vrchat
            .Json("instances/wrld_x:1~group(grp_g)~groupAccessType(public)~region(eu)",
                """{"location":"wrld_x:1","ownerId":"grp_g","description":"Chill","categoryId":"icat_hangout","vibeIds":["ivib_a"],"creationLanguages":["eng"],"world":{"description":"A world"}}""")
            .Json($"users/{app.AccountId}/groups/permissions", "{}");

        var live = await app.Api.GetFromJsonAsync<JsonElement>($"/1/accounts/{app.AccountId}/instances/live?location={Uri.EscapeDataString(Location)}");
        var instance = live.GetProperty("instance");
        await Assert.That(instance.GetProperty("description").GetString()).IsEqualTo("Chill");
        await Assert.That(instance.GetProperty("categoryId").GetString()).IsEqualTo("icat_hangout");
        await Assert.That(instance.GetProperty("vibeIds")[0].GetString()).IsEqualTo("ivib_a");
        await Assert.That(instance.GetProperty("creationLanguages")[0].GetString()).IsEqualTo("eng");
        await Assert.That(instance.GetProperty("world").GetProperty("description").GetString()).IsEqualTo("A world");
    }

    // Upstream issue 1394: Refresh must show VRChat's current count, not a stale one.
    [Test]
    public async Task Each_read_has_vrchats_current_user_count()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        const string Path = "instances/wrld_x:1~group(grp_g)~groupAccessType(public)~region(eu)";
        var url = $"/1/accounts/{app.AccountId}/instances/live?location={Uri.EscapeDataString(Location)}";
        app.Vrchat.Json(Path, """{"location":"wrld_x:1","userCount":5,"capacity":32}""");
        var first = await app.Api.GetFromJsonAsync<JsonElement>(url);
        app.Vrchat.Json(Path, """{"location":"wrld_x:1","userCount":7,"capacity":32}""");
        var second = await app.Api.GetFromJsonAsync<JsonElement>(url);

        await Assert.That(first.GetProperty("instance").GetProperty("userCount").GetInt32()).IsEqualTo(5);
        await Assert.That(second.GetProperty("instance").GetProperty("userCount").GetInt32()).IsEqualTo(7);
    }

    [Test]
    public async Task Close_and_rename_are_sent_as_the_original_sends_them()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        var path = "instances/wrld_x:1~group(grp_g)~groupAccessType(public)~region(eu)";
        app.Vrchat.Json("DELETE", path, """{"closedAt":"2026-11-08T10:00:00Z"}""").Json("PUT", path, """{"displayName":"Party"}""");

        var closed = await app.Api.PostAsJsonAsync($"/1/accounts/{app.AccountId}/instances/close", new { location = Location });
        await Assert.That((int)closed.StatusCode).IsEqualTo(200);
        await Assert.That(app.Vrchat.AssertCalled("DELETE", path).Query).Contains("hardClose=false");

        var renamed = await app.Api.PutAsJsonAsync($"/1/accounts/{app.AccountId}/instances/display-name", new { location = Location, displayName = " Party " });
        await Assert.That((int)renamed.StatusCode).IsEqualTo(200);
        await Assert.That(app.Vrchat.AssertCalled("PUT", path).Body).IsEqualTo("""{"displayName":"Party"}""");
    }

    [Test]
    public async Task Announcements_and_calendar_links_are_sent_and_the_game_location_is_known()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        var path = "instances/wrld_x:1~group(grp_g)~groupAccessType(public)~region(eu)";
        app.Vrchat
            .Json("GET", path, """{"location":"wrld_x:1","ownerId":"grp_g"}""")
            .Json($"users/{app.AccountId}/groups/permissions", """{"grp_g":["group-instance-announcement-create"]}""")
            .Json("PUT", path, """{"calendarEntryId":"cal_1"}""")
            .Json("POST", path + "/announce", "{}")
            .Json("calendar/grp_g", """{"results":[{"id":"cal_1","title":"Meetup"}]}""");
        var acct = $"/1/accounts/{app.AccountId}";

        var live = await app.Api.GetFromJsonAsync<JsonElement>($"{acct}/instances/live?location={Uri.EscapeDataString(Location)}");
        await Assert.That(live.GetProperty("inThisInstance").GetBoolean()).IsFalse();
        await Assert.That(live.TryGetProperty("lastJoinedAt", out _)).IsFalse();

        await using var pc = await app.ConnectDesktopAsync("pc-announce");
        await pc.SendLogAsync(VRCX.Testing.Desktop.GameLog.UserAuthenticated(app.AccountId!, "Me"), VRCX.Testing.Desktop.GameLog.Location(Location, "World"));
        live = await app.Api.GetFromJsonAsync<JsonElement>($"{acct}/instances/live?location={Uri.EscapeDataString(Location)}");
        await Assert.That(live.GetProperty("inThisInstance").GetBoolean()).IsTrue();
        await Assert.That(live.TryGetProperty("lastJoinedAt", out _)).IsTrue();

        var events = await app.Api.GetFromJsonAsync<JsonElement>($"{acct}/groups/grp_g/calendar/recent");
        await Assert.That(events[0].GetProperty("title").GetString()).IsEqualTo("Meetup");
        var query = app.Vrchat.AssertCalled("GET", "calendar/grp_g").Query;
        await Assert.That(query).Contains("n=100");
        await Assert.That(query).Contains("after=");

        (await app.Api.PutAsJsonAsync($"{acct}/instances/calendar-event", new { location = Location, calendarEntryId = "cal_1" })).EnsureSuccessStatusCode();
        await Assert.That(app.Vrchat.AssertCalled("PUT", path).Body).IsEqualTo("""{"calendarEntryId":"cal_1"}""");

        var sent = await app.Api.PostAsJsonAsync($"{acct}/instances/announcement", new { location = Location, message = " Hello all ", imageUrl = "https://api.vrchat.cloud/api/1/file/file_abc-1/3/file" });
        await Assert.That((int)sent.StatusCode).IsEqualTo(204);
        var body = JsonDocument.Parse(app.Vrchat.AssertCalled("POST", path + "/announce").Body!).RootElement;
        await Assert.That(body.GetProperty("title").GetString()).IsEqualTo("Instance Announcement");
        await Assert.That(body.GetProperty("message").GetString()).IsEqualTo("Hello all");
        await Assert.That(body.GetProperty("imageId").GetString()).IsEqualTo("file_abc-1");
        await Assert.That(body.GetProperty("imageVersion").GetString()).IsEqualTo("3");
    }
}
