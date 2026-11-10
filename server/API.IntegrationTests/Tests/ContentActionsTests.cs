using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VRCX.Testing;

namespace VRCX.API.IntegrationTests.Tests;

// The avatar and world pop-ups' own commands (the original's useAvatarDialogCommands and
// useWorldDialogCommands), the home reset, and the tools that only pass VRChat's answer through.
public sealed class ContentActionsTests
{
    private static async Task<HttpResponseMessage> OkAsync(Task<HttpResponseMessage> call, string what)
    {
        var response = await call;
        await VrcxScenario.EnsureSuccessAsync(response, what);
        return response;
    }

    [Test]
    public async Task Saving_an_avatar_sends_the_id_with_only_the_name_description_or_visibility_given()
    {
        await using var app = await VrcxScenario.StartAsync();
        var me = app.Vrchat.SetMe("usr_avatar_owner", "Owner");
        app.Vrchat.Avatar("avtr_edit", "Old", a => a.Author(me.Id, me.DisplayName));
        app.Vrchat.Json("PUT", "avatars/avtr_edit", """{"id":"avtr_edit","name":"Old","authorId":"usr_avatar_owner"}""");
        await app.LoginAsync();
        var path = app.AccountPath("avatars/avtr_edit");

        await OkAsync(app.Api.PutAsJsonAsync(path, new { name = "New Name" }), "rename");
        await Assert.That(app.Vrchat.AssertCalled("PUT", "avatars/avtr_edit").Json!.ToJsonString()).IsEqualTo("""{"id":"avtr_edit","name":"New Name"}""");
        await OkAsync(app.Api.PutAsJsonAsync(path, new { description = "About it" }), "description");
        await Assert.That(app.Vrchat.AssertCalled("PUT", "avatars/avtr_edit").Json!.ToJsonString()).IsEqualTo("""{"id":"avtr_edit","description":"About it"}""");
        await OkAsync(app.Api.PutAsJsonAsync(path, new { releaseStatus = "public" }), "make public");
        await Assert.That(app.Vrchat.AssertCalled("PUT", "avatars/avtr_edit").Json!.ToJsonString()).IsEqualTo("""{"id":"avtr_edit","releaseStatus":"public"}""");
        await OkAsync(app.Api.PutAsJsonAsync(path, new { releaseStatus = "private" }), "make private");
        await Assert.That(app.Vrchat.AssertCalled("PUT", "avatars/avtr_edit").Json!.ToJsonString()).IsEqualTo("""{"id":"avtr_edit","releaseStatus":"private"}""");
    }

    [Test]
    public async Task Fallback_select_and_impostor_commands_reach_vrchat()
    {
        await using var app = await VrcxScenario.StartAsync();
        var me = app.Vrchat.SetMe("usr_avatar_cmds", "Cmds");
        app.Vrchat.Avatar("avtr_cmd", "Cmd", a => a.Author(me.Id, me.DisplayName));
        await app.LoginAsync();

        await OkAsync(app.Api.PostAsync(app.AccountPath("avatars/avtr_cmd/fallback"), null), "fallback");
        await OkAsync(app.Api.PostAsync(app.AccountPath("avatars/avtr_cmd/select"), null), "select");
        await OkAsync(app.Api.PostAsync(app.AccountPath("avatars/avtr_cmd/impostor"), null), "create impostor");
        await OkAsync(app.Api.DeleteAsync(app.AccountPath("avatars/avtr_cmd/impostor")), "delete impostor");

        app.Vrchat.AssertCalled("PUT", "avatars/avtr_cmd/selectfallback");
        app.Vrchat.AssertCalled("PUT", "avatars/avtr_cmd/select");
        app.Vrchat.AssertCalled("POST", "avatars/avtr_cmd/impostor/enqueue");
        app.Vrchat.AssertCalled("DELETE", "avatars/avtr_cmd/impostor");
    }

