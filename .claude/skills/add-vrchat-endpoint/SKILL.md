---
name: add-vrchat-endpoint
description: Add a call to a VRChat API endpoint on the server, through VrchatApiClient and upstream's pacing, with the FakeVrchatApi route and the request-budget tests. Use when the server needs a VRChat endpoint it does not call yet, or when a flow starts calling VRChat more often.
---

# Add a VRChat endpoint

VRChat limits requests per IP, and every account on the server sends from one. Requests are paced
exactly as upstream paces its own (`VrchatRatePolicy.Upstream`: the friend list's pages at 60 a
minute, mutual friends' pages at 5 a second, everything else as it comes; a 429 with
`Retry-After` pauses every account). A new endpoint is paced only if upstream paces its call to
it, with upstream's numbers. A request spent on something stored data could answer is still one
VRChat counts.

## 1. Do you need the call?

- Can stored data answer (Postgres, Redis presence, `CurrentUserCache` for `/me`)? Then read that.
- Does a list endpoint already return it? Do not fetch per item.
- Is it read on every dialog open or page load? Put it behind `VrchatReadCache`
  (`server/API/Services/VrchatReadCache.cs`) with a TTL, and let `?refresh=true` through at most
  every `RefreshMinAge`.
- Look at what the original does in `legacy/src/api/` and `legacy/src/coordinators/`, and match
  its request, paging and throttling.

## 2. The client method

In `server/Vrchat/VrchatApiClient.cs`, in the section for its area, using the private helpers so
the request goes through `VrchatThrottlingHandler` and the shared limiter:

```csharp
/// <summary>GET worlds/{id}/thing. Null when VRChat answers 404.</summary>
public Task<Thing?> GetThingAsync(string worldId, CancellationToken ct = default) =>
    GetOrNullAsync<Thing>($"worlds/{Escape(worldId)}/thing", ct);
```

- `GetOrNullAsync` (404 is null), `GetRequiredAsync`, `SendJsonAsync<T>`, `SendNoContentAsync`;
  paged lists through `PagesAsync`, with VRChat's page size cap.
- Escape every path segment with `Escape`.
- Response models go in `server/Vrchat/Models/<Area>.cs`, shaped like VRChat's JSON (`VrchatJson`
  options). Only the fields the server uses.
- Never create an `HttpClient` to VRChat anywhere else, and never pass
  `VrchatRatePolicy.None` outside tests.

## 3. Use it

- Get the account's client through `IAccountSessionManager`. In a moved feature that is the
  feature's actions class (`Common/Features/Friends/FriendActions.cs`); the accounts controller
  still has `RunAsync(accountId, client => ...)` in `_ApiController.cs` for what has not moved. A
  401 marks the session invalid; do not retry.
- Store what comes back through `VrchatStore` in the caller's unit of work.
- A write that the pipeline will echo (friend add, favorites) should not also trigger a refetch.

## 4. The fake

`server/Testing/FakeVrchat/FakeVrchatApi.cs`. Modelled GETs are in `Get(...)` as `switch` cases on
the path segments; writes the server's flows depend on are in `Write(...)`. Other writes answer
`200 {}` and unknown GETs 404.

- If tests need the endpoint to answer from state, add a case that answers from the model and a
  fixture builder in `Fixtures.cs` if needed. Use VRChat's real response shape.
- For one test, `app.Vrchat.Json(path, json)`, `Fail(method, path, status)` or
  `On(method, path, handler)`.

## 5. Tests

- The feature test asserts the call: `app.Vrchat.AssertCalled("GET", "worlds/wrld_x/thing")`, and
  `AssertNotCalled` for calls that must not happen.
- Pin the budget in `server/API.IntegrationTests/Tests/VrchatRequestBudgetTests.cs` when the call
  happens on a schedule, per item, or per page open: `ClearRequests()`, repeat the trigger, then
  `CountOf(method, path)` or `AssertNotCalled`. Pin the refresh gap if it is cached.
- If the call runs per item in a sync, bound the SQL too (`QueryCountTests`, `app.Sql`).

```sh
cd server
dotnet build VRCXBackend.slnx
dotnet test --project API.IntegrationTests --treenode-filter "/*/*/VrchatRequestBudgetTests/*"
dotnet test --project Common.Tests --treenode-filter "/*/*/VrchatClientTests/*"
```

MCP tools never call VRChat; do not use a new endpoint from `server/API/Mcp`.
