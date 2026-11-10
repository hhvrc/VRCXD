using System.Text;
using Microsoft.AspNetCore.DataProtection;
using VRCX.Common.Services.Sessions;
using VRCX.Common.VrcxDb;

namespace VRCX.Common.Tests;

// The saved password encryption on its own (the original's services/security.js did this in the browser).
public class SavedCredentialSealTests
{
    private const string Password = "test-seal-pass-1";

    // Seal and Open never touch the database.
    private static SavedCredentialStore Store() => new(null!, new EphemeralDataProtectionProvider(), TimeProvider.System);

    private static SavedCredential Row(SealedPassword sealedPassword) => new()
    {
        AccountId = "usr_test",
        Username = "tester",
        Protection = sealedPassword.Protection,
        Secret = sealedPassword.Secret,
        Salt = sealedPassword.Salt,
        Nonce = sealedPassword.Nonce,
        Iterations = sealedPassword.Iterations,
    };

    [Test]
    public async Task Seal_and_open_give_the_password_back_in_both_schemes()
    {
        var store = Store();
        var plain = store.Seal(Password, null);
        var primary = store.Seal(Password, "test-primary-1");

        await Assert.That(plain.Protection).IsEqualTo(CredentialProtection.DataProtection);
        await Assert.That(primary.Protection).IsEqualTo(CredentialProtection.PrimaryPassword);
        await Assert.That(store.Open(Row(plain), null)).IsEqualTo(Password);
        await Assert.That(store.Open(Row(primary), "test-primary-1")).IsEqualTo(Password);
        await Assert.That(Encoding.UTF8.GetString(primary.Secret)).DoesNotContain(Password);
    }

    [Test]
    public async Task Different_primary_passwords_give_different_ciphertext_and_a_wrong_one_is_refused()
    {
        var store = Store();
        var a = store.Seal(Password, "test-primary-1");
        var b = store.Seal(Password, "test-primary-2");

        await Assert.That(a.Secret.SequenceEqual(b.Secret)).IsFalse();
        await Assert.That(() => store.Open(Row(a), "test-primary-2")).Throws<PrimaryPasswordException>();
        await Assert.That(() => store.Open(Row(a), null)).Throws<PrimaryPasswordException>();
    }

    [Test]
    public async Task The_same_password_seals_differently_each_time()
    {
        var store = Store();
        var a = store.Seal(Password, "test-primary-1");
        var b = store.Seal(Password, "test-primary-1");

        await Assert.That(a.Salt!.SequenceEqual(b.Salt!)).IsFalse();
        await Assert.That(a.Secret.SequenceEqual(b.Secret)).IsFalse();
    }
}
