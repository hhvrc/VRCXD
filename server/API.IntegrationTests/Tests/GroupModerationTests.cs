using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using VRCX.Testing;
using VRCX.Testing.FakeVrchat;

namespace VRCX.API.IntegrationTests.Tests;

// The original's GroupMemberModerationDialog: the lists it reads and what each bulk action sends.
public sealed class GroupModerationTests
{
    private const string Group = "grp_mod";
    private const string Target = "usr_target";

    private static string Member(string userId, string status = "member", string extra = "") =>
        $$"""{"id":"gmem_{{userId}}","groupId":"{{Group}}","userId":"{{userId}}","membershipStatus":"{{status}}","roleIds":["grol_a"],"managerNotes":"note","isSubscribedToEventAnnouncements":true,"acceptedById":"usr_mod","lastPostReadAt":"2026-01-02T03:04:05Z","user":{"id":"{{userId}}","displayName":"Name {{userId}}","currentAvatarTags":["tag"]}{{extra}}}""";

    [Test]
    public async Task Members_pass_sort_and_role_to_vrchat_and_keep_its_fields()
    {
        await using var app = await VrcxScenario.StartAsync();
        app.Vrchat.Json($"groups/{Group}/members", $"[{Member(Target)}]");
        await app.LoginAsync();

        var members = await app.GetJsonAsync(app.AccountPath($"groups/{Group}/members?n=100&offset=200&sort=joinedAt:asc&roleId=grol_a"));

        var call = app.Vrchat.AssertCalled("GET", $"groups/{Group}/members");
        await Assert.That(call.Query).IsEqualTo("?n=100&offset=200&sort=joinedAt%3Aasc&roleId=grol_a");
        var member = members[0];
        await Assert.That(member.GetProperty("managerNotes").GetString()).IsEqualTo("note");
        await Assert.That(member.GetProperty("isSubscribedToEventAnnouncements").GetBoolean()).IsTrue();
        await Assert.That(member.GetProperty("acceptedById").GetString()).IsEqualTo("usr_mod");
        await Assert.That(member.GetProperty("user").GetProperty("currentAvatarTags")[0].GetString()).IsEqualTo("tag");
    }

    [Test]
    public async Task Members_without_sort_or_role_send_neither()
    {
        await using var app = await VrcxScenario.StartAsync();
        app.Vrchat.Json($"groups/{Group}/members", "[]");
        await app.LoginAsync();

        await app.GetJsonAsync(app.AccountPath($"groups/{Group}/members?roleId="));

        await Assert.That(app.Vrchat.AssertCalled("GET", $"groups/{Group}/members").Query).IsEqualTo("?n=100&offset=0");
    }

    [Test]
    public async Task A_member_is_returned_and_a_non_member_is_no_content()
    {
        await using var app = await VrcxScenario.StartAsync();
        app.Vrchat.Json($"groups/{Group}/members/{Target}", Member(Target));
        app.Vrchat.Json($"groups/{Group}/members/usr_none", "null");
        app.Vrchat.Fail("GET", $"groups/{Group}/members/usr_gone", HttpStatusCode.NotFound, "Not found");
        await app.LoginAsync();

        var member = await app.GetJsonAsync(app.AccountPath($"groups/{Group}/members/{Target}"));
        var none = await app.Api.GetAsync(app.AccountPath($"groups/{Group}/members/usr_none"));
        var gone = await app.Api.GetAsync(app.AccountPath($"groups/{Group}/members/usr_gone"));

        await Assert.That(member.GetProperty("userId").GetString()).IsEqualTo(Target);
        await Assert.That(none.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(gone.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
    }

    [Test]
    public async Task Search_asks_vrchat_with_the_query()
    {
        await using var app = await VrcxScenario.StartAsync();
        app.Vrchat.Json($"groups/{Group}/members/search", $$"""{"results":[{{Member(Target)}}]}""");
        await app.LoginAsync();

        var found = await app.GetJsonAsync(app.AccountPath($"groups/{Group}/members/search?query=Name%20t&n=100&offset=0"));

        await Assert.That(app.Vrchat.AssertCalled("GET", $"groups/{Group}/members/search").Query).IsEqualTo("?query=Name%20t&n=100&offset=0");
        await Assert.That(found.GetProperty("results")[0].GetProperty("userId").GetString()).IsEqualTo(Target);
    }

    [Test]
    public async Task Bans_invites_and_requests_page_like_the_original()
    {
        await using var app = await VrcxScenario.StartAsync();
        app.Vrchat
            .Json($"groups/{Group}/bans", $"[{Member(Target, "banned", ",\"bannedAt\":\"2026-02-03T04:05:06Z\"")}]")
            .Json($"groups/{Group}/invites", $"[{Member("usr_invited", "invited")}]")
            .Json($"groups/{Group}/requests", $"[{Member("usr_asking", "requested")}]");
        await app.LoginAsync();

        var bans = await app.GetJsonAsync(app.AccountPath($"groups/{Group}/bans?n=100&offset=100"));
        var invites = await app.GetJsonAsync(app.AccountPath($"groups/{Group}/invites?n=100&offset=0"));
        await app.GetJsonAsync(app.AccountPath($"groups/{Group}/requests?n=100&offset=0&blocked=false"));
        var blocked = app.Vrchat.AssertCalled("GET", $"groups/{Group}/requests").Query;
        var requests = await app.GetJsonAsync(app.AccountPath($"groups/{Group}/requests?n=100&offset=0&blocked=true"));

        await Assert.That(app.Vrchat.AssertCalled("GET", $"groups/{Group}/bans").Query).IsEqualTo("?n=100&offset=100");
        await Assert.That(bans[0].GetProperty("bannedAt").GetDateTimeOffset()).IsEqualTo(new DateTimeOffset(2026, 2, 3, 4, 5, 6, TimeSpan.Zero));
        await Assert.That(invites[0].GetProperty("membershipStatus").GetString()).IsEqualTo("invited");
        await Assert.That(blocked).IsEqualTo("?n=100&offset=0&blocked=false");
        await Assert.That(app.Vrchat.AssertCalled("GET", $"groups/{Group}/requests").Query).IsEqualTo("?n=100&offset=0&blocked=true");
        await Assert.That(requests[0].GetProperty("userId").GetString()).IsEqualTo("usr_asking");
    }

