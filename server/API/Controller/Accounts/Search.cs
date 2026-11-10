using System.Net.Mime;
using System.Text.Json;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using OpenShock.Internal.Common.Problems;
using VRCX.Common.Errors;
using VRCX.Common.Services.Ingestion;
using VRCX.Common.VrcxDb;
using VRCX.Vrchat;
using VRCX.Vrchat.Models;

namespace VRCX.API.Controller.Accounts;

public sealed record PurchaseListingBody(string ListingId, int TotalPrice, int Quantity);

// The Search page. Each tab asks VRChat through the account's session and gets VRChat's answer back
// as it came, the way the original reads it. Users found are stored, as everywhere else.
public sealed partial class AccountsController
{
    private static readonly string[] StoreIds =
    [
        "esto_00000000-0000-0000-0000-000000000000",
        "esto_00000000-0000-0000-0000-000000000001",
        "esto_00000000-0000-0000-0000-000000000002",
    ];
    private static readonly HashSet<string> WorldOptions = ["active", "recent", "favorites"];
    private static readonly HttpClient AvatarProviders = CreateAvatarProviderClient();

    // The original identifies itself to avatar databases as "VRCX <version>" with its install id.
    private static HttpClient CreateAvatarProviderClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var version = typeof(AccountsController).Assembly.GetName().Version;
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", $"VRCX {version?.ToString(3) ?? "2.0.0"}");
        return client;
    }

    private const string VrcxIdKey = "VRCX_id";

    private async Task<string> VrcxIdAsync(CancellationToken ct)
    {
        var row = await _db.ServerSettings.FindAsync([VrcxIdKey], ct);
        if (row is not null) return row.Value;
        row = new ServerSetting { Key = VrcxIdKey, Value = Guid.NewGuid().ToString() };
        _db.ServerSettings.Add(row);
        await _db.SaveChangesAsync(ct);
        return row.Value;
    }

    private async Task<IActionResult> FetchAsync(string accountId, Func<VrchatApiClient, Task<object>> fetch)
    {
        object? result = null;
        var outcome = await RunAsync(accountId, async client => result = await fetch(client));
        return result is null ? outcome : Ok(result);
    }

    /// <summary>Search VRChat users, 60 at a time.</summary>
    /// <response code="200">VRChat's results</response>
    /// <response code="409">No live session</response>
    [HttpGet("{accountId}/search/users")]
    [ProducesResponseType<JsonElement>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> SearchUsers([FromRoute] string accountId, [FromQuery] string search, [FromQuery] int offset, [FromQuery] string? sort, [FromServices] VrchatStore store, CancellationToken cancellationToken) =>
        FetchAsync(accountId, async client =>
        {
            var json = await client.GetJsonAsync("users",
                [new("search", search), new("n", "60"), new("offset", offset.ToString()), new("customFields", "displayName"), new("sort", sort is "last_login" ? "last_login" : "relevance")], cancellationToken);
            var now = DateTimeOffset.UtcNow;
            foreach (var user in json.EnumerateArray())
                if (user.Deserialize<LimitedUserSearch>(VrchatJson.Options) is { } found) await store.UpsertSearchResultAsync(_db, found, now, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
            return json;
        });

    /// <summary>Search or browse VRChat worlds with the original's parameters.</summary>
    /// <response code="200">VRChat's results</response>
    /// <response code="409">No live session</response>
    [HttpGet("{accountId}/search/worlds")]
    [ProducesResponseType<JsonElement>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> SearchWorlds([FromRoute] string accountId, [FromQuery] string? option, [FromQuery] string? sort, [FromQuery] bool? featured, [FromQuery] string? order,
        [FromQuery] string? user, [FromQuery] string? releaseStatus, [FromQuery] string? tag, [FromQuery] string? search, [FromQuery] int offset, CancellationToken cancellationToken) =>
        FetchAsync(accountId, async client =>
        {
            List<KeyValuePair<string, string?>> query =
            [
                new("n", "60"), new("offset", offset.ToString()), new("sort", sort), new("featured", featured?.ToString().ToLowerInvariant()), new("order", order),
                new("user", user), new("releaseStatus", releaseStatus), new("tag", tag), new("search", search),
            ];
            return await client.GetJsonAsync(option is not null && WorldOptions.Contains(option) ? $"worlds/{option}" : "worlds", query, cancellationToken);
        });

    /// <summary>The world categories VRChat offers, from its config.</summary>
    /// <response code="200">The dynamicWorldRows array</response>
    /// <response code="409">No live session</response>
    [HttpGet("{accountId}/search/world-rows")]
    [ProducesResponseType<JsonElement>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> ListWorldRows([FromRoute] string accountId, CancellationToken cancellationToken) =>
        FetchAsync(accountId, async client =>
        {
            var config = await client.GetJsonAsync("config", [], cancellationToken);
            return config.TryGetProperty("dynamicWorldRows", out var rows) && rows.ValueKind == JsonValueKind.Array ? rows : JsonDocument.Parse("[]").RootElement;
        });

    /// <summary>Search VRChat groups, 60 at a time.</summary>
    /// <response code="200">VRChat's results</response>
    /// <response code="409">No live session</response>
    [HttpGet("{accountId}/search/groups")]
    [ProducesResponseType<JsonElement>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> SearchGroups([FromRoute] string accountId, [FromQuery] string query, [FromQuery] int offset, CancellationToken cancellationToken) =>
        FetchAsync(accountId, async client => await client.GetJsonAsync("groups", [new("query", query), new("n", "60"), new("offset", offset.ToString())], cancellationToken));

    /// <summary>Groups whose short code matches exactly (groups/strictsearch), for Direct Access.</summary>
    /// <response code="200">VRChat's results</response>
    /// <response code="409">No live session</response>
    [HttpGet("{accountId}/search/groups/strict")]
    [ProducesResponseType<JsonElement>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> StrictSearchGroups([FromRoute] string accountId, [FromQuery] string query, CancellationToken cancellationToken) =>
        FetchAsync(accountId, async client => await client.GetJsonAsync("groups/strictsearch", [new("query", query)], cancellationToken));

    /// <summary>The instance a short name (vrch.at link) points to (instances/s/{shortName}), for Direct Access.</summary>
    /// <response code="200">VRChat's instance, kept as it sends it</response>
    /// <response code="409">No live session</response>
    [HttpGet("{accountId}/search/short-name/{shortName}")]
    [ProducesResponseType<JsonElement>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> InstanceFromShortName([FromRoute] string accountId, [FromRoute] string shortName, CancellationToken cancellationToken) =>
        FetchAsync(accountId, async client => await client.GetJsonAsync($"instances/s/{Uri.EscapeDataString(shortName)}", [], cancellationToken));

    /// <summary>Search the avatar marketplace, 50 at a time.</summary>
    /// <response code="200">VRChat's results and whether there are more</response>
    /// <response code="409">No live session</response>
    [HttpGet("{accountId}/search/marketplace")]
    [ProducesResponseType<JsonElement>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> SearchMarketplace([FromRoute] string accountId, [FromQuery] string? search, [FromQuery] int offset, CancellationToken cancellationToken) =>
        FetchAsync(accountId, async client => await client.GetJsonAsync("avatars",
            [new("n", "50"), new("offset", offset.ToString()), new("releaseStatus", "public"), new("order", "ascending"), new("marketplace", "all"), new("metadata", "true"), new("search", search ?? "")], cancellationToken));

    /// <summary>VRChat's store shelves, the account's purchases and its credit balance.</summary>
    /// <response code="200">Shelves, purchases and balance</response>
    /// <response code="409">No live session</response>
    [HttpGet("{accountId}/search/store")]
    [ProducesResponseType<JsonElement>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> GetStore([FromRoute] string accountId, CancellationToken cancellationToken) =>
        FetchAsync(accountId, async client =>
        {
            var shelves = new List<JsonElement>();
            foreach (var storeId in StoreIds)
            {
                var page = await client.GetJsonAsync("economy/store/shelves", [new("storeId", storeId), new("fetch", "public"), new("hydrateListings", "true")], cancellationToken);
                if (page.ValueKind == JsonValueKind.Array) shelves.AddRange(page.EnumerateArray());
            }
            var (purchases, balance) = await ReadPurchasesAsync(client, accountId, cancellationToken);
            return new { shelves, purchases, balance };
        });

    /// <summary>The account's purchases and its credit balance: what the original's user store keeps for "Owned" and "Buy".</summary>
    /// <response code="200">Purchases and balance</response>
    /// <response code="409">No live session</response>
    [HttpGet("{accountId}/purchases/owned")]
    [ProducesResponseType<JsonElement>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> GetPurchases([FromRoute] string accountId, CancellationToken cancellationToken) =>
        FetchAsync(accountId, async client =>
        {
            var (purchases, balance) = await ReadPurchasesAsync(client, accountId, cancellationToken);
            return new { purchases, balance };
        });

    // getAllProductPurchases (every page) and getVRChatCredits.
    private static async Task<(List<JsonElement> Purchases, int? Balance)> ReadPurchasesAsync(VrchatApiClient client, string accountId, CancellationToken cancellationToken)
    {
        var purchases = new List<JsonElement>();
        for (var offset = 0; offset < 2000; offset += 50)
        {
            var page = await client.GetJsonAsync("economy/purchases",
                [new("buyerId", accountId), new("order", "desc"), new("sort", "purchaseDate"), new("n", "50"), new("offset", offset.ToString())], cancellationToken);
            if (page.ValueKind != JsonValueKind.Array) break;
            purchases.AddRange(page.EnumerateArray());
            if (page.GetArrayLength() < 50) break;
        }
        return (purchases, await ReadBalanceAsync(client, accountId, cancellationToken));
    }

    private static async Task<int?> ReadBalanceAsync(VrchatApiClient client, string accountId, CancellationToken cancellationToken)
    {
        try
        {
            var credits = await client.GetJsonAsync($"user/{Uri.EscapeDataString(accountId)}/economy/balance", [], cancellationToken);
            if (credits.TryGetProperty("balance", out var value) && value.TryGetInt32(out var amount)) return amount;
        }
        catch (VrchatApiException e) when (e is not VrchatUnauthorizedException)
        {
            // The original shows no balance when it cannot read one.
        }
        return null;
    }

    /// <summary>The account's VRC Credits balance (the original's getVRChatCredits), null when VRChat won't say.</summary>
    /// <response code="200">The balance</response>
    /// <response code="409">No live session</response>
    [HttpGet("{accountId}/economy/balance")]
    [ProducesResponseType<JsonElement>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> Credits([FromRoute] string accountId, CancellationToken cancellationToken) =>
        FetchAsync(accountId, async client => new { balance = await ReadBalanceAsync(client, accountId, cancellationToken) });

    /// <summary>Buy a store listing with VRChat credits.</summary>
    /// <response code="200">VRChat's answer</response>
    /// <response code="409">No live session</response>
    /// <response code="502">VRChat refused</response>
    [HttpPost("{accountId}/search/store/purchase")]
    [ProducesResponseType<JsonElement>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> PurchaseListing([FromRoute] string accountId, [FromBody] PurchaseListingBody body, CancellationToken cancellationToken) =>
        FetchAsync(accountId, async client => await client.PostQueryAsync("economy/purchase/listing",
            [new("listingId", body.ListingId), new("totalPrice", body.TotalPrice.ToString()), new("quantity", body.Quantity.ToString())], cancellationToken));

    /// <summary>Search a remote avatar database, as the original's Avatar tab does.</summary>
    /// <response code="200">What the provider returned</response>
    /// <response code="400">The provider is not an http or https address</response>
    /// <response code="502">The provider failed</response>
    [HttpGet("{accountId}/search/avatars")]
    [ProducesResponseType<JsonElement>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status400BadRequest, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> SearchRemoteAvatars([FromRoute] string accountId, [FromQuery] string provider, CancellationToken cancellationToken, [FromQuery] string? search = null, [FromQuery] string? authorId = null)
    {
        if (!Uri.TryCreate(provider, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            return Problem(SearchError.BadProvider);
        var separator = string.IsNullOrEmpty(uri.Query) ? "?" : "&";
        // The original's lookupAvatars: a name search asks for up to 5000; an author's avatars, for the
        // user page's Avatars tab, are asked for by authorId alone.
        var query = !string.IsNullOrEmpty(authorId) ? $"authorId={Uri.EscapeDataString(authorId)}" : $"search={Uri.EscapeDataString(search ?? "")}&n=5000";
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{uri}{separator}{query}");
        request.Headers.Referrer = new Uri("https://vrcx.app");
        request.Headers.TryAddWithoutValidation("VRCX-ID", await VrcxIdAsync(cancellationToken));
        try
        {
            using var response = await AvatarProviders.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode) return Problem(SearchError.ProviderFailed($"{(int)response.StatusCode} {body[..Math.Min(body.Length, 200)]}"));
            var json = JsonDocument.Parse(body).RootElement;
            return Ok(json.ValueKind == JsonValueKind.Array ? json : JsonDocument.Parse("[]").RootElement);
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or TaskCanceledException)
        {
            return Problem(SearchError.ProviderFailed(e.Message));
        }
    }
}