    // The original's Delete removes the avatar from its cache, so the author's avatar lists stop
    // showing it at once.
    [Test]
    public async Task A_deleted_own_avatar_reaches_vrchat_and_leaves_my_avatars()
    {
        await using var app = await VrcxScenario.StartAsync();
        var me = app.Vrchat.SetMe("usr_avatar_deleter", "Deleter");
        app.Vrchat.Avatar("avtr_keep", "Keep", a => a.Author(me.Id, me.DisplayName));
        app.Vrchat.Avatar("avtr_gone", "Gone", a => a.Author(me.Id, me.DisplayName));
        await app.LoginAsync();
        (await app.Api.GetAsync(app.AccountPath("avatars"))).EnsureSuccessStatusCode();

        await OkAsync(app.Api.DeleteAsync(app.AccountPath("avatars/avtr_gone")), "delete");

        app.Vrchat.AssertCalled("DELETE", "avatars/avtr_gone");
        var own = (await app.GetJsonAsync(app.AccountPath("avatars"))).EnumerateArray().Select(a => a.GetProperty("avatar").GetProperty("id").GetString()!).ToList();
        await Assert.That(own).IsEquivalentTo(new[] { "avtr_keep" });
    }

    [Test]
    public async Task Saving_a_world_sends_the_id_with_the_text_and_the_capacities_as_numbers()
    {
        await using var app = await VrcxScenario.StartAsync();
        var me = app.Vrchat.SetMe("usr_world_owner", "Owner");
        app.Vrchat.World("wrld_edit", "Edit", w => w.Author(me.Id, me.DisplayName));
        app.Vrchat.Json("PUT", "worlds/wrld_edit", """{"id":"wrld_edit","name":"Edit","authorId":"usr_world_owner"}""");
        await app.LoginAsync();
        var path = app.AccountPath("worlds/wrld_edit");

        await OkAsync(app.Api.PutAsJsonAsync(path, new { description = "A place" }), "description");
        await Assert.That(app.Vrchat.AssertCalled("PUT", "worlds/wrld_edit").Json!.ToJsonString()).IsEqualTo("""{"id":"wrld_edit","description":"A place"}""");
        await OkAsync(app.Api.PutAsJsonAsync(path, new { capacity = 32 }), "capacity");
        await Assert.That(app.Vrchat.AssertCalled("PUT", "worlds/wrld_edit").Json!.ToJsonString()).IsEqualTo("""{"id":"wrld_edit","capacity":32}""");
        await OkAsync(app.Api.PutAsJsonAsync(path, new { recommendedCapacity = 16 }), "recommended capacity");
        await Assert.That(app.Vrchat.AssertCalled("PUT", "worlds/wrld_edit").Json!.ToJsonString()).IsEqualTo("""{"id":"wrld_edit","recommendedCapacity":16}""");
        // Clearing the YouTube preview sends the empty id rather than leaving the field out.
        await OkAsync(app.Api.PutAsJsonAsync(path, new { previewYoutubeId = "" }), "clear preview");
        await Assert.That(app.Vrchat.AssertCalled("PUT", "worlds/wrld_edit").Json!.ToJsonString()).IsEqualTo("""{"id":"wrld_edit","previewYoutubeId":""}""");
    }

    [Test]
    public async Task Publishing_unpublishing_and_deleting_saved_data_reach_vrchat()
    {
        await using var app = await VrcxScenario.StartAsync();
        var me = app.Vrchat.SetMe("usr_world_pub", "Pub");
        app.Vrchat.World("wrld_pub", "Pub", w => w.Author(me.Id, me.DisplayName));
        await app.LoginAsync();

        await OkAsync(app.Api.PutAsync(app.AccountPath("worlds/wrld_pub/publish"), null), "publish");
        await OkAsync(app.Api.DeleteAsync(app.AccountPath("worlds/wrld_pub/publish")), "unpublish");
        await OkAsync(app.Api.DeleteAsync(app.AccountPath("worlds/wrld_pub/persist")), "delete persist");

        app.Vrchat.AssertCalled("PUT", "worlds/wrld_pub/publish");
        app.Vrchat.AssertCalled("DELETE", "worlds/wrld_pub/publish");
        app.Vrchat.AssertCalled("DELETE", $"users/{me.Id}/wrld_pub/persist");
    }

    [Test]
    public async Task Resetting_home_sends_an_empty_home_location_and_clears_the_stored_one()
    {
        await using var app = await VrcxScenario.StartAsync();
        var me = app.Vrchat.SetMe("usr_home_reset", "Home", u => u.Set("homeLocation", "wrld_home"));
        await app.LoginAsync();
        await using (var db = app.CreateDbContext())
            await db.Accounts.Where(a => a.UserId == me.Id).ExecuteUpdateAsync(s => s.SetProperty(a => a.HomeLocation, "wrld_home"));

        await OkAsync(app.Api.DeleteAsync(app.AccountPath("home")), "reset home");

        await Assert.That(app.Vrchat.AssertCalled("PUT", $"users/{me.Id}").Json!["homeLocation"]!.GetValue<string>()).IsEqualTo("");
        await using var check = app.CreateDbContext();
        await Assert.That((await check.Accounts.AsNoTracking().SingleAsync(a => a.UserId == me.Id)).HomeLocation).IsEqualTo("");
    }

