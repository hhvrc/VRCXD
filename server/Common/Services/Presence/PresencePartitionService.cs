using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VRCX.Common.VrcxDb;

namespace VRCX.Common.Services.Presence;

/// <summary>
/// Keeps monthly presence_events partitions ready ahead of time. Rows that landed in the default
/// partition for a month that had none are moved into the new one, so nothing is left behind.
/// </summary>
public sealed class PresencePartitionService(IServiceProvider services, ILogger<PresencePartitionService> logger) : BackgroundService
{
    private const int MonthsAhead = 2;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(12));
        do
        {
            try
            {
                await EnsurePartitionsAsync(services, DateTimeOffset.UtcNow, stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                // Nothing is lost: until this works, new rows go to the default partition.
                logger.LogError(ex, "Could not create presence_events partitions");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    public static async Task EnsurePartitionsAsync(IServiceProvider services, DateTimeOffset now, CancellationToken ct)
    {
        await using var db = await services.GetRequiredService<IDbContextFactory<VrcxContext>>().CreateDbContextAsync(ct);
        var month = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        for (var i = 0; i <= MonthsAhead; i++) await EnsureMonthAsync(db, month.AddMonths(i), ct);
    }

    /// <summary>Creates the partition for the month <paramref name="at"/> falls in, if it has none. Also used by the legacy importer for past months.</summary>
    public static async Task EnsureMonthAsync(VrcxContext db, DateTimeOffset at, CancellationToken ct)
    {
        var utc = at.ToUniversalTime();
        var from = new DateTimeOffset(utc.Year, utc.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var to = from.AddMonths(1);
        var name = $"presence_events_y{from:yyyy}m{from:MM}";
        var bounds = $"at >= '{from:yyyy-MM-dd}+00' AND at < '{to:yyyy-MM-dd}+00'";
        var sql = $"""
            DO $$
            BEGIN
                IF to_regclass('{name}') IS NULL THEN
                    CREATE TABLE {name} (LIKE presence_events INCLUDING DEFAULTS INCLUDING CONSTRAINTS);
                    WITH moved AS (DELETE FROM presence_events_default WHERE {bounds} RETURNING *)
                    INSERT INTO {name} SELECT * FROM moved;
                    ALTER TABLE presence_events ATTACH PARTITION {name} FOR VALUES FROM ('{from:yyyy-MM-dd}+00') TO ('{to:yyyy-MM-dd}+00');
                END IF;
            END $$;
            """;
        await db.Database.ExecuteSqlRawAsync(sql, ct);
    }
}
