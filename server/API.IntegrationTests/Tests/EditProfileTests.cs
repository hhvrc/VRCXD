using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using VRCX.Testing;

namespace VRCX.API.IntegrationTests.Tests;

// The original's EditProfileDialog: what it opens with, and exactly what each of its saves sends to VRChat.
public sealed class EditProfileTests
{
    private static async Task<VrcxScenario> StartAsync()
    {
        var app = await VrcxScenario.StartAsync();
        app.Vrchat.SetMe("usr_me", "Me", me => me
            .Status("ask me", "around")
            .Set("pronouns", "they/them")
            .Set("statusHistory", new[] { "around", "away" })
            .Set("iconUrl", "https://api.vrchat.cloud/api/1/file/file_icon/1")
            .Tags("system_trust_trusted", "system_supporter", "language_eng", "language_jpn"));
        return app;
    }

    [Test]
    public async Task Opening_reads_the_self_profile_languages_and_config_options()
    {
        await using var app = await StartAsync();
        app.Vrchat
            .Json("GET", "profile/usr_me?asSelf=true", """{"id":"usr_me","bio":"hi","bioLinks":["https://a.example"],"themes":[{"id":"thm_1","name":"Blue"}],"themeId":"thm_1","backgroundType":"gradient"}""")
            .Json("GET", "config", """{"constants":{"LANGUAGE":{"SPOKEN_LANGUAGE_OPTIONS":{"eng":"English","jpn":"日本語"}}},"profileDefaults":{"themeButtonColor":"064b5c"}}""");
        await app.LoginAsync(sync: false);

        var dialog = await app.GetJsonAsync(app.AccountPath("profile"));

        await Assert.That(dialog.GetProperty("status").GetString()).IsEqualTo("ask me");
        await Assert.That(dialog.GetProperty("pronouns").GetString()).IsEqualTo("they/them");
        await Assert.That(dialog.GetProperty("statusHistory").GetArrayLength()).IsEqualTo(2);
        await Assert.That(dialog.GetProperty("iconUrl").GetString()).IsEqualTo("https://api.vrchat.cloud/api/1/file/file_icon/1");
        await Assert.That(dialog.GetProperty("isVrcPlus").GetBoolean()).IsTrue();
        await Assert.That(dialog.GetProperty("isModerator").GetBoolean()).IsFalse();
        await Assert.That(dialog.GetProperty("languages")[1].GetString()).IsEqualTo("jpn");
        await Assert.That(dialog.GetProperty("languageOptions").GetProperty("jpn").GetString()).IsEqualTo("日本語");
        await Assert.That(dialog.GetProperty("profileDefaults").GetProperty("themeButtonColor").GetString()).IsEqualTo("064b5c");
        await Assert.That(dialog.GetProperty("profile").GetProperty("themes")[0].GetProperty("name").GetString()).IsEqualTo("Blue");
        await Assert.That(dialog.GetProperty("profile").GetProperty("backgroundType").GetString()).IsEqualTo("gradient");
    }

    [Test]
    public async Task Saving_the_profile_sends_only_the_fields_given_in_the_original_order()
    {
        await using var app = await StartAsync();
        app.Vrchat.Json("PUT", "profile/usr_me", """{"id":"usr_me"}""");
        await app.LoginAsync(sync: false);

        var response = await app.Api.PutAsJsonAsync(app.AccountPath("profile"), new
        {
            iconFrame = "",
            bioLinks = new[] { "https://a.example", "" },
            bio = "new bio",
            bannerType = "customImage",
            bannerCustomUrl = "https://api.vrchat.cloud/api/1/file/file_banner/1/file",
        });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(app.Vrchat.Bodies["PUT profile/usr_me"]).IsEqualTo(
            """{"bio":"new bio","bioLinks":["https://a.example",""],"bannerCustomUrl":"https://api.vrchat.cloud/api/1/file/file_banner/1/file","bannerType":"customImage","iconFrame":""}""");
    }

    [Test]
    public async Task Status_text_and_pronouns_go_to_the_current_user()
    {
        await using var app = await StartAsync();
        await app.LoginAsync(sync: false);

        var response = await app.Api.PutAsJsonAsync(app.AccountPath("me"), new { statusDescription = "busy day", pronouns = "she/her" });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(app.Vrchat.Bodies["PUT users/usr_me"]).IsEqualTo("""{"statusDescription":"busy day","pronouns":"she/her"}""");
    }

    [Test]
    public async Task Own_options_send_only_the_toggled_flag()
    {
        await using var app = await StartAsync();
        await app.LoginAsync(sync: false);

        var response = await app.Api.PutAsJsonAsync(app.AccountPath("me/options"), new { hasDiscordFriendsOptOut = true });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(app.Vrchat.Bodies["PUT users/usr_me"]).IsEqualTo("""{"hasDiscordFriendsOptOut":true}""");
    }

