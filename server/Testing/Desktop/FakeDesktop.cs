using System.Net.WebSockets;
using Google.Protobuf.WellKnownTypes;
using VRCX.Common.Live;
using VRCX.Testing.Live;

namespace VRCX.Testing.Desktop;

/// <summary>
/// The desktop app on a gaming PC (desktop/src/log_service.cpp), as the server sees it: it says
/// hello with its client id, ships parsed VRChat log lines in batches and waits for the ack,
/// reports whether the game runs, and carries out the actions the server sends (launch,
/// screenshot metadata, the quit fix). Raw log text is parsed by the desktop app itself
/// (desktop/src/log_parser.cpp); this starts from the parsed <see cref="LogEntry"/> it sends, built
/// with <see cref="GameLog"/>.
/// </summary>
public sealed class FakeDesktop : LiveSocket
{
    private readonly TimeProvider _time;

    private FakeDesktop(WebSocket socket, string clientId, TimeProvider time) : base(socket)
    {
        ClientId = clientId;
        _time = time;
    }

    public string ClientId { get; }

    /// <summary>What the server said about this client's earlier log on hello.</summary>
    public GameLogResume? Resume { get; private set; }

    internal static async Task<FakeDesktop> ConnectAsync(WebSocket socket, string clientId, TimeProvider time, bool muteEvents)
    {
        var desktop = new FakeDesktop(socket, clientId, time);
        await desktop.NextAsync(m => m.MessageCase == ServerMessage.MessageOneofCase.Hello, what: "server hello");
        await desktop.SendAsync(new ClientMessage { Hello = new ClientHello { ClientId = clientId, ClientName = "vrcx-desktop/test", ProtocolVersion = 1, MuteEvents = muteEvents } });
        desktop.Resume = (await desktop.NextAsync(m => m.MessageCase == ServerMessage.MessageOneofCase.GameLogResume, what: "game log resume")).GameLogResume;
        return desktop;
    }

    /// <summary>
    /// Ships one batch and waits for its ack. Entries without a time get the scenario clock's
    /// now, one millisecond apart, in order. Returns how many the server accepted and the
    /// actions it sent back for them.
    /// </summary>
    public async Task<LogBatchResult> SendLogAsync(params LogEntry[] entries)
    {
        var now = _time.GetUtcNow();
        var batch = new GameLogBatch();
        for (var i = 0; i < entries.Length; i++)
        {
            var entry = entries[i].Clone();
            entry.CreatedAt ??= Timestamp.FromDateTimeOffset(now.AddMilliseconds(i));
            batch.Entries.Add(entry);
        }
        var before = Received.Count;
        await SendAsync(new ClientMessage { GameLog = batch });
        var ack = await NextAsync(m => m.MessageCase == ServerMessage.MessageOneofCase.GameLogAck, what: "game log ack");
        // Actions for a batch are sent right behind its ack; a ping flushes them.
        await PingAsync();
        var actions = Received.Skip(before).Where(IsAction).Select(m => m.Action).ToList();
        return new LogBatchResult(ack.GameLogAck.Accepted, actions);
    }

    /// <summary>Tells the server VRChat started or stopped on this PC.</summary>
    public async Task GameRunningAsync(bool running)
    {
        await SendAsync(new ClientMessage { GameState = new GameState { Running = running } });
        await PingAsync();
    }

    /// <summary>The next action the server asks for (a launch from the UI, say). Pipe packets are read with <see cref="NextIpcAsync"/>.</summary>
    public async Task<ClientAction> NextActionAsync(TimeSpan? timeout = null) =>
        (await NextAsync(IsAction, timeout, "client action")).Action;

    private static bool IsAction(ServerMessage m) =>
        m.MessageCase == ServerMessage.MessageOneofCase.Action && m.Action.ActionCase != ClientAction.ActionOneofCase.SendIpc;

    /// <summary>
    /// A packet another program wrote to the VRCX named pipe on this PC, as the desktop app relays it
    /// (docs/desktop-ipc.md). A ping behind it waits until the server has applied it.
    /// </summary>
    public async Task SendIpcAsync(IpcPacket packet)
    {
        await SendAsync(new ClientMessage { Ipc = packet });
        await PingAsync();
    }

    /// <summary>The next packet the server asks this app to write to its pipe.</summary>
    public async Task<SendIpc> NextIpcAsync(TimeSpan? timeout = null) =>
        (await NextAsync(m => m.MessageCase == ServerMessage.MessageOneofCase.Action && m.Action.ActionCase == ClientAction.ActionOneofCase.SendIpc, timeout, "pipe packet")).Action.SendIpc;

    /// <summary>Answers an action that carried a request id.</summary>
    public Task CompleteAsync(ClientAction action, ActionStatus status = ActionStatus.Ok, string error = "") =>
        SendAsync(new ClientMessage { ActionResult = new ActionResult { RequestId = action.RequestId, Status = status, Error = error } });
}

