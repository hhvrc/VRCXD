using System.Net;
using System.Net.Http.Json;
using VRCX.Testing;

namespace VRCX.API.IntegrationTests.Tests;

// The original's GroupDialog menu and Info tab: what reaches VRChat, and what comes back.
public sealed class GroupDialogTests
{
    private const string Group = "grp_dialog";

    private static async Task<VrcxScenario> StartAsync()
    {
        var app = await VrcxScenario.StartAsync();
        app.Vrchat.SetMe("usr_me", "Me");
        await app.LoginAsync(sync: false);
        return app;
    }

    [Test]
    public async Task Details_keep_the_store_the_join_prompt_and_the_event_subscription()
    {
        await using var app = await StartAsync();
        app.Vrchat
            .Json("GET", $"groups/{Group}", $$"""{"id":"{{Group}}","name":"Dialog","storeId":"store_1","allowGroupJoinPrompt":true,"myMember":{"isSubscribedToEventAnnouncements":false},"galleries":[{"id":"gal_1","name":"Pics","roleIdsToView":["grol_1"]}]}""")
            .Json("GET", $"users/usr_me/instances/groups/{Group}", """{"instances":[]}""");

        var details = await app.GetJsonAsync(app.AccountPath($"groups/{Group}/details"));
        var group = details.GetProperty("group");

        await Assert.That(group.GetProperty("storeId").GetString()).IsEqualTo("store_1");
        await Assert.That(group.GetProperty("allowGroupJoinPrompt").GetBoolean()).IsTrue();
        await Assert.That(group.GetProperty("myMember").GetProperty("isSubscribedToEventAnnouncements").GetBoolean()).IsFalse();
        await Assert.That(group.GetProperty("galleries")[0].GetProperty("roleIdsToView")[0].GetString()).IsEqualTo("grol_1");
    }

    [Test]
    public async Task Deleting_a_group_sends_a_soft_delete()
    {
        await using var app = await StartAsync();
        app.Vrchat.Json("DELETE", $"groups/{Group}", """{"success":{"message":"ok"}}""");

        var response = await app.Api.DeleteAsync(app.AccountPath($"groups/{Group}"));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(app.Vrchat.Bodies[$"DELETE groups/{Group}"]).IsEqualTo("""{"hardDelete":false}""");
    }

    [Test]
    public async Task The_calendar_fetches_each_event_again_for_its_interest()
    {
        await using var app = await StartAsync();
        app.Vrchat
            .Json("GET", $"calendar/{Group}", """{"results":[{"id":"cal_1","title":"One"},{"id":"cal_2","title":"Two"}]}""")
            .Json("GET", $"calendar/{Group}/cal_1", """{"id":"cal_1","title":"One","userInterest":{"isFollowing":true}}""")
            .Fail("GET", $"calendar/{Group}/cal_2", HttpStatusCode.NotFound);

        var events = await app.GetJsonAsync(app.AccountPath($"groups/{Group}/calendar"));

        await Assert.That(events.GetArrayLength()).IsEqualTo(2);
        await Assert.That(events[0].GetProperty("userInterest").GetProperty("isFollowing").GetBoolean()).IsTrue();
        // A failed fetch keeps the list's copy.
        await Assert.That(events[1].GetProperty("title").GetString()).IsEqualTo("Two");
    }

    [Test]
    public async Task Language_options_come_from_the_config()
    {
        await using var app = await StartAsync();
        app.Vrchat.Json("GET", "config", """{"constants":{"LANGUAGE":{"SPOKEN_LANGUAGE_OPTIONS":{"eng":"English","jpn":"日本語"}}}}""");

        var options = await app.GetJsonAsync(app.AccountPath("languages"));

        await Assert.That(options.GetProperty("jpn").GetString()).IsEqualTo("日本語");
    }

