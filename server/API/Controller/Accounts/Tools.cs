using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using System.Text.Json;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenShock.Internal.Common.Problems;
using VRCX.API.Models.Response;
using VRCX.Common.Errors;
using VRCX.Common.VrcxDb;
using VRCX.Vrchat;
using VRCX.Vrchat.Models;

namespace VRCX.API.Controller.Accounts;

public sealed record SubmitReportBody([Required] string Type, [Required] string Category, [Required] string Reason, [Required] string ContentId, string? Description = null, JsonElement? Details = null);

/// <summary>One friend with what the export tools read: the VRCX memo, the VRChat note, bio and status text.</summary>
public sealed class FriendExportRow
{
    public required string UserId { get; init; }
    public required string DisplayName { get; init; }
    public string? Memo { get; init; }
    public string? Note { get; init; }
    public string? Bio { get; init; }
    public string? StatusDescription { get; init; }
    public string? ThumbnailImageUrl { get; init; }
}

public sealed class UpdateInviteMessageBody
{
    [Required(AllowEmptyStrings = true)]
    [StringLength(64)]
    public required string Message { get; set; }
}

public sealed class SaveUserNoteBody
{
    [Required(AllowEmptyStrings = true)]
    [StringLength(256)]
    public required string Note { get; set; }
}

// The Tools page: the original's group, invite message, report, purchase and export tools.
// Nothing here is stored except notes; these are live reads and writes against VRChat, as
// they were in the original.
public sealed record OnlineUsersResponse(long Users, long? Instances);

