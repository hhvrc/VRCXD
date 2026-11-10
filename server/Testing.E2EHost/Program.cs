using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Text.Json.Nodes;
using VRCX.Common.Services.Ingestion;
using VRCX.Testing;
using VRCX.Testing.FakeVrchat;

// The server the client's Playwright tests run against (client/e2e). One VrcxScenario on a real
// port with a seeded fake account, plus a control API on a second port for pushing pipeline
// frames. Usage: dotnet run --project Server/Testing.E2EHost -- [apiPort] [controlPort]
// (defaults 5299 and 5298). Prints "VRCX_E2E_READY <url>" once the account is logged in.
var apiPort = args.Length > 0 ? int.Parse(args[0]) : 5299;
var controlPort = args.Length > 1 ? int.Parse(args[1]) : 5298;

await using var app = await VrcxScenario.StartAsync(new ScenarioOptions { KestrelPort = apiPort });

var world = app.Vrchat.World("wrld_e2e_lobby", "E2E Lobby");
app.Vrchat.SetMe("usr_e2e_me", "E2E Tester");
var alice = app.Vrchat.Friend("usr_e2e_alice", "Alice Example", u => u.OnlineAt($"{world.Id}:1001~region(eu)").Status("join me", "around"));
var bob = app.Vrchat.Friend("usr_e2e_bob", "Bob Example", u => u.Offline());
var carol = app.Vrchat.Friend("usr_e2e_carol", "Carol Example", u => u.Active());
await app.LoginAsync();

var friends = new Dictionary<string, FakeUser>(StringComparer.Ordinal) { [alice.Id] = alice, [bob.Id] = bob, [carol.Id] = carol };

var builder = WebApplication.CreateSlimBuilder();
builder.WebHost.UseUrls($"http://127.0.0.1:{controlPort}");
builder.Logging.SetMinimumLevel(LogLevel.Warning);
var control = builder.Build();
control.MapGet("/health", () => Results.Ok(new { api = app.BaseAddress.ToString(), account = app.AccountId }));
control.MapPost("/friends/{id}/online", async (string id, string? location) =>
{
    if (!friends.TryGetValue(id, out var friend)) return Results.NotFound();
    await app.Pipeline.FriendOnlineAsync(friend, location ?? $"{world.Id}:2002~region(eu)", world);
    return Results.NoContent();
});
control.MapPost("/friends/{id}/offline", async (string id) =>
{
    if (!friends.TryGetValue(id, out var friend)) return Results.NotFound();
    await app.Pipeline.FriendOfflineAsync(friend);
    // Past the pending-offline delay, so the change lands now.
    app.Time.Advance(PresenceDebouncer.Delay + TimeSpan.FromSeconds(1));
    await app.Jobs.SweepPendingPresenceAsync();
    return Results.NoContent();
});
control.MapPost("/notifications/friend-request", async (string from, string name) =>
{
    var sender = app.Vrchat.User(from, name);
    await app.Pipeline.NotificationAsync(app.Vrchat.Notification("friendRequest", sender));
    return Results.NoContent();
});
control.MapPost("/pipeline", async (JsonObject frame) =>
{
    await app.Pipeline.SendRawAsync(frame.ToJsonString());
    return Results.NoContent();
});
await control.StartAsync();

Console.WriteLine($"VRCX_E2E_READY {app.BaseAddress} account={app.AccountId} control=http://127.0.0.1:{controlPort}");
var stop = new TaskCompletionSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.TrySetResult(); };
AppDomain.CurrentDomain.ProcessExit += (_, _) => stop.TrySetResult();
await stop.Task;
await control.StopAsync();
