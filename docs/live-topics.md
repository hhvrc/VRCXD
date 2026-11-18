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
or group notice that was answered. A row shows its sender's name, so the writer also announces a
user's display name changing, and the rows that user sent are sent again in every account.

`AccountRowsTopic<TRow>` is the base for a topic of this shape: rows per account with a key, one
shared feed per subscribed account, coalescing, and whole-row upserts and removals. A new topic
overrides the key, the read (everything, or just some keys) and the two payload builders, and its
writer calls `Changed(accountId, keys)` after commit, or `ChangedWhere(match)` when what changed is
not a row's key but something rows show (a sender's name). The friends topic predates it and does
the same by hand, because a change to one user reaches every account that has them as a friend.

## Client

`client/src/lib/api/topics.svelte.ts` owns the socket subscription lifecycle. It reference-counts a
shared store by topic and account, subscribes after every `ServerHello`, detects revision gaps, and
resubscribes after reconnects and retryable closes. A subscription the server refuses is `closed`.

A topic is the only way its state reaches the client. There is no REST fallback and no "server
without the topic" case: the web client and the server are released together at one version, so
the server always has the topics the client asks for. The consequence is deliberate: while the
socket is down, what a topic carries is not shown as if it were current. The friend list says it
is waiting for the live connection once four seconds pass without a snapshot, and the
notification center stays empty.

The friend list (`sidebar-data.svelte.ts`) is the `friends` topic's rows. The notification center
(the sidebar bell) is the `notifications` topic's rows that are not hidden. The Notifications page
shows more than the topic carries, so it reads its pages over REST once when it opens and lays the
topic's rows over them (`notification-log.ts`): for the time the topic covers its rows are the
truth, and older rows stay as read. It does not read again on an event.

The `ServerEvent`s for presence, users, friendships and notifications are still sent and still
used, but not to keep these lists: the pop-up and wrist-feed decisions, the nav badges, the feed
and the Friend List and Friend Log pages listen to them.

Keep each feature's encoding and reduction in a plain module:

- `client/src/lib/live/topics/<topic>.ts` maps a snapshot and deltas to the feature's value.
- `client/src/lib/live/topics/<topic>.svelte.ts` is the thin shared store wrapper.
- `client/src/lib/__tests__/<topic>.test.ts` covers initial values, deltas and revision gaps.

Consumers do not call the REST client for what a topic carries.

## Adding a topic

1. Add payload cases and comments to `vrcx_live.proto` without renumbering existing fields, then
   regenerate the client protobuf code through `pnpm run check`.
2. Reuse or extract the feature's shared query. Implement a snapshot and ordered delta behavior in
   a singleton `ILiveTopic`; for rows per account, derive from `AccountRowsTopic<TRow>`.
3. Route every relevant write through the feature writer and publish only after commit. Account
   for changes kept outside Postgres, such as Redis presence.
4. Add server scenario tests for the snapshot, each change source, `rev` ordering, gap recovery,
   overflow, unsubscribe and reconnect. Pin SQL round trips for the snapshot.
5. Add the client codec and `$state` store with pure reducer tests, move the consumers to the
   store, and delete the REST read and the event reloads it replaces.
6. Document new lifecycle or payload rules here and update the transport section of
   `server/README.md`.
