using Microsoft.EntityFrameworkCore;
using VRCX.API.Services;
using VRCX.Common.VrcxDb;
using VRCX.Testing;

namespace VRCX.API.IntegrationTests.Tests;

// The loops that fill in stored rows in the background, one VRChat request at a time: what they
// pick, and how they keep off the rows VRChat will not serve.
public sealed class BackgroundRefreshTests
{
    private static async Task SeedStubWorldsAsync(VrcxScenario app, params string[] ids)
    {
        await using var db = app.CreateDbContext();
        var at = app.Time.GetUtcNow();
        for (var i = 0; i < ids.Length; i++)
            // Seen one after the other, the last newest.
            db.Worlds.Add(new World { Id = ids[i], Name = ids[i], IsStub = true, FirstSeenAt = at.AddMinutes(i - ids.Length) });
        await db.SaveChangesAsync();
    }

    private static int Fetches(VrcxScenario app, string worldId) => app.Vrchat.CountOf("GET", $"worlds/{worldId}");

    // ---- stub worlds and groups ----------------------------------------------

    [Test]
    public async Task Stubs_are_tried_never_tried_first_then_the_ones_tried_longest_ago()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        var ids = Enumerable.Range(0, 12).Select(i => $"wrld_stub{i:D2}").ToArray();
        await SeedStubWorldsAsync(app, ids);
        app.Vrchat.ClearRequests();

        // Never tried, newest first: 11..4, then 3..0.
        await app.Jobs.StubRefreshAsync();
        foreach (var id in ids[4..]) await Assert.That(Fetches(app, id)).IsEqualTo(1);
        foreach (var id in ids[..4]) await Assert.That(Fetches(app, id)).IsEqualTo(0);
        app.Time.Advance(TimeSpan.FromMinutes(1));
        await app.Jobs.StubRefreshAsync();
        foreach (var id in ids[..4]) await Assert.That(Fetches(app, id)).IsEqualTo(1);

        // An hour on, all are due again: the eight tried first go first, as they waited longest.
        app.Time.Advance(StubRefreshService.FirstRetry);
        app.Vrchat.ClearRequests();
        var third = app.Time.GetUtcNow().AddMilliseconds(-1);
        await app.Jobs.StubRefreshAsync();
        foreach (var id in ids[4..]) await Assert.That(Fetches(app, id)).IsEqualTo(1);
        foreach (var id in ids[..4]) await Assert.That(Fetches(app, id)).IsEqualTo(0);

