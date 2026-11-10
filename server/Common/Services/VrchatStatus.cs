using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace VRCX.Common.Services;

/// <summary>VRChat's status page as the original's vrcStatus store reads it.</summary>
/// <param name="Status">The page's description when not all systems are operational, empty otherwise.</param>
/// <param name="Indicator">statuspage's indicator: minor, major or critical, empty when operational.</param>
/// <param name="Summary">The components that are not operational, comma separated.</param>
/// <param name="UpdatedAt">When the status page last changed.</param>
public sealed record VrchatStatusReport(string Status, string Indicator, string Summary, DateTimeOffset? UpdatedAt)
{
    public bool HasIssue => Status.Length > 0;
}

/// <summary>
/// The original's vrcStatus store (upstream issue 1396): status.vrchat.com's status.json, and the
/// summary's non-operational components when there is an issue. Fetched on demand and kept 2 minutes
/// while something is wrong and 15 minutes when all is well, the original's polling intervals.
/// </summary>
public sealed class VrchatStatus(IHttpClientFactory http, TimeProvider time, ILogger<VrchatStatus> logger)
{
    public const string ClientName = "vrchat-status";
    private const string ApiUrl = "https://status.vrchat.com/api/v2";
    private static readonly TimeSpan Fast = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan Slow = TimeSpan.FromMinutes(15);

    private readonly SemaphoreSlim _lock = new(1, 1);
    private VrchatStatusReport? _last;
    private DateTimeOffset _fetchedAt;

    public async Task<VrchatStatusReport> GetAsync(CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var now = time.GetUtcNow();
            if (_last is not null && now - _fetchedAt < (_last.HasIssue ? Fast : Slow)) return _last;
            _last = await FetchAsync(ct);
            _fetchedAt = now;
            return _last;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<VrchatStatusReport> FetchAsync(CancellationToken ct)
    {
        using var status = await GetJsonAsync("status.json", ct);
        if (status is null) return new("Failed to fetch VRC status", "", "", null);
        var root = status.RootElement;
        DateTimeOffset? updatedAt = root.TryGetProperty("page", out var page) && page.TryGetProperty("updated_at", out var u) && u.TryGetDateTimeOffset(out var at) ? at : null;
        var description = root.TryGetProperty("status", out var s) && s.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "";
        if (description is "All Systems Operational" or "") return new("", "", "", updatedAt);
        var indicator = s.TryGetProperty("indicator", out var i) ? i.GetString() ?? "" : "";

        var summary = "";
        using var summaryJson = await GetJsonAsync("summary.json", ct);
        if (summaryJson is not null && summaryJson.RootElement.TryGetProperty("components", out var components))
        {
            summary = string.Join(", ", components.EnumerateArray()
                .Where(c => c.TryGetProperty("status", out var cs) && cs.GetString() != "operational")
                .Select(c => c.TryGetProperty("name", out var n) ? n.GetString() : null)
                .OfType<string>());
        }
        return new(description, indicator, summary, updatedAt);
    }

    private async Task<JsonDocument?> GetJsonAsync(string file, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{ApiUrl}/{file}");
            request.Headers.Referrer = new Uri("https://vrcx.app");
            using var response = await http.CreateClient(ClientName).SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("VRChat status {File} answered {Status}", file, (int)response.StatusCode);
                return null;
            }
            return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "VRChat status {File} failed", file);
            return null;
        }
    }
}
