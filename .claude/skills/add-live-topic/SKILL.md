---
name: add-live-topic
description: How new live data reaches pages over /1/ws as a subscribed topic (snapshot, then deltas) instead of a ServerEvent plus a REST refetch. Use when adding or changing live data on the websocket.
---

# Add a live topic

Use the existing `/1/ws` topic machinery described in `docs/live-topics.md`; do not build another
transport. The friends topic is the reference implementation.

## Server

1. Read `server/AGENTS.md`, `TopicHub.cs`, `ILiveTopic.cs`, `FriendsTopic.cs` and `FriendWriter.cs`.
2. Add protocol payload cases to `server/Common/Protos/vrcx_live.proto`. Never renumber or reuse an
   existing field.
3. Put the topic, its writer and query extensions in `server/Common/Features/<Feature>/`. For rows
   per account with a key, derive the topic from `AccountRowsTopic<TRow>` (`NotificationsTopic` is
   the example) instead of writing the feed and coalescing again.
4. Reuse the exact query used by REST and MCP for the snapshot. Deltas may reread just changed rows,
   but must emit the same mapped rows.
5. Make one feature writer own all relevant mutations and after-commit announcements. Bulk writes
   must not bypass the publication path.
6. Send an ordered snapshot followed by deltas. Close with the appropriate `TopicCloseReason`
   instead of dropping a delta.
7. Add scenario coverage for snapshot equivalence, all change sources, coalescing if used, revision
   order, unsubscribe, reconnect, account isolation and overflow. Pin snapshot SQL round trips in
   `QueryCountTests`.

## Client

1. Add a pure `client/src/lib/live/topics/<topic>.ts` codec/reducer.
2. Add `client/src/lib/live/topics/<topic>.svelte.ts` as the thin store over `subscribeTopic`.
3. Cover snapshot mapping, ordered and out-of-order deltas, reconnect and unsupported servers.
4. Move feature consumers to the store. Keep REST plus the old `ServerEvent` path as fallback for
   servers that omit the topic or do not produce a snapshot in time.

## Finish

Run the checks for every part whose generated protobuf or live handling changed. Update
`docs/live-topics.md` when the generic protocol, recovery, backpressure or extension rules change.
