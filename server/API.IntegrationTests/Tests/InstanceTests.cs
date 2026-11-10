using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace VRCX.API.IntegrationTests.Tests;

// The New Instance, Launch and Invite dialogs, with VRChat faked. Nothing here touches real VRChat.
public sealed class InstanceTests
{
    private const string Account = "usr_instances_account";
    private const string World = "wrld_instances";
    private const string Location = World + ":12345~hidden(usr_instances_account)~region(eu)";

    [ClassDataSource<IsolatedHost>(Shared = SharedType.None)]
    public required IsolatedHost Factory { get; init; }

    private async Task<HttpClient> LoginAsync()
    {
        Factory.Vrchat.Json("auth/user", $$"""{"id":"{{Account}}","displayName":"Instances","username":"instances","tags":["system_supporter"]}""");
        var client = Factory.CreateAuthenticatedClient();
        var login = await client.PostAsJsonAsync("/1/accounts/login", new { username = "instances", password = "pw" });
        await Assert.That(login.StatusCode).IsEqualTo(HttpStatusCode.OK);
        return client;
    }

    private async Task<JsonElement> SentAsync(string call)
    {
        await Assert.That(Factory.Vrchat.Bodies.ContainsKey(call)).IsTrue();
        return JsonDocument.Parse(Factory.Vrchat.Bodies[call]).RootElement;
    }

    private void FakeCreated() => Factory.Vrchat.Json("instances", $$"""{"id":"{{Location}}","location":"{{Location}}","worldId":"{{World}}","instanceId":"12345~hidden(usr_instances_account)~region(eu)","shortName":"short1","secureName":"secure1"}""");

    [Test]
    public async Task An_account_instance_is_owned_by_the_account_and_sends_no_group_fields()
    {
        FakeCreated();
        using var client = await LoginAsync();

        var response = await client.PostAsJsonAsync($"/1/accounts/{Account}/instances", new { worldId = World, type = "hidden", canRequestInvite = false, region = "eu", queueEnabled = true, ageGate = true, groupAccessType = "plus" });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        await Assert.That(created.GetProperty("shortName").GetString()).IsEqualTo("short1");
        var sent = await SentAsync("POST instances");
        await Assert.That(sent.GetProperty("ownerId").GetString()).IsEqualTo(Account);
        await Assert.That(sent.GetProperty("type").GetString()).IsEqualTo("hidden");
        await Assert.That(sent.TryGetProperty("groupAccessType", out _)).IsFalse();
        await Assert.That(sent.TryGetProperty("ageGate", out _)).IsFalse();
        await Assert.That(sent.TryGetProperty("queueEnabled", out _)).IsFalse();
    }

    [Test]
    public async Task A_members_group_instance_is_owned_by_the_group_and_names_its_roles()
    {
        FakeCreated();
        using var client = await LoginAsync();

        var response = await client.PostAsJsonAsync($"/1/accounts/{Account}/instances", new { worldId = World, type = "group", canRequestInvite = false, region = "jp", groupId = "grp_a", groupAccessType = "members", queueEnabled = true, roleIds = new[] { "grol_a" }, minimumAvatarPerformance = "Good", ageGate = true, displayName = "Room" });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var sent = await SentAsync("POST instances");
        await Assert.That(sent.GetProperty("ownerId").GetString()).IsEqualTo("grp_a");
        await Assert.That(sent.GetProperty("groupAccessType").GetString()).IsEqualTo("members");
        await Assert.That(sent.GetProperty("roleIds")[0].GetString()).IsEqualTo("grol_a");
        await Assert.That(sent.GetProperty("queueEnabled").GetBoolean()).IsTrue();
        await Assert.That(sent.GetProperty("ageGate").GetBoolean()).IsTrue();
        await Assert.That(sent.GetProperty("minimumAvatarPerformance").GetString()).IsEqualTo("Good");
        await Assert.That(sent.GetProperty("displayName").GetString()).IsEqualTo("Room");
    }

