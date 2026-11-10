namespace VRCX.Common.Options;

/// <summary>VRCX:DB</summary>
public sealed class DatabaseOptions
{
    public required string Conn { get; init; }
    /// <summary>True for hosts that do not own the schema (Cron) and for integration tests, which migrate once up front.</summary>
    public bool SkipMigration { get; init; }
    public bool Debug { get; init; }
}

/// <summary>VRCX:Redis - current presence and other short-lived state. Everything durable stays in Postgres.</summary>
public sealed class RedisOptions
{
    public required string Conn { get; init; }
}

/// <summary>VRCX:Images - where VRChat's images are kept on disk.</summary>
public sealed class ImageOptions
{
    public string Dir { get; init; } = "image-cache";
}

/// <summary>
/// VRCX:Keys - where the keys that encrypt stored VRChat session cookies are kept. Outside the
/// database on purpose: a database dump alone must not be enough to sign in as the account.
/// Losing the keys only means logging in to VRChat again.
/// </summary>
public sealed class KeyOptions
{
    public string Dir { get; init; } = "keys";
}

/// <summary>
/// VRCX:ErrorReporting - where opted-in clients send error reports (a Sentry DSN). Unset means
/// error reporting is off: no client asks for consent and nothing is sent anywhere.
/// </summary>
public sealed class ErrorReportingOptions
{
    public string? Dsn { get; init; }
}

/// <summary>VRCX:Auth - the one shared token every API caller presents.</summary>
public sealed class AuthOptions
{
    public required string Token { get; init; }
}

/// <summary>
/// VRCX:Vrchat - extra hosts a custom development endpoint may use, besides VRChat's own domains.
/// The server sends the account's session to that endpoint, so only list hosts you trust.
/// </summary>
public sealed class VrchatOptions
{
    public IReadOnlyList<string> EndpointHosts { get; init; } = [];
}

/// <summary>VRCX:Metrics - networks allowed to read /metrics and /health. Private ranges by default.</summary>
public sealed class MetricsOptions
{
    public IReadOnlyList<string> AllowedNetworks { get; init; } = [];
}
