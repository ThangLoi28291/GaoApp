using System.Security.Cryptography;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Services.Banking;
using GaoApp.Infrastructure.Services.Storefront;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;

namespace GaoApp.Tests.Storefront;

[Trait("Category", "StorefrontWeb05")]
public sealed class Web05BankContractTests
{
    [Theory]
    [InlineData("2026-10-31", "2026-10-30T17:00:00Z", "2026-10-31")]
    [InlineData("20261031", "2026-10-30T17:00:00Z", "2026-10-31")]
    [InlineData("2024-02-29", "2024-02-28T17:00:00Z", "2024-02-29")]
    [InlineData("2026-01-01", "2025-12-31T17:00:00Z", "2026-01-01")]
    public void Bank_day_has_explicit_precision_and_only_a_Vietnam_day_anchor(string raw, string anchor, string day)
    {
        var result = BankEvidenceTime.Parse(raw);
        Assert.Equal(BankTimePrecision.BankDay, result.Precision);
        Assert.Equal(DateTime.Parse(day, System.Globalization.CultureInfo.InvariantCulture), result.BusinessDate);
        Assert.Equal(Utc(anchor), result.OccurredAtUtc);
        Assert.Equal(DateTimeKind.Utc, result.OccurredAtUtc!.Value.Kind);
        Assert.Equal(DateTimeKind.Unspecified, result.BusinessDate!.Value.Kind);
    }

    [Theory]
    [InlineData("2026-10-31T00:00:00+07:00", "2026-10-30T17:00:00Z", "2026-10-31")]
    [InlineData("2026-10-30T23:59:59+0700", "2026-10-30T16:59:59Z", "2026-10-30")]
    [InlineData("2026-10-30T17:00:00Z", "2026-10-30T17:00:00Z", "2026-10-31")]
    [InlineData("2026-10-31T00:01:02.123+08:00", "2026-10-30T16:01:02.123Z", "2026-10-30")]
    [InlineData("2026-12-31T23:30:00-05:00", "2027-01-01T04:30:00Z", "2027-01-01")]
    public void Timestamp_uses_supplied_offset_and_Vietnam_business_day(string raw, string utc, string day)
    {
        var result = BankEvidenceTime.Parse(raw);
        Assert.Equal(BankTimePrecision.Timestamp, result.Precision);
        Assert.Equal(Utc(utc), result.OccurredAtUtc);
        Assert.Equal(DateTimeKind.Utc, result.OccurredAtUtc!.Value.Kind);
        Assert.Equal(DateTime.Parse(day, System.Globalization.CultureInfo.InvariantCulture), result.BusinessDate);
    }

    [Theory]
    [InlineData(null)][InlineData("")][InlineData("not-a-date")][InlineData("2026-02-29")]
    [InlineData("2026-13-01")][InlineData("31/10/2026")][InlineData("2026-10-31T09:10:11")]
    [InlineData("2026-10-31T09:10:11+99:00")][InlineData("0001-01-01")][InlineData("9999-12-31T23:59:59Z")]
    public void Unknown_bank_time_does_not_invent_today_or_machine_local_time(string? raw)
    {
        var result = BankEvidenceTime.Parse(raw);
        Assert.Equal(BankTimePrecision.Unknown, result.Precision);
        Assert.Null(result.BusinessDate); Assert.Null(result.OccurredAtUtc);
    }

    [Theory]
    [InlineData("ACB", "00012345", " acb ", "000 12345")]
    [InlineData("ACB", "000AB12", "acb", "000ab12")]
    [InlineData(" ACB ", " 00012345 ", "ACB", "00012345")]
    public void Same_physical_account_ASCII_case_and_spacing_aliases_share_global_identity(string bank, string account, string aliasBank, string aliasAccount)
        => Assert.Equal(GlobalBankReceiptLedger.AccountIdentity(bank, account), GlobalBankReceiptLedger.AccountIdentity(aliasBank, aliasAccount));

    [Theory]
    [InlineData("ACB", "00012345", "ACB", "12345")]
    [InlineData("ACB", "00012345", "VCB", "00012345")]
    [InlineData("ACB", "000-12345", "ACB", "00012345")]
    [InlineData("ACB", "000.12345", "ACB", "00012345")]
    [InlineData("ACB", "00012345", "970416", "00012345")]
    public void Leading_zero_punctuation_and_distinct_bank_identity_are_not_guessed_away(string bank, string account, string otherBank, string otherAccount)
        => Assert.NotEqual(GlobalBankReceiptLedger.AccountIdentity(bank, account), GlobalBankReceiptLedger.AccountIdentity(otherBank, otherAccount));

