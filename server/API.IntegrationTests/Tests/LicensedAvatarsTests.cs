using VRCX.Testing;

namespace VRCX.API.IntegrationTests.Tests;

// The user page's Avatars tab groups your own avatars with the ones you bought, which VRChat lists apart.
public sealed class LicensedAvatarsTests
{
    [Test]
    public async Task Bought_avatars_come_from_every_page_of_VRChats_list_and_are_stored()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        var firstPage = string.Join(",", Enumerable.Range(0, 100).Select(i => $$"""{"id":"avtr_bought{{i}}","name":"Bought {{i}}","authorId":"usr_seller","releaseStatus":"public"}"""));
        app.Vrchat
            .Json("avatars/licensed?n=100&offset=0", $"[{firstPage}]")
            .Json("avatars/licensed?n=100&offset=100", """[{"id":"avtr_last","name":"Last","authorId":"usr_seller","releaseStatus":"private"}]""");

        var avatars = await app.GetJsonAsync(app.AccountPath("avatars/licensed"));

        await Assert.That(avatars.GetArrayLength()).IsEqualTo(101);
        await using var db = app.CreateDbContext();
        await Assert.That(db.Avatars.Any(a => a.Id == "avtr_last" && a.ReleaseStatus == "private")).IsTrue();
    }
}
