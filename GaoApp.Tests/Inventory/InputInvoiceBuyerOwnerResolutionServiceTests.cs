using GaoApp.Application.Services.Inventory;
using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Application.Interfaces.Repositories.LegalEntities;
using GaoApp.Application.Interfaces.Services.Inventory;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using System.Reflection;

namespace GaoApp.Tests.Inventory;

public sealed class InputInvoiceBuyerOwnerResolutionServiceTests
{
    [Theory]
    [InlineData(null, 0, InputInvoiceBuyerOwnerResolutionStatus.MissingBuyerTaxCode, null)]
    [InlineData("010-123 4567", 0, InputInvoiceBuyerOwnerResolutionStatus.NotFound, null)]
    [InlineData("010-123 4567", 2, InputInvoiceBuyerOwnerResolutionStatus.Ambiguous, null)]
    [InlineData("010-123 4567", 1, InputInvoiceBuyerOwnerResolutionStatus.Resolved, 101)]
    public async Task Resolve_is_fail_closed_and_only_exactly_one_candidate_resolves(
        string? buyerTaxCode,
        int candidateCount,
        InputInvoiceBuyerOwnerResolutionStatus expectedStatus,
        int? expectedOwnerId)
    {
        var candidates = Enumerable.Range(0, candidateCount).Select(index => new LegalEntity
        {
            Id = 101 + index,
            StoreId = 7,
            Code = $"LE-{index}",
            Name = $"Owner {index}",
            LegalName = $"Owner {index}",
            TaxCode = "0101234567",
            IsActive = true
        }).ToList();
        var invoice = new InputInvoiceHead { StoreId = 7, BuyerTaxCode = buyerTaxCode };
        var result = await new InputInvoiceBuyerOwnerResolutionService(
            new LegalEntityRepositoryStub(candidates))
            .ResolveWithinTransactionAsync(7, invoice);

        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(expectedOwnerId, result.LegalEntityId);
        Assert.Equal(expectedStatus, invoice.BuyerOwnerResolutionStatus);
        Assert.Equal(expectedOwnerId, invoice.ResolvedBuyerLegalEntityId);
        Assert.NotNull(invoice.BuyerOwnerResolutionUpdatedAtUtc);
    }

    [Fact]
    public async Task Resolver_uses_normalized_same_store_candidate_query_behaviorally()
    {
        var repository = new LegalEntityRepositoryStub([
            new LegalEntity
            {
                Id = 101, StoreId = 7, Code = "LE-01", Name = "Owner",
                LegalName = "Owner", TaxCode = "0101234567", IsActive = true
            }
        ]);
        var invoice = new InputInvoiceHead
        {
            StoreId = 7,
            BuyerTaxCode = " 010-123 4567 "
        };

        var result = await new InputInvoiceBuyerOwnerResolutionService(repository)
            .ResolveWithinTransactionAsync(7, invoice);

        Assert.Equal(7, repository.LastStoreId);
        Assert.Equal("0101234567", repository.LastNormalizedTaxCode);
        Assert.Equal(InputInvoiceBuyerOwnerResolutionStatus.Resolved, result.Status);
        Assert.Equal(101, result.LegalEntityId);
    }

