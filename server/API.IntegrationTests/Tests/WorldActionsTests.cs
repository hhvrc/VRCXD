using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VRCX.Common.VrcxDb;
using VRCX.Testing.FakeVrchat;

namespace VRCX.API.IntegrationTests.Tests;

// The world pop-up, with VRChat faked.
public sealed class WorldActionsTests
{
    private const string Account = "usr_worlds_account";

    [ClassDataSource<IsolatedHost>(Shared = SharedType.None)]
    public required IsolatedHost Factory { get; init; }

    private async Task<HttpClient> LoginAsync(string home = "")
    {
        // The login's background sync copies the account from this, so it has to agree with what a test asserts.
        Factory.Vrchat.Json("auth/user", $$"""{"id":"{{Account}}","displayName":"Worlds","username":"worlds","homeLocation":"{{home}}"}""");
        var client = Factory.CreateAuthenticatedClient();
        var login = await client.PostAsJsonAsync("/1/accounts/login", new { username = "worlds", password = "pw" });
        await Assert.That(login.StatusCode).IsEqualTo(HttpStatusCode.OK);
        return client;
    }

    private async Task<VrcxContext> DbAsync() =>
        await Factory.Services.GetRequiredService<IDbContextFactory<VrcxContext>>().CreateDbContextAsync();

    [Test]
    public async Task Live_world_has_the_momentary_counts_and_instances()
    {
        const string world = "wrld_live";
        Factory.Vrchat.Json($"worlds/{world}", """{"id":"wrld_live","name":"Live","authorId":"usr_x","heat":4,"publicOccupants":7,"instances":[["123~region(eu)",7]]}""");
        using var client = await LoginAsync();

        var live = await client.GetFromJsonAsync<JsonElement>($"/1/accounts/{Account}/worlds/{world}/live");

        await Assert.That(live.GetProperty("heat").GetInt32()).IsEqualTo(4);
        await Assert.That(live.GetProperty("instances")[0][1].GetInt32()).IsEqualTo(7);
    }

    [Test]
    public async Task Stats_add_up_this_accounts_finished_visits()
    {
        const string world = "wrld_stats";
        const string location = world + ":1";
        using var client = await LoginAsync();
        var joined = DateTimeOffset.UtcNow.AddHours(-3);
        await using (var db = await DbAsync())
        {
            db.Worlds.Add(new World { Id = world, Name = "Stats", FirstSeenAt = joined });
            db.Instances.Add(new Instance { Location = location, WorldId = world, InstanceId = "1", InstanceName = "1", Type = InstanceAccessType.Public, FirstSeenAt = joined });
            db.InstanceVisits.Add(new InstanceVisit { UserId = Account, InstanceLocation = location, JoinedAt = joined, LeftAt = joined.AddHours(1), Source = VisitSource.GameLog });
            db.InstanceVisits.Add(new InstanceVisit { UserId = Account, InstanceLocation = location, JoinedAt = joined.AddHours(2), Source = VisitSource.GameLog });
            await db.SaveChangesAsync();
        }

        var stats = await client.GetFromJsonAsync<JsonElement>($"/1/accounts/{Account}/worlds/{world}/stats");

        await Assert.That(stats.GetProperty("visitCount").GetInt32()).IsEqualTo(2);
        await Assert.That(stats.GetProperty("timeSpentMs").GetInt64()).IsEqualTo(3_600_000);
    }

    [Test]
    public async Task A_world_memo_is_kept_and_an_empty_one_removes_it()
    {
        const string world = "wrld_memo";
        using var client = Factory.CreateAuthenticatedClient();
        await using (var db = await DbAsync())
        {
            db.Worlds.Add(new World { Id = world, Name = "Memo", FirstSeenAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }

        var saved = await client.PutAsJsonAsync($"/1/worlds/{world}/memo", new { memo = "nice" });
        await Assert.That(saved.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var read = await client.GetFromJsonAsync<JsonElement>($"/1/worlds/{world}/memo");
        await Assert.That(read.GetProperty("memo").GetString()).IsEqualTo("nice");

        await client.PutAsJsonAsync($"/1/worlds/{world}/memo", new { memo = "" });
        read = await client.GetFromJsonAsync<JsonElement>($"/1/worlds/{world}/memo");
        await Assert.That(read.GetProperty("memo").GetString()).IsEqualTo("");
    }

    [Test]
    public async Task Making_a_world_home_saves_it_on_vrchat_and_here()
    {
        const string world = "wrld_home";
        Factory.Vrchat.Json($"users/{Account}", $$"""{"id":"{{Account}}","displayName":"Worlds","username":"worlds","homeLocation":"{{world}}"}""");
        using var client = await LoginAsync(world);

        var response = await client.PutAsync($"/1/accounts/{Account}/home/{world}", null);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await using var db = await DbAsync();
        await Assert.That((await db.Accounts.AsNoTracking().SingleAsync(a => a.UserId == Account)).HomeLocation).IsEqualTo(world);
    }

    [Test]
    public async Task Saved_data_is_reported_false_when_vrchat_has_none()
    {
        const string world = "wrld_persist";
        Factory.Vrchat.Json($"users/{Account}/wrld_persist_yes/persist/exists", "true");
        using var client = await LoginAsync();

        var none = await client.GetFromJsonAsync<bool>($"/1/accounts/{Account}/worlds/{world}/persist");
        var some = await client.GetFromJsonAsync<bool>($"/1/accounts/{Account}/worlds/wrld_persist_yes/persist");

        await Assert.That(none).IsFalse();
        await Assert.That(some).IsTrue();
    }

    [Test]
    public async Task A_world_rating_is_read_set_and_cleared_on_vrchat()
    {
        const string world = "wrld_rating";
        Factory.Vrchat.Json("GET", $"worlds/{world}/rating", """{"value":"dislike"}""");
        Factory.Vrchat.On("PUT", $"worlds/{world}/rating", r => FakeVrchatApi.Ok(new JsonObject { ["value"] = r.QueryValue("value") }));
        Factory.Vrchat.Json("DELETE", $"worlds/{world}/rating", """{"value":null}""");
        using var client = await LoginAsync();
        var path = $"/1/accounts/{Account}/worlds/{world}/rating";

        var read = await client.GetFromJsonAsync<JsonElement>(path);
        await Assert.That(read.GetProperty("value").GetString()).IsEqualTo("dislike");

        var set = await client.PutAsync($"{path}?value=like", null);
        await Assert.That(set.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That((await set.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("value").GetString()).IsEqualTo("like");
        await Assert.That(Factory.Vrchat.AssertCalled("PUT", $"worlds/{world}/rating").Query).IsEqualTo("?value=like");

        var cleared = await client.DeleteAsync(path);
        await Assert.That(cleared.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That((await cleared.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("value").ValueKind).IsEqualTo(JsonValueKind.Null);
    }

    [Test]
    public async Task A_world_rating_other_than_like_or_dislike_is_refused()
    {
        const string world = "wrld_rating_bad";
        using var client = await LoginAsync();

        var response = await client.PutAsync($"/1/accounts/{Account}/worlds/{world}/rating?value=meh", null);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        Factory.Vrchat.AssertNotCalled("PUT", $"worlds/{world}/rating");
    }
}
