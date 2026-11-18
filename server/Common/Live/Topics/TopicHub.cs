using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using VRCX.Common.VrcxDb;

namespace VRCX.Common.Live.Topics;

/// <summary>
/// The topics this server streams, and the door to them: one <see cref="TopicSession"/> per /1/ws
/// connection. The hub itself holds no subscriptions; sessions own theirs, so a connection closing
/// takes all of its subscriptions with it.
/// </summary>
public sealed class TopicHub(IEnumerable<ILiveTopic> topics, IDbContextFactory<VrcxContext> dbFactory, ILoggerFactory loggers)
{
    private readonly Dictionary<string, ILiveTopic> _topics = topics.ToDictionary(t => t.Name, StringComparer.Ordinal);

    /// <summary>How many snapshots and deltas may wait for a connection's socket before a subscription is closed for overflow.</summary>
    /// <remarks>Settable so tests can make a connection overflow.</remarks>
    public int MaxQueued { get; set; } = 256;

    /// <summary>What ServerHello.topics lists.</summary>
    public IReadOnlyCollection<string> Names => _topics.Keys;

    public TopicSession OpenSession() => new(this, loggers.CreateLogger<TopicSession>());

    internal ILiveTopic? Find(string name) => _topics.GetValueOrDefault(name);

    /// <summary>
    /// Whether this connection may see the account. There is one API token and it sees every
    /// account (as REST does), so this is REST's check: the account is logged in to this server.
    /// </summary>
    internal async Task<bool> AccountVisibleAsync(string accountId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Accounts.AnyAsync(a => a.UserId == accountId, ct);
    }
}

/// <summary>
/// One connection's subscriptions and what they have queued for the socket.
/// <para>
/// Backpressure: every snapshot and delta waits in one queue per connection until the socket's
/// writer sends it. When <see cref="TopicHub.MaxQueued"/> of them are waiting, the connection is not
/// keeping up, and the subscription trying to add one more is closed with OVERFLOW instead: its
/// queued messages are dropped and the client, once it reads the TopicClosed, subscribes again and
/// gets a fresh snapshot. A delta is never dropped on its own, so a client's copy is never silently
/// wrong; it is either current or knows to start over. (The ServerEvent bus drops the oldest event
/// instead, which is fine for a notification but not for a delta.)
/// </para>
/// </summary>
public sealed class TopicSession : IAsyncDisposable
{
    private readonly TopicHub _hub;
    private readonly ILogger _logger;
    private readonly Channel<(Subscription? Sub, ServerMessage Message)> _outbound =
        Channel.CreateUnbounded<(Subscription?, ServerMessage)>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Dictionary<uint, Subscription> _subscriptions = [];
    private readonly Lock _gate = new();
    private readonly CancellationTokenSource _closing = new();
    private int _queued;
    private bool _disposed;

    internal TopicSession(TopicHub hub, ILogger logger)
    {
        _hub = hub;
        _logger = logger;
    }

    /// <summary>The open subscriptions, for tests and logs.</summary>
    public int Count
    {
        get { lock (_gate) return _subscriptions.Count; }
    }

    /// <summary>The connection must stop when it cannot even queue the closure that explains why.</summary>
    public CancellationToken Closing => _closing.Token;

