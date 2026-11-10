namespace VRCX.Common.Constants;

public static class AuthConstants
{
    /// <summary>Header carrying the shared API token (alternatively <c>Authorization: Bearer …</c>).</summary>
    public const string ApiTokenHeaderName = "VrcxToken";
}

public static class Duration
{
    /// <summary>How often the friend list is diffed against VRChat.</summary>
    public static readonly TimeSpan FriendSyncInterval = TimeSpan.FromMinutes(5);
    /// <summary>How often groups, favorites, moderations, notes and notifications are pulled.</summary>
    public static readonly TimeSpan FullSyncInterval = TimeSpan.FromHours(1);
}

public static class HardLimits
{
    public const int UsernameMaxLength = 320;
    public const int PasswordMaxLength = 1024;
    public const int TwoFactorCodeMaxLength = 32;
    public const int VrchatIdMaxLength = 64;
    public const int LocationTagMaxLength = 512;
}