        await using var db = app.CreateDbContext();
        var tried = await db.Worlds.Where(w => w.Id == ids[11]).Select(w => new { w.StubAttemptedAt, w.StubRetryAt }).SingleAsync();
        await Assert.That(tried.StubAttemptedAt!.Value).IsGreaterThan(third);
        await Assert.That(tried.StubRetryAt - tried.StubAttemptedAt).IsEqualTo(StubRefreshService.FirstRetry * 2);
    }

    [Test]
    public async Task A_stub_vrchat_does_not_serve_waits_longer_after_each_try()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        await SeedStubWorldsAsync(app, "wrld_stub_dead");
        app.Vrchat.ClearRequests();

        async Task TickAsync(TimeSpan after, int fetchedSoFar)
        {
            app.Time.Advance(after);
            await app.Jobs.StubRefreshAsync();
            await Assert.That(Fetches(app, "wrld_stub_dead")).IsEqualTo(fetchedSoFar);
        }

        await TickAsync(TimeSpan.Zero, 1);
        // An hour after the first try, two after the second, four after the third.
        foreach (var (wait, n) in new[] { (TimeSpan.FromHours(1), 2), (TimeSpan.FromHours(2), 3), (TimeSpan.FromHours(4), 4) })
        {
            await TickAsync(wait - TimeSpan.FromMinutes(2), n - 1);
            // Each try lands a minute after it is due, so the next tick is a minute before the next.
            await TickAsync(TimeSpan.FromMinutes(3), n);
        }
    }

    [Test]
    public async Task A_filled_in_stub_is_no_longer_tried()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        app.Vrchat.World("wrld_stub_alive", "Alive");
        await SeedStubWorldsAsync(app, "wrld_stub_alive");

        await Assert.That(await app.Jobs.StubRefreshAsync()).IsEqualTo(1);
        app.Time.Advance(StubRefreshService.MaxRetry);
        app.Vrchat.ClearRequests();
        await Assert.That(await app.Jobs.StubRefreshAsync()).IsEqualTo(0);

        app.Vrchat.AssertNotCalled("GET", "worlds/wrld_stub_alive");
        await using var db = app.CreateDbContext();
        await Assert.That((await db.Worlds.SingleAsync(w => w.Id == "wrld_stub_alive")).IsStub).IsFalse();
    }

    [Test]
    public async Task The_wait_doubles_from_an_hour_up_to_a_week()
    {
        var waits = new List<TimeSpan>();
        TimeSpan? last = null;
        for (var i = 0; i < 12; i++) waits.Add((last = StubRefreshService.NextRetry(last)).Value);

        await Assert.That(waits[0]).IsEqualTo(TimeSpan.FromHours(1));
        await Assert.That(waits[1]).IsEqualTo(TimeSpan.FromHours(2));
        await Assert.That(waits[7]).IsEqualTo(TimeSpan.FromHours(128));
        await Assert.That(waits[8]).IsEqualTo(TimeSpan.FromDays(7));
        await Assert.That(waits[11]).IsEqualTo(TimeSpan.FromDays(7));
    }

    // ---- friends' profiles -----------------------------------------------------

    [Test]
    public async Task A_friends_profile_week_is_fixed_by_their_id_and_spread_over_two_days()
    {
        var ids = Enumerable.Range(0, 1000).Select(i => $"usr_{Guid.NewGuid()}").ToArray();
        var ages = ids.Select(ProfileRefreshService.StaleAfterFor).ToArray();

        // The same every time it is asked (and in every process: it is not string.GetHashCode).
        await Assert.That(ids.Select(ProfileRefreshService.StaleAfterFor)).IsEquivalentTo(ages);
        await Assert.That(ProfileRefreshService.StaleAfterFor("usr_c1644b5b-3ca4-45b4-97c6-a2a0de70d469"))
            .IsEqualTo(ProfileRefreshService.StaleAfterFor("usr_c1644b5b-3ca4-45b4-97c6-a2a0de70d469"));

        await Assert.That(ages.Min()).IsGreaterThanOrEqualTo(TimeSpan.FromDays(6));
        await Assert.That(ages.Max()).IsLessThanOrEqualTo(TimeSpan.FromDays(8));
        // Spread: every quarter-day of the two days gets some of them.
        var buckets = ages.GroupBy(a => (int)((a - TimeSpan.FromDays(6)).TotalHours / 6)).ToDictionary(g => g.Key, g => g.Count());
        for (var b = 0; b < 8; b++) await Assert.That(buckets.GetValueOrDefault(b)).IsGreaterThan(50);
    }

    [Test]
    public async Task The_profile_loop_takes_never_fetched_friends_first_then_only_those_past_their_own_week()
    {
        await using var app = await VrcxScenario.StartAsync();
        var fresh = app.Vrchat.Friend("usr_profile_fresh", "Fresh");
        var never = app.Vrchat.Friend("usr_profile_never", "Never");
        var due = app.Vrchat.Friend("usr_profile_due", "Due");
        await app.LoginAsync();
        var now = DateTimeOffset.UtcNow;
        await using (var db = app.CreateDbContext())
        {
            await db.Users.Where(u => u.Id == fresh.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.ProfileFetchedAt, now - ProfileRefreshService.StaleAfterFor(fresh.Id) + TimeSpan.FromHours(1)));
            await db.Users.Where(u => u.Id == never.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.ProfileFetchedAt, (DateTimeOffset?)null));
            await db.Users.Where(u => u.Id == due.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.ProfileFetchedAt, now - ProfileRefreshService.StaleAfterFor(due.Id) - TimeSpan.FromHours(1)));
        }
        var loop = Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance<ProfileRefreshService>(app.Services);

        await Assert.That(await loop.RefreshOneAsync(app.AccountId!, CancellationToken.None)).IsEqualTo(never.Id);
        await Assert.That(await loop.RefreshOneAsync(app.AccountId!, CancellationToken.None)).IsEqualTo(due.Id);
        await Assert.That(await loop.RefreshOneAsync(app.AccountId!, CancellationToken.None)).IsNull();
    }
}
