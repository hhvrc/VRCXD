using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using VRCX.Testing;

namespace VRCX.API.IntegrationTests.Tests;

// The memo endpoints answer through Common/Features/Memos since they left the users, worlds and
// avatars controllers. The routes, statuses and problem types are the contract and did not change
// with the move; these pin the parts no other test asks for.
public sealed class MemoEndpointTests
{
    private static async Task<string> ProblemTypeAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("type").GetString()!;

    [Test]
    public async Task A_user_memo_is_written_read_and_removed()
    {
        await using var app = await VrcxScenario.StartAsync();
        var bob = app.Vrchat.Friend("usr_bob", "Bob");
        await app.LoginAsync();

        var none = await app.Api.GetAsync($"/1/users/{bob.Id}/memo");
        await Assert.That(none.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(await ProblemTypeAsync(none)).IsEqualTo("User.MemoNotFound");

        var written = await app.Api.PutAsJsonAsync($"/1/users/{bob.Id}/memo", new { memo = "Bobby" });
        await Assert.That(written.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var read = await app.GetJsonAsync($"/1/users/{bob.Id}/memo");
        await Assert.That(read.GetProperty("userId").GetString()).IsEqualTo(bob.Id);
        await Assert.That(read.GetProperty("memo").GetString()).IsEqualTo("Bobby");
        await Assert.That(read.GetProperty("updatedAt").GetDateTimeOffset()).IsGreaterThan(DateTimeOffset.UtcNow.AddMinutes(-5));

        // Removing is fine twice: the second time there is none.
        for (var i = 0; i < 2; i++)
            await Assert.That((await app.Api.DeleteAsync($"/1/users/{bob.Id}/memo")).StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That((await app.Api.GetAsync($"/1/users/{bob.Id}/memo")).StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task A_memo_on_something_never_seen_is_refused_and_reads_empty()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();

        var user = await app.Api.PutAsJsonAsync("/1/users/usr_nobody/memo", new { memo = "x" });
        await Assert.That((user.StatusCode, await ProblemTypeAsync(user))).IsEqualTo((HttpStatusCode.NotFound, "User.NotFound"));
        var world = await app.Api.PutAsJsonAsync("/1/worlds/wrld_nowhere/memo", new { memo = "x" });
        await Assert.That((world.StatusCode, await ProblemTypeAsync(world))).IsEqualTo((HttpStatusCode.NotFound, "World.NotFound"));
        var avatar = await app.Api.PutAsJsonAsync("/1/avatars/avtr_none/memo", new { memo = "x" });
        await Assert.That((avatar.StatusCode, await ProblemTypeAsync(avatar))).IsEqualTo((HttpStatusCode.NotFound, "Avatar.NotFound"));

        await Assert.That((await app.GetJsonAsync("/1/worlds/wrld_nowhere/memo")).GetProperty("memo").GetString()).IsEqualTo("");
        await Assert.That((await app.GetJsonAsync("/1/avatars/avtr_none/memo")).GetProperty("memo").GetString()).IsEqualTo("");
    }
}
