using GaoApp.Application.Common;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Invoices;
using GaoApp.Application.Interfaces.Common;
using GaoApp.Application.Interfaces.Repositories.Invoices;
using GaoApp.Application.Interfaces.Services.Invoices;
using GaoApp.Application.Services.Invoices;
using GaoApp.Domain.Constants;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Tests.Invoices;

public sealed class AutoInvoiceServiceTests
{
    [Fact]
    public async Task Paused_admin_keeps_draft_and_does_not_call_provider()
    {
        var repo = CreateRepository(enabled: false);
        var issue = new FakeIssueService();
        var invoice = Invoice(1, completedAtUtc: DateTime.UtcNow.AddHours(-1));
        repo.Candidates.Add(invoice);
        var service = CreateService(repo, issue);

        var result = await service.RunOnceAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(0, issue.Calls);
        Assert.Equal(InvoiceProviderStatus.LocalDraft, invoice.ProviderStatus);
    }

    [Fact]
    public async Task Missing_unit_is_blocked_without_sending()
    {
        var repo = CreateRepository(enabled: true);
        var issue = new FakeIssueService();
        var invoice = Invoice(2, completedAtUtc: DateTime.UtcNow.AddHours(-1));
        invoice.Details.First().UnitName = null;
        repo.Candidates.Add(invoice);
        var service = CreateService(repo, issue);

        await service.RunOnceAsync();

        Assert.Equal(0, issue.Calls);
        Assert.Equal("Invoice.UnitMissing", invoice.LastErrorCode);
        Assert.Contains(repo.Operations, x => x.Status == AutoInvoiceOperationStatus.Blocked);
    }

