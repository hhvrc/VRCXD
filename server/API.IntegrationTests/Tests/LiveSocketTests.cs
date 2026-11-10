using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text.Json;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using VRCX.Common.Live;

namespace VRCX.API.IntegrationTests.Tests;

// The protobuf websocket: hello both ways, a game-log batch in, an ack and a live event out,
// and the rows it left behind visible over REST.
public sealed class LiveSocketTests
{
    [ClassDataSource<IsolatedHost>(Shared = SharedType.None)]
    public required IsolatedHost Factory { get; init; }

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    [Test]
    public async Task Without_a_token_the_upgrade_is_refused()
    {
        var client = Factory.Server.CreateWebSocketClient();
        var ex = await Assert.ThrowsAsync<Exception>(async () =>
            await client.ConnectAsync(new Uri(Factory.Server.BaseAddress, "/1/ws"), CancellationToken.None));
        await Assert.That(ex).IsNotNull();
    }

    [Test]
    public async Task Game_log_batch_becomes_visits_and_a_live_event()
    {
        using var socket = await ConnectAsync();
        var hello = await ReceiveAsync(socket);
        await Assert.That(hello.MessageCase).IsEqualTo(ServerMessage.MessageOneofCase.Hello);
        await Assert.That(hello.Hello.ProtocolVersion).IsEqualTo(1u);

        await SendAsync(socket, new ClientMessage { Hello = new ClientHello { ClientId = "pc-1", ClientName = "test", ProtocolVersion = 1 } });

        var t0 = Timestamp.FromDateTimeOffset(new DateTimeOffset(2026, 10, 31, 20, 0, 0, TimeSpan.Zero));
        await SendAsync(socket, new ClientMessage
        {
            GameLog = new GameLogBatch
            {
                Entries =
                {
                    new LogEntry { CreatedAt = t0, UserAuthenticated = new LogPlayer { DisplayName = "Me", UserId = "usr_pc" } },
                    new LogEntry { CreatedAt = t0, Location = new LogLocation { Location = "wrld_log:777~private(usr_pc)~region(eu)", WorldName = "Log World" } },
                    new LogEntry { CreatedAt = t0, PlayerJoined = new LogPlayer { DisplayName = "Guest", UserId = "usr_guest" } },
                    new LogEntry { CreatedAt = t0, VideoPlay = new LogVideoPlay { VideoUrl = "https://example.test/v.mp4", DisplayName = "Guest" } },
                    // A fresh screenshot line: the server answers with a ProcessScreenshot action (settings default to the helper being on).
                    new LogEntry { CreatedAt = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow), Screenshot = new LogText { Text = @"C:\Pictures\VRChat\shot.png" } },
                },
            },
        });

        // The ack, the action and the visit events arrive in some order; collect until we have them all.
        ServerMessage? ack = null;
        ClientAction? action = null;
        var visits = new List<InstanceVisitChanged>();
        GameLogStored? stored = null;
        while (ack is null || action is null || visits.Count < 2 || stored is null)
        {
            var message = await ReceiveAsync(socket);
            if (message.MessageCase == ServerMessage.MessageOneofCase.GameLogAck) ack = message;
            else if (message.MessageCase == ServerMessage.MessageOneofCase.Action) action = message.Action;
            else if (message.MessageCase == ServerMessage.MessageOneofCase.Event && message.Event.EventCase == ServerEvent.EventOneofCase.Visit) visits.Add(message.Event.Visit);
            else if (message.MessageCase == ServerMessage.MessageOneofCase.Event && message.Event.EventCase == ServerEvent.EventOneofCase.GameLog) stored = message.Event.GameLog;
        }
        await Assert.That(ack!.GameLogAck.Accepted).IsEqualTo(5);
        // The video play is announced too, so game log views refresh for more than stays.
        await Assert.That(stored!.Kinds).Contains("video");
        await Assert.That(stored.InstanceLocation).IsEqualTo("wrld_log:777~private(usr_pc)~region(eu)");
        await Assert.That(action!.ActionCase).IsEqualTo(ClientAction.ActionOneofCase.ProcessScreenshot);
        await Assert.That(action.ProcessScreenshot.Path).IsEqualTo(@"C:\Pictures\VRChat\shot.png");
        await Assert.That(action.ProcessScreenshot.WorldId).IsEqualTo("wrld_log");
        await Assert.That(action.ProcessScreenshot.MetadataJson).Contains("\"name\":\"Log World\"");
        await Assert.That(action.ProcessScreenshot.MetadataJson).Contains("usr_guest");
        await Assert.That(visits.Select(v => v.UserId)).IsEquivalentTo(new[] { "usr_pc", "usr_guest" });
        await Assert.That(visits.All(v => v.Joined && v.Source == "game_log")).IsTrue();

