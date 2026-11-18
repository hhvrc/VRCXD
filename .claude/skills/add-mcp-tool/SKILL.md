---
name: add-mcp-tool
description: Add or change a tool on the server's MCP endpoint (/1/mcp, server/API/Mcp). Covers the annotations, account resolution, paging, the shared query services, the McpTests pins, and the rule that no tool calls VRChat. Use when adding, renaming or changing an MCP tool.
---

# Add an MCP tool

MCP v1 is read-only over what the server stored, plus VRCXD's own memos. A tool never calls
VRChat and never writes anything there. A tool that would write to VRChat needs scoped, revocable
tokens first; stop and ask the owner if that is what is wanted.

## 1. The query

Put the query in the shared read model, not in the tool: today
`server/Common/Services/Queries/<Area>Queries.cs` (registered scoped in
`Common/VrcxServiceHelper.cs`), or the feature's folder under `Common/Features/` once that
feature has moved. If a REST endpoint already answers the same question, move its query there and
call it from both, keeping the route unchanged. New tools inject the query service, not
`VrcxContext`, `VrchatApiClient` or `IAccountSessionManager`.

## 2. The tool

In the matching class in `server/API/Mcp/` (`FriendTools`, `UserTools`, `FeedTools`,
`GameLogTools`, `ListTools`, `ContentTools`, `AccountTools`, `MemoTools`). A new class needs
`[McpServerToolType]` and an entry in `McpSetup.ToolTypes`.

```csharp
[McpServerTool(Name = "get_thing", Title = "Thing", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
[Description("""
    What it returns, in the words a user asks with. Where the data comes from and what it
    cannot know ("only what the server observed while it was running").
    """)]
public async Task<McpPage<McpThing>> GetThing(
    [Description("usr_ id or display name of the logged-in account; optional when only one account exists.")] string? account = null,
    [Description("Rows to return, 1-200 (default 50).")] int limit = McpPaging.DefaultLimit,
    [Description("Rows to skip, from a previous result's nextOffset.")] int offset = 0,
    CancellationToken cancellationToken = default)
{
    var who = await accounts.ResolveAsync(account, cancellationToken);
    ...
    return McpPaging.Page(who, rows, offset, limit);   // or PageAsync over an IQueryable
}
```

- Name in `snake_case`, starting with a verb (`get_`, `find_`, `search_`, `list_`, `set_`).
- State `ReadOnly` and `Destructive` explicitly, and `OpenWorld = false`,
  `UseStructuredContent = true`. Reads are `ReadOnly = true, Destructive = false`. A memo write is
  `ReadOnly = false, Destructive = true` (it replaces the whole memo, so clients confirm it).
- Account-scoped tools take an optional `account` and call `McpAccountResolver.ResolveAsync`;
  it fails with the account list rather than pick one. Include the account in the result.
- Lists return `McpPage<T>` through `McpPaging` (50 by default, 200 at most, `truncated` and
  `nextOffset`).
- Results are records named `Mcp<Thing>`. Give locations as `McpLocation` through
  `McpLocations.NamerAsync`, and validate ids with `McpLocations.RequireId`.
- Fail with `McpException` and a message that says what to pass instead.
- Add the tool to the "Which tool" list in `McpSetup.Instructions`.

## 3. Tests (`server/API.IntegrationTests/Tests/McpTests.cs`)

- Add the name to `ToolNames` (sorted), and to `WriteTools` if it writes. The pins check
  annotations, output schema, description and registration for every tool.
- Add a scenario that arranges data with the fakes, calls `app.Vrchat.ClearRequests()`, calls the
  tool through `await using var mcp = await app.ConnectMcpAsync();` (scenario started with
  `new ScenarioOptions { EnableMcp = true }`), asserts on the structured result, and ends with
  `await Assert.That(app.Vrchat.Calls).IsEmpty();`.
- If the tool shares a moved query with REST, the REST tests must still pass unchanged.

```sh
cd server
dotnet build VRCXBackend.slnx
dotnet test --project API.IntegrationTests --treenode-filter "/*/*/McpTests/*"
```

Update the MCP section of `server/README.md` if what the tools can do changes.
