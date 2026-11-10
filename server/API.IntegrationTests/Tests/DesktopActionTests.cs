using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text.Json;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using VRCX.Common.Live;

namespace VRCX.API.IntegrationTests.Tests;

// Launching goes page to server to desktop app: the app answers over its socket and the REST call returns that.
public sealed class DesktopActionTests
{
    [ClassDataSource<IsolatedHost>(Shared = SharedType.None)]
    public required IsolatedHost Factory { get; init; }

    private const string Url = "vrchat://launch?ref=vrcx.app&id=wrld_a:1~hidden(usr_a)~region(eu)&shortName=abc-1";

    private static string AccountPath(string account, string rest) => $"/1/accounts/{account}/{rest}";

    [Test]
    public async Task With_no_desktop_app_the_launch_is_refused()
    {
        using var http = Factory.CreateAuthenticatedClient();

        var status = await http.GetFromJsonAsync<JsonElement>(AccountPath("usr_nobody", "desktop"));
        await Assert.That(status.GetProperty("connected").GetBoolean()).IsFalse();

        var response = await http.PostAsJsonAsync(AccountPath("usr_nobody", "game/launch"), new { url = Url, desktopMode = false, allowSecondInstance = false });
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        await Assert.That(await response.Content.ReadAsStringAsync()).Contains("Desktop.NotConnected");
    }

    [Test]
    public async Task Anything_but_a_launch_link_is_refused_before_it_reaches_the_app()
    {
        using var http = Factory.CreateAuthenticatedClient();
        foreach (var bad in new[] { "https://example.test", "vrchat://launch?ref=vrcx.app&id=wrld_a:1 --evil", "vrchat://launch?ref=vrcx.app&id=a&x=\"y\"" })
        {
            var response = await http.PostAsJsonAsync(AccountPath("usr_nobody", "game/open"), new { url = bad });
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        }
    }

