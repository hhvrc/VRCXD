# Live topics

Live topics carry state over `/1/ws` that a client keeps as a local copy. A topic is an independent
stream: the client subscribes once, receives one snapshot, and then receives deltas until it
unsubscribes or the stream closes. Topics complement REST; they do not change the REST routes or
responses.

## Protocol

The envelopes and payloads are defined in `server/Common/Protos/vrcx_live.proto`.

1. The client opens the authenticated `/1/ws` connection and waits for `ServerHello`. Its
   `topics` field is the server's supported topic list.
2. The client sends `Subscribe` with a connection-local `subId`, the topic name and, for an
   account-scoped topic, the account id. The `subId` identifies only this subscription.
3. The server replies with exactly one `TopicSnapshot` for that subscription. `rev` starts at 1.
4. Each `TopicDelta` advances `rev` by one and replaces or removes part of the snapshot.
5. The client sends `Unsubscribe` with the `subId` when it no longer needs the stream. A reconnect
   has no remembered subscriptions, so the client subscribes again and starts from a fresh
   snapshot.

Treat payloads of an unrecognized type as unknown, rather than as an empty value. A client that
receives a delta whose `rev` is not the previous `rev + 1` must not apply it: its copy may already
be stale. It should unsubscribe or replace the old subscription, retaining the displayed value
until a new snapshot arrives.

`TopicClosed` explains streams the server cannot continue. `UNKNOWN_TOPIC`, `UNKNOWN_ACCOUNT` and
`BAD_REQUEST` are terminal for that subscription. `OVERFLOW` means the connection fell behind, and
`ERROR` means the topic could not produce a valid stream. The browser waits one second before
resubscribing after `OVERFLOW` or `ERROR`.

## Server

`TopicHub` owns subscriptions for a `/1/ws` connection. It checks account-scoped subscriptions
before starting a topic and numbers every outgoing message for that subscription. Deltas waiting on
one connection are bounded at 256. When another message would exceed the bound, the whole
subscription is closed with `OVERFLOW`; no delta is silently dropped and other subscriptions remain
independent.

Register a topic as one singleton `ILiveTopic`. Put the topic, its writer and any small query
extensions in `server/Common/Features/<Feature>/`. Only the writer for that feature may mutate the
topic's stored data and announce changes. In particular, bulk `ExecuteUpdate` and `ExecuteDelete`
must not bypass the after-commit publication path.

The snapshot builder is also the REST and MCP query. Do not create a second representation for the
same read. Deltas may read only changed rows, but the resulting rows must use the same mapping as
the snapshot.

The `friends` topic reads `FriendQueries.ListFriendsAsync`, so its snapshot is the same list as
`GET /1/accounts/{id}/friends`. `FriendsTopic` shares one feed per subscribed account, coalesces
committed changes for 250 ms, and sends only rows that differ from the last copy. `FriendWriter`
announces friendships, listed user fields, presence, pending-offline state and memo changes after
commit. A feed that fails to read changes closes its subscriptions with `ERROR`.

The `notifications` topic reads `NotificationQueries.Notifications` with hidden rows included, so
its snapshot is the first page of `GET /1/accounts/{id}/notifications?includeHidden=true`: the
newest 500, newest first. Older notifications are only on REST, and a row older than those is not
sent when it changes. `NotificationWriter` announces every notification a `SaveChanges` wrote, and
makes the two bulk changes itself (hiding friend requests answered elsewhere, taking a row off the
page). A removal means the row left the list: it was taken off the page, or it is a friend request
or group notice that was answered. A row carries its sender's name as it was when the row last
changed.

`AccountRowsTopic<TRow>` is the base for a topic of this shape: rows per account with a key, one
shared feed per subscribed account, coalescing, and whole-row upserts and removals. A new topic
overrides the key, the read (everything, or just some keys) and the two payload builders, and its
writer calls `Changed(accountId, keys)` after commit. The friends topic predates it and does the
same by hand, because a change to one user reaches every account that has them as a friend.

## Client

`client/src/lib/api/topics.svelte.ts` owns the socket subscription lifecycle. It reference-counts a
shared store by topic and account, subscribes after every `ServerHello`, detects revision gaps, and
resubscribes after reconnects and retryable closes. A server hello that omits a topic makes that
store `unsupported`.

The notification center (the sidebar bell) follows the `notifications` topic the same way the
friend list follows `friends`: it waits up to four seconds for the first snapshot, shows the rows
that are not hidden, and falls back to REST plus events without it. The Notifications page still
reads its pages over REST, since it shows more than the topic carries.

Keep each feature's encoding and reduction in a plain module:

- `client/src/lib/live/topics/<topic>.ts` maps a snapshot and deltas to the feature's value.
- `client/src/lib/live/topics/<topic>.svelte.ts` is the thin shared store wrapper.
- `client/src/lib/__tests__/<topic>.test.ts` covers initial values, deltas and revision gaps.

The friends feature waits up to four seconds for its first snapshot. If the topic is unsupported,
closed or slow, it reads the friend list over REST once and retains the former `ServerEvent`
fallback. A snapshot received later takes ownership of the list. Consumers do not call the REST
client for a feature once its live store is in charge.

## Adding a topic

1. Add payload cases and comments to `vrcx_live.proto` without renumbering existing fields, then
   regenerate the client protobuf code through `pnpm run check`.
2. Reuse or extract the feature's shared query. Implement a snapshot and ordered delta behavior in
   a singleton `ILiveTopic`; for rows per account, derive from `AccountRowsTopic<TRow>`.
3. Route every relevant write through the feature writer and publish only after commit. Account
   for changes kept outside Postgres, such as Redis presence.
4. Add server scenario tests for the snapshot, each change source, `rev` ordering, gap recovery,
   overflow, unsubscribe and reconnect. Pin SQL round trips for the snapshot.
5. Add the client codec and `$state` store, pure reducer tests, and a REST/events fallback for
   servers that do not advertise the topic.
6. Document new lifecycle or payload rules here and update the transport section of
   `server/README.md`.
