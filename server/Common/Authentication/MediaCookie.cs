using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using VRCX.Common.Options;

namespace VRCX.Common.Authentication;

/// <summary>
/// What lets a browser open /1/ws and load /1/images without the token in the URL. A browser cannot
/// set a header on a websocket upgrade or an img tag, so these took <c>?token=</c>, and a proxy's
/// logs then held the token. The server sets this cookie on a request the token authorized, and
/// takes it on those two paths only. Its value is derived from the token, not the token: it opens
/// the socket and images and nothing else, and a new token makes it worthless. HttpOnly, so the
/// page's scripts never see it, and SameSite=Strict, so another site cannot open the socket with it.
/// </summary>
public sealed class MediaCookie
{
    public const string Name = "vrcx_media";

    // Browsers keep a cookie for at most 400 days.
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(400);

    private readonly string _value;

    public MediaCookie(AuthOptions options)
    {
        _value = Convert.ToBase64String(HMACSHA256.HashData(Encoding.UTF8.GetBytes(options.Token), "vrcxd media cookie"u8))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>Whether the request carries the cookie for the current token.</summary>
    public bool IsOn(HttpRequest request) =>
        request.Cookies.TryGetValue(Name, out var value) && value is not null
        && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(value), Encoding.ASCII.GetBytes(_value));

    /// <summary>The paths that take the cookie: what a browser opens without being able to set a header.</summary>
    public static bool Accepts(HttpContext context) =>
        context.WebSockets.IsWebSocketRequest || context.Request.Path.StartsWithSegments("/1/images");

    public void Set(HttpContext context) =>
        context.Response.Cookies.Append(Name, _value, Options(context, Lifetime));

    public static void Clear(HttpContext context) =>
        context.Response.Cookies.Delete(Name, Options(context, null));

    private static CookieOptions Options(HttpContext context, TimeSpan? maxAge) => new()
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Strict,
        Secure = context.Request.IsHttps,
        Path = "/1",
        MaxAge = maxAge,
        IsEssential = true,
    };
}
