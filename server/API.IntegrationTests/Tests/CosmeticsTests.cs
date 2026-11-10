using VRCX.Testing;

namespace VRCX.API.IntegrationTests.Tests;

// The original's cosmetics: users' icon frames, nameplate and profile effects are kept from what
// VRChat sends, and the cosmetics index gives the images to draw them with.
public sealed class CosmeticsTests
{
    [Test]
    public async Task A_friends_cosmetics_are_stored_and_shown()
    {
        await using var app = await VrcxScenario.StartAsync();
        app.Vrchat.Friend("usr_fancy", "Fancy", u => u.Set("iconFrame", "invt_frame").Set("nameplateEffect", "invt_plate").Set("profileEffect", "invt_effect"));
        app.Vrchat.Friend("usr_plain", "Plain");
        await app.LoginAsync();

        var friends = await app.GetJsonAsync(app.AccountPath("friends"));
        var fancy = friends.EnumerateArray().Single(f => f.GetProperty("userId").GetString() == "usr_fancy");
        var plain = friends.EnumerateArray().Single(f => f.GetProperty("userId").GetString() == "usr_plain");
        await Assert.That(fancy.GetProperty("iconFrame").GetString()).IsEqualTo("invt_frame");
        await Assert.That(plain.TryGetProperty("iconFrame", out _)).IsFalse();

        var user = await app.GetJsonAsync("/1/users/usr_fancy");
        await Assert.That(user.GetProperty("iconFrame").GetString()).IsEqualTo("invt_frame");
        await Assert.That(user.GetProperty("nameplateEffect").GetString()).IsEqualTo("invt_plate");
        await Assert.That(user.GetProperty("profileEffect").GetString()).IsEqualTo("invt_effect");
    }

    [Test]
    public async Task A_users_discord_and_age_verification_come_from_their_full_object()
    {
        await using var app = await VrcxScenario.StartAsync();
        app.Vrchat.User("usr_linked", "Linked", u => u.Set("discordId", "123456789").Set("ageVerified", true).Set("ageVerificationStatus", "18+"));
        await app.LoginAsync();

        var user = await app.GetJsonAsync("/1/users/usr_linked?refresh=true");

        await Assert.That(user.GetProperty("discordId").GetString()).IsEqualTo("123456789");
        await Assert.That(user.GetProperty("ageVerified").GetBoolean()).IsTrue();
    }

    [Test]
    public async Task The_index_lists_each_template_with_its_images()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        app.Vrchat.Json("cosmetics/index/iconFrame", """
            [
              {"id":"invt_frame","name":"Gold","metadata":{"assets":[
                {"type":"base","url":"https://img/base.png"},
                {"type":"introAnimation","url":"https://img/intro.webp","totalDurationMs":1500},
                {"type":"mainAnimation","url":"https://img/main.webp"},
                {"type":"base","url":""}
              ]}},
              {"id":"invt_bare","metadata":{"gradientStart":"ff0000","gradientEnd":"0000ff"}},
              {"name":"no id"}
            ]
            """);

        var index = await app.GetJsonAsync(app.AccountPath("cosmetics/iconFrame"));

        await Assert.That(index.GetArrayLength()).IsEqualTo(2);
        var frame = index[0];
        await Assert.That(frame.GetProperty("id").GetString()).IsEqualTo("invt_frame");
        await Assert.That(frame.GetProperty("assets").GetArrayLength()).IsEqualTo(3);
        await Assert.That(frame.GetProperty("assets")[1].GetProperty("totalDurationMs").GetInt32()).IsEqualTo(1500);
        await Assert.That(index[1].GetProperty("assets").GetArrayLength()).IsEqualTo(0);
        await Assert.That(index[1].GetProperty("gradientStart").GetString()).IsEqualTo("ff0000");
        await Assert.That(index[1].GetProperty("gradientEnd").GetString()).IsEqualTo("0000ff");
    }

    [Test]
    public async Task The_saved_accounts_carry_their_picture_and_icon_frame()
    {
        await using var app = await VrcxScenario.StartAsync();
        app.Vrchat.SetMe(configure: u => u.Set("iconFrame", "invt_myframe"));
        await app.LoginAsync();

        var accounts = await app.GetJsonAsync("/1/accounts");
        var me = accounts.EnumerateArray().Single(a => a.GetProperty("userId").GetString() == "usr_me");
        await Assert.That(me.GetProperty("iconFrame").GetString()).IsEqualTo("invt_myframe");
        // Picked as friends' pictures are.
        await Assert.That(me.GetProperty("thumbnailImageUrl").GetString()).IsEqualTo("https://api.vrchat.cloud/api/1/image/file_usr_me_avatar/1/256");
    }
}
