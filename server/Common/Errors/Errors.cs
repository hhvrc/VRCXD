using System.Net;
using OpenShock.Internal.Common.Problems;

namespace VRCX.Common.Errors;

// One static class per domain; every API failure is one of these, serialized as
// application/problem+json by OpenShockProblem. Type strings are stable contract.

public static class AuthResultError
{
    public static OpenShockProblem UnknownError => new("Authentication.UnknownError", "An unknown error occurred.", HttpStatusCode.InternalServerError);
    public static OpenShockProblem HeaderMissingOrInvalid => new("Authentication.HeaderMissingOrInvalid", "Missing or invalid authentication header.", HttpStatusCode.Unauthorized);
    public static OpenShockProblem TokenInvalid => new("Authentication.TokenInvalid", "The token is invalid", HttpStatusCode.Unauthorized);
}

public static class LoginError
{
    public static OpenShockProblem InvalidCredentials(string? detail) => new("Login.InvalidCredentials", "VRChat rejected the username or password", HttpStatusCode.Unauthorized, detail);
    public static OpenShockProblem UnknownAttempt => new("Login.UnknownAttempt", "Unknown or expired login attempt", HttpStatusCode.NotFound);
    public static OpenShockProblem CodeRejected => new("Login.CodeRejected", "VRChat did not accept the two-factor code", HttpStatusCode.Unauthorized);
    public static OpenShockProblem UnknownMethod => new("Login.UnknownMethod", "The two-factor method must be totp, otp or emailOtp", HttpStatusCode.BadRequest);
    public static OpenShockProblem VrchatUnavailable(string? detail) => new("Login.VrchatUnavailable", "VRChat could not be reached", HttpStatusCode.BadGateway, detail);
}

public static class AccountError
{
    public static OpenShockProblem AccountNotFound => new("Account.NotFound", "No such account is logged into this server", HttpStatusCode.NotFound);
    public static OpenShockProblem NotificationNotFound => new("Notification.NotFound", "No such notification", HttpStatusCode.NotFound);
    public static OpenShockProblem NoLiveSession => new("Account.NoLiveSession", "The account has no live VRChat session; log in again", HttpStatusCode.Conflict);
    public static OpenShockProblem FetchAlreadyRunning => new("Account.FetchAlreadyRunning", "A fetch is already running for this account", HttpStatusCode.Conflict);
    public static OpenShockProblem NoAccounts => new("Account.NoAccounts", "No VRChat account is logged into this server", HttpStatusCode.Conflict);
}

public static class UserError
{
    public static OpenShockProblem UserNotFound => new("User.NotFound", "User not found", HttpStatusCode.NotFound);
    public static OpenShockProblem MemoNotFound => new("User.MemoNotFound", "No memo on this user", HttpStatusCode.NotFound);
}

public static class WorldError
{
    public static OpenShockProblem WorldNotFound => new("World.NotFound", "World not found", HttpStatusCode.NotFound);
}

public static class AvatarError
{
    public static OpenShockProblem AvatarNotFound => new("Avatar.NotFound", "Avatar not found", HttpStatusCode.NotFound);
}

public static class GroupError
{
    public static OpenShockProblem GroupNotFound => new("Group.NotFound", "Group not found", HttpStatusCode.NotFound);
}

public static class InstanceError
{
    public static OpenShockProblem InstanceNotFound => new("Instance.NotFound", "Instance not found", HttpStatusCode.NotFound);
    public static OpenShockProblem NotAnInstance => new("Instance.NotALocation", "That is not an instance location tag", HttpStatusCode.BadRequest);
}

public static class DesktopError
{
    public static OpenShockProblem NotConnected => new("Desktop.NotConnected", "No desktop app for this account is connected", HttpStatusCode.Conflict);
    public static OpenShockProblem GameRunning => new("Desktop.GameRunning", "VRChat is already running on that PC", HttpStatusCode.Conflict);
    public static OpenShockProblem NoAnswer => new("Desktop.NoAnswer", "The desktop app did not answer; it may need updating", HttpStatusCode.GatewayTimeout);
    public static OpenShockProblem ActionFailed(string? detail) => new("Desktop.ActionFailed", "The desktop app could not do that", HttpStatusCode.BadGateway, detail);
    public static OpenShockProblem InvalidLaunchUrl => new("Desktop.InvalidLaunchUrl", "That is not a VRChat launch link", HttpStatusCode.BadRequest);
}

public static class SearchError
{
    public static OpenShockProblem BadProvider => new("Search.BadProvider", "The avatar database address must be http or https", HttpStatusCode.BadRequest);
    public static OpenShockProblem ProviderFailed(string? detail) => new("Search.ProviderFailed", "The avatar database failed", HttpStatusCode.BadGateway, detail);
}

public static class VrchatError
{
    public static OpenShockProblem Upstream(string? detail) => new("Vrchat.Upstream", "VRChat returned an error", HttpStatusCode.BadGateway, detail);
    public static OpenShockProblem SessionExpired => new("Vrchat.SessionExpired", "VRChat no longer accepts this account's session; log in again", HttpStatusCode.Conflict);
}
