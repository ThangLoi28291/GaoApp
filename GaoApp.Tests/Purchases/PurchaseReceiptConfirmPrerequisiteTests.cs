using System.Reflection;
using FluentAssertions;
using GaoApp.Application.Common;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Application.Interfaces.Repositories.LegalEntities;
using GaoApp.Application.Interfaces.Services.Inventory;
using GaoApp.Application.Interfaces.Services.Products;
using GaoApp.Application.Services.Inventory;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Tests.Purchases;

public sealed class PurchaseReceiptConfirmPrerequisiteTests
{
    private static readonly byte[] CurrentVersion = [1, 2, 3, 4];

    [Theory]
    [InlineData(true, "Người bán đã nhận tiền")]
    [InlineData(true, null)]
    [InlineData(false, null)]
    public async Task Direct_receipt_without_supplier_fails_even_when_paid_or_payee_is_present(
        bool paid,
        string? payee)
    {
        var document = CreateDirectReceipt();
        document.SupplierId = null;
        document.Supplier = null;
        var fixture = CreateFixture(document);
        var before = ReceiptSnapshot.Capture(document);

        var action = () => fixture.Service.ApproveCommercialAsync(
            document.Id,
            ValidCommercialRequest(null, paid, payee));

        await action.Should().ThrowAsync<BusinessRuleException>()
            .WithMessage("*chọn nhà cung cấp*");
        ReceiptSnapshot.Capture(document).Should().BeEquivalentTo(before);
        fixture.AssertNoMutationOrPosting();
        fixture.Repository.GetSupplierCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-base64")]
    public async Task Missing_or_invalid_RowVersion_fails_before_supplier_resolution(string? rowVersion)
    {
        var document = CreateDirectReceipt();
        var fixture = CreateFixture(document, CreateSupplier(51));
        var request = ValidCommercialRequest(51);
        request.RowVersion = rowVersion!;

        var action = () => fixture.Service.ApproveCommercialAsync(document.Id, request);

        await action.Should().ThrowAsync<BusinessRuleException>();
        fixture.Repository.GetSupplierCalls.Should().Be(0);
        fixture.AssertNoMutationOrPosting();
    }

    [Fact]
    public async Task Stale_RowVersion_fails_before_supplier_resolution()
    {
        var document = CreateDirectReceipt();
        var fixture = CreateFixture(document, CreateSupplier(51));
        var request = ValidCommercialRequest(51);
        request.RowVersion = Convert.ToBase64String([9, 9, 9]);

        var action = () => fixture.Service.ApproveCommercialAsync(document.Id, request);

        await action.Should().ThrowAsync<BusinessRuleException>()
            .WithMessage("*người khác cập nhật*");
        fixture.Repository.GetSupplierCalls.Should().Be(0);
        fixture.AssertNoMutationOrPosting();
    }

    [Fact]
    public async Task Unresolved_or_foreign_store_supplier_fails_safely_before_mutation()
    {
        var document = CreateDirectReceipt();
        var fixture = CreateFixture(document);
        var before = ReceiptSnapshot.Capture(document);

        var action = () => fixture.Service.ApproveCommercialAsync(
            document.Id,
            ValidCommercialRequest(999));

        await action.Should().ThrowAsync<BusinessRuleException>()
            .WithMessage("*không tồn tại hoặc không thuộc cửa hàng hiện tại*");
        ReceiptSnapshot.Capture(document).Should().BeEquivalentTo(before);
        fixture.Repository.GetSupplierCalls.Should().Be(1);
        fixture.AssertNoMutationOrPosting();
    }

    [Fact]
    public async Task Valid_direct_supplier_is_accepted_and_existing_posting_flow_is_preserved()
    {
        var supplier = CreateSupplier(51);
        var document = CreateDirectReceipt(supplier);
        var fixture = CreateFixture(document, supplier);

        await fixture.Service.ApproveCommercialAsync(
            document.Id,
            ValidCommercialRequest(supplier.Id, paid: false));

        document.Status.Should().Be(StockDocumentStatus.Confirmed);
        document.SupplierId.Should().Be(supplier.Id);
        document.Lines.Single().UnitPriceBeforeVat.Should().Be(12m);
        fixture.Repository.GetSupplierCalls.Should().Be(2,
            "commercial selection and the final service guard both resolve in the current Store");
        fixture.Repository.BeginTransactionCalls.Should().Be(1);
        fixture.Movements.PreLockCalls.Should().Be(1);
        fixture.Movements.CreateCalls.Should().Be(1);
        fixture.Repository.SaveCalls.Should().Be(1);
        fixture.Repository.CommitTransactionCalls.Should().Be(1);
        fixture.Repository.RollbackTransactionCalls.Should().Be(0);
        fixture.Repository.AddedPayables.Should().ContainSingle()
            .Which.SupplierId.Should().Be(supplier.Id);
    }

    [Fact]
    public async Task Purchase_order_receipt_missing_supplier_fails_before_mutation()
    {
        var supplier = CreateSupplier(51);
        var document = CreatePurchaseOrderReceipt(supplier);
        document.SupplierId = null;
        document.Supplier = null;
        var fixture = CreateFixture(document, supplier);

        var action = () => fixture.Service.ApproveCommercialAsync(
            document.Id,
            ValidCommercialRequest(null));

        await action.Should().ThrowAsync<BusinessRuleException>()
            .WithMessage("*chọn nhà cung cấp*");
        fixture.AssertNoMutationOrPosting();
    }

    [Fact]
    public async Task Purchase_order_receipt_with_different_supplier_fails_before_mutation()
    {
        var orderSupplier = CreateSupplier(51);
        var otherSupplier = CreateSupplier(52);
        var document = CreatePurchaseOrderReceipt(orderSupplier);
        var fixture = CreateFixture(document, orderSupplier, otherSupplier);
        var before = ReceiptSnapshot.Capture(document);

        var action = () => fixture.Service.ApproveCommercialAsync(
            document.Id,
            ValidCommercialRequest(otherSupplier.Id));

        await action.Should().ThrowAsync<BusinessRuleException>()
            .WithMessage("*Không thể đổi nhà cung cấp*");
        ReceiptSnapshot.Capture(document).Should().BeEquivalentTo(before);
        fixture.AssertNoMutationOrPosting();
    }

    [Fact]
    public async Task Purchase_order_receipt_with_matching_supplier_remains_valid()
    {
        var supplier = CreateSupplier(51);
        var document = CreatePurchaseOrderReceipt(supplier);
        var fixture = CreateFixture(document, supplier);

        await fixture.Service.ApproveCommercialAsync(
            document.Id,
            ValidCommercialRequest(supplier.Id));

        document.Status.Should().Be(StockDocumentStatus.Confirmed);
        document.PurchaseOrder!.Lines.Single().ReceivedQuantity.Should().Be(2m);
        fixture.Movements.CreateCalls.Should().Be(1);
        fixture.Repository.CommitTransactionCalls.Should().Be(1);
    }

    [Fact]
    public async Task Generic_final_confirm_guard_rejects_missing_supplier_before_transaction()
    {
        var document = CreateDirectReceipt();
        document.SupplierId = null;
        document.Supplier = null;
        var fixture = CreateFixture(document);

        var action = () => fixture.Service.ApproveAsync(
            document.Id,
            approvalNote: null,
            RowVersion(document));

        await action.Should().ThrowAsync<BusinessRuleException>()
            .WithMessage("*chọn nhà cung cấp*");
        fixture.AssertNoMutationOrPosting();
    }

    [Fact]
    public async Task Generic_final_confirm_guard_rejects_unresolved_supplier_before_transaction()
    {
        var document = CreateDirectReceipt();
        var fixture = CreateFixture(document);

        var action = () => fixture.Service.ApproveAsync(
            document.Id,
            approvalNote: null,
            RowVersion(document));

        await action.Should().ThrowAsync<BusinessRuleException>()
            .WithMessage("*không tồn tại hoặc không thuộc cửa hàng hiện tại*");
        fixture.AssertNoMutationOrPosting();
    }

    [Fact]
    public async Task Submit_for_approval_still_permits_missing_supplier()
    {
        var document = CreateDirectReceipt(status: StockDocumentStatus.Draft);
        document.SupplierId = null;
        document.Supplier = null;
        var fixture = CreateFixture(document);

        await fixture.Service.SubmitForApprovalAsync(
            document.Id,
            approvalNote: null,
            RowVersion(document));

        document.Status.Should().Be(StockDocumentStatus.PendingApproval);
        document.SupplierId.Should().BeNull();
        fixture.Repository.SaveCalls.Should().Be(1);
        fixture.Movements.CreateCalls.Should().Be(0);
    }

    [Fact]
    public async Task Draft_header_update_still_permits_null_supplier()
    {
        var document = CreateDirectReceipt(status: StockDocumentStatus.Draft);
        var fixture = CreateFixture(document, CreateSupplier(51));

        await fixture.Service.UpdateHeaderAsync(new UpdateStockDocumentHeaderRequest
        {
            StockDocumentId = document.Id,
            LegalEntityId = 5,
            WarehouseId = 10,
            SupplierId = null
        });

        document.SupplierId.Should().BeNull();
        fixture.Repository.SaveCalls.Should().Be(1);
        fixture.Repository.SupplierExistsCalls.Should().Be(0);
    }

    [Fact]
    public async Task Already_confirmed_retry_remains_no_op_even_without_supplier()
    {
        var document = CreateDirectReceipt(status: StockDocumentStatus.Confirmed);
        document.SupplierId = null;
        document.Supplier = null;
        var fixture = CreateFixture(document);

        await fixture.Service.ApproveAsync(
            document.Id,
            "newer browser note",
            rowVersion: null);

        document.Status.Should().Be(StockDocumentStatus.Confirmed);
        document.ApprovalNote.Should().BeNull();
        fixture.Repository.GetSupplierCalls.Should().Be(0);
        fixture.AssertNoMutationOrPosting();
    }

    private static ServiceFixture CreateFixture(
        StockDocument document,
        params Supplier[] resolvableSuppliers)
    {
        var repository = new RecordingStockDocumentRepository(document, resolvableSuppliers);
        var movements = new RecordingInventoryMovementService();
        var warehouse = document.Warehouse ?? CreateWarehouse();

        var service = new StockDocumentService(
            repository,
            Unused<ILegalEntityRepository>(),
            CreateProxy<IWarehouseRepository>((method, _) =>
                method.Name == nameof(IWarehouseRepository.GetByIdAsync)
                    ? Task.FromResult<Warehouse?>(warehouse)
                    : throw new NotSupportedException(method.Name)),
            Unused<IBarcodeLookupService>(),
            Unused<IInventoryUnitResolver>(),
            movements,
            new InventoryMovementFactory(),
            Unused<IInventoryRevaluationService>(),
            Unused<IDocumentNumberSequenceRepository>(),
            new TenantContextStub(),
            CreateProxy<IInventoryValuationEntryRepository>((method, _) =>
                method.Name == nameof(IInventoryValuationEntryRepository.GetByReferenceAsync)
                    ? Task.FromResult(new List<InventoryValuationEntry>())
                    : throw new NotSupportedException(method.Name)),
            new CurrentUserStub());

        return new ServiceFixture(service, repository, movements);
    }

    private static StockDocument CreateDirectReceipt(
        Supplier? supplier = null,
        StockDocumentStatus status = StockDocumentStatus.PendingApproval)
    {
        supplier ??= CreateSupplier(51);
        var warehouse = CreateWarehouse();
        var document = new StockDocument
        {
            Id = 11,
            StoreId = 1,
            DocumentNo = "NK-B2-001",
            Status = status,
            Type = StockDocumentType.Receipt,
            ReceiptSource = PurchaseReceiptSource.Direct,
            DirectReceiptReason = "Khác",
            WarehouseId = warehouse.Id,
            Warehouse = warehouse,
            SupplierId = supplier.Id,
            Supplier = supplier,
            RowVersion = CurrentVersion.ToArray()
        };
        document.Lines.Add(CreateValidLine(document));
        return document;
    }

    private static StockDocument CreatePurchaseOrderReceipt(Supplier supplier)
    {
        var document = CreateDirectReceipt(supplier);
        var order = new PurchaseOrder
        {
            Id = 70,
            StoreId = 1,
            OrderNumber = "PO-B2-001",
            Status = PurchaseOrderStatus.Approved,
            SupplierId = supplier.Id,
            Supplier = supplier,
            ExpectedWarehouseId = document.WarehouseId,
            ExpectedWarehouse = document.Warehouse,
            LegalEntityId = document.Warehouse.LegalEntityId,
            LegalEntity = document.Warehouse.LegalEntity
        };
        var orderLine = new PurchaseOrderLine
        {
            Id = 71,
            StoreId = 1,
            PurchaseOrderId = order.Id,
            PurchaseOrder = order,
            LineNo = 1,
            ProductVariantId = 31,
            ProductNameSnapshot = "Gạo",
            UnitNameSnapshot = "kg",
            ConversionFactor = 1m,
            OrderedQuantity = 2m
        };
        order.Lines.Add(orderLine);
        document.ReceiptSource = PurchaseReceiptSource.PurchaseOrder;
        document.PurchaseOrderId = order.Id;
        document.PurchaseOrder = order;
        document.Lines.Single().PurchaseOrderLineId = orderLine.Id;
        document.Lines.Single().PurchaseOrderLine = orderLine;
        return document;
    }

    private static StockDocumentLine CreateValidLine(StockDocument document)
        => new()
        {
            Id = 21,
            StockDocumentId = document.Id,
            StockDocument = document,
            LineNo = 1,
            ProductVariantId = 31,
            ProductNameSnapshot = "Gạo",
            Quantity = 2m,
            Factor = 1m,
            BaseQuantity = 2m,
            UnitPriceBeforeVat = 10m,
            UnitPriceAfterVat = 10m,
            UnitCost = 10m,
            LineTotal = 20m
        };

    private static ApprovePurchaseReceiptCommercialRequest ValidCommercialRequest(
        int? supplierId,
        bool paid = false,
        string? payee = null)
        => new()
        {
            RowVersion = Convert.ToBase64String(CurrentVersion),
            SupplierId = supplierId,
            IsMerchandisePaid = paid,
            MerchandisePayeeName = payee,
            Lines =
            [
                new PurchaseReceiptFinancialLineInputDto
                {
                    StockDocumentLineId = 21,
                    UnitPriceBeforeVat = 12m
                }
            ]
        };

    private static Supplier CreateSupplier(int id)
        => new()
        {
            Id = id,
            StoreId = 1,
            Code = $"NCC-{id}",
            Name = $"Nhà cung cấp {id}",
            IsActive = true
        };

    private static Warehouse CreateWarehouse()
    {
        var owner = new LegalEntity
        {
            Id = 5,
            StoreId = 1,
            Code = "LE-01",
            Name = "HKD 01",
            LegalName = "HKD 01",
            IsActive = true
        };
        return new Warehouse
        {
            Id = 10,
            StoreId = 1,
            Code = "WH-01",
            Name = "Kho 01",
            IsActive = true,
            LegalEntityId = owner.Id,
            LegalEntity = owner
        };
    }

    private static string RowVersion(StockDocument document)
        => Convert.ToBase64String(document.RowVersion);

    private static T Unused<T>() where T : class
        => CreateProxy<T>((method, _) => throw new NotSupportedException(method.Name));

    private static T CreateProxy<T>(Func<MethodInfo, object?[]?, object?> handler)
        where T : class
    {
        var proxy = DispatchProxy.Create<T, DelegateProxy>();
        ((DelegateProxy)(object)proxy).Handler = handler;
        return proxy;
    }

    private sealed record ServiceFixture(
        StockDocumentService Service,
        RecordingStockDocumentRepository Repository,
        RecordingInventoryMovementService Movements)
    {
        public void AssertNoMutationOrPosting()
        {
            Repository.SaveCalls.Should().Be(0);
            Repository.BeginTransactionCalls.Should().Be(0);
            Repository.CommitTransactionCalls.Should().Be(0);
            Repository.RollbackTransactionCalls.Should().Be(0);
            Repository.AddedPayables.Should().BeEmpty();
            Movements.PreLockCalls.Should().Be(0);
            Movements.CreateCalls.Should().Be(0);
        }
    }

    private sealed record ReceiptSnapshot(
        StockDocumentStatus Status,
        int? SupplierId,
        bool IsMerchandisePaid,
        string? MerchandisePayeeName,
        bool HasVat,
        decimal UnitPriceBeforeVat,
        decimal UnitCost,
        decimal LineTotal,
        byte[] RowVersion)
    {
        public static ReceiptSnapshot Capture(StockDocument document)
        {
            var line = document.Lines.Single();
            return new ReceiptSnapshot(
                document.Status,
                document.SupplierId,
                document.IsMerchandisePaid,
                document.MerchandisePayeeName,
                document.HasVat,
                line.UnitPriceBeforeVat,
                line.UnitCost,
                line.LineTotal,
                document.RowVersion.ToArray());
        }
    }

    private class DelegateProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Handler { get; set; } = default!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            => Handler(targetMethod!, args);
    }