public sealed record LogBatchResult(int Accepted, IReadOnlyList<ClientAction> Actions);

/// <summary>
/// Builders for the parsed log lines the desktop app sends, one per log_parser.cpp line kind.
/// Chain <see cref="GameLogExtensions.At"/> to give one an explicit time.
/// </summary>
public static class GameLog
{
    /// <summary>"User Authenticated: name (usr_...)": which account plays on this PC.</summary>
    public static LogEntry UserAuthenticated(string userId, string displayName) =>
        new() { UserAuthenticated = new LogPlayer { UserId = userId, DisplayName = displayName } };

    /// <summary>"Joining wrld_...:..." then "Entering Room: name": the PC entered an instance.</summary>
    public static LogEntry Location(string location, string worldName = "") =>
        new() { Location = new LogLocation { Location = location, WorldName = worldName } };

    public static LogEntry LocationDestination(string location) =>
        new() { LocationDestination = new LogLocationDestination { Location = location } };

    /// <summary>"OnPlayerJoined name (usr_...)". Older logs have no id: pass null.</summary>
    public static LogEntry PlayerJoined(string displayName, string? userId) =>
        new() { PlayerJoined = new LogPlayer { DisplayName = displayName, UserId = userId ?? "" } };

    public static LogEntry PlayerLeft(string displayName, string? userId) =>
        new() { PlayerLeft = new LogPlayer { DisplayName = displayName, UserId = userId ?? "" } };

    public static LogEntry VideoPlay(string url, string displayName = "") =>
        new() { VideoPlay = new LogVideoPlay { VideoUrl = url, DisplayName = displayName } };

    public static LogEntry VideoSync(string text) => new() { VideoSync = new LogText { Text = text } };
    public static LogEntry PortalSpawn() => new() { PortalSpawn = new LogNoData() };
    public static LogEntry Event(string text) => new() { Event = new LogText { Text = text } };
    public static LogEntry Vrcx(string text) => new() { Vrcx = new LogText { Text = text } };
    public static LogEntry Notification(string text) => new() { Notification = new LogText { Text = text } };
    public static LogEntry ApiRequest(string url) => new() { ApiRequest = new LogText { Text = url } };
    public static LogEntry AvatarChange(string displayName, string avatarName) => new() { AvatarChange = new LogAvatarChange { DisplayName = displayName, AvatarName = avatarName } };
    public static LogEntry UdonException(string text) => new() { UdonException = new LogText { Text = text } };
    public static LogEntry Quit() => new() { VrcQuit = new LogNoData() };
    public static LogEntry OpenVrInit() => new() { OpenvrInit = new LogNoData() };
    public static LogEntry DesktopMode() => new() { DesktopMode = new LogNoData() };
    public static LogEntry Screenshot(string path) => new() { Screenshot = new LogText { Text = path } };
    public static LogEntry ResourceLoadString(string url) => new() { ResourceLoadString = new LogText { Text = url } };
    public static LogEntry ResourceLoadImage(string url) => new() { ResourceLoadImage = new LogText { Text = url } };

    public static LogEntry StickerSpawn(string userId, string displayName, string inventoryId) =>
        new() { StickerSpawn = new LogStickerSpawn { UserId = userId, DisplayName = displayName, InventoryId = inventoryId } };
}

/// <summary>Builders for the pipe packets the desktop app relays, one per <see cref="IpcPacket"/> case.</summary>
public static class Ipc
{
    public static IpcPacket CustomTag(string userId, string tag, string colour = "") =>
        new() { CustomTag = new IpcCustomTag { UserId = userId, Tag = tag, TagColour = colour } };

    public static IpcPacket ClearCustomTags() => new() { ClearCustomTags = new LogNoData() };
    public static IpcPacket Noty(string data) => new() { Noty = new LogText { Text = data } };

    public static IpcPacket External(string message, string userId = "", string displayName = "", bool? notify = null)
    {
        var external = new IpcExternal { Message = message, UserId = userId, DisplayName = displayName };
        if (notify is { } value) external.Notify = value;
        return new IpcPacket { External = external };
    }

    public static IpcPacket Ping() => new() { Ping = new LogNoData() };
    public static IpcPacket MsgPing(int version) => new() { MsgPing = new IpcMsgPing { Version = version } };
}

public static class GameLogExtensions
{
    public static LogEntry At(this LogEntry entry, DateTimeOffset at)
    {
        var copy = entry.Clone();
        copy.CreatedAt = Timestamp.FromDateTimeOffset(at);
        return copy;
    }

    public static LogEntry InFile(this LogEntry entry, string fileName)
    {
        var copy = entry.Clone();
        copy.FileName = fileName;
        return copy;
    }
}