    [Fact]
    public async Task Central_owner_guard_and_link_service_are_constructed_and_invoked()
    {
        var linkedEventCount = 0;
        var repository = Proxy<IInputInvoiceRepository>((method, _) => method.Name switch
        {
            nameof(IInputInvoiceRepository.EnsureSingleReceiptInvoiceMapAsync) => Task.FromResult(true),
            nameof(IInputInvoiceRepository.AddMissingLineMapsAsync) => Task.CompletedTask,
            nameof(IInputInvoiceRepository.AddPurchaseReceiptAuditEventAsync) => CountLinkedEvent(),
            _ => throw new NotSupportedException(method.Name)
        });
        var warehouses = Proxy<IWarehouseRepository>((method, args) => method.Name switch
        {
            nameof(IWarehouseRepository.LockByStoreAndIdAsync) => Task.FromResult<Warehouse?>(
                new Warehouse
                {
                    Id = (int)args![1]!, StoreId = 7, LegalEntityId = 101,
                    Code = "WH", Name = "Warehouse", IsActive = true
                }),
            _ => throw new NotSupportedException(method.Name)
        });
        var supplier = Proxy<IInputInvoiceSupplierResolutionService>((method, _) => method.Name switch
        {
            nameof(IInputInvoiceSupplierResolutionService.BindCanonicalSupplierWithinTransactionAsync) =>
                Task.CompletedTask,
            _ => throw new NotSupportedException(method.Name)
        });
        var ownerGuard = new InputInvoiceReceiptOwnerGuard(
            warehouses,
            new InputInvoiceBuyerOwnerResolutionService(new LegalEntityRepositoryStub([
                new LegalEntity
                {
                    Id = 101, StoreId = 7, Code = "LE-01", Name = "Owner",
                    LegalName = "Owner", TaxCode = "0101234567", IsActive = true
                }
            ])));
        IInputInvoiceReceiptLinkService linkService = new InputInvoiceReceiptLinkService(
            repository, ownerGuard, supplier);
        var receipt = new StockDocument
        {
            Id = 10, StoreId = 7, Type = StockDocumentType.Receipt,
            Status = StockDocumentStatus.PendingApproval, WarehouseId = 3
        };
        var invoice = new InputInvoiceHead
        {
            Id = 20, StoreId = 7, BuyerTaxCode = "010-123 4567"
        };

        var created = await linkService.LinkWithinTransactionAsync(
            7, receipt, invoice, note: null);

        Assert.True(created);
        Assert.Equal(InputInvoiceBuyerOwnerResolutionStatus.Resolved,
            invoice.BuyerOwnerResolutionStatus);
        Assert.Equal(101, invoice.ResolvedBuyerLegalEntityId);
        Assert.Equal(1, linkedEventCount);
        return;

        Task CountLinkedEvent()
        {
            linkedEventCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class LegalEntityRepositoryStub(IReadOnlyList<LegalEntity> candidates)
        : ILegalEntityRepository
    {
        public int? LastStoreId { get; private set; }
        public string? LastNormalizedTaxCode { get; private set; }

        public Task<IReadOnlyList<LegalEntity>> LockActiveByNormalizedTaxCodeAsync(
            int storeId, string normalizedTaxCode, CancellationToken ct = default)
        {
            LastStoreId = storeId;
            LastNormalizedTaxCode = normalizedTaxCode;
            return Task.FromResult(candidates);
        }
        public Task<List<LegalEntity>> GetAllAsync(CancellationToken ct = default) => Task.FromResult(candidates.ToList());
        public Task<LegalEntity?> GetByIdAsync(int id, CancellationToken ct = default) => Task.FromResult(candidates.FirstOrDefault(x => x.Id == id));
        public Task<LegalEntity?> GetDefaultForPurchaseAsync(CancellationToken ct = default) => Task.FromResult<LegalEntity?>(null);
        public Task<LegalEntity?> GetFirstActiveByPriorityAsync(CancellationToken ct = default) => Task.FromResult<LegalEntity?>(null);
        public Task<bool> ExistsCodeAsync(string code, int? excludeId = null, CancellationToken ct = default) => Task.FromResult(false);
        public Task<bool> ExistsSalePriorityAsync(int salePriority, int? excludeId = null, CancellationToken ct = default) => Task.FromResult(false);
        public Task<bool> ExistsTaxCodeAsync(string taxCode, int? excludeId = null, CancellationToken ct = default) => Task.FromResult(false);
        public Task<bool> ExistsDefaultWarehouseAssignmentAsync(int warehouseId, int? excludeId = null, CancellationToken ct = default) => Task.FromResult(false);
        public Task<bool> ExistsInvoiceSettingAssignmentAsync(int invoiceProviderSettingId, int? excludeId = null, CancellationToken ct = default) => Task.FromResult(false);
        public Task<bool> HasActiveWarehousesAsync(int legalEntityId, CancellationToken ct = default) => Task.FromResult(false);
        public Task<Store?> GetStoreAsync(int storeId, CancellationToken ct = default) => Task.FromResult<Store?>(null);
        public Task<Store?> GetStoreForUpdateAsync(int storeId, CancellationToken ct = default) => Task.FromResult<Store?>(null);
        public Task ClearDefaultForPurchaseAsync(int? exceptLegalEntityId = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task AddAsync(LegalEntity entity, CancellationToken ct = default) => Task.CompletedTask;
        public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private static T Proxy<T>(Func<MethodInfo, object?[]?, object?> handler)
        where T : class
    {
        var proxy = DispatchProxy.Create<T, DelegateProxy>();
        ((DelegateProxy)(object)proxy).Handler = handler;
        return proxy;
    }

    private class DelegateProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Handler { get; set; } = default!;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            => Handler(targetMethod!, args);
    }
}
