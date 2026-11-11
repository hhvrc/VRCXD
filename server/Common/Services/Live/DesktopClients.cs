using System.Collections.Concurrent;
using VRCX.Common.Live;

namespace VRCX.Common.Services.Live;

/// <summary>A connected PC: what its agent does (<see cref="Capabilities"/>, named as in the proto without the
/// prefix, lowercase), its OS and folders, and whether VRChat and SteamVR run on it.</summary>
public sealed record DesktopClient(
    string ClientId,
    string? ClientName,
    bool GameRunning,
    bool SteamVrRunning = false,
    IReadOnlyList<string>? Capabilities = null,
    string? Os = null,
    AgentFolders? Folders = null,
    int UtcOffsetMinutes = 0,
    bool HmdAfk = false)
{
    public bool Can(Capability capability) => (Capabilities ?? []).Contains(CapabilityName(capability));

    public static string CapabilityName(Capability capability) =>
        string.Concat(capability.ToString().Select((c, i) => char.IsUpper(c) ? (i > 0 ? "_" : "") + char.ToLowerInvariant(c) : c.ToString()));
}

/// <summary>
/// The desktop apps holding a live socket, and the way to ask one to do something on its PC.
/// Only the latest connection of a client id counts, so a reconnect replaces the old socket.
/// </summary>
public sealed class DesktopClients(ILiveEventBus bus)
{
    private static readonly TimeSpan AnswerTimeout = TimeSpan.FromSeconds(10);

    private sealed class Connection(ClientHello hello, Func<ClientAction, CancellationToken, Task> send)
    {
        public string? Name { get; } = hello.ClientName;
        public IReadOnlyList<string> Capabilities { get; } =
            hello.Capabilities.Where(c => c != Capability.Unspecified).Select(DesktopClient.CapabilityName).Distinct().ToList();
        public string? Os { get; } = string.IsNullOrEmpty(hello.Os) ? null : hello.Os;
        public AgentFolders? Folders { get; } = hello.Folders;
        public int UtcOffsetMinutes { get; } = hello.UtcOffsetMinutes;
        public Func<ClientAction, CancellationToken, Task> Send { get; } = send;
        public bool GameRunning { get; set; }
        public bool SteamVrRunning { get; set; }
        public bool HmdAfk { get; set; }
    }

    private sealed record Pending(string ClientId, TaskCompletionSource<ActionResult?> Answer);

    private readonly ConcurrentDictionary<string, Connection> _connections = new();
    private readonly ConcurrentDictionary<string, Pending> _pending = new();

    public IReadOnlyCollection<string> ConnectedIds => _connections.Keys.ToList();

    public object Attach(ClientHello hello, Func<ClientAction, CancellationToken, Task> send)
    {
        var clientId = hello.ClientId;
        var connection = new Connection(hello, send);
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

    public void SetGameState(string clientId, object connection, GameState state)
    {
        if (!_connections.TryGetValue(clientId, out var current) || !ReferenceEquals(current, connection)) return;
        current.GameRunning = state.Running;
        current.SteamVrRunning = state.SteamvrRunning;
        current.HmdAfk = state.HmdAfk;
        bus.Publish(LiveEvents.Desktop(clientId, connected: true, state.Running));
    }

    public DesktopClient? Get(string clientId) =>
        _connections.TryGetValue(clientId, out var c)
            ? new DesktopClient(clientId, c.Name, c.GameRunning, c.SteamVrRunning, c.Capabilities, c.Os, c.Folders, c.UtcOffsetMinutes, c.HmdAfk)
            : null;

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
    public async Task<ActionResult?> RequestAsync(string clientId, ClientAction action, CancellationToken ct, TimeSpan? timeout = null)
    {
        if (!_connections.TryGetValue(clientId, out var connection)) return null;
        action.RequestId = Guid.NewGuid().ToString("N");
        var pending = new Pending(clientId, new TaskCompletionSource<ActionResult?>(TaskCreationOptions.RunContinuationsAsynchronously));
        _pending[action.RequestId] = pending;
        try
        {
            await connection.Send(action, ct);
            return await pending.Answer.Task.WaitAsync(timeout ?? AnswerTimeout, ct);
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
