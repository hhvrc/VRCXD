using VRCX.Common.Live;

namespace VRCX.Common.Services.Live.Photon;

/// <summary>
/// The original's Photon HUDs on the SteamVR overlay: which events and timeouts reach the HMD
/// (photon.js addEntryPhotonEvent and photonLobbyWatcher) and how each line reads (Vr.vue's hud-feed).
/// </summary>
public static class PhotonHud
{
    /// <summary>A VIP, Friends or Everyone filter, as photonEventOverlayFilter and timeoutHudOverlayFilter.</summary>
    public static bool Passes(string filter, string userId, bool isFriend, bool isFavorite) => filter switch
    {
        "VIP" => userId.Length > 0 && isFavorite,
        "Friends" => userId.Length > 0 && isFriend,
        _ => true,
    };

    /// <summary>The event's type is one the HUD shows (photonEventTableTypeOverlayFilter, all when empty).</summary>
    public static bool TypeShown(IReadOnlyCollection<string> types, string type) => types.Count == 0 || types.Contains(type);

    private static string Platform(string? platform) => platform switch
    {
        "Desktop" => "Desktop",
        "VR" => "VR",
        "Quest" => "Android",
        "iOS" => "iOS",
        _ => "",
    };

    private static string Join(params string?[] parts) => string.Join(' ', parts.Where(p => !string.IsNullOrEmpty(p)));

    /// <summary>The line after the name, and "yellow" for the lines drawn yellow.</summary>
    public static (string Text, string Color) Line(PhotonFeedEntry e) => e.Type switch
    {
        "ChangeAvatar" => (Join("ChangeAvatar", e.Avatar?.Name, e.Avatar?.ReleaseStatus switch { "public" => "(Public)", "private" => "(Private)", _ => null }), ""),
        "ChangeStatus" => (Join(
            "ChangeStatus",
            e.Status != e.PreviousStatus ? $"{e.PreviousStatus} → {e.Status}" : null,
            e.StatusDescription != e.PreviousStatusDescription ? e.StatusDescription : null), ""),
        "ChangeGroup" => (Join("ChangeGroup", e.GroupName), ""),
        "ChatBoxMessage" => (Join("ChatBox", e.Text), ""),
        "PortalSpawn" => (Join("PortalSpawn", e.WorldName), ""),
        "OnPlayerJoined" => (Join("has joined", Platform(e.Platform), e.Avatar?.Name), ""),
        "SpawnEmoji" => (Join("SpawnEmoji", e.Text), ""),
        _ => (e.Text ?? "", e.Color == "yellow" ? "yellow" : ""),
    };

    public static HudEntry Entry(PhotonFeedEntry e, bool isFriend, bool isFavorite, string? colour = null)
    {
        var (text, color) = Line(e);
        return new HudEntry
        {
            DisplayName = e.DisplayName, Text = text, Color = color, Master = e.IsMaster, Moderator = e.IsModerator == true,
            Friend = isFriend, Favorite = isFavorite, Colour = colour ?? "",
        };
    }
}
