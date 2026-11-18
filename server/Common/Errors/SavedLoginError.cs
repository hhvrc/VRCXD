using System.Net;
using OpenShock.Internal.Common.Problems;

namespace VRCX.Common.Errors;

/// <summary>The saved login problems: the original's toasts for them are picked by type.</summary>
public static class SavedLoginError
{
    public static OpenShockProblem NotFound => new("SavedLogin.NotFound", "No password is saved for this account", HttpStatusCode.NotFound);
    public static OpenShockProblem Invalid => new("SavedLogin.Invalid", "Saved credentials are no longer valid", HttpStatusCode.Unauthorized);
    public static OpenShockProblem PrimaryPasswordRequired => new("SavedLogin.PrimaryPasswordRequired", "The primary password is required", HttpStatusCode.Forbidden);
    public static OpenShockProblem IncorrectPrimaryPassword => new("SavedLogin.IncorrectPrimaryPassword", "Incorrect primary password", HttpStatusCode.Forbidden);
    public static OpenShockProblem PrimaryPasswordAlreadyEnabled => new("SavedLogin.PrimaryPasswordAlreadyEnabled", "The primary password is already on", HttpStatusCode.Conflict);
}