        using var http = Factory.CreateAuthenticatedClient();
        var instance = await http.GetFromJsonAsync<JsonElement>("/1/instances/wrld_log:777~private(usr_pc)~region(eu)");
        await Assert.That(instance.GetProperty("worldName").GetString()).IsEqualTo("Log World");
        await Assert.That(instance.GetProperty("present").EnumerateArray().Select(p => p.GetProperty("userId").GetString()!)).IsEquivalentTo(new[] { "usr_pc", "usr_guest" });

        // Leaving closes both visits and announces it.
        await SendAsync(socket, new ClientMessage { GameLog = new GameLogBatch { Entries = { new LogEntry { CreatedAt = t0, VrcQuit = new LogNoData() } } } });
        var left = new List<InstanceVisitChanged>();
        while (left.Count < 2)
        {
            var message = await ReceiveAsync(socket);
            if (message.MessageCase == ServerMessage.MessageOneofCase.Event && message.Event.EventCase == ServerEvent.EventOneofCase.Visit) left.Add(message.Event.Visit);
        }
        await Assert.That(left.All(v => !v.Joined)).IsTrue();
        var after = await http.GetFromJsonAsync<JsonElement>("/1/instances/wrld_log:777~private(usr_pc)~region(eu)");
        await Assert.That(after.GetProperty("present").GetArrayLength()).IsEqualTo(0);

        // The in-process test server tears the socket down as soon as the close handshake starts.
        try { await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None); }
        catch (Exception ex) when (ex is WebSocketException or IOException or ObjectDisposedException) { }
    }

    [Test]
    public async Task Ping_is_answered_and_a_login_is_announced()
    {
        using var socket = await ConnectAsync();
        await ReceiveAsync(socket); // hello
        await SendAsync(socket, new ClientMessage { Ping = new Ping() });
        var pong = await ReceiveAsync(socket);
        await Assert.That(pong.MessageCase).IsEqualTo(ServerMessage.MessageOneofCase.Pong);

        Factory.Vrchat.Json("auth/user", """{"id":"usr_live","displayName":"Live","tags":[]}""");
        using var http = Factory.CreateAuthenticatedClient();
        var login = await http.PostAsJsonAsync("/1/accounts/login", new { username = "live", password = "pw" });
        await Assert.That(login.IsSuccessStatusCode).IsTrue();

        ServerMessage message;
        do { message = await ReceiveAsync(socket); }
        while (message.MessageCase != ServerMessage.MessageOneofCase.Event || message.Event.EventCase != ServerEvent.EventOneofCase.Account);
        await Assert.That((message.Event.Account.AccountId, message.Event.Account.SessionValid)).IsEqualTo(("usr_live", true));
    }

    private async Task<WebSocket> ConnectAsync()
    {
        var client = Factory.Server.CreateWebSocketClient();
        client.ConfigureRequest = request => request.Headers[Common.Constants.AuthConstants.ApiTokenHeaderName] = IsolatedHost.ApiToken;
        return await client.ConnectAsync(new Uri(Factory.Server.BaseAddress, "/1/ws"), CancellationToken.None);
    }

    private static async Task SendAsync(WebSocket socket, ClientMessage message) =>
        await socket.SendAsync(message.ToByteArray(), WebSocketMessageType.Binary, true, CancellationToken.None);

    private static async Task<ServerMessage> ReceiveAsync(WebSocket socket)
    {
        using var cts = new CancellationTokenSource(Timeout);
        var buffer = new byte[64 * 1024];
        using var frame = new MemoryStream();
        ValueWebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(buffer.AsMemory(), cts.Token);
            if (result.MessageType == WebSocketMessageType.Close) throw new InvalidOperationException("socket closed by server");
            frame.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);
        return ServerMessage.Parser.ParseFrom(frame.GetBuffer(), 0, (int)frame.Length);
    }
}
