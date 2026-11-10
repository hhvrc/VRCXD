using System.Net;
using System.Text;
using VRCX.Vrchat;

namespace VRCX.Common.Tests;

/// <summary>
/// Stands in for api.vrchat.cloud: canned JSON per "path?query" (or per
/// path), 404 for anything else, every request recorded. Build a client
/// with <see cref="Client"/>.
/// </summary>
public sealed class FakeVrchat : HttpMessageHandler
{
    private readonly Dictionary<string, Func<HttpRequestMessage, HttpResponseMessage>> _routes = new(StringComparer.Ordinal);
    public List<string> Calls { get; } = [];

    public FakeVrchat Json(string pathAndQuery, string json, HttpStatusCode status = HttpStatusCode.OK)
    {
        _routes[pathAndQuery] = _ => new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        return this;
    }

    public FakeVrchat Status(string pathAndQuery, HttpStatusCode status, string body = "")
    {
        _routes[pathAndQuery] = _ => new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        return this;
    }

    public FakeVrchat Handle(string pathAndQuery, Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        _routes[pathAndQuery] = handler;
        return this;
    }

    public VrchatApiClient Client(CookieContainer? cookies = null) =>
        VrchatApiClient.Create(cookies, this, VrchatRateLimiter.Unlimited);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath["/api/1/".Length..];
        var withQuery = path + request.RequestUri.Query;
        Calls.Add($"{request.Method.Method} {withQuery}");
        if (_routes.TryGetValue(withQuery, out var handler) || _routes.TryGetValue(path, out handler))
            return Task.FromResult(handler(request));
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("""{"error":{"message":"Not Found","status_code":404}}""", Encoding.UTF8, "application/json"),
        });
    }
}
