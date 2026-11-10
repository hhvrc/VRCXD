using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using VRCX.Common.VrcxDb;

namespace VRCX.Common.Services.Sessions;

/// <summary>The primary password is needed and was not given, or is wrong.</summary>
public sealed class PrimaryPasswordException(string message) : Exception(message);

/// <summary>An encrypted password, ready to store.</summary>
public sealed record SealedPassword(string Protection, byte[] Secret, byte[]? Salt, byte[]? Nonce, int? Iterations);

/// <summary>
/// The original's savedCredentials and primary password. Without a primary password a saved password
/// is encrypted with the server's Data Protection keys, so the server can log in again by itself when
/// a session expires. With one, it is AES-GCM encrypted with a key derived from the primary password,
/// which the server never stores: such a password is only usable while the user types it.
/// </summary>
public sealed class SavedCredentialStore(IDbContextFactory<VrcxContext> dbFactory, IDataProtectionProvider protection, TimeProvider time)
{
    /// <summary>The server setting that says new saved passwords use the primary password (the original's config key).</summary>
    public const string PrimaryPasswordSetting = "enablePrimaryPassword";
    /// <summary>The original's prompt pattern allows 1 to 32 characters.</summary>
    public const int PrimaryPasswordMaxLength = 32;
    private const int Pbkdf2Iterations = 600_000;
    private const int TagSize = 16;

    private readonly IDataProtector _protector = protection.CreateProtector("VRCX.SavedCredentials");
    // Logins waiting on a 2FA code: sealed already, saved once the code verifies.
    private readonly ConcurrentDictionary<string, (string Username, SealedPassword Password, DateTimeOffset At)> _held = new();

    public SealedPassword Seal(string password, string? primaryPassword)
    {
        var plain = Encoding.UTF8.GetBytes(password);
        if (primaryPassword is null)
            return new SealedPassword(CredentialProtection.DataProtection, _protector.Protect(plain), null, null, null);

        var salt = RandomNumberGenerator.GetBytes(16);
        var nonce = RandomNumberGenerator.GetBytes(AesGcm.NonceByteSizes.MaxSize);
        var key = DeriveKey(primaryPassword, salt, Pbkdf2Iterations);
        var secret = new byte[plain.Length + TagSize];
        using (var aes = new AesGcm(key, TagSize))
            aes.Encrypt(nonce, plain, secret.AsSpan(0, plain.Length), secret.AsSpan(plain.Length));
        CryptographicOperations.ZeroMemory(key);
        return new SealedPassword(CredentialProtection.PrimaryPassword, secret, salt, nonce, Pbkdf2Iterations);
    }

    /// <summary>The password in clear. Throws <see cref="PrimaryPasswordException"/> when the primary password is missing or wrong.</summary>
    public string Open(SavedCredential saved, string? primaryPassword)
    {
        if (saved.Protection == CredentialProtection.DataProtection)
            return Encoding.UTF8.GetString(_protector.Unprotect(saved.Secret));

        if (string.IsNullOrEmpty(primaryPassword)) throw new PrimaryPasswordException("The primary password is required");
        if (saved.Salt is null || saved.Nonce is null || saved.Iterations is null || saved.Secret.Length < TagSize)
            throw new CryptographicException("The saved password is incomplete");
        var key = DeriveKey(primaryPassword, saved.Salt, saved.Iterations.Value);
        var plain = new byte[saved.Secret.Length - TagSize];
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(saved.Nonce, saved.Secret.AsSpan(0, plain.Length), saved.Secret.AsSpan(plain.Length), plain);
        }
        catch (AuthenticationTagMismatchException)
        {
            throw new PrimaryPasswordException("Incorrect primary password");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
        return Encoding.UTF8.GetString(plain);
    }

    private static byte[] DeriveKey(string primaryPassword, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(primaryPassword), salt, iterations, HashAlgorithmName.SHA256, 32);