    [Fact]
    public void Registration_is_absent_by_default_and_is_scoped_to_the_explicit_registered_store()
    {
        Assert.Null(new StorefrontBankRegistration(new ConfigurationBuilder().Build()).Get(7));
        var config = Registration();
        var registration = new StorefrontBankRegistration(new ConfigurationBuilder().AddInMemoryCollection(config).Build());
        var value = registration.Get(7); Assert.NotNull(value); Assert.True(value.Verified);
        Assert.Equal("SYNTHETIC-OPERATOR-ATTESTATION-NOT-BANK-ACCEPTANCE", value.EvidenceReference);
        Assert.Equal(41, value.BankAccountId); Assert.Equal(12, value.ReceivingLegalEntityId);
        Assert.Null(registration.Get(8));
    }

    [Theory]
    [InlineData("NamespaceVerified", "false")][InlineData("NamespaceVerified", null)]
    [InlineData("Namespace", "GA")][InlineData("Provider", "OTHER")]
    [InlineData("EvidenceReference", "")][InlineData("MerchantId", "")]
    [InlineData("TerminalCode", "")][InlineData("UserCode", "")]
    [InlineData("BankAccountId", "0")][InlineData("ReceivingLegalEntityId", "0")]
    [InlineData("AccountIdentity", "not-a-digest")]
    public void Incomplete_or_unattested_registration_cannot_enable_QR(string field, string? value)
    {
        var config = Registration(); config["StorefrontBanking:Registrations:7:" + field] = value;
        Assert.Null(new StorefrontBankRegistration(new ConfigurationBuilder().AddInMemoryCollection(config).Build()).Get(7));
    }

    [Fact]
    public void Admin_ACB_protector_survives_provider_restart_and_keeps_store_and_guest_trust_separate()
    {
        var root = Web05Support.RuntimePath("crypto"); var admin = Path.Combine(root, "admin-keys"); var guest = Path.Combine(root, "guest-keys");
        Directory.CreateDirectory(admin); Directory.CreateDirectory(guest);
        static IDataProtectionProvider Provider(string path, string app) => DataProtectionProvider.Create(new DirectoryInfo(path), options => options.SetApplicationName(app));
        static AcbProtocolCore Protocol(IDataProtectionProvider provider) => new(new HttpClient(new Web05RejectAllTransport()), provider);
        var original = Protocol(Provider(admin, "GaoApp"));
        var protectedValue = original.Protect(7, "synthetic-private-secret-WEB05");
        Assert.DoesNotContain("synthetic-private-secret-WEB05", protectedValue);
        Assert.Equal("synthetic-private-secret-WEB05", Protocol(Provider(admin, "GaoApp")).Unprotect(7, protectedValue));
        Assert.Throws<CryptographicException>(() => Protocol(Provider(admin, "GaoApp.Storefront")).Unprotect(7, protectedValue));
        Assert.Throws<CryptographicException>(() => Protocol(Provider(guest, "GaoApp.Storefront")).Unprotect(7, protectedValue));
        Assert.Throws<CryptographicException>(() => Protocol(Provider(guest, "GaoApp")).Unprotect(7, protectedValue));
        Assert.Throws<CryptographicException>(() => original.Unprotect(8, protectedValue));
    }

    private static DateTime Utc(string value) => DateTimeOffset.Parse(value, System.Globalization.CultureInfo.InvariantCulture).UtcDateTime;
    private static Dictionary<string, string?> Registration() => new()
    {
        ["StorefrontBanking:Registrations:7:NamespaceVerified"] = "true", ["StorefrontBanking:Registrations:7:Namespace"] = "W",
        ["StorefrontBanking:Registrations:7:Provider"] = "ACB", ["StorefrontBanking:Registrations:7:EvidenceReference"] = "SYNTHETIC-OPERATOR-ATTESTATION-NOT-BANK-ACCEPTANCE",
        ["StorefrontBanking:Registrations:7:MerchantId"] = "SYNTHETIC", ["StorefrontBanking:Registrations:7:TerminalCode"] = "WEB05-REGISTERED-TERMINAL",
        ["StorefrontBanking:Registrations:7:UserCode"] = "WEB05-REGISTERED-USER", ["StorefrontBanking:Registrations:7:BankAccountId"] = "41",
        ["StorefrontBanking:Registrations:7:ReceivingLegalEntityId"] = "12", ["StorefrontBanking:Registrations:7:AccountIdentity"] = new string('a', 64)
    };
}
