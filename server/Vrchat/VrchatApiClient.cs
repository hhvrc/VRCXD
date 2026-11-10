using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using VRCX.Vrchat.Models;

namespace VRCX.Vrchat;

/// <summary>
/// One logged-in VRChat session: an HttpClient with a cookie jar. Methods map
/// 1:1 onto VRChat endpoints and return typed models; a 404 on a lookup is
/// null, anything else non-2xx throws <see cref="VrchatApiException"/>
/// (<see cref="VrchatUnauthorizedException"/> for 401, so callers can tell
/// "the session died" from "VRChat is having a bad day").
/// </summary>
public sealed class VrchatApiClient : IDisposable
{
    public const string BaseAddress = "https://api.vrchat.cloud/api/1/";
    public const string PipelineAddress = "wss://pipeline.vrchat.cloud/";
    // VRChat's edge rejects requests (and websocket handshakes) with no User-Agent.
    public const string UserAgent = "VRCX.Server/2.0 (github.com/vrcx-team/VRCX)";

    private readonly HttpClient _http;

    public CookieContainer Cookies { get; }

    /// <summary>The API and pipeline this client talks to.</summary>
    public VrchatEndpoint Endpoint { get; }

    public VrchatApiClient(HttpClient http, CookieContainer cookies, VrchatEndpoint? endpoint = null)
    {
        _http = http;
        Cookies = cookies;
        Endpoint = endpoint ?? VrchatEndpoint.Default;
    }

    /// <summary>
    /// A client with its own cookie jar, sharing the process-wide VRChat rate limit
    /// (<see cref="VrchatRateLimiter.Shared"/> unless told otherwise). Pass an inner handler to fake
    /// VRChat in tests, and <see cref="VrchatRateLimiter.Unlimited"/> to skip the limiter there.
    /// </summary>
    public static VrchatApiClient Create(CookieContainer? cookies = null, HttpMessageHandler? inner = null, VrchatRateLimiter? rateLimiter = null, VrchatEndpoint? endpoint = null)
    {
        cookies ??= new CookieContainer();
        inner ??= new SocketsHttpHandler
        {
            CookieContainer = cookies,
            UseCookies = true,
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        };
        var http = new HttpClient(new VrchatThrottlingHandler(inner, rateLimiter ?? VrchatRateLimiter.Shared)) { BaseAddress = (endpoint ?? VrchatEndpoint.Default).Api };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return new VrchatApiClient(http, cookies, endpoint);
    }

    public void Dispose() => _http.Dispose();

    // ---- auth -------------------------------------------------------------