    [Test]
    public async Task Logs_send_the_event_types_and_keep_created_at_and_data()
    {
        await using var app = await VrcxScenario.StartAsync();
        app.Vrchat
            .Json($"groups/{Group}/auditLogTypes", """["group.member.ban","group.member.remove"]""")
            .Json($"groups/{Group}/auditLogs", """
                {"results":[{"id":"gaud_1","created_at":"2026-03-04T05:06:07Z","groupId":"grp_mod","actorId":"usr_mod","actorDisplayName":"Mod","targetId":"usr_target","eventType":"group.member.ban","description":"Banned","data":{"reason":{"old":null,"new":"spam"}}}],
                 "totalCount":1,"hasNext":false}
                """);
        await app.LoginAsync();

        var types = await app.GetJsonAsync(app.AccountPath($"groups/{Group}/audit-log-types"));
        var logs = await app.GetJsonAsync(app.AccountPath($"groups/{Group}/audit-logs?n=100&offset=0&eventTypes=group.member.ban,group.member.remove"));

        await Assert.That(types.GetArrayLength()).IsEqualTo(2);
        await Assert.That(app.Vrchat.AssertCalled("GET", $"groups/{Group}/auditLogs").Query).IsEqualTo("?n=100&offset=0&eventTypes=group.member.ban%2Cgroup.member.remove");
        var entry = logs.GetProperty("results")[0];
        await Assert.That(entry.GetProperty("created_at").GetDateTimeOffset()).IsEqualTo(new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero));
        await Assert.That(entry.GetProperty("data").GetProperty("reason").GetProperty("new").GetString()).IsEqualTo("spam");
        await Assert.That(logs.GetProperty("hasNext").GetBoolean()).IsFalse();
    }

    [Test]
    public async Task Logs_without_event_types_leave_the_filter_out()
    {
        await using var app = await VrcxScenario.StartAsync();
        app.Vrchat.Json($"groups/{Group}/auditLogs", """{"results":[],"hasNext":false}""");
        await app.LoginAsync();

        await app.GetJsonAsync(app.AccountPath($"groups/{Group}/audit-logs?n=100&offset=0"));

        await Assert.That(app.Vrchat.AssertCalled("GET", $"groups/{Group}/auditLogs").Query).IsEqualTo("?n=100&offset=0");
    }

    [Test]
    public async Task Ban_unban_and_kick_send_what_the_original_sends()
    {
        await using var app = await VrcxScenario.StartAsync();
        app.Vrchat.Json("POST", $"groups/{Group}/bans", Member(Target, "banned"));
        await app.LoginAsync();

        var ban = await app.Api.PostAsync(app.AccountPath($"groups/{Group}/bans/{Target}"), null);
        var unban = await app.Api.DeleteAsync(app.AccountPath($"groups/{Group}/bans/{Target}"));
        var kick = await app.Api.DeleteAsync(app.AccountPath($"groups/{Group}/members/{Target}"));

        await VrcxScenario.EnsureSuccessAsync(ban, "ban");
        await Assert.That(JsonNode.Parse(app.Vrchat.AssertCalled("POST", $"groups/{Group}/bans").Body!)!.ToJsonString()).IsEqualTo($$"""{"userId":"{{Target}}"}""");
        await Assert.That(unban.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        app.Vrchat.AssertCalled("DELETE", $"groups/{Group}/bans/{Target}");
        await Assert.That(kick.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        app.Vrchat.AssertCalled("DELETE", $"groups/{Group}/members/{Target}");
    }

    [Test]
    public async Task Save_note_sends_only_the_manager_note()
    {
        await using var app = await VrcxScenario.StartAsync();
        app.Vrchat.Json("PUT", $"groups/{Group}/members/{Target}", Member(Target));
        await app.LoginAsync();

        var saved = await app.Api.PutAsJsonAsync(app.AccountPath($"groups/{Group}/members/{Target}"), new { managerNotes = "watch" });

        await VrcxScenario.EnsureSuccessAsync(saved, "save note");
        await Assert.That(app.Vrchat.AssertCalled("PUT", $"groups/{Group}/members/{Target}").Body).IsEqualTo("""{"managerNotes":"watch"}""");
        var member = await saved.Content.ReadFromJsonAsync<JsonObject>();
        await Assert.That((string?)member!["userId"]).IsEqualTo(Target);
    }

    [Test]
    public async Task Roles_are_added_and_removed_one_at_a_time()
    {
        await using var app = await VrcxScenario.StartAsync();
        app.Vrchat
            .Json("PUT", $"groups/{Group}/members/{Target}/roles/grol_b", """["grol_a","grol_b"]""")
            .Json("DELETE", $"groups/{Group}/members/{Target}/roles/grol_a", """["grol_b"]""");
        await app.LoginAsync();

        var added = await app.Api.PutAsync(app.AccountPath($"groups/{Group}/members/{Target}/roles/grol_b"), null);
        var removed = await app.Api.DeleteAsync(app.AccountPath($"groups/{Group}/members/{Target}/roles/grol_a"));

        await Assert.That((await added.Content.ReadFromJsonAsync<string[]>())!).IsEquivalentTo(["grol_a", "grol_b"]);
        await Assert.That((await removed.Content.ReadFromJsonAsync<string[]>())!).IsEquivalentTo(["grol_b"]);
        await Assert.That(app.Vrchat.AssertCalled("PUT", $"groups/{Group}/members/{Target}/roles/grol_b").Body).IsNull();
    }

    [Test]
    public async Task Invites_and_join_requests_are_answered_like_the_original()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();

        var deleted = await app.Api.DeleteAsync(app.AccountPath($"groups/{Group}/invites/usr_invited"));
        var accepted = await app.Api.PutAsJsonAsync(app.AccountPath($"groups/{Group}/requests/usr_a"), new { action = "accept" });
        var rejected = await app.Api.PutAsJsonAsync(app.AccountPath($"groups/{Group}/requests/usr_r"), new { action = "reject" });
        var blocked = await app.Api.PutAsJsonAsync(app.AccountPath($"groups/{Group}/requests/usr_b"), new { action = "reject", block = true });
        var unblocked = await app.Api.DeleteAsync(app.AccountPath($"groups/{Group}/members/usr_b"));

        await Assert.That(deleted.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        app.Vrchat.AssertCalled("DELETE", $"groups/{Group}/invites/usr_invited");
        await Assert.That(accepted.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(app.Vrchat.AssertCalled("PUT", $"groups/{Group}/requests/usr_a").Body).IsEqualTo("""{"action":"accept"}""");
        await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(app.Vrchat.AssertCalled("PUT", $"groups/{Group}/requests/usr_r").Body).IsEqualTo("""{"action":"reject"}""");
        await Assert.That(blocked.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(app.Vrchat.AssertCalled("PUT", $"groups/{Group}/requests/usr_b").Body).IsEqualTo("""{"action":"reject","block":true}""");
        await Assert.That(unblocked.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        app.Vrchat.AssertCalled("DELETE", $"groups/{Group}/members/usr_b");
    }

    [Test]
    public async Task An_unknown_join_request_action_is_refused_before_vrchat()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();

        var response = await app.Api.PutAsJsonAsync(app.AccountPath($"groups/{Group}/requests/usr_a"), new { action = "block" });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        app.Vrchat.AssertNotCalled("PUT", $"groups/{Group}/requests/usr_a");
    }

    [Test]
    public async Task A_vrchat_refusal_comes_back_as_a_problem()
    {
        await using var app = await VrcxScenario.StartAsync();
        app.Vrchat.Fail("POST", $"groups/{Group}/bans", HttpStatusCode.Forbidden, "You can't ban the owner");
        await app.LoginAsync();

        var response = await app.Api.PostAsync(app.AccountPath($"groups/{Group}/bans/{Target}"), null);

        await Assert.That(response.IsSuccessStatusCode).IsFalse();
        await Assert.That(await response.Content.ReadAsStringAsync()).Contains("You can't ban the owner");
    }
}
