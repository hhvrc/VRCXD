using System.Collections.Concurrent;
using System.Threading.Channels;
using Google.Protobuf.WellKnownTypes;
using VRCX.Common.Live;

namespace VRCX.Common.Services.Live;

/// <summary>
/// In-process fan-out of <see cref="ServerEvent"/>s to every open websocket. Publishers are
/// the ingestion services, right after their SaveChanges; subscribers are the live socket
/// connections. A slow subscriber drops its oldest events rather than stalling the publisher.
/// </summary>
public interface ILiveEventBus
{
    void Publish(ServerEvent serverEvent);
    /// <summary>Subscribes until the returned subscription is disposed.</summary>
    LiveSubscription Subscribe();
    int SubscriberCount { get; }
}

public sealed class LiveSubscription(Channel<ServerEvent> channel, Action onDispose) : IDisposable
{
    public ChannelReader<ServerEvent> Reader => channel.Reader;
    public void Dispose() => onDispose();
}

public sealed class LiveEventBus(ILogger<LiveEventBus> logger) : ILiveEventBus
{
    private const int PerSubscriberBuffer = 1024;
    private readonly ConcurrentDictionary<Guid, Channel<ServerEvent>> _subscribers = new();

    public int SubscriberCount => _subscribers.Count;

    public void Publish(ServerEvent serverEvent)
    {
        if (serverEvent.At is null) serverEvent.At = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow);
        foreach (var channel in _subscribers.Values)
        {
            if (!channel.Writer.TryWrite(serverEvent))
                logger.LogDebug("Dropped a {Event} for a slow live subscriber", serverEvent.EventCase);
        }
    }

    public LiveSubscription Subscribe()
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateBounded<ServerEvent>(new BoundedChannelOptions(PerSubscriberBuffer)
        {
            SingleReader = true,
            FullMode = BoundedChannelFullMode.DropOldest,
        });
        _subscribers[id] = channel;
        return new LiveSubscription(channel, () =>
        {
            if (_subscribers.TryRemove(id, out var removed)) removed.Writer.TryComplete();
        });
    }
}

/// <summary>Builders for the events the ingestion code publishes, so the shapes live in one place.</summary>
public static class LiveEvents
{
    public static ServerEvent Presence(VrcxDb.User user, VrcxDb.PresenceEvent presence) => new()
    {
        Presence = new PresenceChanged
        {
            UserId = user.Id,
            DisplayName = user.DisplayName,
            Kind = presence.Kind.ToString().ToLowerInvariant(),
            State = presence.Kind switch { VrcxDb.PresenceKind.Offline => "offline", VrcxDb.PresenceKind.Active => "active", _ => "online" },
            Location = presence.Location ?? "",
            PreviousLocation = presence.PreviousLocation ?? "",
            TravelingToLocation = presence.TravelingToLocation ?? "",
            InstanceLocation = presence.InstanceLocation ?? "",
            Platform = presence.Platform ?? "",
        },
    };

    public static ServerEvent UserChanged(VrcxDb.User user, IEnumerable<string> changed) => new()
    {
        User = new UserChanged { UserId = user.Id, DisplayName = user.DisplayName, Changed = { changed } },
    };

    public static ServerEvent Friendship(string ownerId, string friendId, string displayName, bool isFriend, int friendNumber) => new()
    {
        Friendship = new FriendshipChanged
        {
            OwnerUserId = ownerId, FriendUserId = friendId, DisplayName = displayName, IsFriend = isFriend, FriendNumber = friendNumber,
        },
    };

    public static ServerEvent Notification(VrcxDb.Notification n, string? senderDisplayName) => new()
    {
        Notification = new NotificationReceived
        {
            OwnerUserId = n.OwnerUserId, NotificationId = n.Id, Version = n.Version, Type = n.Type,
            SenderUserId = n.SenderUserId ?? "", SenderDisplayName = senderDisplayName ?? n.SenderUsername ?? "",
            Title = n.Title ?? "", Message = n.Message,
        },
    };

