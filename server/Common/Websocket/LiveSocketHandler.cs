using System.Net.WebSockets;
using System.Reflection;
using Google.Protobuf;
using VRCX.Common.Live;
using VRCX.Common.Services.Automation;
using VRCX.Common.Services.Live;
using VRCX.Common.Services.Sessions;

namespace VRCX.Common.Websocket;

/// <summary>
/// The /1/ws endpoint: binary protobuf frames, <see cref="ClientMessage"/> in and
/// <see cref="ServerMessage"/> out. One read loop (hello, game-log batches, pings) and one
/// write loop (events from the bus) per connection; either side closing ends both.
/// </summary>
public sealed class LiveSocketHandler(
    ILiveEventBus bus,
    IGameLogIngestor gameLog,
    IAccountSessionManager sessions,
    GameClientRegistry gameClients,
    DesktopClients desktops,
    CrashRelaunch crashRelaunch,
    AgentSettingsPush agentSettings,
    RegistryBackups registryBackups,
    DebugLoggingCheck debugLogging,
    AutoCacheSweep cacheSweep,
    MediaSaver mediaSaver,
    DiscordPresenceService discordPresence,
    VRCX.Common.Services.Live.Photon.PhotonService photon,
    IpcIngestor ipcIngestor,
    ILogger<LiveSocketHandler> logger)
{
    private const int MaxFrameBytes = 4 * 1024 * 1024;
    private static readonly string ServerVersion = typeof(LiveSocketHandler).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "dev";

    public async Task HandleAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status426UpgradeRequired;
            await context.Response.WriteAsync("websocket upgrade required");
            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        var ct = context.RequestAborted;
        var session = new GameLogSession { ClientId = $"anon-{Guid.NewGuid():N}" };
        var ipc = new IpcSession();
        var muteEvents = false;
        object? desktopConnection = null;

        await SendAsync(socket, new ServerMessage
        {
            Hello = new ServerHello { ProtocolVersion = (uint)ProtocolVersion._1, ServerVersion = ServerVersion, AccountIds = { sessions.ActiveAccountIds } },
        }, ct);

        using var subscription = bus.Subscribe();
        using var closing = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var sendLock = new SemaphoreSlim(1, 1);
        // The message loop and the quiet-close timer both change the game-log session; one at a time.
        var ingestLock = new SemaphoreSlim(1, 1);
        CancellationTokenSource? quietClose = null;
        // When VRChat was seen stopping while the app still had old log to send: the stays end then,
        // but only after that backlog has been read.
        DateTimeOffset? stoppedAt = null;

        // VRChat is not running, so a stay still open came from old log being read. It ends with the
        // last line read, but only once reading has gone quiet: closing it after every batch would leave
        // the joins and leaves in the next batch of the same catch-up without an instance.
        void ScheduleQuietClose()
        {
            quietClose?.Cancel();
            var token = (quietClose = CancellationTokenSource.CreateLinkedTokenSource(closing.Token)).Token;
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(QuietCloseDelay, token);
                    await ingestLock.WaitAsync(token);
                    try
                    {
                        if (session.GameRunning == false) await gameLog.GameStoppedAsync(session, stoppedAt ?? Earlier(session.LastEntryAt), CancellationToken.None);
                    }
                    finally { ingestLock.Release(); }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Ending the stays of {Client} after VRChat stopped failed", session.ClientId);
                }
            }, CancellationToken.None);
        }

        var writer = Task.Run(async () =>
        {
            try
            {
                await foreach (var serverEvent in subscription.Reader.ReadAllAsync(closing.Token))
                {
                    if (muteEvents) continue;
                    await sendLock.WaitAsync(closing.Token);
                    try { await SendAsync(socket, new ServerMessage { Event = serverEvent }, closing.Token); }
                    finally { sendLock.Release(); }
                }
            }
            catch (OperationCanceledException) { }
            catch (WebSocketException) { }
        }, CancellationToken.None);

        try
        {
            var buffer = new byte[64 * 1024];
            while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                using var frame = new MemoryStream();
                ValueWebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(buffer.AsMemory(), ct);
                    if (result.MessageType == WebSocketMessageType.Close) goto done;
                    frame.Write(buffer, 0, result.Count);
                    if (frame.Length > MaxFrameBytes) throw new WebSocketException("frame too large");
                } while (!result.EndOfMessage);

                if (result.MessageType != WebSocketMessageType.Binary)
                {
                    logger.LogDebug("Ignoring a text frame on the live socket from {Client}", session.ClientId);
                    continue;
                }

                var message = ClientMessage.Parser.ParseFrom(frame.GetBuffer(), 0, (int)frame.Length);
                ServerMessage? reply = null;
                IReadOnlyList<ClientAction> actions = [];
                switch (message.MessageCase)
                {
                    case ClientMessage.MessageOneofCase.Hello:
                        if (!string.IsNullOrEmpty(message.Hello.ClientId))
                        {
                            if (!session.ClientId.StartsWith("anon-", StringComparison.Ordinal))
                            {
                                gameClients.Disconnected(session.ClientId);
                                if (desktopConnection is not null) desktops.Detach(session.ClientId, desktopConnection);
                            }
                            gameClients.Connected(message.Hello.ClientId);
                            desktopConnection = desktops.Attach(message.Hello, async (action, token) =>
                            {
                                await sendLock.WaitAsync(token);
                                try { await SendAsync(socket, new ServerMessage { Action = action }, token); }
                                finally { sendLock.Release(); }
                            });
                            session = new GameLogSession { ClientId = message.Hello.ClientId, ClientName = message.Hello.ClientName };
                            await gameLog.ResumeAsync(session, ct);
                            var resume = new GameLogResume { AccountId = session.AccountId ?? "", CurrentInstanceLocation = session.CurrentInstanceLocation ?? "" };
                            if (session.LastEntryAt is { } last) resume.LastEntryAt = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(last);
                            reply = new ServerMessage { GameLogResume = resume };
                            // The original's IPCAnnounceStart once signed in: programs on the pipe learn VRCX is up.
                            var helloActions = new List<ClientAction>();
                            if (sessions.ActiveAccountIds.Count > 0)
                                helloActions.Add(new ClientAction { SendIpc = new SendIpc { Type = "VRCXLaunch", MsgType = "VRCXLaunch" } });
                            // What the agent does by itself (the app launcher, the UGC folder), from the account's settings.
                            if (await agentSettings.OnConnectAsync(session.ClientId, session.AccountId, ct) is { } pushed) helloActions.Add(pushed);
                            actions = helloActions;
                            // Answers come back through this loop, so the backup cannot be awaited here.
                            var (backupClient, backupAccount) = (session.ClientId, session.AccountId);
                            _ = Task.Run(() => registryBackups.OnConnectAsync(backupClient, backupAccount, ct), CancellationToken.None);
                            discordPresence.Changed(session.ClientId, session.AccountId, session.CurrentInstanceLocation);
                        }
                        muteEvents = message.Hello.MuteEvents;
                        logger.LogInformation("Live socket hello from {Client} ({Name})", session.ClientId, message.Hello.ClientName);
                        break;
                    case ClientMessage.MessageOneofCase.GameLog:
                        quietClose?.Cancel();
                        GameLogBatchResult batch;
                        await ingestLock.WaitAsync(ct);
                        try { batch = await gameLog.HandleBatchAsync(session, message.GameLog.Entries, ct); }
                        finally { ingestLock.Release(); }
                        if (session.GameRunning == false && session.CurrentInstanceLocation is not null) ScheduleQuietClose();
                        reply = new ServerMessage { GameLogAck = new GameLogAck { Accepted = batch.Accepted } };
                        if (desktopConnection is not null)
                        {
                            mediaSaver.Queue(session.ClientId, session.AccountId, message.GameLog.Entries);
                            photon.Track(session);
                            discordPresence.Changed(session.ClientId, session.AccountId, session.CurrentInstanceLocation);
                        }
                        actions = batch.Actions;
                        break;
                    case ClientMessage.MessageOneofCase.Ping:
                        reply = new ServerMessage { Pong = new Pong() };
                        break;
                    case ClientMessage.MessageOneofCase.Ipc:
                        // Only a desktop app (one that said hello with its client id) has a pipe.
                        if (desktopConnection is null) break;
                        await ingestLock.WaitAsync(ct);
                        try { await ipcIngestor.HandleAsync(session, ipc, message.Ipc, ct); }
                        catch (Exception ex) when (!ct.IsCancellationRequested)
                        {
                            // Nothing resends a packet, so a failed one is lost, not the connection.
                            logger.LogWarning(ex, "An IPC packet from {Client} could not be applied", session.ClientId);
                        }
                        finally { ingestLock.Release(); }
                        break;
                    case ClientMessage.MessageOneofCase.ActionResult:
                        desktops.Complete(message.ActionResult);
                        break;
                    case ClientMessage.MessageOneofCase.GameState:
                        // Session times first: the desktop event makes pages read them.
                        await ingestLock.WaitAsync(ct);
                        try { await gameLog.GameStateAsync(session, message.GameState.Running, DateTimeOffset.UtcNow, ct); }
                        finally { ingestLock.Release(); }
                        if (desktopConnection is not null) desktops.SetGameState(session.ClientId, desktopConnection, message.GameState);
                        var wasRunning = session.GameRunning;
                        // runCheckVRChatDebugLoggingFlow, a minute after the agent says how VRChat is and whenever it starts or stops.
                        if (wasRunning != message.GameState.Running) debugLogging.Schedule(session.ClientId, session.AccountId);
                        session.GameRunning = message.GameState.Running;
                        if (desktopConnection is not null) discordPresence.Changed(session.ClientId, session.AccountId, session.CurrentInstanceLocation);
                        quietClose?.Cancel();
                        stoppedAt = null;
                        if (message.GameState.Running) break;
                        if (wasRunning == true)
                        {
                            var (sweepClient, sweepAccount) = (session.ClientId, session.AccountId);
                            _ = Task.Run(() => cacheSweep.GameStoppedAsync(sweepClient, sweepAccount, CancellationToken.None), CancellationToken.None);
                        }
                        // Closed without logging its quit: maybe start it again where it was (decided after a wait).
                        if (wasRunning == true && desktopConnection is not null && message.GameState is { HasClosedGracefully: true, ClosedGracefully: false })
                        {
                            var (crashedClient, crashedAccount, crashedAt) = (session.ClientId, session.AccountId, session.CurrentInstanceLocation);
                            _ = Task.Run(() => crashRelaunch.GameCrashedAsync(crashedClient, crashedAccount, crashedAt, CancellationToken.None), CancellationToken.None);
                        }
                        if (wasRunning == true && session.LastEntryAt is { } read && DateTimeOffset.UtcNow - read > CaughtUpWithin)
                        {
                            // Stopped while the app is still sending older log (after the server was down,
                            // say). Ending the stays now would leave the rest of that backlog without an
                            // instance; they end at this moment once reading has gone quiet.
                            stoppedAt = DateTimeOffset.UtcNow;
                            ScheduleQuietClose();
                        }
                        else if (wasRunning == true)
                        {
                            // Seen stopping: its stays end now, as the original's runLastLocationResetFlow does.
                            await ingestLock.WaitAsync(ct);
                            try { await gameLog.GameStoppedAsync(session, DateTimeOffset.UtcNow, ct); }
                            finally { ingestLock.Release(); }
                        }
                        // Already stopped when the app connected: when is unknown, so its stays end with the
                        // last log line, once any catch-up has been read.
                        else ScheduleQuietClose();
                        break;
                }

                if (reply is not null)
                {
                    await sendLock.WaitAsync(ct);
                    try
                    {
                        await SendAsync(socket, reply, ct);
                        // What this client's own log lines asked for (screenshot metadata, the quit fix).
                        foreach (var action in actions) await SendAsync(socket, new ServerMessage { Action = action }, ct);
                    }
                    finally { sendLock.Release(); }
                }
            }
            done:;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (WebSocketException ex)
        {
            logger.LogDebug(ex, "Live socket from {Client} dropped", session.ClientId);
        }
        catch (InvalidProtocolBufferException ex)
        {
            logger.LogWarning(ex, "Live socket from {Client} sent an undecodable frame; closing", session.ClientId);
        }
        finally
        {
            if (!session.ClientId.StartsWith("anon-", StringComparison.Ordinal)) gameClients.Disconnected(session.ClientId);
            if (desktopConnection is not null) desktops.Detach(session.ClientId, desktopConnection);
            await closing.CancelAsync();
            quietClose?.Dispose();
            await writer;
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                try { await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None); }
                catch (Exception ex) when (ex is WebSocketException or IOException or ObjectDisposedException) { }
            }
        }
    }

    /// <summary>How long reading old log must be quiet before stays VRChat left open are ended.</summary>
    /// <remarks>Settable so tests need not wait the full delay.</remarks>
    public static TimeSpan QuietCloseDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>A last line read this recently means the app has no backlog when VRChat stops.</summary>
    public static readonly TimeSpan CaughtUpWithin = TimeSpan.FromMinutes(2);

    /// <summary>The last log line's time, never later than now; now when there was none.</summary>
    private static DateTimeOffset Earlier(DateTimeOffset? lastEntryAt)
    {
        var now = DateTimeOffset.UtcNow;
        return lastEntryAt is { } last && last < now ? last : now;
    }

    private static async Task SendAsync(WebSocket socket, ServerMessage message, CancellationToken ct)
    {
        var bytes = message.ToByteArray();
        await socket.SendAsync(bytes, WebSocketMessageType.Binary, endOfMessage: true, ct);
    }
}
