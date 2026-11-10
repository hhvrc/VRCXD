using System.Text.Json.Serialization;

namespace VRCX.Vrchat.Models;

// The Inventory tool, read straight from VRChat. Only the fields the page shows are mapped.

/// <summary>GET files, one uploaded image: a photo, icon, emoji or sticker.</summary>
public sealed class UserFile
{
    public string Id { get; set; } = "";
    public string? Name { get; set; }
    public string? AnimationStyle { get; set; }
    public int? Frames { get; set; }
    public int? FramesOverTime { get; set; }
    public string? LoopStyle { get; set; }
    public List<UserFileVersion> Versions { get; set; } = [];

    /// <summary>The newest version's image.</summary>
    public string? Url => Versions.Count > 0 ? Versions[^1].File?.Url : null;
}

public sealed class UserFileVersion
{
    public int Version { get; set; }
    public UserFileData? File { get; set; }
}

public sealed class UserFileData
{
    public string? Url { get; set; }
}

/// <summary>GET prints/user/{id}, one print.</summary>
public sealed class VrchatPrint
{
    public string Id { get; set; } = "";
    public string? AuthorId { get; set; }
    public string? AuthorName { get; set; }
    public string? Note { get; set; }
    public string? WorldId { get; set; }
    public string? WorldName { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? Timestamp { get; set; }
    public VrchatPrintFiles? Files { get; set; }
}

public sealed class VrchatPrintFiles
{
    public string? Image { get; set; }
}

/// <summary>GET inventory and inventory/global, one item.</summary>
public sealed class InventoryItem
{
    public string Id { get; set; } = "";
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? ImageUrl { get; set; }
    public string? ItemType { get; set; }
    public string? ItemTypeLabel { get; set; }
    /// <summary>What a profile stores to wear the item: nameplate effects, profile effects and icon frames.</summary>
    public string? TemplateId { get; set; }
    [JsonPropertyName("created_at")] public DateTimeOffset? CreatedAt { get; set; }
}

public sealed class InventoryPage
{
    public List<InventoryItem> Data { get; set; } = [];
}
