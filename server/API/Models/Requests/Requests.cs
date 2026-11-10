using System.ComponentModel.DataAnnotations;
using VRCX.Common.Constants;

namespace VRCX.API.Models.Requests;

/// <summary>The address VRChat mails a password recovery link to.</summary>
public sealed class PasswordResetRequest
{
    [Required(AllowEmptyStrings = false)]
    [EmailAddress]
    [StringLength(320, MinimumLength = 3)]
    public required string Email { get; set; }
}

public sealed class LoginRequest
{
    [Required(AllowEmptyStrings = false)]
    [StringLength(HardLimits.UsernameMaxLength, MinimumLength = 1)]
    public required string Username { get; set; }

    [Required(AllowEmptyStrings = false)]
    [StringLength(HardLimits.PasswordMaxLength, MinimumLength = 1)]
    public required string Password { get; set; }

    /// <summary>The original's custom development endpoint: an https API URL. Empty for VRChat's.</summary>
    [StringLength(512)]
    public string? Endpoint { get; set; }

    /// <summary>The wss pipeline URL that goes with <see cref="Endpoint"/>.</summary>
    [StringLength(512)]
    public string? Websocket { get; set; }

    /// <summary>The original's Save Credentials: keep the password (encrypted) to log in again when the session expires.</summary>
    public bool SaveCredentials { get; set; }

    /// <summary>Needed with <see cref="SaveCredentials"/> while the primary password is on: the password is encrypted with it.</summary>
    [StringLength(32)]
    public string? PrimaryPassword { get; set; }
}

/// <summary>Logging in with an account's saved password.</summary>
public sealed class ReloginRequest
{
    /// <summary>Needed when the saved password is encrypted with the primary password.</summary>
    [StringLength(32)]
    public string? PrimaryPassword { get; set; }
}

/// <summary>Turns the primary password on (encrypting every saved password with it) or off (which needs it).</summary>
public sealed class PrimaryPasswordRequest
{
    public required bool Enabled { get; set; }

    [Required(AllowEmptyStrings = false)]
    [StringLength(32, MinimumLength = 1)]
    public required string Password { get; set; }
}

public sealed class TwoFactorRequest
{
    [Required(AllowEmptyStrings = false)]
    public required string PendingId { get; set; }

    /// <summary>totp, otp (recovery code) or emailOtp - one of the methods the login answered with.</summary>
    [Required(AllowEmptyStrings = false)]
    public required string Method { get; set; }

    [Required(AllowEmptyStrings = false)]
    [StringLength(HardLimits.TwoFactorCodeMaxLength, MinimumLength = 1)]
    public required string Code { get; set; }
}
