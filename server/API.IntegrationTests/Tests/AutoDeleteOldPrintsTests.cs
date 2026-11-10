using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using VRCX.Common.Services.Automation;
using VRCX.Testing;

namespace VRCX.API.IntegrationTests.Tests;

// The original's tryDeleteOldPrints: with Auto Delete Old Prints on, prints past the limit less two
// go, oldest first, and favorites are skipped.
public sealed class AutoDeleteOldPrintsTests
{
    [Test]
    public async Task Old_prints_past_the_limit_are_deleted_oldest_first_except_favorites()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        var account = app.AccountId!;
        app.Vrchat
            .Json($"prints/user/{account}?n=100", """
                [{"id":"prnt_1","timestamp":"2026-01-01T00:00:00Z"},{"id":"prnt_2","timestamp":"2026-02-01T00:00:00Z"},
                 {"id":"prnt_3","timestamp":"2026-03-01T00:00:00Z"},{"id":"prnt_4","timestamp":"2026-04-01T00:00:00Z"},
                 {"id":"prnt_5","timestamp":"2026-05-01T00:00:00Z"}]
                """)
            .Json("auth/permissions", """[{"name":"permission-user-prints","data":{"max":5}}]""")
            .Json("DELETE", "prints/prnt_2", "{}")
            .Json("DELETE", "prints/prnt_3", "{}");
        var automation = app.Services.GetRequiredService<AccountAutomation>();

        // Off by default.
        await Assert.That((await automation.DeleteOldPrintsAsync(account, CancellationToken.None)).Count).IsEqualTo(0);

        await app.Api.PutAsync($"/1/accounts/{account}/gallery/prints/prnt_1/favorite", null);
        await app.Api.PutAsJsonAsync($"/1/accounts/{account}/settings", new { autoDeleteOldPrints = true });
        var deleted = await automation.DeleteOldPrintsAsync(account, CancellationToken.None);

        // Five prints, room for three: two go, and the oldest is a favorite.
        await Assert.That(deleted).IsEquivalentTo(["prnt_2", "prnt_3"]);
        app.Vrchat.AssertNotCalled("DELETE", "prints/prnt_1");
    }
}
