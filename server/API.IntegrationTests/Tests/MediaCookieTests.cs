using System.Net;
using VRCX.Common.Authentication;
using VRCX.Testing;

namespace VRCX.API.IntegrationTests.Tests;

// The page's socket and images used to put the API token in their URLs (?token=), and a reverse
// proxy's logs kept it. A request with the token now sets an HttpOnly cookie, derived from the
// token, that /1/ws and /1/images take and nothing else does.
public sealed class MediaCookieTests
{
    private const string Images = "/1/images?url=https%3A%2F%2Fexample.com%2Fnot-vrchat.png";

    private static async Task<string> CookieAsync(VrcxScenario app)
    {
        var response = await app.Api.GetAsync("/1/legacy-import");
        await VrcxScenario.EnsureSuccessAsync(response, "status");
        var header = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(MediaCookie.Name + "=", StringComparison.Ordinal));
        await Assert.That(header).Contains("httponly");
        await Assert.That(header).Contains("samesite=strict");
        await Assert.That(header).Contains("path=/1");
        await Assert.That(header).DoesNotContain(VrcxScenario.ApiToken);
        return header.Split(';')[0];
    }

    private static Task<HttpResponseMessage> GetAsync(VrcxScenario app, string path, string? cookie)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (cookie is not null) request.Headers.Add("Cookie", cookie);
        return app.CreateClient(authenticated: false).SendAsync(request);
    }

    private static async Task<bool> SocketOpensAsync(VrcxScenario app, string cookie)
    {
        var client = app.Server.CreateWebSocketClient();
        client.ConfigureRequest = request => request.Headers.Cookie = cookie;
        try
        {
            using var socket = await client.ConnectAsync(new Uri(app.Server.BaseAddress, "/1/ws"), CancellationToken.None);
            return true;
        }
        catch (InvalidOperationException)
        {
            // TestServer's websocket client throws when the upgrade is answered 401.
            return false;
        }
    }

    [Test]
    public async Task A_request_with_the_token_sets_the_cookie_and_the_socket_and_images_take_it_without_the_token()
    {
        await using var app = await VrcxScenario.StartAsync();
        var cookie = await CookieAsync(app);

        await Assert.That(await SocketOpensAsync(app, cookie)).IsTrue();
        await Assert.That((await GetAsync(app, Images, cookie)).StatusCode).IsNotEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That((await GetAsync(app, Images, null)).StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);

        // A request that already carries it is not sent it again.
        var again = new HttpRequestMessage(HttpMethod.Get, "/1/legacy-import");
        again.Headers.Add("Cookie", cookie);
        var answer = await app.Api.SendAsync(again);
        await Assert.That(answer.Headers.Contains("Set-Cookie")).IsFalse();
    }

    [Test]
    public async Task The_cookie_opens_nothing_else_and_one_for_another_token_opens_nothing()
    {
        await using var app = await VrcxScenario.StartAsync();
        var cookie = await CookieAsync(app);

        await Assert.That((await GetAsync(app, "/1/legacy-import", cookie)).StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That((await GetAsync(app, "/1/database/table-sizes", cookie)).StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);

        var forged = MediaCookie.Name + "=" + "x" + cookie[(MediaCookie.Name.Length + 2)..];
        await Assert.That(await SocketOpensAsync(app, forged)).IsFalse();
        await Assert.That((await GetAsync(app, Images, forged)).StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Forgetting_the_token_takes_the_cookie_off()
    {
        await using var app = await VrcxScenario.StartAsync();
        var set = await app.Api.PostAsync("/1/auth/media-cookie", null);
        await Assert.That(set.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(set.Headers.GetValues("Set-Cookie").Any(c => c.StartsWith(MediaCookie.Name + "=", StringComparison.Ordinal))).IsTrue();

        var cleared = await app.Api.DeleteAsync("/1/auth/media-cookie");
        await Assert.That(cleared.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        var header = cleared.Headers.GetValues("Set-Cookie").Last(c => c.StartsWith(MediaCookie.Name + "=", StringComparison.Ordinal));
        await Assert.That(header).StartsWith(MediaCookie.Name + "=;");
        await Assert.That(header).Contains("expires=Thu, 01 Jan 1970");
    }
}
