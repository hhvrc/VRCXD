using System.Net;
using VRCX.Common.Problems;

namespace VRCX.Common.Errors;

// One static class per domain; every API failure is one of these, serialized as
// application/problem+json by VrcxProblem. Type strings are stable contract.

public static class AuthResultError
{
    public static VrcxProblem UnknownError => new("Authentication.UnknownError", "An unknown error occurred.", HttpStatusCode.InternalServerError);
    public static VrcxProblem HeaderMissingOrInvalid => new("Authentication.HeaderMissingOrInvalid", "Missing or invalid authentication header.", HttpStatusCode.Unauthorized);
    public static VrcxProblem TokenInvalid => new("Authentication.TokenInvalid", "The token is invalid", HttpStatusCode.Unauthorized);
}

public static class LoginError
{
    public static VrcxProblem InvalidCredentials(string? detail) => new("Login.InvalidCredentials", "VRChat rejected the username or password", HttpStatusCode.Unauthorized, detail);
    public static VrcxProblem UnknownAttempt => new("Login.UnknownAttempt", "Unknown or expired login attempt", HttpStatusCode.NotFound);
    public static VrcxProblem CodeRejected => new("Login.CodeRejected", "VRChat did not accept the two-factor code", HttpStatusCode.Unauthorized);
    public static VrcxProblem UnknownMethod => new("Login.UnknownMethod", "The two-factor method must be totp, otp or emailOtp", HttpStatusCode.BadRequest);
    public static VrcxProblem VrchatUnavailable(string? detail) => new("Login.VrchatUnavailable", "VRChat could not be reached", HttpStatusCode.BadGateway, detail);
}

public static class AccountError
{
    public static VrcxProblem AccountNotFound => new("Account.NotFound", "No such account is logged into this server", HttpStatusCode.NotFound);
    public static VrcxProblem NotificationNotFound => new("Notification.NotFound", "No such notification", HttpStatusCode.NotFound);
    public static VrcxProblem NoLiveSession => new("Account.NoLiveSession", "The account has no live VRChat session; log in again", HttpStatusCode.Conflict);
    public static VrcxProblem FetchAlreadyRunning => new("Account.FetchAlreadyRunning", "A fetch is already running for this account", HttpStatusCode.Conflict);
    public static VrcxProblem NoAccounts => new("Account.NoAccounts", "No VRChat account is logged into this server", HttpStatusCode.Conflict);
}

public static class UserError
{
    public static VrcxProblem UserNotFound => new("User.NotFound", "User not found", HttpStatusCode.NotFound);
    public static VrcxProblem MemoNotFound => new("User.MemoNotFound", "No memo on this user", HttpStatusCode.NotFound);
}

public static class WorldError
{
    public static VrcxProblem WorldNotFound => new("World.NotFound", "World not found", HttpStatusCode.NotFound);
}

public static class AvatarError
{
    public static VrcxProblem AvatarNotFound => new("Avatar.NotFound", "Avatar not found", HttpStatusCode.NotFound);
}

public static class GroupError
{
    public static VrcxProblem GroupNotFound => new("Group.NotFound", "Group not found", HttpStatusCode.NotFound);
}

public static class InstanceError
{
    public static VrcxProblem InstanceNotFound => new("Instance.NotFound", "Instance not found", HttpStatusCode.NotFound);
    public static VrcxProblem NotAnInstance => new("Instance.NotALocation", "That is not an instance location tag", HttpStatusCode.BadRequest);
}

public static class DesktopError
{
    public static VrcxProblem NotConnected => new("Desktop.NotConnected", "No desktop app for this account is connected", HttpStatusCode.Conflict);
    public static VrcxProblem GameRunning => new("Desktop.GameRunning", "VRChat is already running on that PC", HttpStatusCode.Conflict);
    public static VrcxProblem NoAnswer => new("Desktop.NoAnswer", "The desktop app did not answer; it may need updating", HttpStatusCode.GatewayTimeout);
    public static VrcxProblem ActionFailed(string? detail) => new("Desktop.ActionFailed", "The desktop app could not do that", HttpStatusCode.BadGateway, detail);
    public static VrcxProblem InvalidLaunchUrl => new("Desktop.InvalidLaunchUrl", "That is not a VRChat launch link", HttpStatusCode.BadRequest);
}

public static class SearchError
{
    public static VrcxProblem BadProvider => new("Search.BadProvider", "The avatar database address must be http or https", HttpStatusCode.BadRequest);
    public static VrcxProblem ProviderFailed(string? detail) => new("Search.ProviderFailed", "The avatar database failed", HttpStatusCode.BadGateway, detail);
}

public static class VrchatError
{
    public static VrcxProblem Upstream(string? detail) => new("Vrchat.Upstream", "VRChat returned an error", HttpStatusCode.BadGateway, detail);
    public static VrcxProblem SessionExpired => new("Vrchat.SessionExpired", "VRChat no longer accepts this account's session; log in again", HttpStatusCode.Conflict);
}

public static class LegacyImportError
{
    public static VrcxProblem Busy => new("LegacyImport.Busy", "A check or an import of the uploaded file is running", HttpStatusCode.Conflict);
    public static VrcxProblem UnknownUpload => new("LegacyImport.UnknownUpload", "No such upload; start the upload again", HttpStatusCode.NotFound);
    public static VrcxProblem NotUploaded => new("LegacyImport.NotUploaded", "The file has not been uploaded completely", HttpStatusCode.Conflict);
    public static VrcxProblem WrongOffset(long expected) => new("LegacyImport.WrongOffset", "That part of the file does not follow what was received", HttpStatusCode.Conflict, $"The upload continues at byte {expected}");
    public static VrcxProblem TooLarge(long max) => new("LegacyImport.TooLarge", "The file is larger than this server takes", HttpStatusCode.RequestEntityTooLarge, $"At most {max} bytes");
    public static VrcxProblem Empty => new("LegacyImport.Empty", "The file is empty", HttpStatusCode.BadRequest);
    public static VrcxProblem NoSpace(long free) => new("LegacyImport.NoSpace", "There is not enough free disk space on the server for the file", HttpStatusCode.InsufficientStorage, $"{free} bytes are free");
    public static VrcxProblem NotSqlite => new("LegacyImport.NotSqlite", "That is not an SQLite database; pick VRCX.sqlite3", HttpStatusCode.UnsupportedMediaType);
    public static VrcxProblem PastTheEnd => new("LegacyImport.PastTheEnd", "More was sent than the file's size", HttpStatusCode.BadRequest);
}
