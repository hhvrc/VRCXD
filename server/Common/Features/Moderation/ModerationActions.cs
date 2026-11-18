using Microsoft.EntityFrameworkCore;
using VRCX.Common.Problems;
using VRCX.Common.Features.Accounts;
using VRCX.Common.Services.Ingestion;
using VRCX.Common.VrcxDb;

namespace VRCX.Common.Features.Moderation;

/// <summary>
/// The player moderations an account applies and removes through VRCXD (block, mute, disabled
/// chatbox, disabled avatar interaction), done on VRChat with the account's own session and then
/// recorded here. A method returns null when it worked, else the problem to answer with.
/// </summary>
public sealed class ModerationActions(VrcxContext db, AccountSessionRunner runner, VrchatStore store)
{
    /// <summary>Blocks, mutes, disables the chatbox of, or disables avatar interaction with a user.</summary>
    public Task<VrcxProblem?> SendAsync(string accountId, string userId, string type, CancellationToken ct) =>
        runner.RunAsync(accountId, async client =>
        {
            var sent = await client.SendPlayerModerationAsync(userId, type, ct);
            var row = await db.PlayerModerations.FirstOrDefaultAsync(m => m.Id == sent.Id, ct);
            if (row is null)
            {
                // A user the server has never stored can't be a row yet; the next sync adds them both.
                if (!await db.Users.AnyAsync(u => u.Id == userId, ct)) return;
                row = new PlayerModeration { Id = sent.Id, OwnerUserId = accountId, TargetUserId = userId, Type = type };
                db.PlayerModerations.Add(row);
            }
            row.CreatedAt = sent.Created;
            row.FetchedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
        }, ct);

    /// <summary>Undoes a block, mute, disabled chatbox or disabled avatar interaction.</summary>
    public Task<VrcxProblem?> DeleteAsync(string accountId, string userId, string type, CancellationToken ct) =>
        runner.RunAsync(accountId, async client =>
        {
            await client.DeletePlayerModerationAsync(userId, type, ct);
            await db.PlayerModerations
                .Where(m => m.OwnerUserId == accountId && m.TargetUserId == userId && m.Type == type)
                .ExecuteDeleteAsync(ct);
        }, ct);

    /// <summary>Removes every moderation of one kind from the account.</summary>
    public Task<VrcxProblem?> ClearAsync(string accountId, string type, CancellationToken ct) =>
        runner.RunAsync(accountId, async client =>
        {
            await client.DeletePlayerModerationsOfTypeAsync(type, ct);
            await db.PlayerModerations.Where(m => m.OwnerUserId == accountId && m.Type == type).ExecuteDeleteAsync(ct);
        }, ct);

    /// <summary>Fetches the account's moderations from VRChat now instead of waiting for the next sync.</summary>
    public Task<VrcxProblem?> RefreshAsync(string accountId, CancellationToken ct) =>
        runner.RunAsync(accountId, async client =>
        {
            var moderations = await client.GetPlayerModerationsAsync(ct);
            await store.SyncPlayerModerationsAsync(db, accountId, moderations, DateTimeOffset.UtcNow, ct);
            await db.SaveChangesAsync(ct);
        }, ct);
}