    [Test]
    public async Task Create_group_sends_the_original_body_without_empty_images()
    {
        await using var app = await StartAsync();
        app.Vrchat
            .Json("GET", "groups/roleTemplates", """{"default":{"name":"Default"},"managedFree":{"name":"Free"}}""")
            .Json("POST", "groups", """{"id":"grp_new","name":"New"}""");

        var templates = await app.GetJsonAsync(app.AccountPath("groups/role-templates"));
        await Assert.That(templates.GetProperty("managedFree").GetProperty("name").GetString()).IsEqualTo("Free");

        var response = await app.Api.PostAsJsonAsync(app.AccountPath("groups"), new { name = "New", shortCode = "NEW1", description = "Hi", joinState = "open", bannerId = "", iconId = "file_icon", privacy = "default", roleTemplate = "managedFree" });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(app.Vrchat.Bodies["POST groups"]).IsEqualTo("""{"name":"New","shortCode":"NEW1","description":"Hi","joinState":"open","iconId":"file_icon","privacy":"default","roleTemplate":"managedFree"}""");
    }

    [Test]
    public async Task Edit_group_sends_the_id_and_the_original_fields()
    {
        await using var app = await StartAsync();
        app.Vrchat.Json("PUT", $"groups/{Group}", $$"""{"id":"{{Group}}","name":"Edited"}""");

        var response = await app.Api.PutAsJsonAsync(app.AccountPath($"groups/{Group}"), new { name = "Edited", shortCode = "EDIT", description = "", joinState = "request", bannerId = "file_banner", languages = new[] { "eng", "jpn" }, rules = "Be nice", links = new[] { "https://example.com" }, allowGroupJoinPrompt = false });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(app.Vrchat.Bodies[$"PUT groups/{Group}"]).IsEqualTo($$"""{"id":"{{Group}}","name":"Edited","shortCode":"EDIT","description":"","joinState":"request","bannerId":"file_banner","languages":["eng","jpn"],"rules":"Be nice","links":["https://example.com"],"allowGroupJoinPrompt":false}""");
    }

    [Test]
    public async Task Edit_group_refuses_a_bad_short_code_without_calling_VRChat()
    {
        await using var app = await StartAsync();

        var response = await app.Api.PutAsJsonAsync(app.AccountPath($"groups/{Group}"), new { name = "Edited", shortCode = "bad", joinState = "open" });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(app.Vrchat.Bodies.ContainsKey($"PUT groups/{Group}")).IsFalse();
    }