    [Test]
    public async Task The_own_row_carries_the_account_standing_and_none_when_vrchat_sends_it_empty()
    {
        await using var app = await VrcxScenario.StartAsync();
        var me = app.Vrchat.SetMe("usr_standing", "Standing", u => u.Set("accountStanding", "good"));
        await app.LoginAsync();

        await Assert.That((await app.GetJsonAsync(app.AccountPath("me"))).GetProperty("accountStanding").GetString()).IsEqualTo("good");
        me.Set("accountStanding", "");
        var row = await app.GetJsonAsync(app.AccountPath("me"));
        await Assert.That(row.TryGetProperty("accountStanding", out var standing) && standing.ValueKind != JsonValueKind.Null).IsFalse();
    }

    [Test]
    public async Task Info_push_purchases_and_credit_transactions_pass_vrchats_answer_through()
    {
        await using var app = await VrcxScenario.StartAsync();
        var me = app.Vrchat.SetMe("usr_economy", "Economy");
        app.Vrchat
            .Json("infoPush?require=user-all&include=help-article&include=quick-menu-banner&include=loading-screen-banners", """[{"id":"ip_1","name":"Banner","data":{"article":{"content":{"text":"hello"}}}}]""")
            .Json($"economy/purchases?buyerId={me.Id}&order=desc&sort=purchaseDate&n=50&offset=0", """[{"purchaseId":"pur_1","listingDisplayName":"Hat","purchasePrice":200,"purchaseActive":true}]""")
            .Json($"user/{me.Id}/economy/transactions?n=100", """{"transactions":[{"transactionLineId":"tx_1","listingDisplayName":"Hat","amount":-200,"balance":800}]}""");
        await app.LoginAsync();

        var info = await app.GetJsonAsync(app.AccountPath("info-push"));
        await Assert.That(info[0].GetProperty("data").GetProperty("article").GetProperty("content").GetProperty("text").GetString()).IsEqualTo("hello");
        var purchases = await app.GetJsonAsync(app.AccountPath("purchases"));
        await Assert.That((purchases[0].GetProperty("purchaseId").GetString(), purchases[0].GetProperty("purchasePrice").GetDecimal())).IsEqualTo(("pur_1", 200m));
        var transactions = await app.GetJsonAsync(app.AccountPath("economy-transactions"));
        var line = transactions.GetProperty("transactions")[0];
        await Assert.That((line.GetProperty("transactionLineId").GetString(), line.GetProperty("amount").GetDecimal(), line.GetProperty("balance").GetDecimal())).IsEqualTo(("tx_1", -200m, 800m));
    }

    [Test]
    public async Task A_changed_bio_on_refresh_is_a_feed_entry_with_the_previous_bio_and_both_link_lists()
    {
        await using var app = await VrcxScenario.StartAsync();
        var alice = app.Vrchat.Friend("usr_bio_alice", "Alice", u => u.Bio("old bio", "https://old.example"));
        await app.LoginAsync();
        await OkAsync(app.Api.PostAsync(app.AccountPath($"friends/{alice.Id}/load"), null), "first load");

        alice.Bio("new bio", "https://new.example");
        await OkAsync(app.Api.PostAsync(app.AccountPath($"friends/{alice.Id}/load"), null), "second load");

        var bio = (await app.GetJsonAsync(app.AccountPath("feed?types=Bio"))).EnumerateArray().Single(e => e.GetProperty("userId").GetString() == alice.Id);
        await Assert.That(bio.GetProperty("bio").GetString()).IsEqualTo("new bio");
        await Assert.That(bio.GetProperty("previousBio").GetString()).IsEqualTo("old bio");
        await Assert.That(bio.GetProperty("bioLinks")[0].GetString()).IsEqualTo("https://new.example");
        await Assert.That(bio.GetProperty("previousBioLinks")[0].GetString()).IsEqualTo("https://old.example");
    }
}
