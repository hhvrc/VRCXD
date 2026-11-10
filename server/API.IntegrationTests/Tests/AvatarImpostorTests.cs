using System.Text.Json.Nodes;
using VRCX.Testing;

namespace VRCX.API.IntegrationTests.Tests;

// Upstream PR 1717: My Avatars' Impostor column, the last impostor package's impostorizer version.
public sealed class AvatarImpostorTests
{
    [Test]
    public async Task Own_avatars_carry_the_impostor_version()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        var packages = JsonNode.Parse("""
            [
              {"id":"unp_1","platform":"standalonewindows","assetVersion":1},
              {"id":"unp_2","platform":"standalonewindows","assetVersion":1,"variant":"impostor","impostorizerVersion":"2"},
              {"id":"unp_3","platform":"android","assetVersion":1,"variant":"impostor","impostorizerVersion":"3"}
            ]
            """)!;
        app.Vrchat.Avatar("avtr_impostor", "With Impostor", a => a.Author(app.AccountId!, "Me").Set("unityPackages", packages));
        app.Vrchat.Avatar("avtr_plain", "Plain", a => a.Author(app.AccountId!, "Me"));

        var list = await app.GetJsonAsync(app.AccountPath("avatars?refresh=true"));
        var rows = list.EnumerateArray().ToDictionary(r => r.GetProperty("avatar").GetProperty("id").GetString()!, r => r.GetProperty("avatar"));

        await Assert.That(rows["avtr_impostor"].GetProperty("impostorVersion").GetString()).IsEqualTo("3");
        await Assert.That(rows["avtr_plain"].TryGetProperty("impostorVersion", out _)).IsFalse();
    }
}