    [Test]
    public async Task Posts_are_created_edited_and_deleted_with_the_original_bodies()
    {
        await using var app = await StartAsync();
        app.Vrchat
            .Json("POST", $"groups/{Group}/posts", $$"""{"id":"gpos_1","groupId":"{{Group}}","title":"Hi","text":"Body"}""")
            .Json("PUT", $"groups/{Group}/posts/gpos_1", $$"""{"id":"gpos_1","groupId":"{{Group}}","title":"Hi","text":"Edited"}""")
            .Json("DELETE", $"groups/{Group}/posts/gpos_1", """{"success":{"message":"ok"}}""");

        var created = await app.Api.PostAsJsonAsync(app.AccountPath($"groups/{Group}/posts"), new { title = "Hi", text = "Body", roleIds = new[] { "grol_1" }, visibility = "group", sendNotification = true, imageId = (string?)null });
        await Assert.That(created.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(app.Vrchat.Bodies[$"POST groups/{Group}/posts"]).IsEqualTo($$"""{"groupId":"{{Group}}","title":"Hi","text":"Body","roleIds":["grol_1"],"visibility":"group","sendNotification":true,"imageId":null}""");

        var edited = await app.Api.PutAsJsonAsync(app.AccountPath($"groups/{Group}/posts/gpos_1"), new { title = "Hi", text = "Edited", roleIds = Array.Empty<string>(), visibility = "public", imageId = "file_1" });
        await Assert.That(edited.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(app.Vrchat.Bodies[$"PUT groups/{Group}/posts/gpos_1"]).IsEqualTo($$"""{"groupId":"{{Group}}","postId":"gpos_1","title":"Hi","text":"Edited","roleIds":[],"visibility":"public","imageId":"file_1"}""");

        var deleted = await app.Api.DeleteAsync(app.AccountPath($"groups/{Group}/posts/gpos_1"));
        await Assert.That(deleted.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(app.Vrchat.CountOf("DELETE", $"groups/{Group}/posts/gpos_1")).IsEqualTo(1);
    }

    [Test]
    public async Task Transfer_checks_the_target_then_sends_the_request()
    {
        await using var app = await StartAsync();
        app.Vrchat
            .Json("GET", $"groups/{Group}/transfer?transferTargetId=usr_target", """{"requirements":{"hasVRCPlus":false,"targetIsGroupMember":true}}""")
            .Json("POST", $"groups/{Group}/transfer", """{"success":{"message":"ok"}}""");

        var check = await app.GetJsonAsync(app.AccountPath($"groups/{Group}/transfer?transferTargetId=usr_target"));
        await Assert.That(check.GetProperty("requirements").GetProperty("hasVRCPlus").GetBoolean()).IsFalse();

        var response = await app.Api.PostAsJsonAsync(app.AccountPath($"groups/{Group}/transfer"), new { transferTargetId = "usr_target" });
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(app.Vrchat.Bodies[$"POST groups/{Group}/transfer"]).IsEqualTo("""{"transferTargetId":"usr_target"}""");
    }

    [Test]
    public async Task Roles_send_only_the_fields_the_role_lets_you_change()
    {
        await using var app = await StartAsync();
        app.Vrchat
            .Json("GET", $"groups/{Group}/permissions", """[{"name":"group-roles-manage","displayName":"Manage Roles","isManagementPermission":true,"allowedToAdd":true}]""")
            .Json("POST", $"groups/{Group}/roles", """{"id":"grol_new","name":"Helpers","order":3}""")
            .Json("PUT", $"groups/{Group}/roles/grol_default", """[{"id":"grol_default","order":0}]""")
            .Json("PUT", $"groups/{Group}/roles/grol_2", """[{"id":"grol_2","order":1}]""")
            .Json("DELETE", $"groups/{Group}/roles/grol_new", """[]""");

        var list = await app.GetJsonAsync(app.AccountPath($"groups/{Group}/permissions"));
        await Assert.That(list[0].GetProperty("name").GetString()).IsEqualTo("group-roles-manage");

        var created = await app.Api.PostAsJsonAsync(app.AccountPath($"groups/{Group}/roles"), new { name = "Helpers", description = "Help", requiresTwoFactor = false, isSelfAssignable = true, isAddedOnJoin = false, requiresPurchase = false, permissions = new[] { "group-roles-manage" } });
        await Assert.That(created.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(app.Vrchat.Bodies[$"POST groups/{Group}/roles"]).IsEqualTo("""{"name":"Helpers","description":"Help","requiresTwoFactor":false,"isSelfAssignable":true,"isAddedOnJoin":false,"requiresPurchase":false,"permissions":["group-roles-manage"]}""");

        // A default role keeps its details and flags; only its permissions go.
        var defaultRole = await app.Api.PutAsJsonAsync(app.AccountPath($"groups/{Group}/roles/grol_default"), new { permissions = new[] { "group-instance-join" } });
        await Assert.That(defaultRole.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(app.Vrchat.Bodies[$"PUT groups/{Group}/roles/grol_default"]).IsEqualTo("""{"permissions":["group-instance-join"]}""");

        var reorder = await app.Api.PutAsJsonAsync(app.AccountPath($"groups/{Group}/roles/grol_2"), new { order = 1 });
        await Assert.That(reorder.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(app.Vrchat.Bodies[$"PUT groups/{Group}/roles/grol_2"]).IsEqualTo("""{"order":1}""");

        var deleted = await app.Api.DeleteAsync(app.AccountPath($"groups/{Group}/roles/grol_new"));
        await Assert.That(deleted.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(app.Vrchat.CountOf("DELETE", $"groups/{Group}/roles/grol_new")).IsEqualTo(1);
    }

    [Test]
    public async Task Events_are_created_as_a_series_edited_and_deleted_with_the_original_bodies()
    {
        await using var app = await StartAsync();
        app.Vrchat
            .Json("POST", $"calendar/{Group}/event", """{"id":"cal_new"}""")
            .Json("PUT", $"calendar/{Group}/cal_new/event", """{"id":"cal_new"}""")
            .Json("GET", $"calendar/{Group}/cal_new", """{"id":"cal_new","title":"Meetup","tags":["a"]}""")
            .Json("DELETE", $"calendar/{Group}/cal_new", """{"success":{"message":"ok"}}""");

        var created = await app.Api.PostAsJsonAsync(app.AccountPath($"groups/{Group}/calendar"), new
        {
            startsAt = "2026-12-01T18:00:00.000Z",
            endsAt = "2026-12-01T20:00:00.000Z",
            title = " Meetup ",
            accessType = "group",
            description = "Come",
            category = "hangout",
            tags = new[] { "a" },
            imageId = (string?)null,
            roleIds = Array.Empty<string>(),
            platforms = new[] { "android" },
            languages = new[] { "eng" },
            sendCreationNotification = true,
            hostEarlyJoinMinutes = 60,
            guestEarlyJoinMinutes = 5,
            closeInstanceAfterEndMinutes = 5,
            recurrence = new { frequency = "weekly", interval = 1, timezone = "Europe/Oslo", daysOfWeek = new[] { "SA" }, end = new { type = "afterOccurrences", count = 3 } },
        });
        await Assert.That(created.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(app.Vrchat.Bodies[$"POST calendar/{Group}/event"]).IsEqualTo("""{"startsAt":"2026-12-01T18:00:00.000Z","endsAt":"2026-12-01T20:00:00.000Z","title":"Meetup","accessType":"group","description":"Come","category":"hangout","tags":["a"],"imageId":null,"roleIds":[],"platforms":["android"],"languages":["eng"],"sendCreationNotification":true,"hostEarlyJoinMinutes":60,"guestEarlyJoinMinutes":5,"closeInstanceAfterEndMinutes":5,"groupId":"grp_dialog","occurrenceKind":"series","recurrence":{"frequency":"weekly","interval":1,"timezone":"Europe/Oslo","daysOfWeek":["SA"],"end":{"type":"afterOccurrences","count":3}}}""");

        var edited = await app.Api.PutAsJsonAsync(app.AccountPath($"groups/{Group}/calendar/cal_new"), new
        {
            startsAt = "2026-12-01T18:00:00.000Z",
            endsAt = "2026-12-01T21:00:00.000Z",
            title = "Meetup",
            accessType = "public",
            description = "Come",
            category = "hangout",
            imageId = "file_1",
            hostEarlyJoinMinutes = 60,
            guestEarlyJoinMinutes = 5,
            closeInstanceAfterEndMinutes = 5,
        });
        await Assert.That(edited.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(app.Vrchat.Bodies[$"PUT calendar/{Group}/cal_new/event"]).IsEqualTo($$"""{"startsAt":"2026-12-01T18:00:00.000Z","endsAt":"2026-12-01T21:00:00.000Z","title":"Meetup","accessType":"public","description":"Come","category":"hangout","tags":[],"imageId":"file_1","roleIds":[],"platforms":[],"languages":[],"sendCreationNotification":false,"hostEarlyJoinMinutes":60,"guestEarlyJoinMinutes":5,"closeInstanceAfterEndMinutes":5,"groupId":"{{Group}}","eventId":"cal_new"}""");

        var one = await app.GetJsonAsync(app.AccountPath($"groups/{Group}/calendar/cal_new"));
        await Assert.That(one.GetProperty("tags")[0].GetString()).IsEqualTo("a");

        var deleted = await app.Api.DeleteAsync(app.AccountPath($"groups/{Group}/calendar/cal_new"));
        await Assert.That(deleted.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(app.Vrchat.CountOf("DELETE", $"calendar/{Group}/cal_new")).IsEqualTo(1);
    }

    [Test]
    public async Task An_event_that_ends_before_it_starts_never_reaches_VRChat()
    {
        await using var app = await StartAsync();

        var response = await app.Api.PostAsJsonAsync(app.AccountPath($"groups/{Group}/calendar"), new { startsAt = "2026-12-01T20:00:00Z", endsAt = "2026-12-01T18:00:00Z", title = "x", accessType = "group", description = "y", category = "other" });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(app.Vrchat.CountOf("POST", $"calendar/{Group}/event")).IsEqualTo(0);
    }

    [Test]
    public async Task Galleries_and_their_images_send_the_original_bodies()
    {
        await using var app = await StartAsync();
        app.Vrchat
            .Json("GET", $"groups/{Group}/galleries/ggal_1?n=100&offset=100", """[{"id":"ggi_1","groupId":"grp_dialog","galleryId":"ggal_1"}]""")
            .Json("POST", $"groups/{Group}/galleries", """{"id":"ggal_2","name":"Pics"}""")
            .Json("PUT", $"groups/{Group}/galleries/ggal_2", """{"id":"ggal_2","name":"Pics"}""")
            .Json("POST", $"groups/{Group}/galleries/ggal_2/images", """{"id":"ggi_2","approved":false}""")
            .Json("DELETE", $"groups/{Group}/galleries/ggal_2/images/ggi_2", """{"success":{"message":"ok"}}""")
            .Json("DELETE", $"groups/{Group}/galleries/ggal_2", """{"success":{"message":"ok"}}""");

        var page = await app.GetJsonAsync(app.AccountPath($"groups/{Group}/galleries/ggal_1?n=100&offset=100"));
        await Assert.That(page[0].GetProperty("id").GetString()).IsEqualTo("ggi_1");

        var created = await app.Api.PostAsJsonAsync(app.AccountPath($"groups/{Group}/galleries"), new { name = " Pics ", description = "", membersOnly = true });
        await Assert.That(created.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(app.Vrchat.Bodies[$"POST groups/{Group}/galleries"]).IsEqualTo("""{"name":"Pics","description":"","membersOnly":true}""");

        var edited = await app.Api.PutAsJsonAsync(app.AccountPath($"groups/{Group}/galleries/ggal_2"), new { name = "Pics", description = "Ours", membersOnly = false });
        await Assert.That(edited.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(app.Vrchat.Bodies[$"PUT groups/{Group}/galleries/ggal_2"]).IsEqualTo("""{"name":"Pics","description":"Ours","membersOnly":false}""");

        var added = await app.Api.PostAsJsonAsync(app.AccountPath($"groups/{Group}/galleries/ggal_2/images"), new { fileId = "file_9" });
        await Assert.That(added.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(app.Vrchat.Bodies[$"POST groups/{Group}/galleries/ggal_2/images"]).IsEqualTo("""{"fileId":"file_9"}""");

        await Assert.That((await app.Api.DeleteAsync(app.AccountPath($"groups/{Group}/galleries/ggal_2/images/ggi_2"))).StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That((await app.Api.DeleteAsync(app.AccountPath($"groups/{Group}/galleries/ggal_2"))).StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(app.Vrchat.CountOf("DELETE", $"groups/{Group}/galleries/ggal_2")).IsEqualTo(1);
    }

    [Test]
    public async Task The_store_tab_asks_for_hydrated_listings()
    {
        await using var app = await StartAsync();
        app.Vrchat.Json("GET", "economy/store?storeId=store_1&hydrateListings=true&hydrateContext=false", """{"id":"store_1","listings":[{"id":"prod_1","displayName":"Sub","priceTokens":1200}]}""");

        var store = await app.GetJsonAsync(app.AccountPath("stores/store_1"));

        await Assert.That(store.GetProperty("listings")[0].GetProperty("priceTokens").GetInt32()).IsEqualTo(1200);
    }

    [Test]
    public async Task Subscriptions_and_visibility_send_only_the_field_changed()
    {
        await using var app = await StartAsync();
        app.Vrchat.Json("PUT", $"groups/{Group}/members/usr_me", $$"""{"id":"gmem_1","groupId":"{{Group}}","userId":"usr_me"}""");

        var events = await app.Api.PutAsJsonAsync(app.AccountPath($"groups/{Group}/members/usr_me"), new { isSubscribedToEventAnnouncements = false });
        await Assert.That(events.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(app.Vrchat.Bodies[$"PUT groups/{Group}/members/usr_me"]).IsEqualTo("""{"isSubscribedToEventAnnouncements":false}""");

        var visibility = await app.Api.PutAsJsonAsync(app.AccountPath($"groups/{Group}/members/usr_me"), new { visibility = "friends" });
        await Assert.That(visibility.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(app.Vrchat.Bodies[$"PUT groups/{Group}/members/usr_me"]).IsEqualTo("""{"visibility":"friends"}""");
    }
}
