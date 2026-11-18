namespace VRCX.Common.Live.Topics;

/// <summary>
/// One topic /1/ws streams (docs/live-topics.md): a piece of server state a client keeps a copy of,
/// sent as a <see cref="TopicSnapshot"/> and then <see cref="TopicDelta"/>s. Registered as a
/// singleton <see cref="ILiveTopic"/>; <see cref="TopicHub"/> finds it by <see cref="Name"/> and
/// lists it in ServerHello.
/// </summary>
public interface ILiveTopic
{
    /// <summary>What Subscribe.topic names, e.g. "friends".</summary>
    string Name { get; }

    /// <summary>Whether a Subscribe must name an account (checked by the hub before <see cref="SubscribeAsync"/>).</summary>
    bool AccountScoped { get; }

    /// <summary>
    /// Starts streaming to <paramref name="sink"/>: exactly one <see cref="ITopicSink.Snapshot"/>
    /// before this returns, then <see cref="ITopicSink.Delta"/>s, each one the change from the
    /// state the previous message left. Stops when the returned handle is disposed. A topic that
    /// cannot go on (its state could not be read) calls <see cref="ITopicSink.Close"/>.
    /// </summary>
    Task<IDisposable> SubscribeAsync(string accountId, ITopicSink sink, CancellationToken ct);
}

/// <summary>
/// The receiving end of one subscription. It numbers what it is given (the rev) and queues it on
/// the connection; callers only fill in the payload. Safe to call from any thread, but a topic
/// must not call it from two threads at once for the same subscription, or the order is theirs.
/// </summary>
public interface ITopicSink
{
    void Snapshot(TopicSnapshot snapshot);
    void Delta(TopicDelta delta);
    void Close(TopicCloseReason reason, string detail = "");
    /// <summary>Closed or unsubscribed: what it is given is dropped.</summary>
    bool IsOpen { get; }
}