    public async Task<bool> PrimaryPasswordEnabledAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await PrimaryPasswordEnabledAsync(db, ct);
    }

    private static async Task<bool> PrimaryPasswordEnabledAsync(VrcxContext db, CancellationToken ct) =>
        (await db.ServerSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == PrimaryPasswordSetting, ct))?.Value == "true";

    private static async Task SetPrimaryPasswordFlagAsync(VrcxContext db, bool enabled, CancellationToken ct)
    {
        var row = await db.ServerSettings.FirstOrDefaultAsync(s => s.Key == PrimaryPasswordSetting, ct);
        if (row is null) db.ServerSettings.Add(new ServerSetting { Key = PrimaryPasswordSetting, Value = enabled ? "true" : "false" });
        else row.Value = enabled ? "true" : "false";
    }

    /// <summary>
    /// As the original's login: the primary password is right when it opens a saved password
    /// encrypted with it; with none saved yet any is accepted.
    /// </summary>
    public async Task<bool> CheckPrimaryPasswordAsync(string primaryPassword, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var any = await db.SavedCredentials.AsNoTracking().FirstOrDefaultAsync(c => c.Protection == CredentialProtection.PrimaryPassword, ct);
        if (any is null) return true;
        try
        {
            Open(any, primaryPassword);
            return true;
        }
        catch (PrimaryPasswordException)
        {
            return false;
        }
    }

    public async Task<SavedCredential?> FindAsync(string accountId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.SavedCredentials.AsNoTracking().FirstOrDefaultAsync(c => c.AccountId == accountId, ct);
    }

    public async Task SaveAsync(string accountId, string username, SealedPassword password, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db.SavedCredentials.FirstOrDefaultAsync(c => c.AccountId == accountId, ct);
        if (row is null)
        {
            row = new SavedCredential { AccountId = accountId, Username = username, Protection = password.Protection, Secret = password.Secret };
            db.SavedCredentials.Add(row);
        }
        row.Username = username;
        row.Protection = password.Protection;
        row.Secret = password.Secret;
        row.Salt = password.Salt;
        row.Nonce = password.Nonce;
        row.Iterations = password.Iterations;
        row.SavedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Keeps a login's sealed password until its 2FA code verifies.</summary>
    public void Hold(string pendingId, string username, SealedPassword password)
    {
        var cutoff = time.GetUtcNow().AddHours(-1);
        foreach (var (id, held) in _held)
            if (held.At < cutoff) _held.TryRemove(id, out _);
        _held[pendingId] = (username, password, time.GetUtcNow());
    }

    public async Task CommitHeldAsync(string pendingId, string accountId, CancellationToken ct)
    {
        if (_held.TryRemove(pendingId, out var held)) await SaveAsync(accountId, held.Username, held.Password, ct);
    }

    public void Release(string pendingId) => _held.TryRemove(pendingId, out _);

    /// <summary>The original's deleteSavedLogin: with no saved account left, the primary password turns off.</summary>
    public async Task<bool> DeleteAsync(string accountId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var removed = await db.SavedCredentials.Where(c => c.AccountId == accountId).ExecuteDeleteAsync(ct) > 0;
        if (!await db.SavedCredentials.AnyAsync(ct) && await PrimaryPasswordEnabledAsync(db, ct))
        {
            await SetPrimaryPasswordFlagAsync(db, false, ct);
            await db.SaveChangesAsync(ct);
        }
        return removed;
    }

    /// <summary>
    /// The original's setPrimaryPassword and enablePrimaryPasswordChange: turning it on encrypts every
    /// saved password with it; turning it off needs it, and changes nothing when it does not open them all.
    /// </summary>
    public async Task SetPrimaryPasswordAsync(bool enabled, string primaryPassword, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = await db.SavedCredentials.ToListAsync(ct);
        foreach (var row in rows)
        {
            var target = enabled ? CredentialProtection.PrimaryPassword : CredentialProtection.DataProtection;
            if (row.Protection == target) continue;
            var sealedPassword = Seal(Open(row, primaryPassword), enabled ? primaryPassword : null);
            row.Protection = sealedPassword.Protection;
            row.Secret = sealedPassword.Secret;
            row.Salt = sealedPassword.Salt;
            row.Nonce = sealedPassword.Nonce;
            row.Iterations = sealedPassword.Iterations;
        }
        // A wrong primary password made Open throw above, before anything was saved.
        await SetPrimaryPasswordFlagAsync(db, enabled, ct);
        await db.SaveChangesAsync(ct);
    }
}
