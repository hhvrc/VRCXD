namespace VRCX.Vrchat;

/// <summary>
/// Where an account talks to VRChat: the API and the pipeline websocket. VRChat's own servers unless
/// the account logged in with the original's "custom development endpoint" option.
/// </summary>
public sealed record VrchatEndpoint(Uri Api, Uri Pipeline)
{
    public static readonly VrchatEndpoint Default = new(new Uri(VrchatApiClient.BaseAddress), new Uri(VrchatApiClient.PipelineAddress));

    /// <summary>Hosts a custom endpoint may use without server configuration: VRChat's own domains.</summary>
    public static readonly IReadOnlyList<string> BuiltInHosts = ["vrchat.cloud", "vrchat.com"];

    public bool IsDefault => this == Default;

    /// <summary>
    /// Reads the original's endpoint and websocket fields. Both empty means VRChat's servers. The
    /// server makes these requests with the account's session, so only https and wss URLs on VRChat's
    /// domains, or on hosts the server lists, are accepted; anything else could reach the server's own
    /// network.
    /// </summary>
    public static bool TryParse(string? api, string? pipeline, IEnumerable<string> extraHosts, out VrchatEndpoint endpoint, out string? error)
    {
        endpoint = Default;
        error = null;
        api = api?.Trim();
        pipeline = pipeline?.Trim();
        if (string.IsNullOrEmpty(api) && string.IsNullOrEmpty(pipeline)) return true;
        var hosts = BuiltInHosts.Concat(extraHosts).Select(h => h.Trim().TrimStart('.').ToLowerInvariant()).Where(h => h.Length > 0).ToList();
        bool Allowed(Uri uri) => hosts.Any(h => uri.Host.Equals(h, StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith("." + h, StringComparison.OrdinalIgnoreCase));

        Uri apiUri = Default.Api, pipelineUri = Default.Pipeline;
        if (!string.IsNullOrEmpty(api))
        {
            if (!Uri.TryCreate(api.EndsWith('/') ? api : api + "/", UriKind.Absolute, out var parsed) || parsed.Scheme != Uri.UriSchemeHttps)
            {
                error = "The endpoint must be an https:// URL";
                return false;
            }
            if (!Allowed(parsed))
            {
                error = $"The endpoint host {parsed.Host} is not allowed on this server";
                return false;
            }
            apiUri = parsed;
        }
        if (!string.IsNullOrEmpty(pipeline))
        {
            if (!Uri.TryCreate(pipeline.EndsWith('/') ? pipeline : pipeline + "/", UriKind.Absolute, out var parsed) || parsed.Scheme != Uri.UriSchemeWss)
            {
                error = "The websocket must be a wss:// URL";
                return false;
            }
            if (!Allowed(parsed))
            {
                error = $"The websocket host {parsed.Host} is not allowed on this server";
                return false;
            }
            pipelineUri = parsed;
        }
        endpoint = new VrchatEndpoint(apiUri, pipelineUri);
        return true;
    }
}
