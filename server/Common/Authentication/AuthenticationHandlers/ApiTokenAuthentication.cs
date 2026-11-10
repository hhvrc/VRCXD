using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using OpenShock.Internal.Common.Problems;
using VRCX.Common.Constants;
using VRCX.Common.Errors;
using VRCX.Common.JsonSerialization;
using VRCX.Common.Options;

namespace VRCX.Common.Authentication.AuthenticationHandlers;

/// <summary>
/// The one credential this server knows: the shared token from VRCX:Auth:Token, sent as
/// <c>VrcxToken: …</c> or <c>Authorization: Bearer …</c>. The server only runs behind a
/// LAN/VPN boundary; this keeps a stray browser on that network out of friend history.
/// </summary>
public sealed class ApiTokenAuthentication : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly byte[] _expected;
    private OpenShockProblem? _authResultError;

    public ApiTokenAuthentication(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        AuthOptions authOptions)
        : base(options, logger, encoder)
    {
        _expected = Encoding.UTF8.GetBytes(authOptions.Token);
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!TryGetToken(out var token))
            return Task.FromResult(Fail(AuthResultError.HeaderMissingOrInvalid));

        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(token), _expected))
            return Task.FromResult(Fail(AuthResultError.TokenInvalid));

        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.AuthenticationMethod, VrcxAuthSchemes.ApiToken),
            new Claim(VrcxAuthClaims.Subject, "api-token"),
        ], VrcxAuthSchemes.ApiToken);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    private bool TryGetToken(out string token)
    {
        if (Context.Request.Headers.TryGetValue(AuthConstants.ApiTokenHeaderName, out var header) && !string.IsNullOrEmpty(header))
        {
            token = header.ToString();
            return true;
        }
        var authorization = Context.Request.Headers.Authorization.ToString();
        const string bearer = "Bearer ";
        if (authorization.StartsWith(bearer, StringComparison.Ordinal) && authorization.Length > bearer.Length)
        {
            token = authorization[bearer.Length..];
            return true;
        }
        // Browsers cannot set headers on a websocket upgrade or an img tag, so /1/ws and /1/images also take ?token=.
        if ((Context.WebSockets.IsWebSocketRequest || Context.Request.Path.StartsWithSegments("/1/images")) && Context.Request.Query.TryGetValue("token", out var query) && !string.IsNullOrEmpty(query))
        {
            token = query.ToString();
            return true;
        }
        token = "";
        return false;
    }

    private AuthenticateResult Fail(OpenShockProblem reason)
    {
        _authResultError = reason;
        return AuthenticateResult.Fail(reason.Title!);
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        _authResultError ??= AuthResultError.UnknownError;
        return _authResultError.WriteAsJsonAsync(Context, JsonOptions.Default, Context.RequestAborted);
    }
}
