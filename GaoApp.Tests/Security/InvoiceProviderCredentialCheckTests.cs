using System.Security.Cryptography;
using GaoApp.Application.Interfaces.Repositories.Invoices;
using GaoApp.Application.Interfaces.Services.Invoices;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Services.Invoices;
using Microsoft.AspNetCore.DataProtection;

namespace GaoApp.Tests.Security;

public class InvoiceProviderCredentialCheckTests
{
    [Fact]
    public async Task Checks_only_active_Viettel_records_without_writing_or_exposing_passwords()
    {
        var web = new InvoiceProviderCredentialProtector(new EphemeralDataProtectionProvider());
        var secret = "synthetic-password-that-must-not-be-reported";
        var ciphertext = web.Protect(secret);
        var repository = new ReadOnlyRepository(new()
        {
            new() { Id = 1, StoreId = 1, ProviderCode = "VIETTEL", IsActive = true, Password = ciphertext },
            new() { Id = 2, StoreId = 2, ProviderCode = "VIETTEL", IsActive = false, Password = "inactive" },
            new() { Id = 3, StoreId = 1, ProviderCode = "OTHER", IsActive = true, Password = "other" }
        });

        var good = Assert.Single(await InvoiceProviderCredentialCheck.RunAsync(repository, web));
        Assert.True(good.CanDecrypt);
        Assert.Null(good.Error);
        Assert.Equal(ciphertext, repository.Settings[0].Password);

        // A host with a different key ring cannot use the Web ciphertext.
        var worker = new InvoiceProviderCredentialProtector(new EphemeralDataProtectionProvider());
        var bad = Assert.Single(await InvoiceProviderCredentialCheck.RunAsync(repository, worker));
        Assert.False(bad.CanDecrypt);
        Assert.DoesNotContain(secret, bad.Error);
        Assert.DoesNotContain(ciphertext, bad.Error);
        Assert.Equal(ciphertext, repository.Settings[0].Password);
    }

    [Fact]
    public async Task One_broken_credential_does_not_skip_other_stores_and_legacy_password_is_not_migrated()
    {
        var repository = new ReadOnlyRepository(new()
        {
            new() { Id = 1, StoreId = 1, ProviderCode = "VIETTEL", IsActive = true, Password = "broken" },
            new() { Id = 2, StoreId = 2, ProviderCode = "VIETTEL", IsActive = true, Password = "legacy" }
        });
        var results = await InvoiceProviderCredentialCheck.RunAsync(repository, new SelectiveProtector());
        Assert.Equal(2, results.Count);
        Assert.False(results[0].CanDecrypt);
        Assert.True(results[1].CanDecrypt);
        Assert.Equal("legacy", repository.Settings[1].Password);
    }

    private sealed class SelectiveProtector : IInvoiceProviderCredentialProtector
    {
        public bool IsProtected(string value) => throw new NotSupportedException();
        public string Protect(string value) => throw new NotSupportedException();
        public string Unprotect(string value) => value == "broken"
            ? throw new CryptographicException("Keyset does not exist") : value;
    }

    private sealed class ReadOnlyRepository(List<InvoiceProviderSetting> settings) : IInvoiceProviderSettingRepository
    {
        public List<InvoiceProviderSetting> Settings { get; } = settings;
        public Task<List<InvoiceProviderSetting>> GetAllAsync(CancellationToken ct = default) => Task.FromResult(Settings);
        public Task<InvoiceProviderSetting?> GetByIdAsync(int id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<InvoiceProviderSetting?> GetByIdWithCredentialAsync(int id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<InvoiceProviderSetting?> GetActiveViettelAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<InvoiceProviderSetting?> GetActiveViettelAsync(int storeId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<InvoiceProviderSetting?> GetForInvoiceAsync(int storeId, int? id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> ExistsDuplicateAsync(int id, string providerCode, string taxCode, string template, string series, CancellationToken ct = default) => throw new NotSupportedException();
        public Task AddAsync(InvoiceProviderSetting entity, CancellationToken ct = default) => throw new NotSupportedException();
        public void Update(InvoiceProviderSetting entity) => throw new NotSupportedException();
        public Task SaveChangesAsync(CancellationToken ct = default) => throw new NotSupportedException();
    }
}