    public static ServerEvent Visit(string userId, string displayName, string instanceLocation, string worldId, bool joined, VrcxDb.VisitSource source) => new()
    {
        Visit = new InstanceVisitChanged
        {
            UserId = userId, DisplayName = displayName, InstanceLocation = instanceLocation, WorldId = worldId, Joined = joined,
            Source = source switch { VrcxDb.VisitSource.GameLog => "game_log", VrcxDb.VisitSource.Api => "api", _ => "pipeline" },
        },
    };

    public static ServerEvent UserChanged(string userId, string displayName, IEnumerable<string> changed) => new()
    {
        User = new UserChanged { UserId = userId, DisplayName = displayName, Changed = { changed } },
    };

    public static ServerEvent GameLog(string? accountId, string? instanceLocation, IEnumerable<string> kinds)
    {
        var stored = new GameLogStored { AccountId = accountId ?? "", InstanceLocation = instanceLocation ?? "" };
        stored.Kinds.AddRange(kinds);
        return new ServerEvent { At = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow), GameLog = stored };
    }

    public static ServerEvent Content(string kind, string id, string name) => new()
    {
        Content = new ContentChanged { Kind = kind, Id = id, Name = name },
    };

    public static ServerEvent Account(string accountId, string displayName, bool sessionValid, string autoLogin = "", int autoLoginDelay = 0) => new()
    {
        Account = new AccountSessionChanged { AccountId = accountId, DisplayName = displayName, SessionValid = sessionValid, AutoLogin = autoLogin, AutoLoginDelay = (uint)autoLoginDelay },
    };

    public static ServerEvent InstanceQueue(string accountId, QueuedInstance queued, bool left, bool removedByMove) => new()
    {
        InstanceQueue = new InstanceQueueChanged
        {
            AccountId = accountId, Location = queued.Location, Position = queued.Position, QueueSize = queued.QueueSize,
            WorldName = queued.WorldName, GroupName = queued.GroupName, Left = left, RemovedByMove = removedByMove,
        },
    };

    public static ServerEvent YouTubeBotError(string? accountId, string message) => new()
    {
        At = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow),
        YoutubeBotError = new YouTubeBotError { AccountId = accountId ?? "", Message = message },
    };

    public static ServerEvent NowPlayingPosition(string? accountId, string videoUrl, double videoPos, double videoLength, DateTimeOffset loggedAt) => new()
    {
        At = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow),
        NowPlayingPosition = new NowPlayingPosition
        {
            AccountId = accountId ?? "",
            VideoUrl = videoUrl,
            VideoPos = videoPos,
            VideoLength = videoLength,
            LoggedAt = Timestamp.FromDateTimeOffset(loggedAt),
        },
    };

    public static ServerEvent ContentRefresh(string accountId, string contentType, string actionType) => new()
    {
        At = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow),
        ContentRefresh = new AccountContentRefreshed { AccountId = accountId, ContentType = contentType, ActionType = actionType },
    };

    public static ServerEvent Credits(string accountId, long? balance)
    {
        var credits = new CreditsChanged { AccountId = accountId };
        if (balance is { } value) credits.Balance = value;
        return new ServerEvent { At = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow), Credits = credits };
    }

    public static ServerEvent CustomTag(string accountId, string userId, string tag, string colour, bool cleared) => new()
    {
        At = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow),
        CustomTag = new CustomTagChanged { AccountId = accountId, UserId = userId, Tag = tag, Colour = colour, Cleared = cleared },
    };

    public static ServerEvent Desktop(string clientId, bool connected, bool gameRunning, bool gameNoVr = false) => new()
    {
        Desktop = new DesktopChanged { ClientId = clientId, Connected = connected, GameRunning = gameRunning, GameNoVr = gameNoVr },
    };
}
