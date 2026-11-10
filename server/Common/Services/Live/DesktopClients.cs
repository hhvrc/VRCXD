using System.Collections.Concurrent;
using VRCX.Common.Live;

namespace VRCX.Common.Services.Live;

public sealed record DesktopClient(string ClientId, string? ClientName, bool GameRunning);

/// <summary>
/// The desktop apps holding a live socket, and the way to ask one to do something on its PC.
/// Only the latest connection of a client id counts, so a reconnect replaces the old socket.
/// </summary>
public sealed class DesktopClients(ILiveEventBus bus)
{
    private static readonly TimeSpan AnswerTimeout = TimeSpan.FromSeconds(10);

    private sealed class Connection(string? name, Func<ClientAction, CancellationToken, Task> send)
    {
        public string? Name { get; } = name;
        public Func<ClientAction, CancellationToken, Task> Send { get; } = send;
        public bool GameRunning { get; set; }
    }

    private sealed record Pending(string ClientId, TaskCompletionSource<ActionResult?> Answer);

    private readonly ConcurrentDictionary<string, Connection> _connections = new();
    private readonly ConcurrentDictionary<string, Pending> _pending = new();

    public IReadOnlyCollection<string> ConnectedIds => _connections.Keys.ToList();

    public object Attach(string clientId, string? clientName, Func<ClientAction, CancellationToken, Task> send)
    {
        var connection = new Connection(clientName, send);
        _connections[clientId] = connection;
        bus.Publish(LiveEvents.Desktop(clientId, connected: true, gameRunning: false));
        return connection;
    }

    public void Detach(string clientId, object connection)
    {
        if (!_connections.TryRemove(new KeyValuePair<string, Connection>(clientId, (Connection)connection))) return;
        foreach (var (id, pending) in _pending)
            if (pending.ClientId == clientId && _pending.TryRemove(id, out _)) pending.Answer.TrySetResult(null);
        bus.Publish(LiveEvents.Desktop(clientId, connected: false, gameRunning: false));
    }

    public void SetGameRunning(string clientId, object connection, bool running)
    {
        if (!_connections.TryGetValue(clientId, out var current) || !ReferenceEquals(current, connection)) return;
        current.GameRunning = running;
        bus.Publish(LiveEvents.Desktop(clientId, connected: true, running));
    }

    public DesktopClient? Get(string clientId) =>
        _connections.TryGetValue(clientId, out var connection) ? new DesktopClient(clientId, connection.Name, connection.GameRunning) : null;

    public void Complete(ActionResult result)
    {
        if (_pending.TryRemove(result.RequestId, out var pending)) pending.Answer.TrySetResult(result);
    }

    /// <summary>Sends an action nobody answers. False when the app is not connected.</summary>
    public async Task<bool> PostAsync(string clientId, ClientAction action, CancellationToken ct)
    {
        if (!_connections.TryGetValue(clientId, out var connection)) return false;
        try
        {
            await connection.Send(action, ct);
            return true;
        }
        catch (Exception ex) when (ex is System.Net.WebSockets.WebSocketException or ObjectDisposedException)
        {
            // The socket closed under it; the app is going away.
            return false;
        }
    }

    /// <summary>Sends an action and waits for its result. Null when the app went away or never answered.</summary>
    public async Task<ActionResult?> RequestAsync(string clientId, ClientAction action, CancellationToken ct)
    {
        if (!_connections.TryGetValue(clientId, out var connection)) return null;
        action.RequestId = Guid.NewGuid().ToString("N");
        var pending = new Pending(clientId, new TaskCompletionSource<ActionResult?>(TaskCreationOptions.RunContinuationsAsynchronously));
        _pending[action.RequestId] = pending;
        try
        {
            await connection.Send(action, ct);
            return await pending.Answer.Task.WaitAsync(AnswerTimeout, ct);
        }
        catch (TimeoutException)
        {
            return null;
        }
        finally
        {
            _pending.TryRemove(action.RequestId, out _);
        }
    }
}
