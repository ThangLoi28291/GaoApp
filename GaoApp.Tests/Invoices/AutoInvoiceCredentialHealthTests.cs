using System.Security.Cryptography;
using GaoApp.Application.Common;
using GaoApp.Application.Interfaces.Repositories.Invoices;
using GaoApp.Application.Services.Invoices;
using GaoApp.Domain.Entities;

namespace GaoApp.Tests.Invoices;

public sealed partial class AutoInvoiceServiceTests
{
    [Fact]
    public async Task Worker_keyset_failure_blocks_issuance_and_reports_worker_key_access_instead_of_wrong_password()
    {
        var repo = CreateRepository(true);
        repo.Candidates.Add(Invoice(991, DateTime.UtcNow.AddHours(-1)));
        var issue = new FakeIssueService();
        var providers = new CredentialHealthRepository
        {
            Failure = new CryptographicException("Keyset does not exist")
        };
        var service = CredentialHealthService(repo, issue, providers);

        await service.RunOnceAsync(force: true);

        Assert.Equal(0, issue.Calls);
        Assert.Empty(repo.Operations);
        var state = Assert.Single(repo.WorkerStates);
        Assert.Equal(InvoiceCredentialErrors.KeyUnavailableCode, state.LastErrorCode);
        Assert.Contains("không cần đổi mật khẩu Viettel", state.LastErrorMessage);
        Assert.Contains(Environment.MachineName, state.LastErrorMessage);
        Assert.DoesNotContain("nhập lại mật khẩu Viettel và bấm Lưu", state.LastErrorMessage);

        // The next cycle must check the credential again, rather than leaving
        // the queue permanently blocked by its previous host error.
        providers.Failure = null;
        repo.Candidates.Clear();
        await service.RunOnceAsync(force: true);
        Assert.Null(state.LastErrorCode);
        Assert.Null(state.LastErrorMessage);
        Assert.Equal(2, providers.ActiveReads);
    }

    private static AutoInvoiceService CredentialHealthService(
        FakeAutoInvoiceRepository repo, FakeIssueService issue, IInvoiceProviderSettingRepository providers)
        => new(repo, new SufficientStockRepository(), issue, new FoundLookupService(),
            new NoopUnitOfWork(), new StoreTenant(1), new TestCurrentUser(), TimeProvider.System, providers);

    private sealed class CredentialHealthRepository : IInvoiceProviderSettingRepository
    {
        public Exception? Failure { get; set; }
        public int ActiveReads { get; private set; }
        public Task<InvoiceProviderSetting?> GetActiveViettelAsync(int storeId, CancellationToken ct = default)
        {
            ActiveReads++;
            return Failure == null
                ? Task.FromResult<InvoiceProviderSetting?>(new() { Id = 2, StoreId = storeId, Password = "test-only" })
                : Task.FromException<InvoiceProviderSetting?>(Failure);
        }
        public Task<List<InvoiceProviderSetting>> GetAllAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<InvoiceProviderSetting?> GetByIdAsync(int id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<InvoiceProviderSetting?> GetByIdWithCredentialAsync(int id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<InvoiceProviderSetting?> GetActiveViettelAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<InvoiceProviderSetting?> GetForInvoiceAsync(int storeId, int? id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> ExistsDuplicateAsync(int id, string providerCode, string taxCode, string template, string series, CancellationToken ct = default) => throw new NotSupportedException();
        public Task AddAsync(InvoiceProviderSetting entity, CancellationToken ct = default) => throw new NotSupportedException();
        public void Update(InvoiceProviderSetting entity) => throw new NotSupportedException();
        public Task SaveChangesAsync(CancellationToken ct = default) => throw new NotSupportedException();
    }
}
