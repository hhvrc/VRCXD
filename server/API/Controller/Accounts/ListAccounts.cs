using System.Net.Mime;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenShock.Internal.Common.Problems;
using VRCX.API.Models.Response;
using VRCX.Common.Errors;
using VRCX.Common.VrcxDb;
using VRCX.Vrchat;

namespace VRCX.API.Controller.Accounts;

public sealed partial class AccountsController
{
    /// <summary>
    /// Every VRChat account that has logged into this server, with its session and sync state.
    /// </summary>
    /// <response code="200">The accounts</response>
    [HttpGet]
    [ProducesResponseType<AccountResponse[]>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [MapToApiVersion("1")]
    public async Task<AccountResponse[]> ListAccounts(CancellationToken cancellationToken)
    {
        var live = _sessions.ActiveAccountIds.ToHashSet();
        var accounts = await _db.Accounts.AsNoTracking()
            .Select(a => new
            {
                a.UserId, a.User.DisplayName, a.Username, a.SessionValid, a.ApiEndpoint, a.PipelineEndpoint, a.LoggedInAt, a.LastFriendSyncAt, a.LastFullSyncAt, a.HomeLocation,
                a.User.IconUrl, a.User.CurrentAvatarThumbnailImageUrl, a.User.CurrentAvatarImageUrl, a.User.IconFrame,
            })
            .ToListAsync(cancellationToken);
        var saved = await _db.SavedCredentials.AsNoTracking().Select(c => new { c.AccountId, c.Protection }).ToDictionaryAsync(c => c.AccountId, c => c.Protection, cancellationToken);
        return accounts.Select(a => new AccountResponse
        {
            UserId = a.UserId,
            DisplayName = a.DisplayName,
            Username = a.Username,
            SessionValid = a.SessionValid,
            ApiEndpoint = a.ApiEndpoint,
            PipelineEndpoint = a.PipelineEndpoint,
            Connected = live.Contains(a.UserId),
            LoggedInAt = a.LoggedInAt,
            LastFriendSyncAt = a.LastFriendSyncAt,
            LastFullSyncAt = a.LastFullSyncAt,
            HomeLocation = a.HomeLocation,
            ThumbnailImageUrl = UserImage.Pick(a.IconUrl, a.CurrentAvatarThumbnailImageUrl, a.CurrentAvatarImageUrl),
            IconFrame = a.IconFrame,
            HasSavedCredentials = saved.ContainsKey(a.UserId),
            SavedCredentialsNeedPrimaryPassword = saved.GetValueOrDefault(a.UserId) == CredentialProtection.PrimaryPassword,
        }).ToArray();
    }

    /// <summary>
    /// Pull everything VRChat knows about the account right now (friends, groups, favorites, moderations, notes, notifications).
    /// </summary>
    /// <response code="204">Synced</response>
    /// <response code="404">No such account</response>
    /// <response code="409">The account has no live VRChat session</response>
    [HttpPost("{accountId}/sync")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> Sync([FromRoute] string accountId, CancellationToken cancellationToken)
    {
        if (!await _db.Accounts.AnyAsync(a => a.UserId == accountId, cancellationToken)) return Problem(AccountError.AccountNotFound);
        if (_sessions.Get(accountId) is null) return Problem(AccountError.NoLiveSession);
        try
        {
            await _sync.SyncEverythingAsync(accountId, cancellationToken);
        }
        catch (VrchatUnauthorizedException)
        {
            await _sessions.InvalidateAsync(accountId, cancellationToken);
            return Problem(VrchatError.SessionExpired);
        }
        catch (VrchatApiException ex)
        {
            return VrchatProblem(ex);
        }
        return NoContent();
    }
}
