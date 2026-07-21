using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Interfaces.Services.Invoices;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Repositories.Invoices;
using GaoApp.Infrastructure.Tenant;
using GaoApp.Tests.Data;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;

namespace GaoApp.Tests.Invoices;

public class InvoiceProviderSettingCredentialRecoveryTests
{
    [Fact]
    public async Task GetByIdAsync_ForAdminEdit_ShouldNotDecryptCredential()
    {
        await using var context = CreateContext();

        var setting = CreateSetting();
        context.InvoiceProviderSettings.Add(setting);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var protector = new ThrowingCredentialProtector();
        var repository = new InvoiceProviderSettingRepository(
            context,
            protector);

        var result = await repository.GetByIdAsync(setting.Id);

        Assert.NotNull(result);
        Assert.Equal("gdp:v1:missing-key", result.Password);
        Assert.Equal(0, protector.UnprotectCallCount);
    }

    [Fact]
    public async Task GetByIdWithCredentialAsync_ForRuntimeUse_ShouldDecryptCredential()
    {
        await using var context = CreateContext();

        var setting = CreateSetting();
        context.InvoiceProviderSettings.Add(setting);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var protector = new ThrowingCredentialProtector();
        var repository = new InvoiceProviderSettingRepository(
            context,
            protector);

        await Assert.ThrowsAsync<CryptographicException>(
            () => repository.GetByIdWithCredentialAsync(setting.Id));

        Assert.Equal(1, protector.UnprotectCallCount);
    }

    private static InMemoryAppDbContext CreateContext()
    {
        var tenant = new TenantContext();
        tenant.SetStore(1, "minhhung");

        var options =
            new DbContextOptionsBuilder<InMemoryAppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

        var context = new InMemoryAppDbContext(
            options,
            tenant,
            new TestCurrentUser());

        context.VerifyRowVersionConfiguration();

        return context;
    }

    private static InvoiceProviderSetting CreateSetting()
    {
        return new InvoiceProviderSetting
        {
            StoreId = 1,
            ProviderCode = "VIETTEL",
            BaseUrl = "https://example.test",
            Username = "demo-user",
            Password = "gdp:v1:missing-key",
            SupplierTaxCode = "0100000001",
            InvoiceType = "1",
            TemplateCode = "1/001",
            InvoiceSeries = "C26TAA",
            CurrencyCode = "VND",
            PaymentMethodName = "TM",
            IsActive = true
        };
    }

    private sealed class ThrowingCredentialProtector
        : IInvoiceProviderCredentialProtector
    {
        public int UnprotectCallCount { get; private set; }

        public bool IsProtected(string value)
        {
            return value.StartsWith(
                "gdp:v1:",
                StringComparison.Ordinal);
        }

        public string Protect(string plaintext)
        {
            return plaintext;
        }

        public string Unprotect(string protectedOrLegacyPlaintext)
        {
            UnprotectCallCount++;

            throw new CryptographicException(
                "Data Protection key is unavailable.");
        }
    }

    private sealed class TestCurrentUser : ICurrentUser
    {
        public int? UserId => 1;
        public string? UserName => "credential-recovery-test";
        public int? TerminalId => null;
        public string? TerminalCode => null;
        public bool IsAuthenticated => true;
    }
}