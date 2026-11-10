using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace VRCX.API.IntegrationTests.Tests;

// The fake VRChat behind the shared host is one mutable route table, so these run one at a time.
public sealed class AccountsTests
{
    [ClassDataSource<IsolatedHost>(Shared = SharedType.None)]
    public required IsolatedHost Factory { get; init; }

    private const string Me = """
        {"id":"usr_me","displayName":"Me","username":"me","status":"active","tags":["system_trust_trusted"],
         "onlineFriends":["usr_a"],"activeFriends":[],"friendGroupNames":["group_0"],"homeLocation":"wrld_home"}
        """;

    [Test]
    public async Task Without_the_api_token_everything_is_401()
    {
        using var client = Factory.CreateClient();
        var response = await client.GetAsync("/1/accounts");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("application/problem+json");
    }

    [Test]
    public async Task Health_reports_the_database()
    {
        using var client = Factory.CreateClient();
        var response = await client.GetAsync("/1/health");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        await Assert.That(body.GetProperty("entries").TryGetProperty("database", out _)).IsTrue();
    }

    [Test]
    public async Task Login_then_friends_then_logout()
    {
        Factory.Vrchat
            .Json("auth/user", Me)
            .Json("auth/user/friends?offline=false&n=100&offset=0", """[{"id":"usr_a","displayName":"Alice","location":"wrld_x:1~region(eu)","tags":["system_trust_veteran"]}]""")
            .Json("auth/user/friends?offline=true&n=100&offset=0", """[{"id":"usr_b","displayName":"Bob","location":"offline","tags":[]}]""")
            .Json("users/usr_me/groups", "[]")
            .Json("favorite/groups?n=100", "[]")
            .Json("favorites?n=100&offset=0", "[]")
            .Json("auth/user/playermoderations", "[]")
            .Json("userNotes?n=100&offset=0", "[]")
            .Json("auth/user/notifications?n=100&offset=0", "[]")
            .Json("notifications?n=100&offset=0", "[]")
            .Json("logout", "{}");
        using var client = Factory.CreateAuthenticatedClient();

        var login = await client.PostAsJsonAsync("/1/accounts/login", new { username = "me", password = "pw" });
        await Assert.That(login.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var loginBody = await login.Content.ReadFromJsonAsync<JsonElement>();
        await Assert.That(loginBody.GetProperty("status").GetString()).IsEqualTo("loggedIn");
        await Assert.That(loginBody.GetProperty("userId").GetString()).IsEqualTo("usr_me");

        var accounts = await client.GetFromJsonAsync<JsonElement>("/1/accounts");
        var account = accounts.EnumerateArray().Single(a => a.GetProperty("userId").GetString() == "usr_me");
        await Assert.That(account.GetProperty("connected").GetBoolean()).IsTrue();

        var sync = await client.PostAsync("/1/accounts/usr_me/sync", null);
        await Assert.That(sync.StatusCode).IsEqualTo(HttpStatusCode.NoContent);

        var friends = await client.GetFromJsonAsync<JsonElement>("/1/accounts/usr_me/friends");
        var names = friends.EnumerateArray().Select(f => f.GetProperty("displayName").GetString()!).ToList();
        await Assert.That(names).IsEquivalentTo(new[] { "Alice", "Bob" });
        var alice = friends.EnumerateArray().Single(f => f.GetProperty("userId").GetString() == "usr_a");
        // Alice was in the current user's onlineFriends list and in an instance: presence followed.
        await Assert.That(alice.GetProperty("state").GetString()).IsEqualTo("online");
        await Assert.That(alice.GetProperty("trustLevel").GetString()).IsEqualTo("Trusted User");

        var instance = await client.GetFromJsonAsync<JsonElement>("/1/instances/wrld_x:1~region(eu)");
        await Assert.That(instance.GetProperty("present").EnumerateArray().Single().GetProperty("userId").GetString()).IsEqualTo("usr_a");

        var log = await client.GetFromJsonAsync<JsonElement>("/1/accounts/usr_me/friend-log");
        // The first sync is the starting point, so nobody counts as a new friend yet.
        await Assert.That(log.GetArrayLength()).IsEqualTo(0);

        var logout = await client.DeleteAsync("/1/accounts/usr_me/session");
        await Assert.That(logout.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        var after = await client.GetFromJsonAsync<JsonElement>("/1/accounts");
        var mine = after.EnumerateArray().Single(a => a.GetProperty("userId").GetString() == "usr_me");
        await Assert.That(mine.GetProperty("sessionValid").GetBoolean()).IsFalse();
    }

    [Test]
    public async Task Bad_credentials_are_a_401_problem()
    {
        Factory.Vrchat.Status("auth/user", HttpStatusCode.Unauthorized, """{"error":{"message":"Invalid Username/Email or Password","status_code":401}}""");
        using var client = Factory.CreateAuthenticatedClient();

        var login = await client.PostAsJsonAsync("/1/accounts/login", new { username = "me", password = "nope" });

        await Assert.That(login.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        var problem = await login.Content.ReadFromJsonAsync<JsonElement>();
        await Assert.That(problem.GetProperty("type").GetString()).IsEqualTo("Login.InvalidCredentials");
    }

    [Test]
    public async Task Validation_errors_are_problem_details()
    {
        using var client = Factory.CreateAuthenticatedClient();
        var login = await client.PostAsJsonAsync("/1/accounts/login", new { username = "", password = "" });
        await Assert.That(login.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        var problem = await login.Content.ReadFromJsonAsync<JsonElement>();
        await Assert.That(problem.GetProperty("type").GetString()).IsEqualTo("Validation.Error");
    }

    [Test]
    public async Task Unknown_user_is_404_and_refresh_needs_an_account()
    {
        using var client = Factory.CreateAuthenticatedClient();
        var missing = await client.GetAsync("/1/users/usr_nobody");
        await Assert.That(missing.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        var problem = await missing.Content.ReadFromJsonAsync<JsonElement>();
        await Assert.That(problem.GetProperty("type").GetString()).IsEqualTo("User.NotFound");
    }
}
