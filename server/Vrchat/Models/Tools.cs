using System.Text.Json;

namespace VRCX.Vrchat.Models;

// What the Tools page reads straight from VRChat. Only the fields the page shows are mapped.

/// <summary>GET users/{id}/groups/{invited|requested|userblocked}.</summary>
public sealed class GroupByStatus
{
    public string Id { get; set; } = "";
    public string GroupId { get; set; } = "";
    public string? Name { get; set; }
    public string? ShortCode { get; set; }
    public string? Discriminator { get; set; }
    public string? IconUrl { get; set; }
    public int MemberCount { get; set; }
}

/// <summary>GET message/{id}/{type}.</summary>
public sealed class InviteMessage
{
    public string Id { get; set; } = "";
    public int Slot { get; set; }
    public string Message { get; set; } = "";
    public string MessageType { get; set; } = "";
    public DateTimeOffset? UpdatedAt { get; set; }
    public int RemainingCooldownMinutes { get; set; }
    public bool CanBeUpdated { get; set; }
}

/// <summary>GET moderationReports, one result.</summary>
public sealed class ModerationReport
{
    public string Id { get; set; } = "";
    public string? Type { get; set; }
    public string? ContentId { get; set; }
    public string? ContentName { get; set; }
    public string? ContentThumbnailImageUrl { get; set; }
    public string? Category { get; set; }
    public string? Reason { get; set; }
    public string? Description { get; set; }
    public DateTimeOffset? Created { get; set; }
}

public sealed class ModerationReportPage
{
    public List<ModerationReport> Results { get; set; } = [];
    public bool HasNext { get; set; }
}

/// <summary>GET economy/purchases, one purchase.</summary>
public sealed class ProductPurchase
{
    public string PurchaseId { get; set; } = "";
    public string? ListingDisplayName { get; set; }
    public string? ListingSubtitle { get; set; }
    public string? ListingImageId { get; set; }
    public string? ListingType { get; set; }
    public string? SellerDisplayName { get; set; }
    public decimal PurchasePrice { get; set; }
    public bool IsGift { get; set; }
    public bool PurchaseActive { get; set; }
    public DateTimeOffset? PurchaseDate { get; set; }
    public DateTimeOffset? PurchaseEndDate { get; set; }
}

/// <summary>GET user/{id}/economy/transactions, one line.</summary>
public sealed class EconomyTransaction
{
    public string TransactionLineId { get; set; } = "";
    public string? ListingDisplayName { get; set; }
    public string? ListingType { get; set; }
    public string? ReasonLabel { get; set; }
    public string? FromUserId { get; set; }
    public string? FromUserDisplayName { get; set; }
    public string? ToUserId { get; set; }
    public string? ToUserDisplayName { get; set; }
    public string? Platform { get; set; }
    public decimal Amount { get; set; }
    public decimal Balance { get; set; }
    public DateTimeOffset? Date { get; set; }
}

public sealed class EconomyTransactionPage
{
    public List<EconomyTransaction> Transactions { get; set; } = [];
}

/// <summary>GET calendar, calendar/following and calendar/featured, one event.</summary>
public sealed class CalendarEvent
{
    public string Id { get; set; } = "";
    public string OwnerId { get; set; } = "";
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? ImageUrl { get; set; }
    public string? Category { get; set; }
    public string? AccessType { get; set; }
    public string? SeriesId { get; set; }
    public int InterestedUserCount { get; set; }
    public int CloseInstanceAfterEndMinutes { get; set; }
    public DateTimeOffset? StartsAt { get; set; }
    public DateTimeOffset? EndsAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public CalendarUserInterest? UserInterest { get; set; }
}

public sealed class CalendarUserInterest
{
    public bool IsFollowing { get; set; }
}

public sealed class CalendarEventPage
{
    public List<CalendarEvent> Results { get; set; } = [];
}

/// <summary>The report labels out of GET config.</summary>
public sealed class ReportOptions
{
    public required string[] Types { get; init; }
    public required Dictionary<string, string> Categories { get; init; }
    public required Dictionary<string, string> Reasons { get; init; }
    /// <summary>Per report type, its categories and the reasons each offers: config.reportOptions.</summary>
    public Dictionary<string, Dictionary<string, string[]>> Options { get; init; } = [];
    /// <summary>The order categories are listed in.</summary>
    public Dictionary<string, int> CategoryOrder { get; init; } = [];
    public Dictionary<string, string> CategoryTooltips { get; init; } = [];
    public Dictionary<string, string> ReasonTooltips { get; init; } = [];

    public static ReportOptions FromConfig(JsonElement config) => new()
    {
        Options = OptionsOf(config),
        CategoryOrder = Orders(config, "reportCategories"),
        CategoryTooltips = Labels(config, "reportCategories", "tooltip"),
        ReasonTooltips = Labels(config, "reportReasons", "tooltip"),
        Types = config.TryGetProperty("reportOptions", out var options) && options.ValueKind == JsonValueKind.Object
            ? options.EnumerateObject().Select(p => p.Name).ToArray()
            : [],
        Categories = Labels(config, "reportCategories"),
        Reasons = Labels(config, "reportReasons"),
    };

    private static Dictionary<string, string> Labels(JsonElement config, string property, string field = "text")
    {
        var labels = new Dictionary<string, string>();
        if (!config.TryGetProperty(property, out var section) || section.ValueKind != JsonValueKind.Object) return labels;
        foreach (var entry in section.EnumerateObject())
        {
            if (entry.Value.ValueKind == JsonValueKind.Object && entry.Value.TryGetProperty(field, out var text) && text.ValueKind == JsonValueKind.String)
                labels[entry.Name] = text.GetString()!;
        }
        return labels;
    }

    private static Dictionary<string, int> Orders(JsonElement config, string property)
    {
        var orders = new Dictionary<string, int>();
        if (!config.TryGetProperty(property, out var section) || section.ValueKind != JsonValueKind.Object) return orders;
        foreach (var entry in section.EnumerateObject())
        {
            if (entry.Value.ValueKind == JsonValueKind.Object && entry.Value.TryGetProperty("order", out var order) && order.TryGetInt32(out var value))
                orders[entry.Name] = value;
        }
        return orders;
    }

    private static Dictionary<string, Dictionary<string, string[]>> OptionsOf(JsonElement config)
    {
        var result = new Dictionary<string, Dictionary<string, string[]>>();
        if (!config.TryGetProperty("reportOptions", out var options) || options.ValueKind != JsonValueKind.Object) return result;
        foreach (var type in options.EnumerateObject())
        {
            if (type.Value.ValueKind != JsonValueKind.Object) continue;
            var categories = new Dictionary<string, string[]>();
            foreach (var category in type.Value.EnumerateObject())
            {
                categories[category.Name] = category.Value.ValueKind == JsonValueKind.Array
                    ? category.Value.EnumerateArray().Where(r => r.ValueKind == JsonValueKind.String).Select(r => r.GetString()!).ToArray()
                    : [];
            }
            result[type.Name] = categories;
        }
        return result;
    }
}
