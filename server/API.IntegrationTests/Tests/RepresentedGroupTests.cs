using System.Text.Json;
using VRCX.Testing;

namespace VRCX.API.IntegrationTests.Tests;

// Upstream issue 1301: VRChat's group list marks every group as represented when the user represents
// none. Which group is represented comes from users/{id}/groups/represented, as the original reads it.
public sealed class RepresentedGroupTests
{
    private const string Groups = """[{"id":"gmem_1","groupId":"grp_a","name":"A","memberCount":3,"isRepresenting":true},{"id":"gmem_2","groupId":"grp_b","name":"B","memberCount":5,"isRepresenting":true}]""";

    [Test]
    public async Task Representing_is_taken_from_the_represented_endpoint()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        app.Vrchat.Json("users/usr_member/groups", Groups).Json("users/usr_member/groups/represented", "{}");

        var none = (await app.GetJsonAsync(app.AccountPath("users/usr_member/groups"))).EnumerateArray().ToList();
        await Assert.That(none.Count).IsEqualTo(2);
        await Assert.That(none.Any(g => g.GetProperty("isRepresenting").GetBoolean())).IsFalse();

        app.Vrchat.Json("users/usr_member/groups/represented", """{"groupId":"grp_b","name":"B","isRepresenting":true}""");
        var one = (await app.GetJsonAsync(app.AccountPath("users/usr_member/groups"))).EnumerateArray().ToList();
        await Assert.That(one.Where(g => g.GetProperty("isRepresenting").GetBoolean()).Select(g => g.GetProperty("groupId").GetString()!)).IsEquivalentTo(new[] { "grp_b" });
    }
}
