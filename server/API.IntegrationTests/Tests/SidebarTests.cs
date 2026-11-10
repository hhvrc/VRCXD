using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace VRCX.API.IntegrationTests.Tests;

// The right-hand sidebar: the account's own row and the Groups tab, with VRChat faked.
public sealed class SidebarTests
{
    private const string Account = "usr_sidebar_account";

    [ClassDataSource<IsolatedHost>(Shared = SharedType.None)]
    public required IsolatedHost Factory { get; init; }

    private static string Me(string status, string text) =>
        $$"""{"id":"{{Account}}","displayName":"Side","username":"side","status":"{{status}}","statusDescription":"{{text}}","statusHistory":["{{text}}","older"],"isBoopingEnabled":false,"currentAvatar":"avtr_worn"}""";

    private async Task<HttpClient> LoginAsync()
    {
        Factory.Vrchat.Json("auth/user", Me("active", "hello"));
        var client = Factory.CreateAuthenticatedClient();
        var login = await client.PostAsJsonAsync("/1/accounts/login", new { username = "side", password = "pw" });
        await Assert.That(login.StatusCode).IsEqualTo(HttpStatusCode.OK);
        return client;
    }

    [Test]
    public async Task The_own_row_shows_status_history_and_booping()
    {
        using var client = await LoginAsync();

        var me = await client.GetFromJsonAsync<JsonElement>($"/1/accounts/{Account}/me");

        await Assert.That(me.GetProperty("status").GetString()).IsEqualTo("active");
        await Assert.That(me.GetProperty("statusHistory").GetArrayLength()).IsEqualTo(2);
        await Assert.That(me.GetProperty("isBoopingEnabled").GetBoolean()).IsFalse();
        // Select Avatar is disabled for the avatar worn now, as the original.
        await Assert.That(me.GetProperty("currentAvatar").GetString()).IsEqualTo("avtr_worn");
    }

    [Test]
    public async Task Changing_status_reaches_vrchat()
    {
        using var client = await LoginAsync();
        Factory.Vrchat.Json($"users/{Account}", Me("busy", "away"));

        var response = await client.PutAsJsonAsync($"/1/accounts/{Account}/me", new { status = "busy" });
        var me = await response.Content.ReadFromJsonAsync<JsonElement>();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(Factory.Vrchat.Calls).Contains($"PUT users/{Account}");
        await Assert.That(me.GetProperty("status").GetString()).IsEqualTo("busy");
    }

    [Test]
    public async Task Group_instances_carry_their_group()
    {
        using var client = await LoginAsync();
        const string location = "wrld_side:1~group(grp_side)~groupAccessType(public)~ageGate~region(us)";
        Factory.Vrchat
            .Json($"users/{Account}/instances/groups", $$"""{"instances":[{"id":"{{location}}","location":"{{location}}","worldId":"wrld_side","instanceId":"1~group(grp_side)","ownerId":"grp_side","n_users":3,"capacity":16}],"fetchedAt":"2026-11-08T00:00:00Z"}""")
            .Json("groups/grp_side?includeRoles=false", """{"id":"grp_side","name":"Side Group","iconUrl":"https://img/icon"}""");

        var list = await client.GetFromJsonAsync<JsonElement>($"/1/accounts/{Account}/group-instances");

        await Assert.That(list.GetArrayLength()).IsEqualTo(1);
        var row = list[0];
        await Assert.That(row.GetProperty("groupName").GetString()).IsEqualTo("Side Group");
        await Assert.That(row.GetProperty("userCount").GetInt32()).IsEqualTo(3);
        await Assert.That(row.GetProperty("ageGate").GetBoolean()).IsTrue();
    }

    [Test]
    public async Task Quick_search_lists_every_category()
    {
        using var client = await LoginAsync();

        var index = await client.GetFromJsonAsync<JsonElement>($"/1/accounts/{Account}/quick-search");

        foreach (var key in new[] { "friends", "ownAvatars", "favAvatars", "ownWorlds", "favWorlds", "groups" })
            await Assert.That(index.GetProperty(key).ValueKind).IsEqualTo(JsonValueKind.Array);
    }
}
