using System.Net;
using System.Text.Json;
using VRCX.Common.VrcxDb;
using VRCX.Testing;

namespace VRCX.API.IntegrationTests.Tests;

// The friend log pages in SQL. Pages put back together must be the whole log exactly: every rule
// that drops a row (the first sync's friends, changes outside a friendship, the veteran toggle,
// rows the account hid) has to hold page by page, and no entry may fall between two pages.
public sealed class FriendLogPagingTests
{
    private static async Task<string[]> LogAsync(VrcxScenario app, string query = "")
    {
        var log = await app.GetJsonAsync(app.AccountPath("friend-log" + query));
        return log.EnumerateArray().Select(e => e.GetRawText()).ToArray();
    }

    private static async Task SeedHistoryAsync(VrcxScenario app)
    {
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        await using var db = app.CreateDbContext();
        var users = Enumerable.Range(0, 12).Select(i => new User { Id = $"usr_page{i:D2}", DisplayName = $"Now {i}" }).ToList();
        db.Users.AddRange(users);
        // Three at the first sync's instant (no entries), then friends at their own times, two of
        // them at the same moment, some since ended.
        for (var i = 0; i < users.Count; i++)
        {
            var started = i < 3 ? start : start.AddDays(i < 6 ? 5 : i);
            db.Friendships.Add(new Friendship
            {
                OwnerUserId = app.AccountId!, FriendUserId = users[i].Id, FriendNumber = i + 1, StartedAt = started, LastConfirmedAt = started,
                EndedAt = i % 4 == 3 ? started.AddDays(20) : null,
            });
            // A rename inside the friendship, one before it, and one on the same day as another entry.
            db.UserDisplayNameChanges.AddRange(
                new UserDisplayNameChange { UserId = users[i].Id, ChangedAt = started.AddDays(-1), PreviousDisplayName = $"Older {i}", DisplayName = $"Then {i}" },
                new UserDisplayNameChange { UserId = users[i].Id, ChangedAt = started.AddDays(2), PreviousDisplayName = $"Then {i}", DisplayName = $"Now {i}" });
            db.UserTrustChanges.AddRange(
                new UserTrustChange { UserId = users[i].Id, ChangedAt = started.AddDays(3), PreviousTrustLevel = "Known User", TrustLevel = "Trusted User" },
                new UserTrustChange { UserId = users[i].Id, ChangedAt = started.AddDays(4), PreviousTrustLevel = "Trusted User", TrustLevel = "Veteran User" });
        }
        for (var i = 0; i < 6; i++)
            db.FriendRequestEvents.Add(new FriendRequestEvent
            {
                OwnerUserId = app.AccountId!, TargetUserId = $"usr_asked{i}", DisplayName = $"Asked {i}",
                Type = i % 2 == 0 ? "FriendRequest" : "CancelFriendRequest", CreatedAt = start.AddDays(5 + i / 2),
            });
        await db.SaveChangesAsync();
    }

    [Test]
    public async Task Pages_put_together_are_the_whole_log()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        await SeedHistoryAsync(app);
        var whole = await LogAsync(app);
        // Hide a few rows of different kinds, which a page must not count.
        foreach (var raw in whole.Where((_, i) => i % 5 == 1).Take(4))
        {
            var key = JsonDocument.Parse(raw).RootElement.GetProperty("key").GetString()!;
            var hide = await app.Api.DeleteAsync(app.AccountPath($"friend-log/{Uri.EscapeDataString(key)}"));
            await Assert.That(hide.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        }
        whole = await LogAsync(app);
        await Assert.That(whole.Length).IsGreaterThan(30);

        foreach (var limit in new[] { 1, 2, 3, 7, 1000 })
        {
            var pages = new List<string>();
            string? before = null;
            for (var n = 0; n < 200; n++)
            {
                var page = await LogAsync(app, $"?limit={limit}" + (before is null ? "" : $"&before={Uri.EscapeDataString(before)}"));
                if (page.Length == 0) break;
                // At least a page, ties at its end aside; only the last may be short.
                if (pages.Count + page.Length < whole.Length) await Assert.That(page.Length).IsGreaterThanOrEqualTo(limit);
                pages.AddRange(page);
                before = JsonDocument.Parse(page[^1]).RootElement.GetProperty("createdAt").GetString();
            }
            await Assert.That(pages).IsEquivalentTo(whole, TUnit.Assertions.Enums.CollectionOrdering.Matching).Because($"limit {limit}");
        }
    }

    [Test]
    public async Task A_page_keeps_every_entry_at_its_oldest_time()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        await SeedHistoryAsync(app);

        // Day 5 has three friendships and two requests; a page of one that reaches it holds all five.
        var whole = (await LogAsync(app)).Select(r => JsonDocument.Parse(r).RootElement).ToArray();
        var day5 = new DateTimeOffset(2026, 1, 6, 0, 0, 0, TimeSpan.Zero);
        var before = whole.Where(e => e.GetProperty("createdAt").GetDateTimeOffset() > day5).Min(e => e.GetProperty("createdAt").GetDateTimeOffset());
        var page = (await LogAsync(app, $"?limit=1&before={Uri.EscapeDataString(before.ToString("O"))}")).Select(r => JsonDocument.Parse(r).RootElement).ToArray();

        await Assert.That(page.Select(e => e.GetProperty("createdAt").GetDateTimeOffset()).Distinct()).IsEquivalentTo([day5]);
        await Assert.That(page.Length).IsEqualTo(whole.Count(e => e.GetProperty("createdAt").GetDateTimeOffset() == day5));
        await Assert.That(page.Length).IsEqualTo(5);
    }
}
