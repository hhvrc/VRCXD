using Microsoft.EntityFrameworkCore;
using VRCX.Common.Services;
using VRCX.Common.VrcxDb;

namespace VRCX.Common.Features.Users;

/// <summary>The stored copies of the notes an account keeps on users on VRChat.</summary>
public sealed class UserNotes(VrcxContext db)
{
    /// <summary>
    /// VRChat saved the account's note on a user: the stored copy follows. A user the server has
    /// never stored gets no row; the next sync adds both.
    /// </summary>
    /// <param name="accountId">The account whose note it is.</param>
    /// <param name="userId">The user the note is on.</param>
    /// <param name="text">The note as typed.</param>
    /// <param name="saved">VRChat's answer to the save.</param>
    /// <param name="cancellationToken"></param>
    public async Task SavedAsync(string accountId, string userId, string text, VRCX.Vrchat.Models.UserNote saved, CancellationToken cancellationToken)
    {
        // The original's EditNoteAndMemoDialog shows the saved note through replaceBioSymbols.
        var note = VrchatText.ReplaceBioSymbols(text);
        var row = await db.UserNotes.FirstOrDefaultAsync(n => n.OwnerUserId == accountId && n.TargetUserId == userId, cancellationToken);
        if (row is not null)
        {
            row.Note = note;
            row.FetchedAt = DateTimeOffset.UtcNow;
        }
        else if (await db.Users.AnyAsync(u => u.Id == userId, cancellationToken))
        {
            db.UserNotes.Add(new UserNote
            {
                Id = saved.Id,
                OwnerUserId = accountId,
                TargetUserId = userId,
                Note = note,
                CreatedAt = saved.CreatedAt,
                FetchedAt = DateTimeOffset.UtcNow,
            });
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>The account's note on a user as last stored, or null when there is none.</summary>
    public Task<string?> NoteAsync(string accountId, string userId, CancellationToken cancellationToken) =>
        db.UserNotes.AsNoTracking()
            .Where(n => n.OwnerUserId == accountId && n.TargetUserId == userId)
            .Select(n => (string?)n.Note)
            .FirstOrDefaultAsync(cancellationToken);
}
