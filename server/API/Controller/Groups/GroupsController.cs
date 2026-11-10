using System.Net.Mime;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenShock.Internal.Common.Problems;
using VRCX.API.Models.Response;
using VRCX.Common;
using VRCX.Common.Authentication;
using VRCX.Common.Errors;
using VRCX.Common.Services.Sessions;
using VRCX.Common.Services.Sync;
using VRCX.Common.VrcxDb;
using VRCX.Vrchat;

namespace VRCX.API.Controller.Groups;

/// <summary>
/// Groups, their roles and the memberships seen.
/// </summary>
[ApiController]
[Tags("Groups")]
[ApiVersion("1")]
[Route("/{version:apiVersion}/groups")]
[Authorize(AuthenticationSchemes = VrcxAuthSchemes.ApiToken)]
public sealed class GroupsController : VrcxControllerBase
{
    private readonly VrcxContext _db;
    private readonly IAccountSessionManager _sessions;
    private readonly IAccountSyncService _sync;

    public GroupsController(VrcxContext db, IAccountSessionManager sessions, IAccountSyncService sync)
    {
        _db = db;
        _sessions = sessions;
        _sync = sync;
    }

    /// <summary>
    /// A group with its roles and current members. With refresh=true, GET groups/{id}?includeRoles=true is fetched from VRChat first.
    /// </summary>
    /// <response code="200">The group</response>
    /// <response code="404">Never seen, or VRChat has no such group</response>
    /// <response code="409">refresh=true but no account is logged in</response>
    [HttpGet("{groupId}")]
    [ProducesResponseType<GroupResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> GetGroup([FromRoute] string groupId, [FromQuery] bool refresh, [FromQuery] string? account, CancellationToken cancellationToken)
    {
        if (refresh)
        {
            var via = PickAccount(account, _sessions);
            if (via is null) return Problem(AccountError.NoAccounts);
            try
            {
                await _sync.RefreshGroupAsync(via, groupId, cancellationToken);
            }
            catch (VrchatApiException ex)
            {
                return VrchatProblem(ex);
            }
        }

        var group = await _db.Groups.AsNoTracking().Include(g => g.Roles).FirstOrDefaultAsync(g => g.Id == groupId, cancellationToken);
        if (group is null) return Problem(GroupError.GroupNotFound);

        var members = await _db.GroupMembers.AsNoTracking()
            .Where(m => m.GroupId == groupId && m.LeftAt == null)
            .OrderBy(m => m.JoinedAt)
            .Select(m => new GroupMemberResponse
            {
                UserId = m.UserId,
                DisplayName = m.User.DisplayName,
                MembershipStatus = m.MembershipStatus,
                IsRepresenting = m.IsRepresenting,
                JoinedAt = m.JoinedAt,
                Roles = m.Roles.Select(r => r.Role.Name).ToList(),
            })
            .ToListAsync(cancellationToken);

        return Ok(new GroupResponse
        {
            Id = group.Id, Name = group.Name, ShortCode = group.ShortCode, Discriminator = group.Discriminator, Description = group.Description,
            IconUrl = group.IconUrl, BannerUrl = group.BannerUrl, OwnerUserId = group.OwnerUserId, Privacy = group.Privacy, JoinState = group.JoinState,
            Rules = group.Rules, Links = group.Links, Languages = group.Languages, Tags = group.Tags, MemberCount = group.MemberCount,
            IsVerified = group.IsVerified, CreatedAt = group.CreatedAt, IsStub = group.IsStub, FetchedAt = group.FetchedAt,
            Roles = group.Roles.OrderBy(r => r.Order).Select(r => new GroupRoleResponse
            {
                Id = r.Id, Name = r.Name, Description = r.Description, IsManagementRole = r.IsManagementRole, IsSelfAssignable = r.IsSelfAssignable,
                Permissions = r.Permissions, Order = r.Order,
            }).ToList(),
            Members = members,
        });
    }
}
