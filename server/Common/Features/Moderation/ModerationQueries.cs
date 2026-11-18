using Microsoft.EntityFrameworkCore;
using VRCX.Common.VrcxDb;

namespace VRCX.Common.Features.Moderation;

public sealed class PlayerModerationResponse
{
    public required string Id { get; init; }
    public required string SourceUserId { get; init; }
    public required string SourceDisplayName { get; init; }
    public required string TargetUserId { get; init; }
    public required string TargetDisplayName { get; init; }
    /// <summary>block, mute, hideAvatar, interactOff, ...</summary>
    public required string Type { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
}

/// <summary>
/// The player moderations an account applied, as VRChat keeps them and the full sync mirrors them.
/// Read only; shared by the moderations endpoint and the MCP tools.
/// </summary>
public sealed class ModerationQueries(VrcxContext db)
{
    /// <summary>Blocks, mutes and avatar hides the account has applied, as VRChat reports them.</summary>
    public Task<PlayerModerationResponse[]> ModerationsAsync(string accountId, CancellationToken cancellationToken) =>
        db.PlayerModerations.AsNoTracking()
            .Where(m => m.OwnerUserId == accountId)
            .OrderBy(m => m.Target.DisplayName).ThenBy(m => m.Type)
            .Select(m => new PlayerModerationResponse
            {
                Id = m.Id, SourceUserId = m.OwnerUserId, SourceDisplayName = m.Owner.User.DisplayName, TargetUserId = m.TargetUserId, TargetDisplayName = m.Target.DisplayName, Type = m.Type, CreatedAt = m.CreatedAt,
            })
            .ToArrayAsync(cancellationToken);
}
