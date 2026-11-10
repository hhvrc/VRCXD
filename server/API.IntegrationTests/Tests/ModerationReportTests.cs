using System.Net.Http.Json;
using System.Text.Json.Nodes;
using VRCX.Testing;

namespace VRCX.API.IntegrationTests.Tests;

// The original's ModerationReportDialog: options from VRChat's config, and the report it posts.
public sealed class ModerationReportTests
{
    [Test]
    public async Task Report_options_list_each_types_categories_with_their_reasons_order_and_tooltips()
    {
        await using var app = await VrcxScenario.StartAsync();
        app.Vrchat.Json("config", """
            {"reportOptions":{"user":{"behavior":["harassment","spam"]}},
             "reportCategories":{"behavior":{"text":"Behavior","order":2,"tooltip":"How they act"}},
             "reportReasons":{"harassment":{"text":"Harassment","tooltip":"Targeting someone"},"spam":{"text":"Spam"}}}
            """);
        await app.LoginAsync();

        var options = await app.GetJsonAsync(app.AccountPath("moderation-reports/options"));

        var reasons = options.GetProperty("options").GetProperty("user").GetProperty("behavior");
        await Assert.That(reasons.GetArrayLength()).IsEqualTo(2);
        await Assert.That(options.GetProperty("categories").GetProperty("behavior").GetString()).IsEqualTo("Behavior");
        await Assert.That(options.GetProperty("categoryOrder").GetProperty("behavior").GetInt32()).IsEqualTo(2);
        await Assert.That(options.GetProperty("categoryTooltips").GetProperty("behavior").GetString()).IsEqualTo("How they act");
        await Assert.That(options.GetProperty("reasonTooltips").GetProperty("harassment").GetString()).IsEqualTo("Targeting someone");
    }

    [Test]
    public async Task A_report_posts_the_trimmed_description_and_leaves_out_an_empty_one()
    {
        await using var app = await VrcxScenario.StartAsync();
        app.Vrchat.Json("POST", "moderationReports", """{"id":"rpt_1"}""");
        await app.LoginAsync();

        var first = await app.Api.PostAsJsonAsync(app.AccountPath("moderation-reports"), new { type = "user", category = "behavior", reason = "spam", contentId = "usr_bad", description = "  sends links  " });
        await Assert.That(first.IsSuccessStatusCode).IsTrue();
        var sent = JsonNode.Parse(app.Vrchat.Bodies["POST moderationReports"])!;
        await Assert.That((string?)sent["contentId"]).IsEqualTo("usr_bad");
        await Assert.That((string?)sent["description"]).IsEqualTo("sends links");

        await app.Api.PostAsJsonAsync(app.AccountPath("moderation-reports"), new { type = "user", category = "behavior", reason = "spam", contentId = "usr_bad", description = "   " });
        var second = JsonNode.Parse(app.Vrchat.Bodies["POST moderationReports"])!;
        await Assert.That(second["description"]).IsNull();
    }
}
