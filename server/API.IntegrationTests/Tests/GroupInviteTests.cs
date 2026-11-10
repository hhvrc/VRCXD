using System.Text.Json.Nodes;
using VRCX.Testing;

namespace VRCX.API.IntegrationTests.Tests;

// The original's InviteGroupDialog: one group invite per chosen user.
public sealed class GroupInviteTests
{
    [Test]
    public async Task A_group_invite_posts_the_user_to_the_groups_invites()
    {
        await using var app = await VrcxScenario.StartAsync();
        app.Vrchat.Json("POST", "groups/grp_invite/invites", """{"success":{"message":"OK","status_code":200}}""");
        await app.LoginAsync();

        var response = await app.Api.PostAsync(app.AccountPath("groups/grp_invite/invites/usr_friend"), null);

        await Assert.That(response.IsSuccessStatusCode).IsTrue();
        var sent = JsonNode.Parse(app.Vrchat.Bodies["POST groups/grp_invite/invites"])!;
        await Assert.That((string?)sent["userId"]).IsEqualTo("usr_friend");
    }
}
