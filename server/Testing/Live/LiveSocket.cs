using System.Net.WebSockets;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using VRCX.Common.Live;

namespace VRCX.Testing.Live;

/// <summary>
/// One connection to /1/ws, as a browser or the desktop app makes it: binary protobuf frames,
/// <see cref="ClientMessage"/> out and <see cref="ServerMessage"/> in. A background loop reads
/// every frame into an inbox; the typed helpers wait on it.
/// </summary>
public abstract class LiveSocket : IAsyncDisposable
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    private readonly WebSocket _socket;
    private readonly List<ServerMessage> _received = [];
    private readonly HashSet<int> _claimed = [];
    private readonly Lock _gate = new();
    private TaskCompletionSource _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _closed;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _reader;

    protected LiveSocket(WebSocket socket)
    {
        _socket = socket;
        _reader = Task.Run(ReadLoopAsync);
    }

    /// <summary>Every message received so far, in order.</summary>
    public IReadOnlyList<ServerMessage> Received
    {
        get { lock (_gate) return _received.ToList(); }
    }

    public async Task SendAsync(ClientMessage message)
    {
        await _socket.SendAsync(message.ToByteArray(), WebSocketMessageType.Binary, endOfMessage: true, CancellationToken.None);
    }

    /// <summary>
    /// The oldest message matching that no earlier wait has claimed, waiting for it if needed.
    /// Messages other waits skip stay available, so the order of waits does not matter.
    /// </summary>
    public async Task<ServerMessage> NextAsync(Func<ServerMessage, bool> match, TimeSpan? timeout = null, string? what = null)
    {
        var limit = timeout ?? DefaultTimeout;
        var deadline = DateTime.UtcNow + limit;
        while (true)
        {
            Task changed;
            lock (_gate)
            {
                for (var i = 0; i < _received.Count; i++)
                {
                    if (_claimed.Contains(i) || !match(_received[i])) continue;
                    _claimed.Add(i);
                    return _received[i];
                }
                if (_closed)
                    throw new InvalidOperationException($"The server closed the socket before {what ?? "a matching message"} arrived. Received:{Environment.NewLine}  {string.Join(Environment.NewLine + "  ", _received.Select(Describe))}");
                changed = _changed.Task;
            }
            var left = deadline - DateTime.UtcNow;
            if (left <= TimeSpan.Zero || await Task.WhenAny(changed, Task.Delay(left)) != changed)
                throw new TimeoutException($"No {what ?? "matching message"} arrived within {limit.TotalSeconds:0.#}s. Received:{Environment.NewLine}  {string.Join(Environment.NewLine + "  ", Received.Select(Describe))}");
        }
    }

    private void Signal()
    {
        TaskCompletionSource previous;
        lock (_gate)
        {
            previous = _changed;
            _changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        previous.TrySetResult();
    }

    /// <summary>Round-trips a ping: everything the server sent before answering it is in the inbox afterwards.</summary>
    public async Task PingAsync()
    {
        await SendAsync(new ClientMessage { Ping = new Ping() });
        await NextAsync(m => m.MessageCase == ServerMessage.MessageOneofCase.Pong, what: "pong");
    }

    private async Task ReadLoopAsync()
    {
        var buffer = new byte[64 * 1024];
        try
        {
            while (_socket.State == WebSocketState.Open && !_stop.IsCancellationRequested)
            {
                using var frame = new MemoryStream();
                ValueWebSocketReceiveResult result;
                do
                {
                    result = await _socket.ReceiveAsync(buffer.AsMemory(), _stop.Token);
                    if (result.MessageType == WebSocketMessageType.Close) return;
                    frame.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);
                var message = ServerMessage.Parser.ParseFrom(frame.GetBuffer(), 0, (int)frame.Length);
                lock (_gate) _received.Add(message);
                Signal();
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException or IOException or ObjectDisposedException)
        {
        }
        finally
        {
            lock (_gate) _closed = true;
            Signal();
        }
    }

    public static string Describe(ServerMessage message) => message.MessageCase == ServerMessage.MessageOneofCase.Event
        ? $"Event {message.Event.EventCase}: {Payload(message.Event)}"
        : $"{message.MessageCase}: {message}";

    /// <summary>The message inside a ServerEvent's oneof (a PresenceChanged, a NotificationReceived, ...).</summary>
    public static IMessage? Payload(ServerEvent serverEvent)
    {
        var oneof = ServerEvent.Descriptor.Oneofs.Single(o => o.Name == "event");
        return oneof.Accessor.GetCaseFieldDescriptor(serverEvent)?.Accessor.GetValue(serverEvent) as IMessage;
    }

    private int _disposed;

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        try
        {
            if (_socket.State == WebSocketState.Open)
                await _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "test done", CancellationToken.None);
        }
        catch (Exception ex) when (ex is WebSocketException or IOException or ObjectDisposedException or OperationCanceledException)
        {
        }
        await _stop.CancelAsync();
        try { await _reader; } catch (Exception) { }
        _socket.Dispose();
        _stop.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// The web UI's side of /1/ws: it subscribes to <see cref="ServerEvent"/>s. Use
/// <see cref="WaitForAsync{T}"/> to await one event of a kind (PresenceChanged,
/// InstanceVisitChanged, NotificationReceived, ...) and <see cref="AssertNoneAsync{T}"/> to check
/// one did not come.
/// </summary>
public sealed class LiveClient : LiveSocket
{
    private LiveClient(WebSocket socket) : base(socket) { }

    internal static async Task<LiveClient> ConnectAsync(WebSocket socket)
    {
        var client = new LiveClient(socket);
        await client.NextAsync(m => m.MessageCase == ServerMessage.MessageOneofCase.Hello, what: "server hello");
        await client.SendAsync(new ClientMessage { Hello = new ClientHello { ClientName = "vrcx-tests/live", ProtocolVersion = 1 } });
        // The server subscribes to the event bus right after its hello; a ping answered means it has.
        await client.PingAsync();
        return client;
    }

    /// <summary>Every event received so far, oldest first.</summary>
    public IReadOnlyList<ServerEvent> Events =>
        Received.Where(m => m.MessageCase == ServerMessage.MessageOneofCase.Event).Select(m => m.Event).ToList();

    /// <summary>The events of one kind received so far.</summary>
    public IReadOnlyList<T> All<T>() where T : class, IMessage =>
        Events.Select(Payload).OfType<T>().ToList();

    /// <summary>The next event of type T (matching, when given). Events of other kinds are skipped but kept in <see cref="Events"/>.</summary>
    public async Task<T> WaitForAsync<T>(Func<T, bool>? match = null, TimeSpan? timeout = null) where T : class, IMessage
    {
        var message = await NextAsync(m => m.MessageCase == ServerMessage.MessageOneofCase.Event && Payload(m.Event) is T e && (match?.Invoke(e) ?? true),
            timeout, what: typeof(T).Name);
        return (T)Payload(message.Event)!;
    }

    /// <summary>
    /// Fails when an event of type T (matching) has arrived, or arrives before a ping round-trip
    /// completes. Publishing happens before the REST call or pipeline dispatch that caused it
    /// returns, so a ping is enough to flush it.
    /// </summary>
    public async Task AssertNoneAsync<T>(Func<T, bool>? match = null) where T : class, IMessage
    {
        await PingAsync();
        var found = All<T>().Where(e => match?.Invoke(e) ?? true).ToList();
        if (found.Count > 0)
            throw new InvalidOperationException($"Expected no {typeof(T).Name}, got:\n  {string.Join("\n  ", found)}");
    }
}
