using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VRCX.Common.Live;
using VRCX.Common.Services.Images;
using VRCX.Common.Services.Sessions;
using VRCX.Common.VrcxDb;
using VRCX.Vrchat;

namespace VRCX.Common.Services.Live;

/// <summary>
/// The original's save prints, stickers and emoji options (gallery.js queueSavePrintToFile,
/// trySaveStickerToFile, queueCheckInstanceInventory and trySaveEmojiToFile, fed by gameLog.js
/// parsePrintFromUrl and parseInventoryFromUrl). The game log says what was seen; the server looks
/// it up with the account's VRChat session, downloads the image and the PC's agent writes it into
/// the UGC folder, under the original's file names.
/// </summary>
public sealed class MediaSaver(
    IDbContextFactory<VrcxContext> dbFactory,
    DesktopClients desktops,
    IAccountSessionManager sessions,
    ImageCache images,
    ILiveEventBus bus,
    ILogger<MediaSaver> logger)
{
    /// <summary>The original's queue workers run one item every 2.5 seconds.</summary>
    public static TimeSpan Spacing { get; set; } = TimeSpan.FromMilliseconds(2500);

    /// <summary>Log lines older than this were read back from an old log, not seen now.</summary>
    private static readonly TimeSpan Fresh = TimeSpan.FromHours(1);

    private sealed class Recent
    {
        private readonly LinkedList<string> _order = new();
        private readonly HashSet<string> _set = [];

        public bool Contains(string id)
        {
            lock (_set) return _set.Contains(id);
        }

        /// <summary>False when it was there; keeps the last 100, as the original's caches.</summary>
        public bool Add(string id)
        {
            lock (_set)
            {
                if (!_set.Add(id)) return false;
                _order.AddLast(id);
                if (_order.Count > 100)
                {
                    _set.Remove(_order.First!.Value);
                    _order.RemoveFirst();
                }
                return true;
            }
        }
    }

    private sealed class AccountState
    {
        public readonly Recent Prints = new();
        public readonly Recent Inventory = new();
        public readonly Recent Stickers = new();
        public readonly SemaphoreSlim PrintQueue = new(1, 1);
        public readonly SemaphoreSlim InventoryQueue = new(1, 1);
    }

    private readonly ConcurrentDictionary<string, AccountState> _accounts = new();

    /// <summary>parsePrintFromUrl: a prints/{id} request's print id.</summary>
    public static string? PrintIdOf(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !uri.AbsolutePath.StartsWith("/api/1/prints/", StringComparison.Ordinal)) return null;
        var parts = uri.AbsolutePath.Split('/');
        return parts.Length > 4 && parts[4].Length == 41 ? parts[4] : null;
    }

    /// <summary>parseInventoryFromUrl: a user/{userId}/inventory/{inventoryId} request's ids.</summary>
    public static (string UserId, string InventoryId)? InventoryOf(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        var path = uri.AbsolutePath;
        if (!path.StartsWith("/api/1/user/", StringComparison.Ordinal) || !path.Contains("/inventory/inv_", StringComparison.Ordinal)) return null;
        var parts = path.Split('/');
        return parts.Length > 6 && parts[4].Length > 0 && parts[6].Length == 40 ? (parts[4], parts[6]) : null;
    }

    /// <summary>JavaScript's toISOString without the zone, as the original's file names use it: 2024-11-03_16-14-25.757.</summary>
    private static string FileDate(DateTime at) => at.ToString("yyyy-MM-dd'_'HH-mm-ss.fff", CultureInfo.InvariantCulture);

    /// <summary>getPrintLocalDate: createdAt in the PC's local time, else the timestamp as is, else now locally.</summary>
    public static DateTime PrintDate(DateTimeOffset? createdAt, DateTimeOffset? timestamp, int utcOffsetMinutes, DateTimeOffset now)
    {
        if (createdAt is { } c) return c.UtcDateTime.AddMinutes(utcOffsetMinutes);
        if (timestamp is { } t) return t.UtcDateTime;
        return now.UtcDateTime.AddMinutes(utcOffsetMinutes);
    }

    /// <summary>getPrintFileName.</summary>
    public static string PrintFileName(string? authorName, DateTime date, string printId) => $"{authorName ?? "undefined"}_{FileDate(date)}_{printId}.png";

    /// <summary>trySaveStickerToFile's name, from the item's created_at text.</summary>
    public static string StickerFileName(string displayName, string createdAt, string inventoryId) =>
        $"{displayName}_{createdAt.Replace(":", "-").Replace("T", "_").Replace("Z", "")}_{inventoryId}.png";

    /// <summary>getEmojiFileName, with the name the original gives it ("{holder}_{inventory id}").</summary>
    public static string EmojiFileName(string name, JsonElement metadata)
    {
        static string Text(JsonElement m, string key) =>
            m.ValueKind == JsonValueKind.Object && m.TryGetProperty(key, out var v) && v.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined
                ? v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.GetRawText()
                : "undefined";
        var frames = metadata.ValueKind == JsonValueKind.Object && metadata.TryGetProperty("frames", out var f) && f.ValueKind == JsonValueKind.Number && f.GetDouble() != 0;
        var animation = Text(metadata, "animationStyle");
        if (!frames) return $"{name}_{animation}animationStyle.png";
        var loop = Text(metadata, "loopStyle") is { } l && l != "undefined" && l.Length > 0 ? l : "linear";
        return $"{name}_{animation}animationStyle_{Text(metadata, "frames")}frames_{Text(metadata, "framesOverTime")}fps_{loop}loopStyle.png";
    }

    /// <summary>Looks at a stored batch for prints, stickers and emoji to save; the saving runs in the background.</summary>
    public void Queue(string clientId, string? accountId, IReadOnlyList<LogEntry> entries)
    {
        if (accountId is null) return;
        var now = DateTimeOffset.UtcNow;
        var prints = new List<string>();
        var inventory = new List<(string UserId, string InventoryId)>();
        var stickers = new List<LogStickerSpawn>();
        foreach (var entry in entries)
        {
            if (entry.CreatedAt is { } at && now - at.ToDateTimeOffset() > Fresh) continue;
            switch (entry.EntryCase)
            {
                case LogEntry.EntryOneofCase.ApiRequest:
                    if (PrintIdOf(entry.ApiRequest.Text) is { } printId) prints.Add(printId);
                    if (InventoryOf(entry.ApiRequest.Text) is { } item) inventory.Add(item);
                    break;
                case LogEntry.EntryOneofCase.StickerSpawn when entry.StickerSpawn.InventoryId.Length > 0:
                    stickers.Add(entry.StickerSpawn);
                    break;
            }
        }
        if (prints.Count == 0 && inventory.Count == 0 && stickers.Count == 0) return;
        _ = Task.Run(() => SaveAsync(clientId, accountId, prints, inventory, stickers), CancellationToken.None);
    }

    private async Task SaveAsync(string clientId, string accountId, List<string> prints, List<(string UserId, string InventoryId)> inventory, List<LogStickerSpawn> stickers)
    {
        try
        {
            if (desktops.Get(clientId) is not { } desktop || !desktop.Can(Capability.SaveMedia) || sessions.Get(accountId) is not { } client) return;
            AccountSettings? settings;
            await using (var db = await dbFactory.CreateDbContextAsync())
                settings = await db.Accounts.AsNoTracking().Where(a => a.UserId == accountId).Select(a => a.Settings).FirstOrDefaultAsync();
            if (settings is null) return;
            var state = _accounts.GetOrAdd(accountId, _ => new AccountState());
            var work = new List<Task>();
            if (settings.SaveInstanceStickers)
                foreach (var sticker in stickers)
                    if (state.Stickers.Add(sticker.InventoryId))
                        work.Add(SaveStickerAsync(desktop, accountId, client, sticker));
            if (settings.SaveInstanceEmoji)
            {
                var fresh = inventory.Where(i => !state.Stickers.Contains(i.InventoryId) && state.Inventory.Add(i.InventoryId)).ToList();
                if (fresh.Count > 0) work.Add(InQueueAsync(state.InventoryQueue, fresh, i => SaveEmojiAsync(desktop, accountId, client, i.UserId, i.InventoryId)));
            }
            if (settings.SaveInstancePrints)
            {
                var fresh = prints.Where(state.Prints.Add).ToList();
                if (fresh.Count > 0) work.Add(InQueueAsync(state.PrintQueue, fresh, id => SavePrintAsync(desktop, accountId, client, id, settings.CropInstancePrints)));
            }
            await Task.WhenAll(work);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Saving prints, stickers or emoji for {Account} failed", accountId);
        }
    }

    private static async Task InQueueAsync<T>(SemaphoreSlim queue, List<T> items, Func<T, Task> save)
    {
        foreach (var item in items)
        {
            await queue.WaitAsync();
            try
            {
                await Task.Delay(Spacing);
                await save(item);
            }
            finally { queue.Release(); }
        }
    }

    private async Task SavePrintAsync(DesktopClient desktop, string accountId, VrchatApiClient client, string printId, bool crop)
    {
        try
        {
            var print = await client.GetPrintAsync(printId);
            if (string.IsNullOrEmpty(print.Files?.Image))
            {
                logger.LogWarning("Print {Print} has no image", printId);
                return;
            }
            var date = PrintDate(print.CreatedAt, print.Timestamp, desktop.UtcOffsetMinutes, DateTimeOffset.UtcNow);
            await WriteAsync(desktop, accountId, MediaKind.Print, "print", print.Files.Image, date.ToString("yyyy-MM", CultureInfo.InvariantCulture), PrintFileName(print.AuthorName, date, print.Id), crop);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Saving print {Print} failed", printId);
        }
    }

    private async Task SaveStickerAsync(DesktopClient desktop, string accountId, VrchatApiClient client, LogStickerSpawn sticker)
    {
        try
        {
            var item = await client.GetUserInventoryItemAsync(sticker.UserId, sticker.InventoryId);
            if (!IsUgc(item, "sticker")) return;
            var createdAt = Str(item, "created_at");
            if (createdAt.Length < 7 || ImageUrl(item) is not { } url) return;
            await WriteAsync(desktop, accountId, MediaKind.Sticker, "sticker", url, createdAt[..7], StickerFileName(sticker.DisplayName, createdAt, sticker.InventoryId), false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Saving sticker {Inventory} failed", sticker.InventoryId);
        }
    }

    private async Task SaveEmojiAsync(DesktopClient desktop, string accountId, VrchatApiClient client, string userId, string inventoryId)
    {
        try
        {
            var item = await client.GetUserInventoryItemAsync(userId, inventoryId);
            if (!IsUgc(item, "emoji")) return;
            var holder = Str(item, "holderId");
            var displayName = holder.Length > 0 ? (await client.GetUserAsync(holder))?.DisplayName ?? "" : "";
            var createdAt = Str(item, "created_at");
            if (createdAt.Length < 7 || ImageUrl(item) is not { } url) return;
            var metadata = item.TryGetProperty("metadata", out var m) ? m : default;
            await WriteAsync(desktop, accountId, MediaKind.Emoji, "emoji", url, createdAt[..7], EmojiFileName($"{displayName}_{inventoryId}", metadata), false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Saving emoji {Inventory} failed", inventoryId);
        }
    }

    private static string Str(JsonElement item, string key) =>
        item.ValueKind == JsonValueKind.Object && item.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static bool IsUgc(JsonElement item, string type) =>
        Str(item, "itemType") == type
        && item.TryGetProperty("flags", out var flags) && flags.ValueKind == JsonValueKind.Array
        && flags.EnumerateArray().Any(f => f.ValueKind == JsonValueKind.String && f.GetString() == "ugc");

    /// <summary>metadata.imageUrl, else imageUrl.</summary>
    private static string? ImageUrl(JsonElement item)
    {
        var url = item.TryGetProperty("metadata", out var m) ? Str(m, "imageUrl") : "";
        if (url.Length == 0) url = Str(item, "imageUrl");
        return url.Length == 0 ? null : url;
    }

    private async Task WriteAsync(DesktopClient desktop, string accountId, MediaKind kind, string label, string url, string monthFolder, string fileName, bool crop)
    {
        var image = await images.GetAsync(url, CancellationToken.None);
        if (image is null)
        {
            logger.LogWarning("Could not download the {Kind} image {Url}", label, url);
            return;
        }
        var action = new ClientAction
        {
            SaveMedia = new SaveMedia
            {
                Kind = kind,
                Url = url,
                MonthFolder = monthFolder,
                FileName = fileName,
                Crop = crop,
                Data = Google.Protobuf.ByteString.CopyFrom(await File.ReadAllBytesAsync(image.Path)),
            },
        };
        var answer = await desktops.RequestAsync(desktop.ClientId, action, CancellationToken.None, TimeSpan.FromMinutes(1));
        if (answer is null) return;
        if (answer.Status == ActionStatus.Ok)
        {
            logger.LogInformation("{Kind} saved to file: {Month}\\{File}", label, monthFolder, fileName);
            return;
        }
        logger.LogWarning("The PC could not save the {Kind} {File}: {Error}", label, fileName, answer.Error);
        // handleFileSaveError: Windows refusing the folder (controlled folder access) is the user's to fix.
        if (answer.Error.Contains("denied", StringComparison.OrdinalIgnoreCase) || answer.Error.Contains("Could not find file", StringComparison.OrdinalIgnoreCase))
            bus.Publish(LiveEvents.AgentAlert(accountId, AgentAlertKind.SaveMediaBlocked, label));
    }
}