    [Test]
    public async Task Reduced_profile_animations_read_and_save_the_client_config()
    {
        await using var app = await StartAsync();
        app.Vrchat
            .Json("GET", "users/usr_me/clientConfig", """{"accessReduceDecorAnim":false,"configString":""}""")
            .Json("PUT", "users/usr_me/clientConfig", """{"accessReduceDecorAnim":true,"configString":""}""");
        await app.LoginAsync(sync: false);

        var read = await app.GetJsonAsync(app.AccountPath("client-config"));
        await Assert.That(read.GetProperty("accessReduceDecorAnim").GetBoolean()).IsFalse();
        var saved = await app.Api.PutAsJsonAsync(app.AccountPath("client-config"), new { accessReduceDecorAnim = true });
        var json = await saved.Content.ReadFromJsonAsync<JsonObject>();
        await Assert.That((bool?)json!["accessReduceDecorAnim"]).IsTrue();
        await Assert.That(app.Vrchat.Bodies["PUT users/usr_me/clientConfig"]).IsEqualTo("""{"accessReduceDecorAnim":true}""");
    }

    [Test]
    public async Task Themes_are_created_saved_and_deleted()
    {
        await using var app = await StartAsync();
        app.Vrchat
            .Json("POST", "profile/theme", """{"id":"thm_new","name":"Mine","buttonColor":"064b5c","iconColor":"6ae3f9","subtextColor":"a9a9a9"}""")
            .Json("PUT", "profile/theme/thm_new", "")
            .Json("DELETE", "profile/theme/thm_new", "");
        await app.LoginAsync(sync: false);

        var created = await app.Api.PostAsJsonAsync(app.AccountPath("profile/themes"), new { name = "Mine", buttonColor = "064b5c", iconColor = "6ae3f9", subtextColor = "a9a9a9" });
        var theme = await created.Content.ReadFromJsonAsync<JsonObject>();
        await Assert.That((string?)theme!["id"]).IsEqualTo("thm_new");
        await Assert.That(app.Vrchat.Bodies["POST profile/theme"]).IsEqualTo("""{"name":"Mine","buttonColor":"064b5c","iconColor":"6ae3f9","subtextColor":"a9a9a9"}""");

        var saved = await app.Api.PutAsJsonAsync(app.AccountPath("profile/themes/thm_new"), new { name = "Renamed", buttonColor = "112233", iconColor = "445566", subtextColor = "778899" });
        await Assert.That(saved.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(app.Vrchat.Bodies["PUT profile/theme/thm_new"]).IsEqualTo("""{"themeId":"thm_new","name":"Renamed","buttonColor":"112233","iconColor":"445566","subtextColor":"778899"}""");

        var deleted = await app.Api.DeleteAsync(app.AccountPath("profile/themes/thm_new"));
        await Assert.That(deleted.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        app.Vrchat.AssertCalled("DELETE", "profile/theme/thm_new");
    }

    [Test]
    public async Task Languages_are_added_and_removed_as_tags()
    {
        await using var app = await StartAsync();
        app.Vrchat
            .Json("POST", "users/usr_me/addTags", """{"id":"usr_me","displayName":"Me","tags":["language_eng","language_jpn","language_kor"]}""")
            .Json("POST", "users/usr_me/removeTags", """{"id":"usr_me","displayName":"Me","tags":["language_jpn"]}""");
        await app.LoginAsync(sync: false);

        var added = await app.Api.PostAsJsonAsync(app.AccountPath("profile/languages"), new { language = "kor" });
        var afterAdd = await added.Content.ReadFromJsonAsync<string[]>();
        await Assert.That(app.Vrchat.Bodies["POST users/usr_me/addTags"]).IsEqualTo("""{"tags":["language_kor"]}""");
        await Assert.That(afterAdd!).IsEquivalentTo(new[] { "eng", "jpn", "kor" });

        var removed = await app.Api.DeleteAsync(app.AccountPath("profile/languages/eng"));
        var afterRemove = await removed.Content.ReadFromJsonAsync<string[]>();
        await Assert.That(app.Vrchat.Bodies["POST users/usr_me/removeTags"]).IsEqualTo("""{"tags":["language_eng"]}""");
        await Assert.That(afterRemove!).IsEquivalentTo(new[] { "jpn" });
    }

    [Test]
    public async Task Inventory_items_carry_their_template_id()
    {
        await using var app = await StartAsync();
        app.Vrchat
            .Json("GET", "inventory?n=100&offset=0&order=newest&notFlags=ugc", """{"data":[{"id":"inv_1","name":"Sparkle","itemType":"nameplateEffect","templateId":"invt_sparkle","imageUrl":"https://img/sparkle"}]}""")
            .Json("GET", "inventory?n=100&offset=100&order=newest&notFlags=ugc", """{"data":[]}""")
            .Json("GET", "inventory/global", "[]");
        await app.LoginAsync(sync: false);

        var items = await app.GetJsonAsync(app.AccountPath("inventory"));

        await Assert.That(items[0].GetProperty("templateId").GetString()).IsEqualTo("invt_sparkle");
    }
}
