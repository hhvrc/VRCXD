using System.Net;
using VRCX.Common.Problems;

namespace VRCX.Common.Errors;

/// <summary>The saved login problems: the original's toasts for them are picked by type.</summary>
public static class SavedLoginError
{
    public static VrcxProblem NotFound => new("SavedLogin.NotFound", "No password is saved for this account", HttpStatusCode.NotFound);
    public static VrcxProblem Invalid => new("SavedLogin.Invalid", "Saved credentials are no longer valid", HttpStatusCode.Unauthorized);
    public static VrcxProblem PrimaryPasswordRequired => new("SavedLogin.PrimaryPasswordRequired", "The primary password is required", HttpStatusCode.Forbidden);
    public static VrcxProblem IncorrectPrimaryPassword => new("SavedLogin.IncorrectPrimaryPassword", "Incorrect primary password", HttpStatusCode.Forbidden);
    public static VrcxProblem PrimaryPasswordAlreadyEnabled => new("SavedLogin.PrimaryPasswordAlreadyEnabled", "The primary password is already on", HttpStatusCode.Conflict);
}