    [Test]
    public async Task The_app_of_the_account_does_the_launch_and_its_answer_comes_back()
    {
        const string account = "usr_launch_a";
        using var socket = await ConnectDesktopAsync("pc-launch-a", account);
        using var other = await ConnectDesktopAsync("pc-launch-other", "usr_launch_other");
        using var http = Factory.CreateAuthenticatedClient();

        await SendAsync(socket, new ClientMessage { GameState = new GameState { Running = true } });
        await WaitForAsync(async () => (await http.GetFromJsonAsync<JsonElement>(AccountPath(account, "desktop"))).GetProperty("gameRunning").GetBoolean());
        var status = await http.GetFromJsonAsync<JsonElement>(AccountPath(account, "desktop"));
        await Assert.That(status.GetProperty("clientId").GetString()).IsEqualTo("pc-launch-a");

        // Refused while the game runs, then allowed once the second copy is asked for.
        var refused = http.PostAsJsonAsync(AccountPath(account, "game/launch"), new { url = Url, desktopMode = true, allowSecondInstance = false });
        var first = await NextActionAsync(socket);
        await Assert.That(first.ActionCase).IsEqualTo(ClientAction.ActionOneofCase.LaunchVrchat);
        await Assert.That(first.LaunchVrchat.LaunchUrl).IsEqualTo(Url);
        await Assert.That(first.LaunchVrchat.DesktopMode).IsTrue();
        await Assert.That(first.LaunchVrchat.AllowSecondInstance).IsFalse();
        await SendAsync(socket, new ClientMessage { ActionResult = new ActionResult { RequestId = first.RequestId, Status = ActionStatus.GameRunning } });
        var refusedResponse = await refused;
        await Assert.That(refusedResponse.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        await Assert.That(await refusedResponse.Content.ReadAsStringAsync()).Contains("Desktop.GameRunning");

        var allowed = http.PostAsJsonAsync(AccountPath(account, "game/launch"), new { url = Url, desktopMode = false, allowSecondInstance = true });
        var second = await NextActionAsync(socket);
        await Assert.That(second.LaunchVrchat.AllowSecondInstance).IsTrue();
        await SendAsync(socket, new ClientMessage { ActionResult = new ActionResult { RequestId = second.RequestId, Status = ActionStatus.Ok } });
        await Assert.That((await allowed).StatusCode).IsEqualTo(HttpStatusCode.NoContent);

        // A failure carries the app's reason.
        var failing = http.PostAsJsonAsync(AccountPath(account, "game/open"), new { url = Url });
        var third = await NextActionAsync(socket);
        await Assert.That(third.ActionCase).IsEqualTo(ClientAction.ActionOneofCase.OpenInstance);
        await SendAsync(socket, new ClientMessage { ActionResult = new ActionResult { RequestId = third.RequestId, Status = ActionStatus.Failed, Error = "pipe closed" } });
        var failure = await failing;
        await Assert.That(failure.StatusCode).IsEqualTo(HttpStatusCode.BadGateway);
        await Assert.That(await failure.Content.ReadAsStringAsync()).Contains("pipe closed");

        await CloseAsync(socket);
        await CloseAsync(other);
    }

    [Test]
    public async Task An_app_going_away_ends_the_wait_and_clears_the_state()
    {
        const string account = "usr_launch_b";
        var socket = await ConnectDesktopAsync("pc-launch-b", account);
        using var http = Factory.CreateAuthenticatedClient();

        var waiting = http.PostAsJsonAsync(AccountPath(account, "game/open"), new { url = Url });
        await NextActionAsync(socket);
        await CloseAsync(socket);
        socket.Dispose();

        await Assert.That((await waiting).StatusCode).IsEqualTo(HttpStatusCode.GatewayTimeout);
        await WaitForAsync(async () => !(await http.GetFromJsonAsync<JsonElement>(AccountPath(account, "desktop"))).GetProperty("connected").GetBoolean());
    }

    private async Task<WebSocket> ConnectDesktopAsync(string clientId, string account)
    {
        var client = Factory.Server.CreateWebSocketClient();
        client.ConfigureRequest = request => request.Headers[Common.Constants.AuthConstants.ApiTokenHeaderName] = IsolatedHost.ApiToken;
        var socket = await client.ConnectAsync(new Uri(Factory.Server.BaseAddress, "/1/ws"), CancellationToken.None);
        await SendAsync(socket, new ClientMessage { Hello = new ClientHello { ClientId = clientId, ClientName = "test", ProtocolVersion = 1, MuteEvents = true } });
        // The server learns the PC's account from its log.
        await SendAsync(socket, new ClientMessage
        {
            GameLog = new GameLogBatch { Entries = { new LogEntry { CreatedAt = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow), UserAuthenticated = new LogPlayer { DisplayName = account, UserId = account } } } },
        });
        while ((await ReceiveAsync(socket)).MessageCase != ServerMessage.MessageOneofCase.GameLogAck) { }
        return socket;
    }

    private static async Task WaitForAsync(Func<Task<bool>> condition)
    {
        for (var i = 0; i < 50; i++)
        {
            if (await condition()) return;
            await Task.Delay(100);
        }
        throw new TimeoutException("condition never held");
    }

    private static async Task<ClientAction> NextActionAsync(WebSocket socket)
    {
        while (true)
        {
            var message = await ReceiveAsync(socket);
            if (message.MessageCase == ServerMessage.MessageOneofCase.Action) return message.Action;
        }
    }

    private static async Task CloseAsync(WebSocket socket)
    {
        try { await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None); }
        catch (Exception ex) when (ex is WebSocketException or IOException or ObjectDisposedException) { }
    }

    private static async Task SendAsync(WebSocket socket, ClientMessage message) =>
        await socket.SendAsync(message.ToByteArray(), WebSocketMessageType.Binary, true, CancellationToken.None);

    private static async Task<ServerMessage> ReceiveAsync(WebSocket socket)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
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
