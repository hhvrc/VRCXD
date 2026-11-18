using Microsoft.EntityFrameworkCore;
using VRCX.Common.VrcxDb;

namespace VRCX.Common.Services;

/// <summary>What a memo write left behind. <see cref="Found"/> is false when the user, world or avatar was never seen.</summary>
public sealed record MemoWriteResult(bool Found, string Memo, DateTimeOffset? UpdatedAt);

/// <summary>
/// VRCX's own memos on users, worlds and avatars: local data, VRChat never sees it. The memo
/// endpoints and the MCP memo tools both write through here.
/// </summary>
public sealed class MemoStore(VrcxContext db)
{
    /// <summary>Write (or replace) the memo on a user. An empty memo is stored as empty; <see cref="DeleteUserMemoAsync"/> removes it.</summary>
    public async Task<MemoWriteResult> SetUserMemoAsync(string userId, string text, CancellationToken cancellationToken)
    {
        if (!await db.Users.AnyAsync(u => u.Id == userId, cancellationToken)) return new MemoWriteResult(false, "", null);
        var memo = await db.UserMemos.FirstOrDefaultAsync(m => m.UserId == userId, cancellationToken);
        if (memo is null)
        {
            memo = new UserMemo { UserId = userId, Memo = text };
            db.UserMemos.Add(memo);
        }
        memo.Memo = text;
        memo.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return new MemoWriteResult(true, memo.Memo, memo.UpdatedAt);
    }

    public Task DeleteUserMemoAsync(string userId, CancellationToken cancellationToken) =>
        db.UserMemos.Where(m => m.UserId == userId).ExecuteDeleteAsync(cancellationToken);

    /// <summary>Write the memo on a world. An empty memo removes it, whether or not the world was seen.</summary>
    public async Task<MemoWriteResult> SetWorldMemoAsync(string worldId, string text, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            await db.WorldMemos.Where(m => m.WorldId == worldId).ExecuteDeleteAsync(cancellationToken);
            return new MemoWriteResult(true, "", null);
        }
        if (!await db.Worlds.AnyAsync(w => w.Id == worldId, cancellationToken)) return new MemoWriteResult(false, "", null);
        var memo = await db.WorldMemos.FirstOrDefaultAsync(m => m.WorldId == worldId, cancellationToken);
        if (memo is null)
        {
            memo = new WorldMemo { WorldId = worldId, Memo = text };
            db.WorldMemos.Add(memo);
        }
        memo.Memo = text;
        memo.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return new MemoWriteResult(true, memo.Memo, memo.UpdatedAt);
    }

    /// <summary>Write the memo on an avatar. An empty memo removes it, whether or not the avatar was seen.</summary>
    public async Task<MemoWriteResult> SetAvatarMemoAsync(string avatarId, string text, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            await db.AvatarMemos.Where(m => m.AvatarId == avatarId).ExecuteDeleteAsync(cancellationToken);
            return new MemoWriteResult(true, "", null);
        }
        if (!await db.Avatars.AnyAsync(a => a.Id == avatarId, cancellationToken)) return new MemoWriteResult(false, "", null);
        var memo = await db.AvatarMemos.FirstOrDefaultAsync(m => m.AvatarId == avatarId, cancellationToken);
        if (memo is null)
        {
            memo = new AvatarMemo { AvatarId = avatarId, Memo = text };
            db.AvatarMemos.Add(memo);
        }
        memo.Memo = text;
        memo.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return new MemoWriteResult(true, memo.Memo, memo.UpdatedAt);
    }
}
