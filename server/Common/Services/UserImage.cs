namespace VRCX.Common.Services;

/// <summary>The picture to show for a user, in the original's userImage order. VRChat often sends no thumbnail for friends.</summary>
public static class UserImage
{
    public static string? Pick(string? iconUrl, string? thumbnailUrl, string? imageUrl) =>
        !string.IsNullOrEmpty(iconUrl) ? iconUrl
        : !string.IsNullOrEmpty(thumbnailUrl) ? thumbnailUrl
        : string.IsNullOrEmpty(imageUrl) ? null : imageUrl;
}