    [Test]
    public async Task Options_flag_only_groups_the_account_can_open_instances_in()
    {
        Factory.Vrchat
            .Json($"users/{Account}/groups", """[{"id":"gmem_a","groupId":"grp_can","name":"Can","privacy":"private"},{"id":"gmem_b","groupId":"grp_owner","name":"Owner"},{"id":"gmem_c","groupId":"grp_cannot","name":"Cannot"}]""")
            .Json($"users/{Account}/groups/permissions", """{"grp_can":["group-instance-plus-create"],"grp_owner":["*"],"grp_cannot":["group-members-viewall"]}""");
        using var client = await LoginAsync();

        var options = await client.GetFromJsonAsync<JsonElement>($"/1/accounts/{Account}/instances/options");

        await Assert.That(options.GetProperty("isSupporter").GetBoolean()).IsTrue();
        var groups = options.GetProperty("groups").EnumerateArray().ToDictionary(g => g.GetProperty("id").GetString()!, g => g.GetProperty("canCreateInstance").GetBoolean());
        await Assert.That(groups["grp_can"]).IsTrue();
        await Assert.That(groups["grp_owner"]).IsTrue();
        await Assert.That(groups["grp_cannot"]).IsFalse();
    }

    [Test]
    public async Task A_self_invite_goes_to_the_instance_with_its_short_name()
    {
        // VRChat answers 400 if the colon and brackets are escaped.
        var path = $"invite/myself/to/{Location}";
        Factory.Vrchat.Json(path, """{"id":"not_a"}""");
        using var client = await LoginAsync();

        var response = await client.PostAsJsonAsync($"/1/accounts/{Account}/instances/self-invite", new { location = Location, shortName = "short1" });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        var sent = await SentAsync($"POST {path}");
        await Assert.That(sent.GetProperty("shortName").GetString()).IsEqualTo("short1");
    }

    [Test]
    public async Task An_invite_sends_the_location_and_a_chosen_message()
    {
        Factory.Vrchat.Json("invite/usr_instances_friend", """{"id":"not_b"}""");
        using var client = await LoginAsync();

        var response = await client.PostAsJsonAsync($"/1/accounts/{Account}/invites/usr_instances_friend", new { location = Location, worldName = "Place", messageSlot = 3 });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        var sent = await SentAsync("POST invite/usr_instances_friend");
        await Assert.That(sent.GetProperty("instanceId").GetString()).IsEqualTo(Location);
        await Assert.That(sent.GetProperty("worldName").GetString()).IsEqualTo("Place");
        await Assert.That(sent.GetProperty("messageSlot").GetInt32()).IsEqualTo(3);
        await Assert.That(sent.TryGetProperty("rsvp", out _)).IsFalse();
    }

    [Test]
    public async Task A_refused_create_comes_back_as_a_problem_not_a_success()
    {
        Factory.Vrchat.Status("instances", HttpStatusCode.Forbidden, """{"error":{"message":"not allowed","status_code":403}}""");
        using var client = await LoginAsync();

        var response = await client.PostAsJsonAsync($"/1/accounts/{Account}/instances", new { worldId = World, type = "public", canRequestInvite = false, region = "us", queueEnabled = false, ageGate = false });

        await Assert.That(response.IsSuccessStatusCode).IsFalse();
    }

