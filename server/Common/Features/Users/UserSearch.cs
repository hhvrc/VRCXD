using Microsoft.EntityFrameworkCore;
using VRCX.Common.Features.Accounts;
using VRCX.Common.Features.Friends;
using VRCX.Common.Services.Ingestion;
using VRCX.Common.VrcxDb;
using VRCX.Vrchat.Models;

namespace VRCX.Common.Features.Users;

public sealed class UserSearchResultResponse
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string Status { get; init; }
    public string? StatusDescription { get; init; }
    public required string TrustLevel { get; init; }
    public string? ThumbnailImageUrl { get; init; }
    public required bool IsFriend { get; init; }
}

/// <summary>
/// Searching VRChat for users by display name, through a logged-in account, and storing what comes
/// back so the results can be opened without another fetch.
/// </summary>
public sealed class UserSearch(VrcxContext db, AccountSessionRunner runner, VrchatStore store)
{
    /// <summary>
    /// Up to 20 results, each marked when it is a friend of the account searched with. The problem
    /// when no account is logged in or VRChat refused. A blank search answers nothing without asking.
    /// </summary>
    public async Task<Result<UserSearchResultResponse[]>> SearchAsync(string? query, string? account, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query)) return Array.Empty<UserSearchResultResponse>();
        var via = runner.PickAccount(account);
        if (via is null) return Errors.AccountError.NoAccounts;

        return await runner.ReadAsync(via, async client =>
        {
            var results = await client.SearchUsersAsync(query.Trim(), 20, 0, ct);
            var now = DateTimeOffset.UtcNow;
            foreach (var result in results) await store.UpsertSearchResultAsync(db, result, now, ct);
            await db.SaveChangesAsync(ct);

            var friendIds = await db.Friendships.CurrentOf(via).Select(f => f.FriendUserId).ToHashSetAsync(ct);
            return results.Select(r => new UserSearchResultResponse
            {
                Id = r.Id, DisplayName = r.DisplayName, Status = r.Status, StatusDescription = r.StatusDescription,
                TrustLevel = TrustLevel.FromTags(r.Tags), ThumbnailImageUrl = r.CurrentAvatarThumbnailImageUrl,
                IsFriend = friendIds.Contains(r.Id),
            }).ToArray();
        }, ct);
    }
}
