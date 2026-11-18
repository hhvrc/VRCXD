using VRCX.Common.VrcxDb;

namespace VRCX.Common.Features.Friends;

public static class FriendshipQueryExtensions
{
    /// <summary>The account's open friendships: its friends right now (an ended row is the friend log's).</summary>
    public static IQueryable<Friendship> CurrentOf(this IQueryable<Friendship> friendships, string accountId) =>
        friendships.Where(f => f.OwnerUserId == accountId && f.EndedAt == null);
}