    /// <summary>
    /// What to send, in order: the socket's writer loop reads this. Messages of a subscription that
    /// was closed or unsubscribed after they were queued are left out, except its TopicClosed.
    /// </summary>
    public async IAsyncEnumerable<ServerMessage> ReadAllAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var (sub, message) in _outbound.Reader.ReadAllAsync(ct))
        {
            Interlocked.Decrement(ref _queued);
            if (message.MessageCase == ServerMessage.MessageOneofCase.TopicClosed)
            {
                yield return message;
                continue;
            }
            if (sub is { IsOpen: true }) yield return message;
        }
    }

    /// <summary>A Subscribe from the client. Returns at once; the snapshot is read in the background.</summary>
    public void Subscribe(Subscribe request)
    {
        Subscription sub;
        Subscription? replaced;
        lock (_gate)
        {
            if (_disposed) return;
            _subscriptions.Remove(request.SubId, out replaced);
            sub = new Subscription(this, request.SubId);
            _subscriptions[request.SubId] = sub;
        }
        replaced?.End();

        var topic = _hub.Find(request.Topic);
        if (topic is null)
        {
            sub.Close(TopicCloseReason.UnknownTopic, request.Topic);
            return;
        }
        if (topic.AccountScoped && string.IsNullOrEmpty(request.AccountId))
        {
            sub.Close(TopicCloseReason.BadRequest, "account_id is required");
            return;
        }
        var ct = _closing.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                if (topic.AccountScoped && !await _hub.AccountVisibleAsync(request.AccountId, ct))
                {
                    sub.Close(TopicCloseReason.UnknownAccount, request.AccountId);
                    return;
                }
                sub.Attach(await topic.SubscribeAsync(request.AccountId, sub, ct));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Subscribing to {Topic} for {Account} failed", request.Topic, request.AccountId);
                sub.Close(TopicCloseReason.Error, "snapshot failed");
            }
        }, CancellationToken.None);
    }

    /// <summary>An Unsubscribe from the client: nothing more is sent for it.</summary>
    public void Unsubscribe(uint subId)
    {
        Subscription? sub;
        lock (_gate) _subscriptions.Remove(subId, out sub);
        sub?.End();
    }

    private void Forget(Subscription sub)
    {
        lock (_gate)
        {
            if (_subscriptions.TryGetValue(sub.SubId, out var current) && current == sub) _subscriptions.Remove(sub.SubId);
        }
    }

    /// <summary>Queues a snapshot or delta; false when the connection is too far behind to take it.</summary>
    private bool TryQueue(Subscription? sub, ServerMessage message, int? maxQueued = null)
    {
        if (Interlocked.Increment(ref _queued) > (maxQueued ?? _hub.MaxQueued))
        {
            Interlocked.Decrement(ref _queued);
            return false;
        }
        if (_outbound.Writer.TryWrite((sub, message))) return true;
        Interlocked.Decrement(ref _queued);
        return true; // The connection is closing: nothing will be sent anyway.
    }

    private void QueueClosed(ServerMessage message)
    {
        // Even MaxQueued = 0 has room for one explanation of overflow. Further undrained closures
        // would otherwise let invalid subscriptions grow an unbounded per-connection queue.
        if (TryQueue(null, message, Math.Max(1, _hub.MaxQueued))) return;
        _closing.Cancel();
        _outbound.Writer.TryComplete();
    }

    public async ValueTask DisposeAsync()
    {
        List<Subscription> all;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            all = [.. _subscriptions.Values];
            _subscriptions.Clear();
        }
        await _closing.CancelAsync();
        foreach (var sub in all) sub.End();
        _outbound.Writer.TryComplete();
        _closing.Dispose();
    }

    /// <summary>One subscription: numbers its messages and queues them on the session.</summary>
    private sealed class Subscription(TopicSession session, uint subId) : ITopicSink
    {
        private readonly Lock _gate = new();
        private ulong _rev;
        // 0 open, 1 closed by the server (TopicClosed sent), 2 ended by the client or the connection.
        private int _state;
        private IDisposable? _handle;

        public uint SubId => subId;
        public bool IsOpen => Volatile.Read(ref _state) == 0;

        public void Snapshot(TopicSnapshot snapshot)
        {
            lock (_gate)
            {
                if (_state != 0) return;
                snapshot.SubId = subId;
                snapshot.Rev = ++_rev;
                if (session.TryQueue(this, new ServerMessage { TopicSnapshot = snapshot })) return;
            }
            Close(TopicCloseReason.Overflow, "the connection fell behind");
        }

        public void Delta(TopicDelta delta)
        {
            lock (_gate)
            {
                if (_state != 0) return;
                delta.SubId = subId;
                delta.Rev = ++_rev;
                if (session.TryQueue(this, new ServerMessage { TopicDelta = delta })) return;
            }
            Close(TopicCloseReason.Overflow, "the connection fell behind");
        }

        public void Close(TopicCloseReason reason, string detail = "")
        {
            IDisposable? handle;
            lock (_gate)
            {
                if (_state != 0) return;
                _state = 1;
                handle = _handle;
                _handle = null;
                session.QueueClosed(new ServerMessage { TopicClosed = new TopicClosed { SubId = subId, Reason = reason, Detail = detail } });
            }
            session.Forget(this);
            handle?.Dispose();
        }

        /// <summary>Unsubscribed, replaced or disconnected: stop without a word.</summary>
        public void End()
        {
            IDisposable? handle;
            lock (_gate)
            {
                _state = 2;
                handle = _handle;
                _handle = null;
            }
            handle?.Dispose();
        }

        /// <summary>The topic's handle, once its snapshot is out. Disposed at once if the subscription ended meanwhile.</summary>
        public void Attach(IDisposable handle)
        {
            lock (_gate)
            {
                if (_state == 0)
                {
                    _handle = handle;
                    return;
                }
            }
            handle.Dispose();
        }
    }
}
