using GaoApp.Infrastructure.Services.Invoices;
using Microsoft.AspNetCore.DataProtection;

namespace GaoApp.Tests.Security;

public class InvoiceProviderCredentialProtectorTests
{
    [Fact]
    public void Protect_ThenUnprotect_RestoresOriginalSecret()
    {
        var sut = CreateSut();
        const string plaintext = "provider-secret-for-test";

        var protectedValue = sut.Protect(plaintext);

        Assert.NotEqual(plaintext, protectedValue);
        Assert.True(sut.IsProtected(protectedValue));
        Assert.Equal(plaintext, sut.Unprotect(protectedValue));
    }

    [Fact]
    public void Protect_WhenAlreadyProtected_IsIdempotent()
    {
        var sut = CreateSut();
        var protectedValue = sut.Protect("provider-secret-for-test");

        Assert.Equal(protectedValue, sut.Protect(protectedValue));
    }

    [Fact]
    public void Unprotect_LegacyPlaintext_RemainsBackwardCompatible()
    {
        var sut = CreateSut();

        Assert.Equal("legacy-secret", sut.Unprotect("legacy-secret"));
    }

    [Fact]
    public void Protect_MaximumAllowedUnicodeSecret_FitsDatabaseColumn()
    {
        var sut = CreateSut();
        var protectedValue = sut.Protect(new string('ấ', 64));

        Assert.True(protectedValue.Length <= 500);
    }

    private static InvoiceProviderCredentialProtector CreateSut()
        => new(new EphemeralDataProtectionProvider());
}