    /// <summary>GET auth/user with Basic credentials. 2FA accounts come back as RequiresTwoFactor; verify, then GetCurrentUser.</summary>
    public async Task<LoginResult> LoginAsync(string username, string password, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "auth/user");
        var credentials = $"{Uri.EscapeDataString(username)}:{Uri.EscapeDataString(password)}";
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(credentials)));
        using var response = await _http.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            return new LoginResult.InvalidCredentials(await ReadErrorMessageAsync(response, ct));
        await EnsureSuccessAsync(response, "GET", "auth/user", ct);

        // With 2FA pending the body is just {"requiresTwoFactorAuth":[...]} -
        // none of the user's fields - so look before parsing it as a user.
        var body = await response.Content.ReadAsByteArrayAsync(ct);
        using (var probe = JsonDocument.Parse(body))
        {
            if (probe.RootElement.TryGetProperty("requiresTwoFactorAuth", out var methods) && methods.ValueKind == JsonValueKind.Array)
                return new LoginResult.RequiresTwoFactor(methods.EnumerateArray().Select(m => m.GetString() ?? "").ToList());
        }
        var user = VrchatJson.Deserialize<CurrentUser>(body) ?? throw new JsonException("auth/user returned null");
        return new LoginResult.LoggedIn(user);
    }

    /// <summary>POST auth/twofactorauth/{totp|otp|emailotp}/verify.</summary>
    public async Task<bool> VerifyTwoFactorAsync(TwoFactorMethod method, string code, CancellationToken ct = default)
    {
        var path = method switch
        {
            TwoFactorMethod.Totp => "auth/twofactorauth/totp/verify",
            TwoFactorMethod.RecoveryCode => "auth/twofactorauth/otp/verify",
            TwoFactorMethod.EmailOtp => "auth/twofactorauth/emailotp/verify",
            _ => throw new ArgumentOutOfRangeException(nameof(method)),
        };
        using var response = await _http.PostAsJsonAsync(path, new { code }, VrchatJson.Options, ct);
        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized) return false;
        await EnsureSuccessAsync(response, "POST", path, ct);
        return (await ReadAsync<TwoFactorVerifyResult>(response, ct)).Verified;
    }

    /// <summary>
    /// The original's QR login, step one: GET config (as it does first), then POST auth/device/code for a
    /// code another signed-in device approves at the verification address.
    /// </summary>
    public async Task<DeviceCodeGrant> StartDeviceLoginAsync(CancellationToken ct = default)
    {
        await GetRequiredAsync<JsonElement>("config", ct);
        return await SendJsonAsync<DeviceCodeGrant>(HttpMethod.Post, "auth/device/code", null, ct);
    }

    /// <summary>
    /// POST auth/device/status: true once the code is approved and this client holds the session,
    /// false while VRChat still answers authorization_pending or slow_down.
    /// </summary>
    public async Task<bool> PollDeviceLoginAsync(string deviceCode, CancellationToken ct = default)
    {
        JsonElement status;
        try
        {
            status = await SendJsonAsync<JsonElement>(HttpMethod.Post, "auth/device/status", new { deviceCode }, ct);
        }
        catch (VrchatApiException ex) when (ex.Message.Contains("authorization_pending", StringComparison.Ordinal) || ex.Message.Contains("slow_down", StringComparison.Ordinal))
        {
            return false;
        }
        return status.ValueKind == JsonValueKind.Object && status.TryGetProperty("user", out var user) && user.ValueKind == JsonValueKind.Object;
    }

    /// <summary>PUT auth/password: VRChat mails a password recovery link to the address.</summary>
    public Task RequestPasswordResetAsync(string email, CancellationToken ct = default) =>
        SendJsonAsync<JsonElement>(HttpMethod.Put, "auth/password", new { email }, ct);

    /// <summary>GET auth/user. Throws VrchatUnauthorizedException when the session is gone.</summary>
    public async Task<CurrentUser> GetCurrentUserAsync(CancellationToken ct = default) =>
        await GetRequiredAsync<CurrentUser>("auth/user", ct);

    /// <summary>GET auth - the short-lived token the pipeline websocket authenticates with.</summary>
    public async Task<string> GetPipelineTokenAsync(CancellationToken ct = default)
    {
        var auth = await GetRequiredAsync<AuthToken>("auth", ct);
        return auth.Ok && !string.IsNullOrEmpty(auth.Token) ? auth.Token : throw new InvalidOperationException("GET auth returned no token");
    }

    /// <summary>PUT logout - invalidates the session cookie server-side.</summary>
    public async Task LogoutAsync(CancellationToken ct = default)
    {
        using var response = await _http.PutAsync("logout", null, ct);
        if (response.StatusCode != HttpStatusCode.Unauthorized) await EnsureSuccessAsync(response, "PUT", "logout", ct);
    }

    // ---- users ------------------------------------------------------------

    public Task<User?> GetUserAsync(string userId, CancellationToken ct = default) =>
        GetOrNullAsync<User>($"users/{Escape(userId)}", ct);

    /// <summary>GET profile/{id}, with the Supporter badge added for VRC+ users.</summary>
    public async Task<PublicProfile?> GetPublicProfileAsync(string userId, CancellationToken ct = default) =>
        (await GetOrNullAsync<PublicProfile>($"profile/{Escape(userId)}", ct))?.WithSupporterBadge();

    /// <summary>GET auth/user/friends - one page. VRChat caps n at 100.</summary>

    public async Task<IReadOnlyList<LimitedUserFriend>> GetFriendsAsync(bool offline, int offset = 0, int n = 100, CancellationToken ct = default) =>
        await GetRequiredAsync<List<LimitedUserFriend>>($"auth/user/friends?offline={(offline ? "true" : "false")}&n={n}&offset={offset}", ct);

    /// <summary>Every friend, online and offline, paged until a short page comes back.</summary>
    public async Task<IReadOnlyList<LimitedUserFriend>> GetAllFriendsAsync(CancellationToken ct = default)
    {
        var all = new Dictionary<string, LimitedUserFriend>();
        foreach (var offline in new[] { false, true })
        {
            for (var offset = 0; ; offset += 100)
            {
                var page = await GetFriendsAsync(offline, offset, 100, ct);
                foreach (var friend in page) all[friend.Id] = friend;
                if (page.Count < 100) break;
            }
        }
        return all.Values.ToList();
    }

    /// <summary>GET users/{userId}/mutuals - the counts of shared friends and groups.</summary>
    public Task<MutualCounts> GetMutualCountsAsync(string userId, CancellationToken ct = default) =>
        GetRequiredAsync<MutualCounts>($"users/{Escape(userId)}/mutuals", ct);

    /// <summary>PUT users/{ownUserId}/badges/{badgeId} - hide or showcase one of the account's own badges.</summary>
    public Task<Badge> UpdateBadgeAsync(string ownUserId, string badgeId, bool hidden, bool showcased, CancellationToken ct = default) =>
        SendJsonAsync<Badge>(HttpMethod.Put, $"users/{Escape(ownUserId)}/badges/{Escape(badgeId)}", new { userId = ownUserId, badgeId, hidden, showcased }, ct);

    /// <summary>GET users/{userId}/mutuals/friends - friends that user shares with the account. 403/404 when they opted out.</summary>
    public async Task<IReadOnlyList<LimitedUserSearch>> GetMutualFriendsAsync(string userId, int n = 100, int offset = 0, CancellationToken ct = default) =>
        await GetRequiredAsync<List<LimitedUserSearch>>($"users/{Escape(userId)}/mutuals/friends?n={n}&offset={offset}", ct);

    public async Task<IReadOnlyList<LimitedUserSearch>> GetAllMutualFriendsAsync(string userId, CancellationToken ct = default)
    {
        var all = new List<LimitedUserSearch>();
        for (var offset = 0; ; offset += 100)
        {
            var page = await GetMutualFriendsAsync(userId, 100, offset, ct);
            all.AddRange(page);
            if (page.Count < 100) break;
        }
        return all;
    }

    public async Task<IReadOnlyList<LimitedUserSearch>> SearchUsersAsync(string query, int n = 20, int offset = 0, CancellationToken ct = default, bool? fuzzy = null) =>
        await GetRequiredAsync<List<LimitedUserSearch>>($"users?search={Escape(query)}&n={n}&offset={offset}{(fuzzy is { } f ? $"&fuzzy={(f ? "true" : "false")}" : "")}", ct);

    public Task<FriendStatus?> GetFriendStatusAsync(string userId, CancellationToken ct = default) =>
        GetOrNullAsync<FriendStatus>($"user/{Escape(userId)}/friendStatus", ct);

    public Task SendFriendRequestAsync(string userId, CancellationToken ct = default) =>
        SendNoContentAsync(HttpMethod.Post, $"user/{Escape(userId)}/friendRequest", ct);

    public Task CancelFriendRequestAsync(string userId, CancellationToken ct = default) =>
        SendNoContentAsync(HttpMethod.Delete, $"user/{Escape(userId)}/friendRequest", ct);

    public Task<PlayerModeration> SendPlayerModerationAsync(string userId, string type, CancellationToken ct = default) =>
        SendJsonAsync<PlayerModeration>(HttpMethod.Post, "auth/user/playermoderations", new { moderated = userId, type }, ct);

    public Task DeletePlayerModerationAsync(string userId, string type, CancellationToken ct = default) =>
        SendJsonAsync<JsonElement>(HttpMethod.Put, "auth/user/unplayermoderate", new { moderated = userId, type }, ct);

    public Task DeletePlayerModerationsOfTypeAsync(string type, CancellationToken ct = default) =>
        SendJsonAsync<JsonElement>(HttpMethod.Put, "auth/user/unplayermoderate", new { type }, ct);

    public Task SendRequestInviteAsync(string userId, int? requestSlot = null, CancellationToken ct = default) =>
        SendJsonAsync<JsonElement>(HttpMethod.Post, $"requestInvite/{Escape(userId)}",
            requestSlot is { } slot ? new Dictionary<string, object> { ["platform"] = "standalonewindows", ["requestSlot"] = slot } : new Dictionary<string, object> { ["platform"] = "standalonewindows" }, ct);

    /// <summary>The original's uploadImageLegacy: the JSON body as a "data" field and the picture as image.png.</summary>
    public async Task SendPhotoAsync(string path, object data, Stream image, CancellationToken ct = default)
    {
        using var content = new MultipartFormDataContent("---------------------------" + DateTime.Now.Ticks.ToString("x"));
        content.Add(new StringContent(JsonSerializer.Serialize(data)), "data");
        var file = new StreamContent(image);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        content.Add(file, "image", "image.png");
        using var response = await _http.PostAsync(path, content, ct);
        await EnsureSuccessAsync(response, "POST", path, ct);
    }

    public Task SendBoopAsync(string userId, string? emojiId, CancellationToken ct = default) =>
        SendJsonAsync<JsonElement>(HttpMethod.Post, $"users/{Escape(userId)}/boop", emojiId is null ? new Dictionary<string, string>() : new Dictionary<string, string> { ["emojiId"] = emojiId }, ct);

    public Task AcceptFriendRequestNotificationAsync(string notificationId, CancellationToken ct = default) =>
        SendJsonAsync<JsonElement>(HttpMethod.Put, $"auth/user/notifications/{Escape(notificationId)}/accept", null, ct);

    public Task UnfriendAsync(string userId, CancellationToken ct = default) =>
        SendNoContentAsync(HttpMethod.Delete, $"auth/user/friends/{Escape(userId)}", ct);

    public async Task<IReadOnlyList<UserNote>> GetUserNotesAsync(int n = 100, int offset = 0, CancellationToken ct = default) =>
        await GetRequiredAsync<List<UserNote>>($"userNotes?n={n}&offset={offset}", ct);

    public async Task<IReadOnlyList<PlayerModeration>> GetPlayerModerationsAsync(CancellationToken ct = default) =>
        await GetRequiredAsync<List<PlayerModeration>>("auth/user/playermoderations", ct);

    // ---- content ----------------------------------------------------------

    public Task<World> SaveWorldAsync(string worldId, IReadOnlyDictionary<string, object> changes, CancellationToken ct = default) =>
        SendJsonAsync<World>(HttpMethod.Put, $"worlds/{Escape(worldId)}", changes, ct);

    public Task PublishWorldAsync(string worldId, CancellationToken ct = default) =>
        SendJsonAsync<JsonElement>(HttpMethod.Put, $"worlds/{Escape(worldId)}/publish", null, ct);

    public Task UnpublishWorldAsync(string worldId, CancellationToken ct = default) =>
        SendJsonAsync<JsonElement>(HttpMethod.Delete, $"worlds/{Escape(worldId)}/publish", null, ct);

    public Task DeleteWorldAsync(string worldId, CancellationToken ct = default) =>
        SendJsonAsync<JsonElement>(HttpMethod.Delete, $"worlds/{Escape(worldId)}", null, ct);

    public Task DeleteWorldPersistDataAsync(string userId, string worldId, CancellationToken ct = default) =>
        SendJsonAsync<JsonElement>(HttpMethod.Delete, $"users/{Escape(userId)}/{Escape(worldId)}/persist", null, ct);

    /// <summary>GET users/{id}/{world}/persist/exists answers true, false or 404.</summary>
    public async Task<bool> HasWorldPersistDataAsync(string userId, string worldId, CancellationToken ct = default)
    {
        try
        {
            return (await GetRequiredAsync<JsonElement>($"users/{Escape(userId)}/{Escape(worldId)}/persist/exists", ct)).ValueKind != JsonValueKind.False;
        }
        catch (VrchatApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    /// <summary>GET worlds/{id}/rating: this account's like or dislike of a world, or none.</summary>
    public Task<WorldRating> GetWorldRatingAsync(string worldId, CancellationToken ct = default) =>
        GetRequiredAsync<WorldRating>($"worlds/{Escape(worldId)}/rating", ct);

    /// <summary>PUT worlds/{id}/rating?value=like|dislike.</summary>
    public Task<WorldRating> SetWorldRatingAsync(string worldId, string value, CancellationToken ct = default) =>
        SendJsonAsync<WorldRating>(HttpMethod.Put, $"worlds/{Escape(worldId)}/rating?value={Escape(value)}", null, ct);

    public Task<WorldRating> DeleteWorldRatingAsync(string worldId, CancellationToken ct = default) =>
        SendJsonAsync<WorldRating>(HttpMethod.Delete, $"worlds/{Escape(worldId)}/rating", null, ct);

    public Task<World?> GetWorldAsync(string worldId, CancellationToken ct = default) =>
        GetOrNullAsync<World>($"worlds/{Escape(worldId)}", ct);

    /// <summary>GET avatars?user=me - every avatar the account uploaded, newest update first, all release statuses.</summary>
    public async Task<IReadOnlyList<Avatar>> GetOwnAvatarsAsync(CancellationToken ct = default)
    {
        var all = new Dictionary<string, Avatar>();
        for (var offset = 0; ; offset += 50)
        {
            var page = await GetRequiredAsync<List<Avatar>>($"avatars?user=me&releaseStatus=all&sort=updated&order=descending&n=50&offset={offset}", ct);
            foreach (var a in page) all[a.Id] = a;
            if (page.Count < 50) break;
        }
        return all.Values.ToList();
    }

    /// <summary>GET avatars/licensed - every avatar the account bought, 100 a page.</summary>
    public async Task<IReadOnlyList<Avatar>> GetLicensedAvatarsAsync(CancellationToken ct = default)
    {
        var all = new Dictionary<string, Avatar>();
        for (var offset = 0; ; offset += 100)
        {
            var page = await GetRequiredAsync<List<Avatar>>($"avatars/licensed?n=100&offset={offset}", ct);
            foreach (var a in page) all[a.Id] = a;
            if (page.Count < 100) break;
        }
        return all.Values.ToList();
    }

    /// <summary>PUT avatars/{id} with the fields to change (name, description, releaseStatus, imageUrl, ...).</summary>
    public Task<Avatar> SaveAvatarAsync(string avatarId, object changes, CancellationToken ct = default) =>
        SendJsonAsync<Avatar>(HttpMethod.Put, $"avatars/{Escape(avatarId)}", changes, ct);

    /// <summary>PUT avatars/{id}/select - wear the avatar. Answers with the current user.</summary>
    /// <summary>PUT users/{id}: the original's saveCurrentUser, here for status, status text and pronouns.</summary>
    public Task<CurrentUser> SaveCurrentUserAsync(string userId, string? status, string? statusDescription, CancellationToken ct = default) =>
        SaveCurrentUserAsync(userId, status, statusDescription, null, ct);

    public Task<CurrentUser> SaveCurrentUserAsync(string userId, string? status, string? statusDescription, string? pronouns, CancellationToken ct = default)
    {
        var body = new Dictionary<string, string>();
        if (status is not null) body["status"] = status;
        if (statusDescription is not null) body["statusDescription"] = statusDescription;
        if (pronouns is not null) body["pronouns"] = pronouns;
        return SendJsonAsync<CurrentUser>(HttpMethod.Put, $"users/{Escape(userId)}", body, ct);
    }

    /// <summary>POST users/{id}/addTags: the original's addUserTags (spoken languages are language_ tags).</summary>
    public Task<CurrentUser> AddUserTagsAsync(string userId, IReadOnlyList<string> tags, CancellationToken ct = default) =>
        SendJsonAsync<CurrentUser>(HttpMethod.Post, $"users/{Escape(userId)}/addTags", new { tags }, ct);

    /// <summary>POST users/{id}/removeTags: the original's removeUserTags.</summary>
    public Task<CurrentUser> RemoveUserTagsAsync(string userId, IReadOnlyList<string> tags, CancellationToken ct = default) =>
        SendJsonAsync<CurrentUser>(HttpMethod.Post, $"users/{Escape(userId)}/removeTags", new { tags }, ct);

    /// <summary>GET users/{id}/instances/groups: open instances in the user's groups.</summary>
    public async Task<IReadOnlyList<Instance>> GetUserGroupInstancesAsync(string userId, CancellationToken ct = default) =>
        (await GetRequiredAsync<GroupInstanceList>($"users/{Escape(userId)}/instances/groups", ct)).Instances;

    private sealed record GroupInstanceList
    {
        public IReadOnlyList<Instance> Instances { get; init; } = [];
    }

    public Task<CurrentUser> SelectAvatarAsync(string avatarId, CancellationToken ct = default) =>
        SendJsonAsync<CurrentUser>(HttpMethod.Put, $"avatars/{Escape(avatarId)}/select", null, ct);

    /// <summary>POST avatars/{id}/impostor/enqueue - ask VRChat to build the impostor.</summary>
    public async Task EnqueueImpostorAsync(string avatarId, CancellationToken ct = default)
    {
        var path = $"avatars/{Escape(avatarId)}/impostor/enqueue";
        using var response = await _http.PostAsync(path, null, ct);
        await EnsureSuccessAsync(response, "POST", path, ct);
    }

    public async Task<IReadOnlyList<AvatarModeration>> GetAvatarModerationsAsync(CancellationToken ct = default) =>
        await GetRequiredAsync<List<AvatarModeration>>("auth/user/avatarmoderations", ct);

    public Task BlockAvatarAsync(string avatarId, CancellationToken ct = default) =>
        SendJsonAsync<JsonElement>(HttpMethod.Post, "auth/user/avatarmoderations", new { avatarModerationType = "block", targetAvatarId = avatarId }, ct);

    public Task UnblockAvatarAsync(string avatarId, CancellationToken ct = default) =>
        SendJsonAsync<JsonElement>(HttpMethod.Delete, $"auth/user/avatarmoderations?targetAvatarId={Escape(avatarId)}&avatarModerationType=block", null, ct);

    public Task SelectFallbackAvatarAsync(string avatarId, CancellationToken ct = default) =>
        SendJsonAsync<JsonElement>(HttpMethod.Put, $"avatars/{Escape(avatarId)}/selectfallback", null, ct);

    public Task DeleteAvatarAsync(string avatarId, CancellationToken ct = default) =>
        SendJsonAsync<JsonElement>(HttpMethod.Delete, $"avatars/{Escape(avatarId)}", null, ct);

    public Task DeleteImpostorAsync(string avatarId, CancellationToken ct = default) =>
        SendJsonAsync<JsonElement>(HttpMethod.Delete, $"avatars/{Escape(avatarId)}/impostor", null, ct);

    public Task<Avatar?> GetAvatarAsync(string avatarId, CancellationToken ct = default) =>
        GetOrNullAsync<Avatar>($"avatars/{Escape(avatarId)}", ct);

    /// <summary>A location tag for a URL path. VRChat answers 400 "malformed url" when its ":", "(" and ")" are escaped, so they stay as they are.</summary>
    private static string LocationPath(string location) =>
        Uri.EscapeDataString(location).Replace("%3A", ":").Replace("%28", "(").Replace("%29", ")");

    /// <summary>GET instances/{worldId}:{instanceId}. Takes the full location tag.</summary>
    public Task<Instance?> GetInstanceAsync(string location, CancellationToken ct = default) =>
        GetOrNullAsync<Instance>($"instances/{LocationPath(location)}", ct);

    /// <summary>GET file/{fileId} - the file behind an image URL (null for an unknown id).</summary>
    public Task<VrchatFile?> GetFileAsync(string fileId, CancellationToken ct = default) =>
        GetOrNullAsync<VrchatFile>($"file/{Escape(fileId)}", ct);

    // ---- multi-part file uploads (the original's api/image.ts) --------------

    /// <summary>POST file/{id}: a new version of a file, sized and hashed up front. Answers with the file.</summary>
    public Task<VrchatFile> CreateFileVersionAsync(string fileId, string fileMd5, long fileSizeInBytes, string signatureMd5, long signatureSizeInBytes, CancellationToken ct = default) =>
        SendJsonAsync<VrchatFile>(HttpMethod.Post, $"file/{Escape(fileId)}", new { fileMd5, fileSizeInBytes, signatureMd5, signatureSizeInBytes }, ct);

    /// <summary>PUT file/{id}/{version}/{file|signature}/start: where to upload that part.</summary>
    public Task<FileUploadUrl> StartFileUploadAsync(string fileId, int version, string part, CancellationToken ct = default) =>
        SendJsonAsync<FileUploadUrl>(HttpMethod.Put, $"file/{Escape(fileId)}/{version}/{part}/start", new { }, ct);

    /// <summary>PUT file/{id}/{version}/{file|signature}/finish, after the part is uploaded.</summary>
    public Task FinishFileUploadAsync(string fileId, int version, string part, CancellationToken ct = default) =>
        SendJsonAsync<JsonElement>(HttpMethod.Put, $"file/{Escape(fileId)}/{version}/{part}/finish", new { maxParts = 0, nextPartNumber = 0 }, ct);

    /// <summary>The original's upload cleanup: finishes both parts of the file's last version, whatever they say back.</summary>
    public async Task AbandonFileUploadAsync(string fileId, CancellationToken ct = default)
    {
        try
        {
            var file = await GetRequiredAsync<VrchatFile>($"file/{Escape(fileId)}", ct);
            if (file.Versions.Count == 0) return;
            var version = file.Versions[^1].Version;
            foreach (var part in new[] { "signature", "file" })
            {
                try
                {
                    await SendJsonAsync<JsonElement>(HttpMethod.Put, $"file/{Escape(file.Id)}/{version}/{part}/finish", new { }, ct);
                }
                catch (VrchatApiException)
                {
                    // As the original, a cleanup that fails is only logged there.
                }
            }
        }
        catch (VrchatApiException)
        {
        }
    }

    /// <summary>PUT one part to the upload URL VRChat handed out, with its type and MD5, as the original's uploadFilePUT.</summary>
    public async Task UploadFilePartAsync(string url, byte[] data, string mime, string md5Base64, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new VrchatApiException(HttpStatusCode.BadGateway, "PUT", "upload", "VRChat gave no usable upload address");
        using var request = new HttpRequestMessage(HttpMethod.Put, uri) { Content = new ByteArrayContent(data) };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(mime);
        request.Content.Headers.ContentMD5 = Convert.FromBase64String(md5Base64);
        using var response = await _http.SendAsync(request, ct);
        if (response.StatusCode != HttpStatusCode.OK)
            throw new VrchatApiException(response.StatusCode, "PUT", uri.GetLeftPart(UriPartial.Path), "image upload failed");
    }

    /// <summary>The file_… id inside a VRChat image URL, or null.</summary>
    public static string? ExtractFileId(string? url)
    {
        if (string.IsNullOrEmpty(url)) return null;
        var match = System.Text.RegularExpressions.Regex.Match(url, "file_[0-9A-Za-z-]+");
        return match.Success ? match.Value : null;
    }

    // ---- groups -----------------------------------------------------------

    public Task<Group?> GetGroupAsync(string groupId, bool includeRoles = true, CancellationToken ct = default) =>
        GetOrNullAsync<Group>($"groups/{Escape(groupId)}?includeRoles={(includeRoles ? "true" : "false")}", ct);

    public async Task<IReadOnlyList<GroupRole>> GetGroupRolesAsync(string groupId, CancellationToken ct = default) =>
        await GetRequiredAsync<List<GroupRole>>($"groups/{Escape(groupId)}/roles", ct);

    /// <summary>GET groups/{id}/members/{userId}. Null when VRChat has no such member, which it answers with 404 or a null body.</summary>
    public async Task<GroupMember?> GetGroupMemberAsync(string groupId, string userId, CancellationToken ct = default)
    {
        var path = $"groups/{Escape(groupId)}/members/{Escape(userId)}";
        using var response = await _http.GetAsync(path, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        await EnsureSuccessAsync(response, "GET", path, ct);
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        var text = System.Text.Encoding.UTF8.GetString(bytes).Trim();
        return text.Length == 0 || text == "null" ? null : VrchatJson.Deserialize<GroupMember>(bytes);
    }

    /// <summary>GET groups/{id}/members. sort is "joinedAt:desc" or "joinedAt:asc"; roleId narrows to one role.</summary>
    public async Task<IReadOnlyList<GroupMember>> GetGroupMembersAsync(string groupId, int n = 100, int offset = 0, string? sort = null, string? roleId = null, CancellationToken ct = default) =>
        await GetRequiredAsync<List<GroupMember>>($"groups/{Escape(groupId)}/members" + QueryString([new("n", n.ToString(System.Globalization.CultureInfo.InvariantCulture)), new("offset", offset.ToString(System.Globalization.CultureInfo.InvariantCulture)), new("sort", sort), new("roleId", roleId)]), ct);

    /// <summary>GET groups/{id}/members/search.</summary>
    public Task<GroupMemberSearchResult> SearchGroupMembersAsync(string groupId, string query, int n = 100, int offset = 0, CancellationToken ct = default) =>
        GetRequiredAsync<GroupMemberSearchResult>($"groups/{Escape(groupId)}/members/search?query={Escape(query)}&n={n}&offset={offset}", ct);

    /// <summary>PUT groups/{id}/members/{userId}: visibility, announcement subscriptions or the manager note.</summary>
    public Task<GroupMember> UpdateGroupMemberAsync(string groupId, string userId, GroupMemberUpdate update, CancellationToken ct = default) =>
        SendJsonAsync<GroupMember>(HttpMethod.Put, $"groups/{Escape(groupId)}/members/{Escape(userId)}", update, ct);

    /// <summary>PUT groups/{id}/members/{userId}/roles/{roleId}: the member's role ids after.</summary>
    public async Task<IReadOnlyList<string>> AddGroupMemberRoleAsync(string groupId, string userId, string roleId, CancellationToken ct = default) =>
        await SendJsonAsync<List<string>>(HttpMethod.Put, $"groups/{Escape(groupId)}/members/{Escape(userId)}/roles/{Escape(roleId)}", null, ct);

    /// <summary>DELETE groups/{id}/members/{userId}/roles/{roleId}: the member's role ids after.</summary>
    public async Task<IReadOnlyList<string>> RemoveGroupMemberRoleAsync(string groupId, string userId, string roleId, CancellationToken ct = default) =>
        await SendJsonAsync<List<string>>(HttpMethod.Delete, $"groups/{Escape(groupId)}/members/{Escape(userId)}/roles/{Escape(roleId)}", null, ct);

    /// <summary>GET groups/{id}/bans.</summary>
    public async Task<IReadOnlyList<GroupMember>> GetGroupBansAsync(string groupId, int n = 100, int offset = 0, CancellationToken ct = default) =>
        await GetRequiredAsync<List<GroupMember>>($"groups/{Escape(groupId)}/bans?n={n}&offset={offset}", ct);

    /// <summary>POST groups/{id}/bans with the user id: the banned membership.</summary>
    public Task<GroupMember> BanGroupMemberAsync(string groupId, string userId, CancellationToken ct = default) =>
        SendJsonAsync<GroupMember>(HttpMethod.Post, $"groups/{Escape(groupId)}/bans", new { userId }, ct);

    /// <summary>DELETE groups/{id}/bans/{userId}.</summary>
    public Task UnbanGroupMemberAsync(string groupId, string userId, CancellationToken ct = default) =>
        SendNoContentAsync(HttpMethod.Delete, $"groups/{Escape(groupId)}/bans/{Escape(userId)}", ct);

    /// <summary>GET groups/{id}/invites: the invites the group has sent.</summary>
    public async Task<IReadOnlyList<GroupMember>> GetGroupInvitesAsync(string groupId, int n = 100, int offset = 0, CancellationToken ct = default) =>
        await GetRequiredAsync<List<GroupMember>>($"groups/{Escape(groupId)}/invites?n={n}&offset={offset}", ct);

    /// <summary>DELETE groups/{id}/invites/{userId}: take back a sent invite.</summary>
    public Task DeleteGroupInviteAsync(string groupId, string userId, CancellationToken ct = default) =>
        SendNoContentAsync(HttpMethod.Delete, $"groups/{Escape(groupId)}/invites/{Escape(userId)}", ct);

    /// <summary>GET groups/{id}/requests: join requests, or the blocked ones.</summary>
    public async Task<IReadOnlyList<GroupMember>> GetGroupJoinRequestsAsync(string groupId, bool blocked, int n = 100, int offset = 0, CancellationToken ct = default) =>
        await GetRequiredAsync<List<GroupMember>>($"groups/{Escape(groupId)}/requests?n={n}&offset={offset}&blocked={(blocked ? "true" : "false")}", ct);

    /// <summary>PUT groups/{id}/requests/{userId}: accept or reject, and with block a rejection also blocks.</summary>
    public Task RespondGroupJoinRequestAsync(string groupId, string userId, string action, bool block, CancellationToken ct = default) =>
        SendJsonAsync<JsonElement>(HttpMethod.Put, $"groups/{Escape(groupId)}/requests/{Escape(userId)}",
            block ? new Dictionary<string, object> { ["action"] = action, ["block"] = true } : new Dictionary<string, object> { ["action"] = action }, ct);

    /// <summary>GET groups/{id}/auditLogTypes.</summary>
    public async Task<IReadOnlyList<string>> GetGroupAuditLogTypesAsync(string groupId, CancellationToken ct = default) =>
        await GetRequiredAsync<List<string>>($"groups/{Escape(groupId)}/auditLogTypes", ct);

    /// <summary>GET groups/{id}/auditLogs. eventTypes is a comma separated list, as the original sends it.</summary>
    public Task<GroupAuditLogPage> GetGroupAuditLogsAsync(string groupId, int n, int offset, string? eventTypes, CancellationToken ct = default) =>
        GetRequiredAsync<GroupAuditLogPage>($"groups/{Escape(groupId)}/auditLogs" + QueryString([new("n", n.ToString(System.Globalization.CultureInfo.InvariantCulture)), new("offset", offset.ToString(System.Globalization.CultureInfo.InvariantCulture)), new("eventTypes", string.IsNullOrEmpty(eventTypes) ? null : eventTypes)]), ct);

    /// <summary>GET worlds - a user's public worlds, or all of them for the account itself.</summary>
    public async Task<IReadOnlyList<World>> GetWorldsByAuthorAsync(string userId, bool isSelf, CancellationToken ct = default)
    {
        var scope = isSelf ? "user=me&releaseStatus=all" : $"user=friends&userId={Escape(userId)}&releaseStatus=public";
        var worlds = new List<World>();
        for (var offset = 0; ; offset += 100)
        {
            var page = await GetRequiredAsync<List<World>>($"worlds?{scope}&sort=updated&order=descending&n=100&offset={offset}", ct);
            worlds.AddRange(page);
            if (page.Count < 100) return worlds;
        }
    }

    /// <summary>GET users/{id}/groups - the groups a user is in (as visible to this account).</summary>
    public async Task<IReadOnlyList<LimitedUserGroup>> GetUserGroupsAsync(string userId, CancellationToken ct = default) =>
        await GetRequiredAsync<List<LimitedUserGroup>>($"users/{Escape(userId)}/groups", ct);

    /// <summary>
    /// A user's groups with isRepresenting taken from users/{id}/groups/represented: the list itself marks
    /// every group as represented when the user represents none (upstream issue 1301).
    /// </summary>
    public async Task<IReadOnlyList<LimitedUserGroup>> GetUserGroupsWithRepresentationAsync(string userId, CancellationToken ct = default)
    {
        var groups = await GetUserGroupsAsync(userId, ct);
        RepresentedGroup? represented;
        try
        {
            represented = await GetRepresentedGroupAsync(userId, ct);
        }
        catch (VrchatApiException ex) when (ex is not VrchatUnauthorizedException)
        {
            return groups;
        }
        var representedId = represented is { IsRepresenting: true } ? represented.GroupId : null;
        return groups.Select(g => g with { IsRepresenting = representedId is not null && g.GroupId == representedId }).ToList();
    }

    /// <summary>GET users/{id}/groups/represented - null fields (an empty object) when they represent none.</summary>
    public Task<RepresentedGroup?> GetRepresentedGroupAsync(string userId, CancellationToken ct = default) =>
        GetOrNullAsync<RepresentedGroup>($"users/{Escape(userId)}/groups/represented", ct);

    // ---- notifications & favorites ---------------------------------------

    public async Task<IReadOnlyList<Notification>> GetNotificationsAsync(int n = 100, int offset = 0, bool hidden = false, CancellationToken ct = default) =>
        await GetRequiredAsync<List<Notification>>($"auth/user/notifications?n={n}&offset={offset}{(hidden ? "&hidden=true" : "")}", ct);

    public async Task<IReadOnlyList<NotificationV2>> GetNotificationsV2Async(int n = 100, int offset = 0, CancellationToken ct = default) =>
        await GetRequiredAsync<List<NotificationV2>>($"notifications?n={n}&offset={offset}", ct);

    public async Task<IReadOnlyList<Favorite>> GetFavoritesAsync(int n = 100, int offset = 0, CancellationToken ct = default) =>
        await GetRequiredAsync<List<Favorite>>($"favorites?n={n}&offset={offset}", ct);

    /// <summary>Every favorite of every kind, including VRC+ world groups. No type filter, as the original asks.</summary>
    /// <summary>GET avatars/favorites, every page: the favorited avatars themselves, as the original loads them.</summary>
    public Task<IReadOnlyList<Avatar>> GetFavoriteAvatarsAsync(CancellationToken ct = default) =>
        PagesAsync<Avatar>(offset => $"avatars/favorites?n=50&offset={offset}", 50, ct);

    /// <summary>GET worlds/favorites, every page.</summary>
    public Task<IReadOnlyList<World>> GetFavoriteWorldsAsync(CancellationToken ct = default) =>
        PagesAsync<World>(offset => $"worlds/favorites?n=50&offset={offset}", 50, ct);

    // Deleted or hidden entries come back as placeholders ("???", no author); those are left out one by one.
    private async Task<IReadOnlyList<T>> PagesAsync<T>(Func<int, string> path, int size, CancellationToken ct)
    {
        var all = new List<T>();
        for (var offset = 0; offset < 5000; offset += size)
        {
            var page = await GetRequiredAsync<List<JsonElement>>(path(offset), ct);
            foreach (var item in page)
            {
                try
                {
                    if (item.Deserialize<T>(VrchatJson.Options) is { } value) all.Add(value);
                }
                catch (JsonException)
                {
                }
            }
            if (page.Count < size) break;
        }
        return all;
    }

    public async Task<IReadOnlyList<Favorite>> GetAllFavoritesAsync(CancellationToken ct = default)
    {
        var all = new List<Favorite>();
        for (var offset = 0; ; offset += 100)
        {
            var page = await GetFavoritesAsync(100, offset, ct);
            all.AddRange(page);
            if (page.Count < 100) break;
        }
        return all;
    }

    /// <summary>A user's public favorite world lists. A list VRChat refuses is left out, as the original does.</summary>
    public async Task<IReadOnlyList<(FavoriteGroup Group, IReadOnlyList<World> Worlds)>> GetUserFavoriteWorldsAsync(string userId, CancellationToken ct = default)
    {
        var owner = Escape(userId);
        var groups = await GetRequiredAsync<List<FavoriteGroup>>($"favorite/groups?ownerId={owner}&n=100&offset=0", ct);
        var lists = await Task.WhenAll(groups.Where(g => g.Type is "world" or "vrcPlusWorld").Select(async group =>
        {
            try
            {
                var worlds = await GetRequiredAsync<List<World>>($"worlds/favorites?ownerId={owner}&n=100&offset=0&userId={owner}&tag={Escape(group.Name)}", ct);
                return ((FavoriteGroup Group, IReadOnlyList<World> Worlds)?)(group, worlds);
            }
            catch (VrchatApiException)
            {
                return null;
            }
        }));
        return lists.OfType<(FavoriteGroup, IReadOnlyList<World>)>().ToList();
    }

    /// <summary>PUT favorite/group/{type}/{name}/{userId}: rename a favorite group or change who can see it.</summary>
    public Task<FavoriteGroup> SaveFavoriteGroupAsync(string type, string name, string userId, string? displayName, string? visibility, CancellationToken ct = default)
    {
        var body = new Dictionary<string, string>();
        if (displayName is not null) body["displayName"] = displayName;
        if (visibility is not null) body["visibility"] = visibility;
        return SendJsonAsync<FavoriteGroup>(HttpMethod.Put, $"favorite/group/{Escape(type)}/{Escape(name)}/{Escape(userId)}", body, ct);
    }

    /// <summary>DELETE favorite/group/{type}/{name}/{userId}: empty a favorite group.</summary>
    public Task ClearFavoriteGroupAsync(string type, string name, string userId, CancellationToken ct = default) =>
        SendJsonAsync<JsonElement>(HttpMethod.Delete, $"favorite/group/{Escape(type)}/{Escape(name)}/{Escape(userId)}", null, ct);

    public Task<FavoriteLimits> GetFavoriteLimitsAsync(CancellationToken ct = default) =>
        GetRequiredAsync<FavoriteLimits>("auth/user/favoritelimits", ct);

    /// <summary>POST favorites. The group goes as a plain string in tags, as the original sends it.</summary>
    public Task<Favorite> AddFavoriteAsync(string type, string targetId, string group, CancellationToken ct = default) =>
        SendJsonAsync<Favorite>(HttpMethod.Post, "favorites", new { type, favoriteId = targetId, tags = group }, ct);

    public Task RemoveFavoriteAsync(string targetId, CancellationToken ct = default) =>
        SendJsonAsync<JsonElement>(HttpMethod.Delete, $"favorites/{Escape(targetId)}", null, ct);

    public async Task<IReadOnlyList<FavoriteGroup>> GetFavoriteGroupsAsync(CancellationToken ct = default) =>
        await GetRequiredAsync<List<FavoriteGroup>>("favorite/groups?n=100", ct);

    // ---- plumbing ---------------------------------------------------------

    private static string Escape(string value) => Uri.EscapeDataString(value);

    // --- Inventory ---

    /// <summary>GET cosmetics/index/{kind}: every icon frame, profile effect or nameplate effect template.</summary>
    /// <summary>GET analysis/{fileId}/{version}/{variant}: a content bundle's sizes and stats, or null when VRChat has none.</summary>
    public async Task<JsonElement?> GetFileAnalysisAsync(string fileId, int version, string variant, CancellationToken ct = default)
    {
        using var doc = await GetOrNullAsync<JsonDocument>($"analysis/{Escape(fileId)}/{version}/{Escape(variant)}", ct);
        return doc?.RootElement.Clone();
    }

    public Task<JsonElement> GetCosmeticsIndexAsync(string kind, CancellationToken ct = default) =>
        GetRequiredAsync<JsonElement>($"cosmetics/index/{Escape(kind)}", ct);

    public Task<JsonElement> GetPermissionsAsync(CancellationToken ct = default) =>
        GetRequiredAsync<JsonElement>("auth/permissions", ct);

    public Task<List<UserFile>> GetFilesAsync(string tag, CancellationToken ct = default) =>
        GetRequiredAsync<List<UserFile>>($"files?tag={Escape(tag)}&n=100", ct);

    public async Task<UserFile> UploadImageAsync(Stream image, IReadOnlyDictionary<string, string> fields, CancellationToken ct = default)
    {
        using var content = new MultipartFormDataContent();
        foreach (var (key, value) in fields) content.Add(new StringContent(value), key);
        var file = new StreamContent(image);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        content.Add(file, "file", "blob");
        using var response = await _http.PostAsync("file/image", content, ct);
        await EnsureSuccessAsync(response, "POST", "file/image", ct);
        return await ReadAsync<UserFile>(response, ct);
    }

    /// <summary>PUT files/order: a gallery's order, as the original's setAvatarGalleryOrder.</summary>
    public Task SetFileOrderAsync(IReadOnlyList<string> ids, string galleryId, CancellationToken ct = default) =>
        SendJsonAsync<JsonElement>(HttpMethod.Put, "files/order", new { ids, galleryId }, ct);

    public Task DeleteFileAsync(string fileId, CancellationToken ct = default) =>
        SendNoContentAsync(HttpMethod.Delete, $"file/{Escape(fileId)}", ct);

    public Task SaveProfileAsync(string userId, IReadOnlyDictionary<string, string> changes, CancellationToken ct = default) =>
        SendJsonAsync<JsonElement>(HttpMethod.Put, $"profile/{Escape(userId)}", changes, ct);

    /// <summary>GET profile/{id}?asSelf=true: the original's getSelfProfile, with themes, effects and background.</summary>
    public Task<JsonElement> GetSelfProfileAsync(string userId, CancellationToken ct = default) =>
        GetRequiredAsync<JsonElement>($"profile/{Escape(userId)}?asSelf=true", ct);

    /// <summary>PUT profile/{id}: the original's saveProfile, with only the fields that changed.</summary>
    public Task<JsonElement> UpdateProfileAsync(string userId, IReadOnlyDictionary<string, object> changes, CancellationToken ct = default) =>
        SendJsonAsync<JsonElement>(HttpMethod.Put, $"profile/{Escape(userId)}", changes, ct);

    /// <summary>POST profile/theme: the original's createProfileTheme. Answers with the new theme.</summary>
    public Task<JsonElement> CreateProfileThemeAsync(object theme, CancellationToken ct = default) =>
        SendJsonAsync<JsonElement>(HttpMethod.Post, "profile/theme", theme, ct);

    /// <summary>PUT profile/theme/{id}: the original's saveProfileTheme. Its answer is not used.</summary>
    public async Task SaveProfileThemeAsync(string themeId, object theme, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"profile/theme/{Escape(themeId)}")
        {
            Content = new StringContent(VrchatJson.Serialize(theme), Encoding.UTF8, "application/json"),
        };
        using var response = await _http.SendAsync(request, ct);
        await EnsureSuccessAsync(response, "PUT", $"profile/theme/{themeId}", ct);
    }

    /// <summary>DELETE profile/theme/{id}: the original's deleteProfileTheme.</summary>
    public Task DeleteProfileThemeAsync(string themeId, CancellationToken ct = default) =>
        SendNoContentAsync(HttpMethod.Delete, $"profile/theme/{Escape(themeId)}", ct);

    public Task<List<VrchatPrint>> GetPrintsAsync(string userId, CancellationToken ct = default) =>
        GetRequiredAsync<List<VrchatPrint>>($"prints/user/{Escape(userId)}?n=100", ct);

    public async Task<VrchatPrint> UploadPrintAsync(Stream image, string note, string timestamp, CancellationToken ct = default)
    {
        using var content = new MultipartFormDataContent();
        var file = new StreamContent(image);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        content.Add(file, "image", "image");
        content.Add(new StringContent(note), "note");
        content.Add(new StringContent(timestamp), "timestamp");
        using var response = await _http.PostAsync("prints", content, ct);
        await EnsureSuccessAsync(response, "POST", "prints", ct);
        return await ReadAsync<VrchatPrint>(response, ct);
    }

    public Task DeletePrintAsync(string printId, CancellationToken ct = default) =>
        SendNoContentAsync(HttpMethod.Delete, $"prints/{Escape(printId)}", ct);

    public async Task<IReadOnlyList<InventoryItem>> GetAllInventoryAsync(CancellationToken ct = default)
    {
        const int pageSize = 100;
        var all = new List<InventoryItem>();
        for (var page = 0; page < 100; page++)
        {
            var items = await GetRequiredAsync<InventoryPage>($"inventory?n={pageSize}&offset={page * pageSize}&order=newest&notFlags=ugc", ct);
            if (items.Data.Count == 0) break;
            all.AddRange(items.Data);
        }
        all.AddRange(await GetRequiredAsync<List<InventoryItem>>("inventory/global", ct));
        return all;
    }

    public Task ConsumeInventoryBundleAsync(string inventoryId, CancellationToken ct = default) =>
        SendNoContentAsync(HttpMethod.Put, $"inventory/{Escape(inventoryId)}/consume", ct);

    public Task RedeemRewardAsync(string code, CancellationToken ct = default) =>
        SendJsonAsync<JsonElement>(HttpMethod.Post, "reward/redeem", new { code }, ct);

    // --- Automation ---

    public Task<CurrentUser> UpdateCurrentUserAsync(string userId, IReadOnlyDictionary<string, string> changes, CancellationToken ct = default) =>
        SendJsonAsync<CurrentUser>(HttpMethod.Put, $"users/{Escape(userId)}", changes, ct);

    /// <summary>PUT users/{id} with on/off account options (allowAvatarCopying, isBoopingEnabled, ...).</summary>
    public Task<CurrentUser> UpdateCurrentUserFlagsAsync(string userId, IReadOnlyDictionary<string, bool> changes, CancellationToken ct = default) =>
        SendJsonAsync<CurrentUser>(HttpMethod.Put, $"users/{Escape(userId)}", changes, ct);

    /// <summary>GET users/{id}/clientConfig: the account's VRChat client options (accessReduceDecorAnim).</summary>
    public Task<JsonElement> GetClientConfigAsync(string userId, CancellationToken ct = default) =>
        GetRequiredAsync<JsonElement>($"users/{Escape(userId)}/clientConfig", ct);

    /// <summary>PUT users/{id}/clientConfig with the options to change.</summary>
    public Task<JsonElement> SaveClientConfigAsync(string userId, IReadOnlyDictionary<string, bool> changes, CancellationToken ct = default) =>
        SendJsonAsync<JsonElement>(HttpMethod.Put, $"users/{Escape(userId)}/clientConfig", changes, ct);

    public Task SendInviteAsync(string userId, string location, string worldName, CancellationToken ct = default) =>
        SendJsonAsync<JsonElement>(HttpMethod.Post, $"invite/{Escape(userId)}", new { instanceId = location, worldId = location, worldName, rsvp = true }, ct);

    /// <summary>POST invite/{userId}, the original's sendInvite: no RSVP, and a saved message when one is chosen.</summary>
    public Task SendInstanceInviteAsync(string userId, string location, string worldName, int? messageSlot, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object> { ["instanceId"] = location, ["worldId"] = location, ["worldName"] = worldName };
        if (messageSlot is { } slot) body["messageSlot"] = slot;
        return SendJsonAsync<JsonElement>(HttpMethod.Post, $"invite/{Escape(userId)}", body, ct);
    }

    /// <summary>POST invite/myself/to/{location}.</summary>
    public Task SelfInviteAsync(string location, string? shortName, CancellationToken ct = default) =>
        SendJsonAsync<JsonElement>(HttpMethod.Post, $"invite/myself/to/{LocationPath(location)}", string.IsNullOrEmpty(shortName) ? new Dictionary<string, string>() : new Dictionary<string, string> { ["shortName"] = shortName }, ct);

    /// <summary>POST instances. The body is what the original's createNewInstance sends.</summary>
    public Task<Instance> CreateInstanceAsync(IReadOnlyDictionary<string, object> request, CancellationToken ct = default) =>
        SendJsonAsync<Instance>(HttpMethod.Post, "instances", request, ct);

    /// <summary>GET instanceCategories: the categories an instance creator can pick, kept as VRChat sends them.</summary>
    public Task<JsonElement> GetInstanceCategoriesAsync(CancellationToken ct = default) =>
        GetRequiredAsync<JsonElement>("instanceCategories", ct);

    /// <summary>GET instanceVibes: the vibes an instance creator can pick, kept as VRChat sends them.</summary>
    public Task<JsonElement> GetInstanceVibesAsync(CancellationToken ct = default) =>
        GetRequiredAsync<JsonElement>("instanceVibes", ct);

    /// <summary>GET instances/{location}, kept as VRChat sends it (platform counts, languages, queue, content settings, hype train).</summary>
    public Task<JsonElement> GetInstanceJsonAsync(string location, CancellationToken ct = default) =>
        GetRequiredAsync<JsonElement>($"instances/{LocationPath(location)}", ct);

    /// <summary>DELETE instances/{location}?hardClose=false: the original's Close Instance. VRChat answers the closed instance.</summary>
    public Task<JsonElement> CloseInstanceAsync(string location, CancellationToken ct = default) =>
        SendJsonAsync<JsonElement>(HttpMethod.Delete, $"instances/{LocationPath(location)}?hardClose=false", null, ct);

    /// <summary>PUT instances/{location} with a display name (null clears it): the original's Rename Instance.</summary>
    public Task<JsonElement> RenameInstanceAsync(string location, string? displayName, CancellationToken ct = default) =>
        SendJsonAsync<JsonElement>(HttpMethod.Put, $"instances/{LocationPath(location)}", new Dictionary<string, object?> { ["displayName"] = displayName }, ct);

    /// <summary>PUT instances/{location} with a calendar event to link, or null to unlink (the original's InstanceCalendarLinkDialog).</summary>
    public Task<JsonElement> LinkInstanceCalendarEventAsync(string location, string? calendarEntryId, CancellationToken ct = default) =>
        SendJsonAsync<JsonElement>(HttpMethod.Put, $"instances/{LocationPath(location)}", new Dictionary<string, object?> { ["calendarEntryId"] = calendarEntryId }, ct);

    /// <summary>POST instances/{location}/announce (the original's InstanceAnnouncementDialog).</summary>
    public Task<JsonElement> AnnounceInInstanceAsync(string location, string title, string message, string? imageId, string? imageVersion, CancellationToken ct = default) =>
        SendJsonAsync<JsonElement>(HttpMethod.Post, $"instances/{LocationPath(location)}/announce", new Dictionary<string, object?>
        {
            ["location"] = location, ["title"] = title, ["message"] = message, ["imageId"] = imageId, ["imageVersion"] = imageVersion,
        }, ct);

    /// <summary>GET instances/{location}/shortName, kept as VRChat sends it.</summary>
    public Task<JsonElement> GetInstanceShortNameAsync(string location, CancellationToken ct = default) =>
        GetRequiredAsync<JsonElement>($"instances/{LocationPath(location)}/shortName", ct);

    /// <summary>GET users/{id}/groups/permissions: group id to the permissions this account holds in it.</summary>
    public async Task<IReadOnlyDictionary<string, List<string>>> GetGroupPermissionsAsync(string userId, CancellationToken ct = default) =>
        await GetRequiredAsync<Dictionary<string, List<string>>>($"users/{Escape(userId)}/groups/permissions", ct);

    public Task SeeNotificationAsync(string notificationId, CancellationToken ct = default) =>
        SendJsonAsync<JsonElement>(HttpMethod.Put, $"auth/user/notifications/{Escape(notificationId)}/see", null, ct);

    public Task SeeNotificationV2Async(string notificationId, CancellationToken ct = default) =>
        SendJsonAsync<JsonElement>(HttpMethod.Post, $"notifications/{Escape(notificationId)}/see", null, ct);

    public Task HideNotificationV2Async(string notificationId, CancellationToken ct = default) =>
        SendJsonAsync<JsonElement>(HttpMethod.Delete, $"notifications/{Escape(notificationId)}", null, ct);

    public Task RespondToNotificationV2Async(string notificationId, string responseType, string responseData, CancellationToken ct = default) =>
        SendJsonAsync<JsonElement>(HttpMethod.Post, $"notifications/{Escape(notificationId)}/respond", new { notificationId, responseType, responseData }, ct);

    /// <summary>POST invite/{id}/response: answer an invite or invite request with a saved message slot.</summary>
    public Task SendInviteResponseAsync(string notificationId, int responseSlot, CancellationToken ct = default) =>
        SendJsonAsync<JsonElement>(HttpMethod.Post, $"invite/{Escape(notificationId)}/response", new { responseSlot, rsvp = true }, ct);

    /// <summary>DELETE user/{id}/friendRequest: drop a friend request that was ignored.</summary>
    public Task DeleteHiddenFriendRequestAsync(string userId, CancellationToken ct = default) =>
        SendJsonAsync<JsonElement>(HttpMethod.Delete, $"user/{Escape(userId)}/friendRequest", null, ct);

    public Task HideNotificationAsync(string notificationId, CancellationToken ct = default) =>
        SendJsonAsync<JsonElement>(HttpMethod.Put, $"auth/user/notifications/{Escape(notificationId)}/hide", null, ct);

    // --- Tools page ---

    public async Task<IReadOnlyList<CalendarEvent>> GetAllCalendarEventsAsync(string path, string date, CancellationToken ct = default)
    {
        const int pageSize = 100;
        var all = new List<CalendarEvent>();
        for (var offset = 0; ; offset += pageSize)
        {
            var page = await GetRequiredAsync<CalendarEventPage>($"{path}?n={pageSize}&offset={offset}&date={Escape(date)}", ct);
            all.AddRange(page.Results);
            if (page.Results.Count < pageSize) return all;
        }
    }

    public Task<CalendarEvent> FollowCalendarEventAsync(string groupId, string eventId, bool isFollowing, CancellationToken ct = default) =>
        SendJsonAsync<CalendarEvent>(HttpMethod.Post, $"calendar/{Escape(groupId)}/{Escape(eventId)}/follow", new { isFollowing }, ct);

    public async Task<string> GetCalendarEventIcsAsync(string groupId, string eventId, CancellationToken ct = default)
    {
        var path = $"calendar/{Escape(groupId)}/{Escape(eventId)}.ics";
        using var response = await _http.GetAsync(path, ct);
        await EnsureSuccessAsync(response, "GET", path, ct);
        return await response.Content.ReadAsStringAsync(ct);
    }

    public Task<JsonElement> GetInfoPushAsync(CancellationToken ct = default) =>
        GetRequiredAsync<JsonElement>("infoPush?require=user-all&include=help-article&include=quick-menu-banner&include=loading-screen-banners", ct);

    public async Task<IReadOnlyList<GroupByStatus>> GetGroupsByStatusAsync(string userId, string status, CancellationToken ct = default) =>
        await GetRequiredAsync<List<GroupByStatus>>($"users/{Escape(userId)}/groups/{Escape(status)}", ct);

    /// <summary>GET groups/{id}/posts, kept as VRChat sends it.</summary>
    public Task<JsonElement> GetGroupPostsAsync(string groupId, int n = 100, int offset = 0, CancellationToken ct = default) =>
        GetRequiredAsync<JsonElement>($"groups/{Escape(groupId)}/posts?n={n}&offset={offset}&publicOnly=false", ct);

    /// <summary>GET users/{id}/instances/groups/{groupId}, kept as VRChat sends it.</summary>
    public Task<JsonElement> GetGroupInstancesAsync(string userId, string groupId, CancellationToken ct = default) =>
        GetRequiredAsync<JsonElement>($"users/{Escape(userId)}/instances/groups/{Escape(groupId)}", ct);

    /// <summary>POST groups/{groupId}/invites - invite a user to a group.</summary>
    public Task SendGroupInviteAsync(string groupId, string userId, CancellationToken ct = default) =>
        SendJsonAsync<JsonElement>(HttpMethod.Post, $"groups/{Escape(groupId)}/invites", new { userId }, ct);

    public Task LeaveGroupAsync(string groupId, CancellationToken ct = default) =>
        SendNoContentAsync(HttpMethod.Post, $"groups/{Escape(groupId)}/leave", ct);

    public Task SetGroupRepresentationAsync(string groupId, bool isRepresenting, CancellationToken ct = default) =>
        SendJsonAsync<JsonElement>(HttpMethod.Put, $"groups/{Escape(groupId)}/representation", new { isRepresenting }, ct);

    /// <summary>POST groups/{id}/join: the account's membership afterwards (member, or requested for a request-to-join group).</summary>
    public Task<JsonElement> JoinGroupAsync(string groupId, CancellationToken ct = default) =>
        SendJsonAsync<JsonElement>(HttpMethod.Post, $"groups/{Escape(groupId)}/join", null, ct);

    public Task BlockGroupAsync(string groupId, CancellationToken ct = default) =>
        SendNoContentAsync(HttpMethod.Post, $"groups/{Escape(groupId)}/block", ct);

    public Task CancelGroupRequestAsync(string groupId, CancellationToken ct = default) =>
        SendNoContentAsync(HttpMethod.Delete, $"groups/{Escape(groupId)}/requests", ct);

    /// <summary>Removes the membership row: declines an invite or lifts a block, as the original does.</summary>
    public Task RemoveGroupMemberAsync(string groupId, string userId, CancellationToken ct = default) =>
        SendNoContentAsync(HttpMethod.Delete, $"groups/{Escape(groupId)}/members/{Escape(userId)}", ct);

    // ---- the group pop-up's editing: the original's api/group.ts, one VRChat call each ----------
    // Bodies are built by the caller in the original's order (VrchatJson keeps nulls), and the
    // answers stay VRChat's JSON.

    /// <summary>DELETE groups/{id} with {"hardDelete":false}: the sole owner's Delete Group.</summary>
    public Task DeleteGroupAsync(string groupId, CancellationToken ct = default) =>
        SendDiscardAsync(HttpMethod.Delete, $"groups/{Escape(groupId)}", new { hardDelete = false }, ct);

    /// <summary>GET groups/roleTemplates: the role templates Create Group offers.</summary>
    public Task<JsonElement> GetGroupRoleTemplatesAsync(CancellationToken ct = default) =>
        GetRequiredAsync<JsonElement>("groups/roleTemplates", ct);

    /// <summary>POST groups: the new group.</summary>
    public Task<JsonElement> CreateGroupAsync(object body, CancellationToken ct = default) =>
        SendJsonAsync<JsonElement>(HttpMethod.Post, "groups", body, ct);

    /// <summary>PUT groups/{id}: the group after the edit.</summary>
    public Task<JsonElement> UpdateGroupAsync(string groupId, object body, CancellationToken ct = default) =>
        SendJsonAsync<JsonElement>(HttpMethod.Put, $"groups/{Escape(groupId)}", body, ct);

    /// <summary>GET calendar/{groupId}?n=&amp;after=: events from a point in time, as the calendar link dialog asks.</summary>
    public Task<JsonElement> GetGroupCalendarAfterAsync(string groupId, DateTimeOffset after, int n, CancellationToken ct = default) =>
        GetRequiredAsync<JsonElement>($"calendar/{Escape(groupId)}?n={n}&after={Uri.EscapeDataString(after.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture))}", ct);

    /// <summary>GET calendar/{groupId}: the group's events (the first page, as the original asks).</summary>
    public Task<JsonElement> GetGroupCalendarAsync(string groupId, CancellationToken ct = default) =>
        GetRequiredAsync<JsonElement>($"calendar/{Escape(groupId)}", ct);

    /// <summary>GET calendar/{groupId}/{eventId}: one event, with this account's interest in it.</summary>
    public Task<JsonElement> GetCalendarEventAsync(string groupId, string eventId, CancellationToken ct = default) =>
        GetRequiredAsync<JsonElement>($"calendar/{Escape(groupId)}/{Escape(eventId)}", ct);

    /// <summary>POST calendar/{groupId}/event: the new event.</summary>
    public Task<JsonElement> CreateCalendarEventAsync(string groupId, object body, CancellationToken ct = default) =>
        SendJsonAsync<JsonElement>(HttpMethod.Post, $"calendar/{Escape(groupId)}/event", body, ct);

    /// <summary>PUT calendar/{groupId}/{eventId}/event: the event after the edit.</summary>
    public Task<JsonElement> UpdateCalendarEventAsync(string groupId, string eventId, object body, CancellationToken ct = default) =>
        SendJsonAsync<JsonElement>(HttpMethod.Put, $"calendar/{Escape(groupId)}/{Escape(eventId)}/event", body, ct);

    /// <summary>DELETE calendar/{groupId}/{eventId}.</summary>
    public Task DeleteCalendarEventAsync(string groupId, string eventId, CancellationToken ct = default) =>
        SendNoContentAsync(HttpMethod.Delete, $"calendar/{Escape(groupId)}/{Escape(eventId)}", ct);

    /// <summary>POST groups/{groupId}/posts: the new post.</summary>
    public Task<JsonElement> CreateGroupPostAsync(string groupId, object body, CancellationToken ct = default) =>
        SendJsonAsync<JsonElement>(HttpMethod.Post, $"groups/{Escape(groupId)}/posts", body, ct);

    /// <summary>PUT groups/{groupId}/posts/{postId}: the post after the edit.</summary>
    public Task<JsonElement> UpdateGroupPostAsync(string groupId, string postId, object body, CancellationToken ct = default) =>
        SendJsonAsync<JsonElement>(HttpMethod.Put, $"groups/{Escape(groupId)}/posts/{Escape(postId)}", body, ct);

    /// <summary>DELETE groups/{groupId}/posts/{postId}.</summary>
    public Task DeleteGroupPostAsync(string groupId, string postId, CancellationToken ct = default) =>
        SendNoContentAsync(HttpMethod.Delete, $"groups/{Escape(groupId)}/posts/{Escape(postId)}", ct);

    /// <summary>GET groups/{groupId}/transfer?transferTargetId=: whether the target can take the group over.</summary>
    public Task<JsonElement> GetGroupTransferabilityAsync(string groupId, string targetUserId, CancellationToken ct = default) =>
        GetRequiredAsync<JsonElement>($"groups/{Escape(groupId)}/transfer?transferTargetId={Escape(targetUserId)}", ct);

    /// <summary>POST groups/{groupId}/transfer: asks the target to take the group over.</summary>
    public Task<JsonElement> TransferGroupAsync(string groupId, string targetUserId, CancellationToken ct = default) =>
        SendJsonAsync<JsonElement>(HttpMethod.Post, $"groups/{Escape(groupId)}/transfer", new { transferTargetId = targetUserId }, ct);

    /// <summary>GET groups/{groupId}/permissions: every permission a role can hold.</summary>
    public Task<JsonElement> GetGroupPermissionListAsync(string groupId, CancellationToken ct = default) =>
        GetRequiredAsync<JsonElement>($"groups/{Escape(groupId)}/permissions", ct);

    /// <summary>POST groups/{groupId}/roles: the roles after the new one.</summary>
    public Task<JsonElement> CreateGroupRoleAsync(string groupId, object body, CancellationToken ct = default) =>
        SendJsonAsync<JsonElement>(HttpMethod.Post, $"groups/{Escape(groupId)}/roles", body, ct);

    /// <summary>PUT groups/{groupId}/roles/{roleId}: the roles after the edit.</summary>
    public Task<JsonElement> UpdateGroupRoleAsync(string groupId, string roleId, object body, CancellationToken ct = default) =>
        SendJsonAsync<JsonElement>(HttpMethod.Put, $"groups/{Escape(groupId)}/roles/{Escape(roleId)}", body, ct);

    /// <summary>DELETE groups/{groupId}/roles/{roleId}: the roles left.</summary>
    public Task<JsonElement> DeleteGroupRoleAsync(string groupId, string roleId, CancellationToken ct = default) =>
        SendJsonAsync<JsonElement>(HttpMethod.Delete, $"groups/{Escape(groupId)}/roles/{Escape(roleId)}", null, ct);

    /// <summary>GET groups/{groupId}/galleries/{galleryId}?n=&amp;offset=: one page of a gallery's images.</summary>
    public Task<JsonElement> GetGroupGalleryImagesAsync(string groupId, string galleryId, int n, int offset, CancellationToken ct = default) =>
        GetRequiredAsync<JsonElement>($"groups/{Escape(groupId)}/galleries/{Escape(galleryId)}?n={n}&offset={offset}", ct);

    /// <summary>POST groups/{groupId}/galleries: the new gallery.</summary>
    public Task<JsonElement> CreateGroupGalleryAsync(string groupId, object body, CancellationToken ct = default) =>
        SendJsonAsync<JsonElement>(HttpMethod.Post, $"groups/{Escape(groupId)}/galleries", body, ct);

    /// <summary>PUT groups/{groupId}/galleries/{galleryId}: the gallery after the edit.</summary>
    public Task<JsonElement> UpdateGroupGalleryAsync(string groupId, string galleryId, object body, CancellationToken ct = default) =>
        SendJsonAsync<JsonElement>(HttpMethod.Put, $"groups/{Escape(groupId)}/galleries/{Escape(galleryId)}", body, ct);

    /// <summary>DELETE groups/{groupId}/galleries/{galleryId}.</summary>
    public Task DeleteGroupGalleryAsync(string groupId, string galleryId, CancellationToken ct = default) =>
        SendNoContentAsync(HttpMethod.Delete, $"groups/{Escape(groupId)}/galleries/{Escape(galleryId)}", ct);

    /// <summary>POST groups/{groupId}/galleries/{galleryId}/images: the image added (approved or waiting).</summary>
    public Task<JsonElement> AddGroupGalleryImageAsync(string groupId, string galleryId, string fileId, CancellationToken ct = default) =>
        SendJsonAsync<JsonElement>(HttpMethod.Post, $"groups/{Escape(groupId)}/galleries/{Escape(galleryId)}/images", new { fileId }, ct);

    /// <summary>DELETE groups/{groupId}/galleries/{galleryId}/images/{imageId}.</summary>
    public Task DeleteGroupGalleryImageAsync(string groupId, string galleryId, string imageId, CancellationToken ct = default) =>
        SendNoContentAsync(HttpMethod.Delete, $"groups/{Escape(groupId)}/galleries/{Escape(galleryId)}/images/{Escape(imageId)}", ct);

    /// <summary>GET economy/store?storeId=&amp;hydrateListings=true&amp;hydrateContext=false: a group's store with its listings.</summary>
    public Task<JsonElement> GetStoreAsync(string storeId, CancellationToken ct = default) =>
        GetRequiredAsync<JsonElement>($"economy/store?storeId={Escape(storeId)}&hydrateListings=true&hydrateContext=false", ct);

    /// <summary>A VRChat write whose answer is not used.</summary>
    private async Task SendDiscardAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
            request.Content = new StringContent(VrchatJson.Serialize(body), System.Text.Encoding.UTF8, "application/json");
        using var response = await _http.SendAsync(request, ct);
        await EnsureSuccessAsync(response, method.Method, path, ct);
    }

    public async Task<IReadOnlyList<InviteMessage>> GetInviteMessagesAsync(string userId, string type, CancellationToken ct = default) =>
        await GetRequiredAsync<List<InviteMessage>>($"message/{Escape(userId)}/{Escape(type)}", ct);

    public async Task<IReadOnlyList<InviteMessage>> UpdateInviteMessageAsync(string userId, string type, int slot, string message, CancellationToken ct = default) =>
        await SendJsonAsync<List<InviteMessage>>(HttpMethod.Put, $"message/{Escape(userId)}/{Escape(type)}/{slot}", new { message }, ct);

    public Task<ModerationReportPage> GetModerationReportsAsync(string reportingUserId, string? type, int n, int offset, CancellationToken ct = default) =>
        GetRequiredAsync<ModerationReportPage>($"moderationReports?reportingUserId={Escape(reportingUserId)}&n={n}&offset={offset}" + (string.IsNullOrEmpty(type) ? "" : $"&type={Escape(type)}"), ct);

    /// <summary>POST moderationReports - report a user, world, avatar or group to VRChat.</summary>
    public Task<JsonElement> SubmitModerationReportAsync(object report, CancellationToken ct = default) =>
        SendJsonAsync<JsonElement>(HttpMethod.Post, "moderationReports", report, ct);

    /// <summary>GET visits: how many users are online, a bare number.</summary>
    public Task<long> GetVisitsAsync(CancellationToken ct = default) => GetRequiredAsync<long>("visits", ct);

    /// <summary>GET instances/count: {"instances": n}, how many instances are active.</summary>
    public Task<JsonElement> GetInstanceCountAsync(CancellationToken ct = default) => GetRequiredAsync<JsonElement>("instances/count", ct);

    public Task<JsonElement> GetConfigAsync(CancellationToken ct = default) =>
        GetRequiredAsync<JsonElement>("config", ct);

    public async Task<IReadOnlyList<ProductPurchase>> GetAllProductPurchasesAsync(string buyerId, CancellationToken ct = default)
    {
        const int pageSize = 50;
        var all = new List<ProductPurchase>();
        for (var offset = 0; ; offset += pageSize)
        {
            var page = await GetRequiredAsync<List<ProductPurchase>>($"economy/purchases?buyerId={Escape(buyerId)}&order=desc&sort=purchaseDate&n={pageSize}&offset={offset}", ct);
            all.AddRange(page);
            if (page.Count < pageSize) return all;
        }
    }

    public Task<EconomyTransactionPage> GetEconomyTransactionsAsync(string userId, int n, CancellationToken ct = default) =>
        GetRequiredAsync<EconomyTransactionPage>($"user/{Escape(userId)}/economy/transactions?n={n}", ct);

    public Task<UserNote> SaveUserNoteAsync(string targetUserId, string note, CancellationToken ct = default) =>
        SendJsonAsync<UserNote>(HttpMethod.Post, "userNotes", new { targetUserId, note }, ct);

    /// <summary>GET a VRChat path with a query and return its JSON as it came. Null query values are left out.</summary>
    public Task<JsonElement> GetJsonAsync(string path, IEnumerable<KeyValuePair<string, string?>> query, CancellationToken ct = default) =>
        GetRequiredAsync<JsonElement>(path + QueryString(query), ct);

    /// <summary>POST to a VRChat path that takes its parameters in the query, as the store purchase does.</summary>
    public Task<JsonElement> PostQueryAsync(string path, IEnumerable<KeyValuePair<string, string?>> query, CancellationToken ct = default) =>
        SendJsonAsync<JsonElement>(HttpMethod.Post, path + QueryString(query), null, ct);

    private static string QueryString(IEnumerable<KeyValuePair<string, string?>> query)
    {
        var parts = query.Where(p => p.Value is not null).Select(p => $"{Escape(p.Key)}={Escape(p.Value!)}").ToArray();
        return parts.Length == 0 ? "" : "?" + string.Join('&', parts);
    }

    private async Task<T> GetRequiredAsync<T>(string path, CancellationToken ct)
    {
        using var response = await _http.GetAsync(path, ct);
        await EnsureSuccessAsync(response, "GET", path, ct);
        return await ReadAsync<T>(response, ct);
    }

    private async Task<T?> GetOrNullAsync<T>(string path, CancellationToken ct) where T : class
    {
        using var response = await _http.GetAsync(path, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        await EnsureSuccessAsync(response, "GET", path, ct);
        return await ReadAsync<T>(response, ct);
    }

    private async Task<T> SendJsonAsync<T>(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
            request.Content = new StringContent(VrchatJson.Serialize(body), System.Text.Encoding.UTF8, "application/json");
        using var response = await _http.SendAsync(request, ct);
        await EnsureSuccessAsync(response, method.Method, path, ct);
        return await ReadAsync<T>(response, ct);
    }

    private async Task SendNoContentAsync(HttpMethod method, string path, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        using var response = await _http.SendAsync(request, ct);
        await EnsureSuccessAsync(response, method.Method, path, ct);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        return VrchatJson.Deserialize<T>(bytes) ?? throw new JsonException($"VRChat returned a null body for {typeof(T).Name}");
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string method, string path, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        var message = await ReadErrorMessageAsync(response, ct);
        var cleanPath = path.Split('?')[0];
        throw response.StatusCode == HttpStatusCode.Unauthorized
            ? new VrchatUnauthorizedException(method, cleanPath, message)
            : new VrchatApiException(response.StatusCode, method, cleanPath, message);
    }

    private static async Task<string?> ReadErrorMessageAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            if (string.IsNullOrWhiteSpace(body)) return null;
            return VrchatJson.Deserialize<ErrorEnvelope>(body)?.Error?.Message ?? body[..Math.Min(body.Length, 200)];
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

public enum TwoFactorMethod
{
    Totp,
    RecoveryCode,
    EmailOtp,
}

public abstract record LoginResult
{
    public sealed record LoggedIn(CurrentUser User) : LoginResult;
    /// <summary>Methods as VRChat names them: "totp", "otp", "emailOtp".</summary>
    public sealed record RequiresTwoFactor(IReadOnlyList<string> Methods) : LoginResult
    {
        public bool IsEmailOtp => Methods.Contains("emailOtp");
    }
    public sealed record InvalidCredentials(string? Message) : LoginResult;
}