    [Test]
    public async Task An_instance_sends_its_description_category_vibes_languages_and_avatar_performance()
    {
        FakeCreated();
        using var client = await LoginAsync();

        var response = await client.PostAsJsonAsync($"/1/accounts/{Account}/instances", new { worldId = World, type = "friends", canRequestInvite = false, region = "us", queueEnabled = false, ageGate = false, minimumAvatarPerformance = "Medium", displayName = "Room", description = "Chill hangout", categoryId = "icat_hangout", languages = new[] { "eng", "jpn", "kor", "fra" }, vibeIds = new[] { "ivib_a", "ivib_b" } });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var sent = await SentAsync("POST instances");
        await Assert.That(sent.GetProperty("minimumAvatarPerformance").GetString()).IsEqualTo("Medium");
        await Assert.That(sent.GetProperty("displayName").GetString()).IsEqualTo("Room");
        await Assert.That(sent.GetProperty("description").GetString()).IsEqualTo("Chill hangout");
        await Assert.That(sent.GetProperty("categoryId").GetString()).IsEqualTo("icat_hangout");
        var languages = sent.GetProperty("languages").EnumerateArray().Select(l => l.GetString()).ToList();
        await Assert.That(string.Join(",", languages)).IsEqualTo("eng,jpn,kor");
        await Assert.That(sent.GetProperty("vibeIds").GetArrayLength()).IsEqualTo(2);
    }

    [Test]
    public async Task Unset_new_fields_are_not_sent()
    {
        FakeCreated();
        using var client = await LoginAsync();

        var response = await client.PostAsJsonAsync($"/1/accounts/{Account}/instances", new { worldId = World, type = "public", canRequestInvite = false, region = "us", queueEnabled = false, ageGate = false, description = "", languages = Array.Empty<string>() });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var sent = await SentAsync("POST instances");
        foreach (var field in new[] { "description", "categoryId", "languages", "vibeIds", "displayName", "minimumAvatarPerformance" })
            await Assert.That(sent.TryGetProperty(field, out _)).IsFalse();
    }

    [Test]
    public async Task An_age_verified_account_can_age_gate_its_own_instance()
    {
        FakeCreated();
        using var client = await LoginAsync();
        Factory.Vrchat.Json("auth/user", $$"""{"id":"{{Account}}","displayName":"Instances","username":"instances","tags":[],"ageVerificationStatus":"18+","ageVerified":true}""");

        var response = await client.PostAsJsonAsync($"/1/accounts/{Account}/instances", new { worldId = World, type = "private", canRequestInvite = true, region = "us", queueEnabled = false, ageGate = true });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var sent = await SentAsync("POST instances");
        await Assert.That(sent.GetProperty("ageGate").GetBoolean()).IsTrue();
    }

    [Test]
    public async Task Options_say_whether_the_account_is_age_verified()
    {
        Factory.Vrchat
            .Json($"users/{Account}/groups", "[]")
            .Json($"users/{Account}/groups/permissions", "{}");
        using var client = await LoginAsync();
        Factory.Vrchat.Json("auth/user", $$"""{"id":"{{Account}}","displayName":"Instances","username":"instances","tags":[],"ageVerificationStatus":"18+"}""");

        var options = await client.GetFromJsonAsync<JsonElement>($"/1/accounts/{Account}/instances/options");

        await Assert.That(options.GetProperty("ageVerified").GetBoolean()).IsTrue();
        await Assert.That(options.GetProperty("isSupporter").GetBoolean()).IsFalse();
    }

    [Test]
    public async Task Instance_categories_and_vibes_come_back_as_vrchat_sends_them()
    {
        Factory.Vrchat
            .Json("instanceCategories", """[{"id":"icat_hangout","name":"Hangout","iconUrl":"vrchat://category_hangout","order":1,"deleted":false}]""")
            .Json("instanceVibes", """[{"id":"ivib_chill","title":"Chill","deleted":false}]""");
        using var client = await LoginAsync();

        var categories = await client.GetFromJsonAsync<JsonElement>($"/1/accounts/{Account}/instances/categories");
        var vibes = await client.GetFromJsonAsync<JsonElement>($"/1/accounts/{Account}/instances/vibes");

        await Assert.That(categories[0].GetProperty("iconUrl").GetString()).IsEqualTo("vrchat://category_hangout");
        await Assert.That(vibes[0].GetProperty("title").GetString()).IsEqualTo("Chill");
    }
}
