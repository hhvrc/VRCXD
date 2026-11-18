---
name: add-live-topic
description: PROVISIONAL. How new live data reaches pages over /1/ws as a subscribed topic (snapshot, then deltas) instead of a ServerEvent plus a REST refetch. The topic machinery is being built with the friends topic and is not on master yet. Use when adding or changing live data on the websocket.
---

# Add a live topic (provisional)

**Not built yet.** The friends topic, the first one, is being built in another worktree. Until it
lands on `master` none of the types below exist, and this skill will be rewritten from that code.
Before adding live data, ask the owner whether to wait for it. Do not build a second topic
mechanism of your own.

## The design being built

- A `TopicHub` on the server holds subscriptions per connection on `/1/ws`.
- `ClientMessage` gains `Subscribe` / `Unsubscribe`; `ServerMessage` gains `TopicSnapshot`,
  `TopicDelta` and `TopicClosed` (`server/Common/Protos/vrcx_live.proto`; add `oneof` cases, never
  renumber or reuse a number).
- Every subscription carries a `rev`. A gap in `rev`, or an overflowing subscriber, means the
  client resubscribes and gets a fresh snapshot. A reconnect resubscribes the same way.
- Topics are independent streams, not request/response.
- The feature's writer, for friends `FriendWriter` in `server/Common/Features/Friends/`, is the
  only code that changes the topic's tables and emits its deltas. No `ExecuteUpdate` or
  `ExecuteDelete` on them elsewhere: bulk writes bypass `LiveChangeInterceptor` and publish
  nothing.
- The snapshot builder is the same code that serves the feature's REST GET and its MCP read.
- Client: `client/src/lib/live/topics/<topic>.svelte.ts`, a thin `$state` shell over a pure
  reducer module `<topic>.ts` (`applySnapshot`, `applyDelta`) covered by vitest. Components use
  the store, not `#lib/api/client`, for that feature.

## Tests to expect

- Server: a scenario that subscribes, checks the snapshot, causes a change (pipeline frame, sync,
  game log) and checks the delta and its `rev`; a gap forces a fresh snapshot. Pin SQL round trips
  for the snapshot in `QueryCountTests`.
- Client: reducer tests for `applySnapshot` and `applyDelta`, including an out-of-order `rev`.
