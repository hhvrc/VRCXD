using System.Net;

namespace VRCX.Vrchat;

/// <summary>VRChat answered with a non-success status (other than the 404s the Get* methods turn into null).</summary>
public class VrchatApiException : Exception
{
    public HttpStatusCode StatusCode { get; }
    public string Method { get; }
    public string Path { get; }
    /// <summary>VRChat's own error.message, when the body had one.</summary>
    public string? VrchatMessage { get; }

    public VrchatApiException(HttpStatusCode statusCode, string method, string path, string? vrchatMessage)
        : base($"VRChat {method} {path} returned {(int)statusCode}{(vrchatMessage is null ? "" : $": {vrchatMessage}")}")
    {
        StatusCode = statusCode;
        Method = method;
        Path = path;
        VrchatMessage = vrchatMessage;
    }
}

/// <summary>401 - the session cookie is gone or expired; the account has to log in again.</summary>
public sealed class VrchatUnauthorizedException(string method, string path, string? vrchatMessage)
    : VrchatApiException(HttpStatusCode.Unauthorized, method, path, vrchatMessage);
