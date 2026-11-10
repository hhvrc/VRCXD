using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace VRCX.Common.HealthChecks;

/// <summary>Tags used to select subsets of the registered health checks.</summary>
public static class HealthCheckTags
{
    /// <summary>A dependency the host needs before it can actually serve (Postgres).</summary>
    public const string Ready = "ready";
}

/// <summary>Names the individual health checks are registered under; they show up verbatim in the health payload.</summary>
public static class HealthCheckNames
{
    public const string Database = "database";
    public const string Accounts = "accounts";
}

/// <summary>Path the health endpoint is served on.</summary>
public static class HealthCheckPaths
{
    public const string Health = "/1/health";
}

/// <summary>
/// Body written by the health endpoint: the per-check breakdown rather than the default
/// bare status word, so a failing probe names the dependency that is down.
/// </summary>
public sealed class HealthReportResponse
{
    public required HealthStatus Status { get; init; }
    public required double DurationMs { get; init; }
    public required IReadOnlyDictionary<string, HealthReportEntryResponse> Entries { get; init; }

    public static HealthReportResponse From(HealthReport report) => new()
    {
        Status = report.Status,
        DurationMs = report.TotalDuration.TotalMilliseconds,
        Entries = report.Entries.ToDictionary(
            entry => entry.Key,
            entry => new HealthReportEntryResponse
            {
                Status = entry.Value.Status,
                DurationMs = entry.Value.Duration.TotalMilliseconds,
                Description = entry.Value.Description,
                Error = entry.Value.Exception?.Message,
                Data = entry.Value.Data.Count > 0 ? entry.Value.Data : null
            })
    };
}

public sealed class HealthReportEntryResponse
{
    public required HealthStatus Status { get; init; }
    public required double DurationMs { get; init; }
    public string? Description { get; init; }
    public string? Error { get; init; }
    /// <summary>What the check found, such as each account's session state and friend sync age.</summary>
    public IReadOnlyDictionary<string, object>? Data { get; init; }
}

/// <summary>
/// Whether every account is still being collected: its VRChat session is valid and its friend sync,
/// which runs every few minutes, is recent. Degraded rather than unhealthy, since the server itself
/// still serves; it names the accounts so a lost session is noticed instead of found weeks later.
/// </summary>
public sealed class AccountCollectionHealthCheck(Microsoft.EntityFrameworkCore.IDbContextFactory<VrcxDb.VrcxContext> dbFactory) : IHealthCheck
{
    /// <summary>A friend sync older than this means collection for the account has stalled.</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(20);

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var accounts = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(
            db.Accounts.Select(a => new { a.UserId, a.SessionValid, a.LastFriendSyncAt }), cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var data = new Dictionary<string, object>();
        var problems = new List<string>();
        foreach (var a in accounts)
        {
            var age = a.LastFriendSyncAt is { } at ? now - at : (TimeSpan?)null;
            data[a.UserId] = new { sessionValid = a.SessionValid, lastFriendSyncAt = a.LastFriendSyncAt, lastFriendSyncAgeSeconds = age?.TotalSeconds };
            if (!a.SessionValid) problems.Add($"{a.UserId}: session invalid, log in again");
            else if (age is null || age > StaleAfter) problems.Add($"{a.UserId}: no friend sync for {(age is { } t ? $"{t.TotalMinutes:F0} min" : "ever")}");
        }
        return problems.Count == 0
            ? HealthCheckResult.Healthy($"{accounts.Count} account(s) collecting", data)
            : HealthCheckResult.Degraded(string.Join("; ", problems), data: data);
    }
}