    private sealed class CurrentUserStub : ICurrentUser
    {
        public int? UserId => 7;
        public string? UserName => "b2-test";
        public int? TerminalId => null;
        public string? TerminalCode => null;
        public bool IsAuthenticated => true;
    }

    private sealed class TenantContextStub : ITenantContext
    {
        public int? StoreId => 1;
        public bool IsHostAdmin => false;
        public string? Subdomain => "test";
    }

    private sealed class RecordingInventoryMovementService : IInventoryMovementService
    {
        public int PreLockCalls { get; private set; }
        public int CreateCalls { get; private set; }

        public Task PreLockBalancesAsync(
            IEnumerable<InventoryPostingLockKey> keys,
            CancellationToken ct = default)
        {
            keys.Should().NotBeEmpty();
            PreLockCalls++;
            return Task.CompletedTask;
        }

        public Task<InventoryMovementResultDto> CreateAsync(
            CreateInventoryMovementRequest request,
            CancellationToken ct = default)
        {
            CreateCalls++;
            return Task.FromResult(new InventoryMovementResultDto { IsCreated = true });
        }

        public Task<decimal> PeekOutboundUnitCostAsync(
            int warehouseId,
            int productVariantId,
            decimal quantity,
            CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class RecordingStockDocumentRepository : IStockDocumentRepository
    {
        private readonly Dictionary<int, Supplier> _suppliers;

        public RecordingStockDocumentRepository(
            StockDocument document,
            IEnumerable<Supplier> suppliers)
        {
            Document = document;
            _suppliers = suppliers.ToDictionary(x => x.Id);
        }

        public StockDocument Document { get; }
        public int SaveCalls { get; private set; }
        public int BeginTransactionCalls { get; private set; }
        public int CommitTransactionCalls { get; private set; }
        public int RollbackTransactionCalls { get; private set; }
        public int GetSupplierCalls { get; private set; }
        public int SupplierExistsCalls { get; private set; }
        public List<PurchasePayable> AddedPayables { get; } = [];

        public Task AddAsync(StockDocument entity, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<StockDocument?> GetByIdAsync(int id, CancellationToken ct = default)
            => Task.FromResult<StockDocument?>(Document);
        public Task<StockDocument?> GetDetailAsync(int id, CancellationToken ct = default)
            => Task.FromResult<StockDocument?>(Document);
        public Task<StockDocument?> GetForConfirmAsync(int id, CancellationToken ct = default)
            => Task.FromResult<StockDocument?>(Document);
        public Task<StockDocumentLine?> GetLineByIdAsync(int lineId, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<int> GetNextLineNoAsync(int stockDocumentId, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<bool> WarehouseExistsAsync(int warehouseId, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<bool> SupplierExistsAsync(int supplierId, CancellationToken ct = default)
        {
            SupplierExistsCalls++;
            return Task.FromResult(_suppliers.ContainsKey(supplierId));
        }
        public Task<Supplier?> GetSupplierAsync(int supplierId, CancellationToken ct = default)
        {
            GetSupplierCalls++;
            return Task.FromResult(_suppliers.GetValueOrDefault(supplierId));
        }
        public Task<ProductVariant?> GetVariantForStockDocumentAsync(int productVariantId, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<ProductUnitConversion?> GetConversionAsync(int productVariantId, int unitId, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<ProductUnitConversion?> GetBaseConversionAsync(int productVariantId, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<Tax?> GetTaxAsync(int taxId, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<List<Tax>> GetTaxesAsync(CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<Dictionary<int, decimal>> GetLastPurchaseBaseUnitPricesBeforeVatAsync(
            IEnumerable<int> productVariantIds,
            CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<PurchaseOrder?> GetPurchaseOrderForReceiptAsync(int purchaseOrderId, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task AddPurchasePayableAsync(PurchasePayable payable, CancellationToken ct = default)
        {
            AddedPayables.Add(payable);
            return Task.CompletedTask;
        }
        public Task<bool> PurchasePayableExistsAsync(string sourceKey, CancellationToken ct = default)
            => Task.FromResult(false);
        public Task<InventoryBalance?> GetInventoryBalanceAsync(int warehouseId, int productVariantId, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task AddInventoryBalanceAsync(InventoryBalance entity, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task AddInventoryTransactionAsync(InventoryTransaction entity, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<List<StockDocument>> GetReceiptListAsync(CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task RemoveLineAsync(StockDocumentLine line, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<bool> ExistsInventoryTransactionByReferenceLineAsync(
            InventoryReferenceType referenceType,
            string referenceId,
            int referenceLineId,
            CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task BeginTransactionAsync(CancellationToken ct = default)
        {
            BeginTransactionCalls++;
            return Task.CompletedTask;
        }
        public Task CommitTransactionAsync(CancellationToken ct = default)
        {
            CommitTransactionCalls++;
            return Task.CompletedTask;
        }
        public Task RollbackTransactionAsync(CancellationToken ct = default)
        {
            RollbackTransactionCalls++;
            return Task.CompletedTask;
        }
        public Task SaveChangesAsync(CancellationToken ct = default)
        {
            SaveCalls++;
            return Task.CompletedTask;
        }
        public Task MarkVariantsHasInputInvoiceAsync(
            IEnumerable<int> productVariantIds,
            int? userId,
            CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