public sealed partial class AccountsController
{
    /// <summary>The account's friends with their memos, notes, bios and status text, for the export tools.</summary>
    /// <response code="200">The friends</response>
    /// <response code="404">No such account</response>
    /// <summary>The original's Online Users row in Settings, Advanced: VRChat's online user and active instance counts (upstream issue 122).</summary>
    /// <response code="200">The counts</response>
    /// <response code="409">No live session</response>
    [HttpGet("{accountId}/online-users")]
    [ProducesResponseType<OnlineUsersResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> GetOnlineUsers([FromRoute] string accountId, CancellationToken cancellationToken) =>
        ReadLiveAsync(accountId, async client =>
        {
            var visits = await client.GetVisitsAsync(cancellationToken);
            var count = await client.GetInstanceCountAsync(cancellationToken);
            long? instances = count.ValueKind == System.Text.Json.JsonValueKind.Object && count.TryGetProperty("instances", out var n) && n.TryGetInt64(out var value) ? value : null;
            return new OnlineUsersResponse(visits, instances);
        });

    [HttpGet("{accountId}/tools/friends")]
    [ProducesResponseType<FriendExportRow[]>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> ListFriendsForExport([FromRoute] string accountId, CancellationToken cancellationToken)
    {
        if (!await _db.Accounts.AnyAsync(a => a.UserId == accountId, cancellationToken)) return Problem(AccountError.AccountNotFound);

        var rows = await _db.Friendships.AsNoTracking()
            .Where(f => f.OwnerUserId == accountId && f.EndedAt == null)
            .OrderBy(f => f.FriendNumber)
            .Select(f => new FriendExportRow
            {
                UserId = f.Friend.Id,
                DisplayName = f.Friend.DisplayName,
                Memo = _db.UserMemos.Where(m => m.UserId == f.Friend.Id).Select(m => m.Memo).FirstOrDefault(),
                Note = _db.UserNotes.Where(n => n.OwnerUserId == accountId && n.TargetUserId == f.Friend.Id).Select(n => n.Note).FirstOrDefault(),
                Bio = f.Friend.Bio,
                StatusDescription = f.Friend.StatusDescription,
                ThumbnailImageUrl = UserImage.Pick(f.Friend.IconUrl, f.Friend.CurrentAvatarThumbnailImageUrl, f.Friend.CurrentAvatarImageUrl),
            })
            .ToArrayAsync(cancellationToken);
        return Ok(rows);
    }

    /// <summary>Write the account's VRChat note on a user (POST userNotes) and keep the stored copy in step.</summary>
    /// <response code="204">Saved</response>
    /// <response code="409">No live session</response>
    [HttpPut("{accountId}/user-notes/{userId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> SaveUserNote([FromRoute] string accountId, [FromRoute] string userId, [FromBody] SaveUserNoteBody body, CancellationToken cancellationToken) =>
        RunAsync(accountId, async client =>
        {
            var saved = await client.SaveUserNoteAsync(userId, body.Note, cancellationToken);
            // The original's EditNoteAndMemoDialog shows the saved note through replaceBioSymbols.
            var note = VRCX.Common.Services.VrchatText.ReplaceBioSymbols(body.Note);
            var row = await _db.UserNotes.FirstOrDefaultAsync(n => n.OwnerUserId == accountId && n.TargetUserId == userId, cancellationToken);
            if (row is not null)
            {
                row.Note = note;
                row.FetchedAt = DateTimeOffset.UtcNow;
            }
            else if (await _db.Users.AnyAsync(u => u.Id == userId, cancellationToken))
            {
                _db.UserNotes.Add(new global::VRCX.Common.VrcxDb.UserNote
                {
                    Id = saved.Id,
                    OwnerUserId = accountId,
                    TargetUserId = userId,
                    Note = note,
                    CreatedAt = saved.CreatedAt,
                    FetchedAt = DateTimeOffset.UtcNow,
                });
            }
            await _db.SaveChangesAsync(cancellationToken);
        });

    /// <summary>The account's groups in one membership state, live from VRChat.</summary>
    /// <param name="accountId">The account</param>
    /// <param name="status">invited, requested or userblocked</param>
    /// <param name="cancellationToken">Cancelled with the request</param>
    /// <response code="200">The groups</response>
    /// <response code="409">No live session</response>
    [HttpGet("{accountId}/groups/{status:regex(^(invited|requested|userblocked)$)}")]
    [ProducesResponseType<GroupByStatus[]>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> ListGroupsByStatus([FromRoute] string accountId, [FromRoute] string status, CancellationToken cancellationToken) =>
        ReadLiveAsync(accountId, client => client.GetGroupsByStatusAsync(accountId, status, cancellationToken));

    /// <summary>Accept a group invite.</summary>
    /// <response code="204">Joined</response>
    [HttpPost("{accountId}/groups/{groupId}/join")]
    [ProducesResponseType<GroupJoinResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> JoinGroup([FromRoute] string accountId, [FromRoute] string groupId, CancellationToken cancellationToken) =>
        ReadLiveAsync(accountId, async client =>
        {
            var member = await client.JoinGroupAsync(groupId, cancellationToken);
            // The original toasts from this: "member" joined, "requested" sent a request.
            var status = member.ValueKind == JsonValueKind.Object && member.TryGetProperty("membershipStatus", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;
            return new GroupJoinResponse(status);
        });

    /// <summary>Block a group, which also declines its invite.</summary>
    /// <response code="204">Blocked</response>
    [HttpPost("{accountId}/groups/{groupId}/block")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> BlockGroup([FromRoute] string accountId, [FromRoute] string groupId, CancellationToken cancellationToken) =>
        RunAsync(accountId, client => client.BlockGroupAsync(groupId, cancellationToken));

    /// <summary>Withdraw a request to join a group.</summary>
    /// <response code="204">Withdrawn</response>
    [HttpDelete("{accountId}/groups/{groupId}/request")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> CancelGroupRequest([FromRoute] string accountId, [FromRoute] string groupId, CancellationToken cancellationToken) =>
        RunAsync(accountId, client => client.CancelGroupRequestAsync(groupId, cancellationToken));

    /// <summary>Decline a group invite or lift a block: both remove the account's membership row.</summary>
    /// <response code="204">Removed</response>
    [HttpDelete("{accountId}/groups/{groupId}/membership")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> RemoveGroupMembership([FromRoute] string accountId, [FromRoute] string groupId, CancellationToken cancellationToken) =>
        RunAsync(accountId, client => client.RemoveGroupMemberAsync(groupId, accountId, cancellationToken));

    /// <summary>The account's invite messages of one type.</summary>
    /// <param name="accountId">The account</param>
    /// <param name="type">message, request, requestResponse or response</param>
    /// <param name="cancellationToken">Cancelled with the request</param>
    /// <response code="200">The message slots</response>
    /// <response code="409">No live session</response>
    [HttpGet("{accountId}/invite-messages/{type:regex(^(message|request|requestResponse|response)$)}")]
    [ProducesResponseType<InviteMessage[]>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> ListInviteMessages([FromRoute] string accountId, [FromRoute] string type, CancellationToken cancellationToken) =>
        ReadLiveAsync(accountId, client => client.GetInviteMessagesAsync(accountId, type, cancellationToken));

    /// <summary>Change one invite message slot. VRChat answers with every slot of that type.</summary>
    /// <response code="200">The message slots after the change</response>
    /// <response code="409">No live session</response>
    [HttpPut("{accountId}/invite-messages/{type:regex(^(message|request|requestResponse|response)$)}/{slot:int}")]
    [ProducesResponseType<InviteMessage[]>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> UpdateInviteMessage([FromRoute] string accountId, [FromRoute] string type, [FromRoute] int slot, [FromBody] UpdateInviteMessageBody body, CancellationToken cancellationToken) =>
        ReadLiveAsync(accountId, client => client.UpdateInviteMessageAsync(accountId, type, slot, body.Message, cancellationToken));

    /// <summary>The reports this account has submitted, a page at a time.</summary>
    /// <response code="200">A page of reports</response>
    /// <response code="409">No live session</response>
    [HttpGet("{accountId}/moderation-reports")]
    [ProducesResponseType<ModerationReportPage>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> ListModerationReports([FromRoute] string accountId, [FromQuery] string? type, [FromQuery, Range(1, 100)] int n = 100, [FromQuery, Range(0, int.MaxValue)] int offset = 0, CancellationToken cancellationToken = default) =>
        ReadLiveAsync(accountId, client => client.GetModerationReportsAsync(accountId, type, n, offset, cancellationToken));

    /// <summary>Report a user, world, avatar or group to VRChat, the original's ModerationReportDialog.</summary>
    /// <response code="200">The report as VRChat stored it</response>
    /// <response code="409">No live session</response>
    /// <response code="502">VRChat refused</response>
    [HttpPost("{accountId}/moderation-reports")]
    [ProducesResponseType<JsonElement>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> SubmitModerationReport([FromRoute] string accountId, [FromBody] SubmitReportBody body, CancellationToken cancellationToken) =>
        ReadLiveAsync(accountId, client =>
        {
            var report = new Dictionary<string, object> { ["type"] = body.Type, ["category"] = body.Category, ["reason"] = body.Reason, ["contentId"] = body.ContentId };
            if (!string.IsNullOrWhiteSpace(body.Description)) report["description"] = body.Description.Trim();
            if (body.Details is { ValueKind: not JsonValueKind.Undefined and not JsonValueKind.Null } details) report["details"] = details;
            return client.SubmitModerationReportAsync(report, cancellationToken);
        });

    /// <summary>The report types and the labels of categories and reasons, from VRChat's config.</summary>
    /// <response code="200">The options</response>
    /// <response code="409">No live session</response>
    [HttpGet("{accountId}/moderation-reports/options")]
    [ProducesResponseType<ReportOptions>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> GetReportOptions([FromRoute] string accountId, CancellationToken cancellationToken) =>
        ReadLiveAsync(accountId, async client => ReportOptions.FromConfig(await client.GetConfigAsync(cancellationToken)));

    /// <summary>Everything the account has bought, newest first.</summary>
    /// <response code="200">The purchases</response>
    /// <response code="409">No live session</response>
    [HttpGet("{accountId}/purchases")]
    [ProducesResponseType<ProductPurchase[]>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> ListPurchases([FromRoute] string accountId, CancellationToken cancellationToken) =>
        ReadLiveAsync(accountId, client => client.GetAllProductPurchasesAsync(accountId, cancellationToken));

    /// <summary>The latest VRChat credit transactions (the original's fixed 100).</summary>
    /// <response code="200">The transactions</response>
    /// <response code="409">No live session</response>
    [HttpGet("{accountId}/economy-transactions")]
    [ProducesResponseType<EconomyTransactionPage>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> ListEconomyTransactions([FromRoute] string accountId, CancellationToken cancellationToken) =>
        ReadLiveAsync(accountId, client => client.GetEconomyTransactionsAsync(accountId, 100, cancellationToken));

    private async Task<IActionResult> ReadLiveAsync<T>(string accountId, Func<VrchatApiClient, Task<T>> read)
    {
        if (_sessions.Get(accountId) is not { } client) return Problem(AccountError.NoLiveSession);
        try
        {
            return Ok(await read(client));
        }
        catch (VrchatUnauthorizedException)
        {
            await _sessions.InvalidateAsync(accountId, HttpContext.RequestAborted);
            return Problem(VrchatError.SessionExpired);
        }
        catch (VrchatApiException ex)
        {
            return VrchatProblem(ex);
        }
    }
}

/// <summary>The account's membership status after joining: member, requested, or null when VRChat didn't say.</summary>
public sealed record GroupJoinResponse(string? MembershipStatus);
