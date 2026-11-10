using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VRCX.Common.VrcxDb;

namespace VRCX.API.IntegrationTests.Tests;

// The Previous Instances dialogs, over stored visits. Needs no VRChat.
public sealed class PreviousInstanceTests
{
    private const string Account = "usr_previous_account";
    private const string Friend = "usr_previous_friend";
    private const string WorldId = "wrld_previous";
    private const string Location = WorldId + ":12345~hidden(usr_previous_friend)~region(eu)";
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [ClassDataSource<IsolatedHost>(Shared = SharedType.None)]
    public required IsolatedHost Factory { get; init; }

    private async Task SeedAsync()
    {
        await using var db = await Factory.Services.GetRequiredService<IDbContextFactory<VrcxContext>>().CreateDbContextAsync();
        if (await db.Instances.AnyAsync(i => i.Location == Location)) return;
        db.Users.Add(new User { Id = Account, DisplayName = "Me", FirstSeenAt = Start });
        db.Users.Add(new User { Id = Friend, DisplayName = "Friend", FirstSeenAt = Start });
        db.Worlds.Add(new World { Id = WorldId, Name = "Place", FirstSeenAt = Start });
        db.Instances.Add(new Instance { Location = Location, WorldId = WorldId, InstanceId = "12345~hidden(usr_previous_friend)~region(eu)", InstanceName = "12345", Type = InstanceAccessType.Hidden, OwnerUserId = Friend });
        InstanceVisit Stay(string user, int from, int? to) => new() { UserId = user, InstanceLocation = Location, JoinedAt = Start.AddMinutes(from), LeftAt = to is { } m ? Start.AddMinutes(m) : null, Source = VisitSource.GameLog };
        db.InstanceVisits.AddRange(
            Stay(Account, 0, 60),
            Stay(Account, 0, 60), // the same stay logged twice
            Stay(Friend, 10, 20),
            Stay(Friend, 30, 40),
            Stay(Friend, 50, null));
        await db.SaveChangesAsync();
    }

    [Test]
    public async Task A_world_lists_the_accounts_instance_once_with_repeats_not_counted()
    {
        await SeedAsync();
        using var client = Factory.CreateAuthenticatedClient();

        var rows = await client.GetFromJsonAsync<JsonElement>($"/1/accounts/{Account}/previous-instances?worldId={WorldId}");

        await Assert.That(rows.GetArrayLength()).IsEqualTo(1);
        await Assert.That(rows[0].GetProperty("timeMs").GetInt64()).IsEqualTo(3_600_000);
        await Assert.That(rows[0].GetProperty("ownerName").GetString()).IsEqualTo("Friend");
    }

    [Test]
    public async Task A_user_lists_their_own_stays()
    {
        await SeedAsync();
        using var client = Factory.CreateAuthenticatedClient();

        var rows = await client.GetFromJsonAsync<JsonElement>($"/1/accounts/{Account}/previous-instances?userId={Friend}");

        await Assert.That(rows[0].GetProperty("timeMs").GetInt64()).IsEqualTo(1_200_000);
    }

    // Upstream PR 1305: a 24/7 instance visited on different days is a row per session, not one row.
    [Test]
    public async Task A_user_in_a_24_7_instance_gets_one_row_per_session()
    {
        await SeedAsync();
        const string Regular = "usr_previous_regular";
        const string Lounge = WorldId + ":247~region(eu)";
        await using (var db = await Factory.Services.GetRequiredService<IDbContextFactory<VrcxContext>>().CreateDbContextAsync())
        {
            if (!await db.Users.AnyAsync(u => u.Id == Regular))
            {
                db.Users.Add(new User { Id = Regular, DisplayName = "Regular", FirstSeenAt = Start });
                db.Instances.Add(new Instance { Location = Lounge, WorldId = WorldId, InstanceId = "247~region(eu)", InstanceName = "247", Type = InstanceAccessType.Public });
                InstanceVisit Stay(int fromMinutes, int toMinutes) => new() { UserId = Regular, InstanceLocation = Lounge, JoinedAt = Start.AddMinutes(fromMinutes), LeftAt = Start.AddMinutes(toMinutes), Source = VisitSource.GameLog };
                db.InstanceVisits.AddRange(Stay(0, 30), Stay(40, 60), Stay(2 * 24 * 60, 2 * 24 * 60 + 15));
                await db.SaveChangesAsync();
            }
        }
        using var client = Factory.CreateAuthenticatedClient();

        var rows = (await client.GetFromJsonAsync<JsonElement>($"/1/accounts/{Account}/previous-instances?userId={Regular}")).EnumerateArray().ToList();

        await Assert.That(rows.Count).IsEqualTo(2);
        await Assert.That(rows[0].GetProperty("createdAt").GetDateTimeOffset()).IsEqualTo(Start.AddDays(2));
        await Assert.That(rows[0].GetProperty("timeMs").GetInt64()).IsEqualTo(15 * 60_000);
        await Assert.That(rows[1].GetProperty("createdAt").GetDateTimeOffset()).IsEqualTo(Start);
        await Assert.That(rows[1].GetProperty("timeMs").GetInt64()).IsEqualTo(50 * 60_000);
    }

    [Test]
    public async Task Players_total_their_stays_and_an_open_stay_adds_no_time()
    {
        await SeedAsync();
        using var client = Factory.CreateAuthenticatedClient();

        var response = await client.GetFromJsonAsync<JsonElement>($"/1/accounts/{Account}/previous-instances/players?location={Uri.EscapeDataString(Location)}");

        var friend = response.GetProperty("players").EnumerateArray().First(p => p.GetProperty("userId").GetString() == Friend);
        await Assert.That(friend.GetProperty("count").GetInt32()).IsEqualTo(3);
        await Assert.That(friend.GetProperty("timeMs").GetInt64()).IsEqualTo(1_200_000);
        await Assert.That(response.GetProperty("stays").GetArrayLength()).IsEqualTo(4);
    }

    [Test]
    public async Task No_filter_gives_nothing()
    {
        await SeedAsync();
        using var client = Factory.CreateAuthenticatedClient();

        var response = await client.GetAsync($"/1/accounts/{Account}/previous-instances");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That((await response.Content.ReadFromJsonAsync<JsonElement>()).GetArrayLength()).IsEqualTo(0);
    }
}