    [Fact]
    public async Task Insufficient_input_invoice_stock_blocks_group_with_shortage_details()
    {
        var repo = CreateRepository(enabled: true);
        var issue = new FakeIssueService();
        var first = Invoice(20, completedAtUtc: DateTime.UtcNow.AddHours(-2));
        var second = Invoice(21, completedAtUtc: DateTime.UtcNow.AddHours(-1));
        first.GrandTotal = 60_000m;
        second.GrandTotal = 60_000m;
        repo.Candidates.AddRange([first, second]);
        var service = CreateService(repo, issue, new InsufficientStockRepository());

        await service.RunOnceAsync();

        Assert.Equal(0, issue.Calls);
        var blocked = Assert.Single(repo.Operations.Where(x => x.Status == AutoInvoiceOperationStatus.Blocked));
        Assert.Equal("Invoice.InputInvoiceStockInsufficient", blocked.ErrorCode);
        Assert.Contains("cần", blocked.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("thiếu", blocked.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Manual_issue_and_worker_share_active_source_claim()
    {
        var repo = CreateRepository(enabled: true);
        var issue = new FakeIssueService();
        var invoice = Invoice(3, completedAtUtc: DateTime.UtcNow.AddHours(-1));
        repo.Candidates.Add(invoice);
        repo.Operations.Add(new AutoInvoiceOperation
        {
            Id = 77,
            StoreId = 1,
            Status = AutoInvoiceOperationStatus.Processing,
            InvoiceHeadId = invoice.Id,
            Sources =
            [new AutoInvoiceOperationSource
            {
                StoreId = 1,
                InvoiceHeadId = invoice.Id,
                IsActive = true,
                Status = AutoInvoiceSourceStatus.Claimed
            }]
        });
        var service = CreateService(repo, issue);

        var result = await service.IssueManualAsync(invoice.Id);

        Assert.False(result.IsSuccess);
        Assert.Equal("Conflict", result.Error!.Code);
        Assert.Equal(0, issue.Calls);
    }

    [Fact]
    public async Task Worker_resumes_durable_claim_left_by_previous_worker()
    {
        var repo = CreateRepository(enabled: true);
        var issue = new FakeIssueService();
        var invoice = Invoice(6, completedAtUtc: DateTime.UtcNow.AddHours(-1));
        repo.Candidates.Add(invoice);
        repo.Operations.Add(new AutoInvoiceOperation
        {
            Id = 78,
            StoreId = 1,
            Status = AutoInvoiceOperationStatus.Processing,
            InvoiceHeadId = invoice.Id,
            Sources =
            [new AutoInvoiceOperationSource
            {
                StoreId = 1,
                InvoiceHeadId = invoice.Id,
                IsActive = true,
                Status = AutoInvoiceSourceStatus.Claimed
            }]
        });

        var service = CreateService(repo, issue);

        await service.RunOnceAsync();

        Assert.Equal(1, issue.Calls);
        Assert.Equal(AutoInvoiceOperationStatus.Succeeded, repo.Operations.Single().Status);
        Assert.False(repo.Operations.Single().Sources.Single().IsActive);
    }

    [Fact]
    public async Task Unknown_result_is_resolved_by_uuid_lookup_before_reissue()
    {
        var repo = CreateRepository(enabled: true);
        var issue = new FakeIssueService
        {
            Result = Result<ViettelInvoiceIssueResultDto>.Failure(
                Error.Validation("TIMEOUT", "Timeout khi gọi Viettel."))
        };
        var invoice = Invoice(4, completedAtUtc: DateTime.UtcNow.AddHours(-1));
        repo.Candidates.Add(invoice);
        var service = CreateService(repo, issue);

        await service.RunOnceAsync();
        Assert.Contains(repo.Operations, x => x.Status == AutoInvoiceOperationStatus.Unknown);

        var lookup = await service.SyncUnknownAsync(invoice.Id);

        Assert.True(lookup.IsSuccess);
        Assert.True(lookup.Value!.IsFound);
        Assert.Contains(repo.Operations, x => x.Status == AutoInvoiceOperationStatus.Succeeded);
    }

    [Fact]
    public async Task Timeout_exception_is_recorded_as_unknown_instead_of_retried()
    {
        var repo = CreateRepository(enabled: true);
        var issue = new FakeIssueService { Exception = new TimeoutException("provider timeout") };
        repo.Candidates.Add(Invoice(5, completedAtUtc: DateTime.UtcNow.AddHours(-1)));
        var service = CreateService(repo, issue);

        await service.RunOnceAsync();

        Assert.Contains(repo.Operations, x => x.Status == AutoInvoiceOperationStatus.Unknown);
        Assert.Equal("TIMEOUT", repo.Operations.Single().ErrorCode);
    }

    [Fact]
    public async Task Failed_invoice_is_left_for_errors_and_does_not_stop_next_invoice()
    {
        var repo = CreateRepository(enabled: true);
        var issue = new FakeIssueService();
        var failed = Invoice(30, completedAtUtc: DateTime.UtcNow.AddHours(-2));
        failed.LastErrorCode = "Invoice.UnitMissing";
        failed.LastErrorMessage = "Thiếu đơn vị tính.";
        var next = Invoice(31, completedAtUtc: DateTime.UtcNow.AddHours(-1));
        repo.Candidates.AddRange([failed, next]);
        var service = CreateService(repo, issue);

        await service.RunOnceAsync();

        Assert.Equal(1, issue.Calls);
        Assert.Contains(repo.Operations, x => x.InvoiceHeadId == next.Id && x.Status == AutoInvoiceOperationStatus.Succeeded);
        Assert.DoesNotContain(repo.Operations, x => x.InvoiceHeadId == failed.Id);
    }

    [Fact]
    public async Task Resolved_stock_error_reenters_queue_without_retrying_unresolved_errors()
    {
        var repo = CreateRepository(enabled: true);
        var issue = new FakeIssueService();
        var invoice = Invoice(36, completedAtUtc: DateTime.UtcNow.AddHours(-1));
        invoice.LastErrorCode = "Invoice.InputInvoiceStockInsufficient";
        invoice.LastErrorMessage = "Còn thiếu tồn trước đó.";
        repo.Candidates.Add(invoice);

        var service = CreateService(repo, issue, new SufficientStockRepository());

        await service.RunOnceAsync();

        Assert.Equal(1, issue.Calls);
        Assert.Contains(repo.Operations, x => x.InvoiceHeadId == invoice.Id && x.Status == AutoInvoiceOperationStatus.Succeeded);
    }

    [Fact]
    public async Task Invoice_before_minimum_age_stays_waiting_without_provider_call()
    {
        var repo = CreateRepository(enabled: true);
        repo.Settings.MinimumAgeMinutes = 5;
        var issue = new FakeIssueService();
        repo.Candidates.Add(Invoice(32, completedAtUtc: DateTime.UtcNow.AddMinutes(-1)));
        var service = CreateService(repo, issue);

        await service.RunOnceAsync();

        Assert.Equal(0, issue.Calls);
        Assert.Empty(repo.Operations);
    }

    [Fact]
    public async Task Forced_admin_cycle_bypasses_previous_send_interval()
    {
        var repo = CreateRepository(enabled: true);
        repo.WorkerStates.Add(new AutoInvoiceWorkerState
        {
            Id = 1,
            StoreId = 1,
            WorkerName = "auto-invoice-worker",
            NextRunAtUtc = DateTime.UtcNow.AddHours(1)
        });
        var issue = new FakeIssueService();
        repo.Candidates.Add(Invoice(33, completedAtUtc: DateTime.UtcNow.AddHours(-1)));
        var service = CreateService(repo, issue);

        await service.RunOnceAsync(force: true);

        Assert.Equal(1, issue.Calls);
    }

    [Fact]
    public async Task Failed_group_marks_all_sources_and_is_not_submitted_again()
    {
        var repo = CreateRepository(enabled: true);
        var issue = new FakeIssueService
        {
            Result = Result<ViettelInvoiceIssueResultDto>.Failure(
                Error.Validation("InvoiceProvider.CredentialKeyUnavailable", "Key không đọc được."))
        };
        var first = Invoice(34, completedAtUtc: DateTime.UtcNow.AddHours(-2));
        var second = Invoice(35, completedAtUtc: DateTime.UtcNow.AddHours(-1));
        first.GrandTotal = 60_000m;
        second.GrandTotal = 60_000m;
        repo.Candidates.AddRange([first, second]);
        var service = CreateService(repo, issue);

        await service.RunOnceAsync();
        await service.RunOnceAsync(force: true);

        Assert.Equal(1, issue.Calls);
        Assert.Equal("InvoiceProvider.CredentialKeyUnavailable", first.LastErrorCode);
        Assert.Equal("InvoiceProvider.CredentialKeyUnavailable", second.LastErrorCode);
    }

    private static AutoInvoiceService CreateService(
        FakeAutoInvoiceRepository repo,
        FakeIssueService issue,
        IInvoiceInputStockRepository? stock = null)
        => new(
            repo,
            stock ?? new SufficientStockRepository(),
            issue,
            new FoundLookupService(),
            new NoopUnitOfWork(),
            new StoreTenant(1),
            new TestCurrentUser(),
            new FixedTimeProvider(DateTime.UtcNow));

    private static FakeAutoInvoiceRepository CreateRepository(bool enabled)
        => new()
        {
            Settings = new AutoInvoiceSettings
            {
                Id = 1,
                StoreId = 1,
                IsEnabled = enabled,
                MinimumAgeMinutes = 0,
                SeparateAmountThreshold = 100_000m,
                GroupTargetAmount = 100_000m,
                SendIntervalSeconds = 1,
                ClosingTimeLocal = new(23, 0, 0),
                IssueOldDayRemainder = true,
                TimeZoneId = TimeZoneInfo.Utc.Id
            }
        };

    private static InvoiceHead Invoice(int id, DateTime completedAtUtc)
        => new()
        {
            Id = id,
            StoreId = 1,
            OrderId = id,
            Order = new Order
            {
                Id = id,
                StoreId = 1,
                CompletedAtUtc = completedAtUtc
            },
            InvoiceDate = completedAtUtc,
            BuyerType = InvoiceBuyerTypes.NoInvoice,
            GrandTotal = 200_000m,
            SubTotal = 200_000m,
            ProviderCode = "VIETTEL",
            InvoiceProviderSettingId = 10,
            InvoiceProviderSetting = new InvoiceProviderSetting
            {
                Id = 10,
                StoreId = 1,
                ProviderCode = "VIETTEL",
                IsActive = true,
                BaseUrl = "https://example.test",
                Username = "test",
                Password = "test",
                SupplierTaxCode = "0100000000",
                InvoiceType = "1",
                TemplateCode = "1/001",
                InvoiceSeries = "C26TAA"
            },
            Details =
            [new InvoiceDetail
            {
                Id = id * 10,
                StoreId = 1,
                InvoiceHeadId = id,
                ItemName = "Hàng test",
                UnitName = "cái",
                Quantity = 1,
                UnitPrice = 200_000m,
                Amount = 200_000m,
                TotalAmount = 200_000m,
                SourceType = InvoiceDetailSourceType.FromOrderLine
            }]
        };

    private sealed class FakeAutoInvoiceRepository : IAutoInvoiceRepository
    {
        public AutoInvoiceSettings Settings { get; set; } = new();
        public List<InvoiceHead> Candidates { get; } = [];
        public List<AutoInvoiceOperation> Operations { get; } = [];
        public List<AutoInvoiceWorkerState> WorkerStates { get; } = [];
        private int _nextOperationId = 100;

        public Task<AutoInvoiceSettings?> GetSettingsAsync(int storeId, CancellationToken ct = default) => Task.FromResult<AutoInvoiceSettings?>(Settings);
        public Task AddSettingsAsync(AutoInvoiceSettings settings, CancellationToken ct = default) { Settings = settings; return Task.CompletedTask; }
        public Task<List<int>> GetActiveStoreIdsAsync(CancellationToken ct = default) => Task.FromResult(new List<int> { 1 });
        public Task<AutoInvoiceWorkerState?> GetWorkerStateAsync(int storeId, string workerName, CancellationToken ct = default) => Task.FromResult(WorkerStates.FirstOrDefault(x => x.StoreId == storeId && x.WorkerName == workerName));
        public Task AddWorkerStateAsync(AutoInvoiceWorkerState state, CancellationToken ct = default) { state.Id = WorkerStates.Count + 1; WorkerStates.Add(state); return Task.CompletedTask; }
        public Task<List<InvoiceHead>> GetCandidateInvoicesAsync(int storeId, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default) => Task.FromResult(Candidates.Where(x => x.StoreId == storeId).ToList());
        public Task<int> ClearRecoverableCredentialErrorsAsync(int storeId, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default) => Task.FromResult(0);
        public Task MarkInvoiceErrorsAsync(int storeId, IReadOnlyCollection<int> invoiceHeadIds, string errorCode, string? errorMessage, CancellationToken ct = default)
        {
            foreach (var invoice in Candidates.Where(x => invoiceHeadIds.Contains(x.Id)))
            {
                invoice.LastErrorCode = errorCode;
                invoice.LastErrorMessage = errorMessage;
            }
            return Task.CompletedTask;
        }
        public Task<List<AutoInvoiceOperation>> GetActiveOperationsAsync(int storeId, CancellationToken ct = default) => Task.FromResult(Operations.Where(x => x.StoreId == storeId && x.Status is AutoInvoiceOperationStatus.Pending or AutoInvoiceOperationStatus.Processing or AutoInvoiceOperationStatus.Unknown).ToList());
        public Task<InvoiceHead?> GetInvoiceHeadForAutomaticIssueAsync(int storeId, int invoiceHeadId, CancellationToken ct = default) => Task.FromResult(Candidates.FirstOrDefault(x => x.StoreId == storeId && x.Id == invoiceHeadId));
        public Task<List<AutoInvoiceOperation>> GetOperationsAsync(int storeId, int take, CancellationToken ct = default) => Task.FromResult(Operations.Where(x => x.StoreId == storeId).OrderByDescending(x => x.Id).Take(take).ToList());
        public Task<AutoInvoiceOperation?> GetOperationAsync(int storeId, int operationId, CancellationToken ct = default) => Task.FromResult(Operations.FirstOrDefault(x => x.StoreId == storeId && x.Id == operationId));
        public Task<bool> HasActiveSourceAsync(int storeId, int invoiceHeadId, CancellationToken ct = default) => Task.FromResult(Operations.Any(x => x.StoreId == storeId && x.Sources.Any(s => s.InvoiceHeadId == invoiceHeadId && s.IsActive)));
        public Task AddOperationAsync(AutoInvoiceOperation operation, CancellationToken ct = default) { operation.Id = ++_nextOperationId; Operations.Add(operation); return Task.CompletedTask; }
        public Task AddInvoiceHeadAsync(InvoiceHead invoice, CancellationToken ct = default) { invoice.Id = 1000; return Task.CompletedTask; }
        public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
        public void DiscardFailedAutoInvoiceChanges(bool includeWorkerState = false) { }
    }

    private sealed class SufficientStockRepository : IInvoiceInputStockRepository
    {
        public Task LockStoreForIssueAsync(int storeId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<InvoiceInputStockAvailabilityDto> GetAvailabilityAsync(int invoiceHeadId, CancellationToken ct = default) => Task.FromResult(new InvoiceInputStockAvailabilityDto { InvoiceHeadId = invoiceHeadId });
    }

    private sealed class InsufficientStockRepository : IInvoiceInputStockRepository
    {
        public Task LockStoreForIssueAsync(int storeId, CancellationToken ct = default) => Task.CompletedTask;

        public Task<InvoiceInputStockAvailabilityDto> GetAvailabilityAsync(int invoiceHeadId, CancellationToken ct = default)
            => Task.FromResult(new InvoiceInputStockAvailabilityDto
            {
                InvoiceHeadId = invoiceHeadId,
                Lines =
                [new InvoiceInputStockAvailabilityLineDto
                {
                    WarehouseId = 7,
                    ProductVariantId = 8,
                    ItemName = "Hàng thiếu tồn",
                    RequiredBaseQuantity = 3,
                    EligibleInboundBaseQuantity = 1,
                    CommittedOutboundBaseQuantity = 0
                }]
            });
    }

    private sealed class FakeIssueService : IViettelInvoiceIssueService
    {
        public int Calls { get; private set; }
        public Exception? Exception { get; set; }
        public Result<ViettelInvoiceIssueResultDto> Result { get; set; } = Result<ViettelInvoiceIssueResultDto>.Success(new ViettelInvoiceIssueResultDto { IsSuccess = true });
        public Task<Result<ViettelInvoiceIssueResultDto>> IssueAsync(int invoiceHeadId, CancellationToken ct = default)
        {
            Calls++;
            if (Exception != null)
                throw Exception;
            return Task.FromResult(Result);
        }
    }

    private sealed class FoundLookupService : IViettelInvoiceSyncService
    {
        public Task<Result<ViettelInvoiceLookupResultDto>> SyncByTransactionUuidAsync(int invoiceHeadId, CancellationToken ct = default)
            => Task.FromResult(Result<ViettelInvoiceLookupResultDto>.Success(new ViettelInvoiceLookupResultDto { InvoiceHeadId = invoiceHeadId, IsFound = true, TransactionUuid = "test-uuid" }));
    }

    private sealed class NoopUnitOfWork : IAppUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
        public Task<IAppTransaction> BeginTransactionAsync(CancellationToken ct = default) => Task.FromResult<IAppTransaction>(new NoopTransaction());
    }

    private sealed class NoopTransaction : IAppTransaction
    {
        public Task CommitAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task RollbackAsync(CancellationToken ct = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class StoreTenant(int storeId) : ITenantContext
    {
        public int? StoreId => storeId;
        public bool IsHostAdmin => false;
        public string? Subdomain => "test";
    }

    private sealed class TestCurrentUser : ICurrentUser
    {
        public int? UserId => 5;
        public string? UserName => "tester";
        public int? TerminalId => null;
        public string? TerminalCode => null;
        public bool IsAuthenticated => true;
    }

    private sealed class FixedTimeProvider(DateTime nowUtc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc));
    }
}
