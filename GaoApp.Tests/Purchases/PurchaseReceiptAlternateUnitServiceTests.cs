using System.Reflection;
using GaoApp.Application.Common;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.DTOs.Purchases;
using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Application.Interfaces.Repositories.LegalEntities;
using GaoApp.Application.Interfaces.Services.Inventory;
using GaoApp.Application.Interfaces.Services.Products;
using GaoApp.Application.Services.Inventory;
using GaoApp.Application.Services.Purchases;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Tests.Purchases;

public sealed class PurchaseReceiptAlternateUnitServiceTests
{
    [Fact]
    public void Request_defaults_to_ordered_unit_when_receipt_unit_is_omitted()
    {
        var request = new CreatePurchaseReceiptLineRequest
        {
            PurchaseOrderLineId = 10,
            Quantity = 3m
        };
        Assert.Null(request.ReceiptUnitId);
    }

    [Fact]
    public void Alternate_and_ordered_units_produce_the_same_official_po_increment()
    {
        var fromBoxes = PurchaseReceiptQuantityConversionPolicy.ToOrderedEquivalent(
            PurchaseReceiptQuantityConversionPolicy.ToCanonical(3m, 12m), 12m);
        var fromBottles = PurchaseReceiptQuantityConversionPolicy.ToOrderedEquivalent(
            PurchaseReceiptQuantityConversionPolicy.ToCanonical(36m, 1m), 12m);

        Assert.Equal(3m, fromBoxes);
        Assert.Equal(fromBoxes, fromBottles);
    }

    [Fact]
    public void Server_source_uses_snapshot_base_quantity_for_allocation_and_posting()
    {
        var root = FindRepositoryRoot();
        var service = File.ReadAllText(Path.Combine(root, "GaoApp.Application", "Services", "Inventory", "StockDocumentService.cs"));
        var repository = File.ReadAllText(Path.Combine(root, "GaoApp.Infrastructure", "Repositories", "Inventory", "StockDocumentRepository.cs"));

        Assert.DoesNotContain("canonicalQuantity > projection.AvailableToAllocateQuantity", service);
        Assert.Contains("canonical != line.BaseQuantity", service);
        Assert.Contains("EvaluateOverdelivery", service);
        Assert.Contains("CurrentReceiptBaseQuantity", service);
        Assert.Contains("qtyBase: line.BaseQuantity", service);
        Assert.Contains("x.Sum(y => y.BaseQuantity)", repository);
    }

    [Fact]
    public void Purchase_order_dto_returns_zero_alternate_entry_maximum_at_ordered_storage_boundary()
    {
        var orderedUnit = new Unit
        {
            Id = 50, StoreId = 1, Code = "TINY", Name = "đơn vị nhỏ", IsActive = true
        };
        var baseUnit = new Unit
        {
            Id = 51, StoreId = 1, Code = "BASE", Name = "đơn vị gốc", IsActive = true
        };
        var product = new Product
        {
            Id = 80, StoreId = 1, Name = "Gạo", Alias = "gao",
            BaseUnitId = baseUnit.Id, BaseUnit = baseUnit, IsActive = true
        };
        var variant = new ProductVariant
        {
            Id = 40, StoreId = 1, ProductId = product.Id, Product = product,
            Sku = "SKU-C3-LIMIT", IsActive = true
        };
        var orderedConversion = new ProductUnitConversion
        {
            Id = 60, StoreId = 1, ProductVariantId = variant.Id, ProductVariant = variant,
            UnitId = orderedUnit.Id, Unit = orderedUnit, Factor = 0.0001m, IsActive = true
        };
        var alternateConversion = new ProductUnitConversion
        {
            Id = 61, StoreId = 1, ProductVariantId = variant.Id, ProductVariant = variant,
            UnitId = baseUnit.Id, Unit = baseUnit, Factor = 1m, IsBaseUnit = true, IsActive = true
        };
        variant.UnitConversions.Add(orderedConversion);
        variant.UnitConversions.Add(alternateConversion);
        var line = new PurchaseOrderLine
        {
            Id = 31, StoreId = 1, LineNo = 1,
            ProductVariantId = variant.Id, ProductVariant = variant,
            ProductUnitConversionId = orderedConversion.Id,
            ProductUnitConversion = orderedConversion,
            UnitId = orderedUnit.Id,
            UnitNameSnapshot = orderedUnit.Name,
            ProductNameSnapshot = product.Name,
            ConversionFactor = 0.0001m,
            OrderedQuantity = PurchaseReceiptQuantityConversionPolicy.MaximumStoredQuantity,
            ReceivedQuantity = PurchaseReceiptQuantityConversionPolicy.MaximumStoredQuantity - 0.001m
        };
        var map = typeof(PurchaseOrderService).GetMethod(
            "MapDetailLine",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("MapDetailLine not found.");

        var dto = Assert.IsType<PurchaseOrderLineDto>(map.Invoke(null, [line, false, 0m]));

        Assert.Equal(0m, dto.AllowedReceiptUnits.Single(x => x.UnitId == baseUnit.Id)
            .MaximumEntryQuantity);
    }

    [Fact]
    public void Purchase_order_dto_preserves_same_unit_fractional_entry_at_storage_boundary()
    {
        var orderedUnit = new Unit
        {
            Id = 50, StoreId = 1, Code = "HALF", Name = "nửa đơn vị gốc", IsActive = true
        };
        var product = new Product
        {
            Id = 80, StoreId = 1, Name = "Gạo", Alias = "gao",
            BaseUnitId = orderedUnit.Id, BaseUnit = orderedUnit, IsActive = true
        };
        var variant = new ProductVariant
        {
            Id = 40, StoreId = 1, ProductId = product.Id, Product = product,
            Sku = "SKU-C3-SAME-LIMIT", IsActive = true
        };
        var orderedConversion = new ProductUnitConversion
        {
            Id = 60, StoreId = 1, ProductVariantId = variant.Id, ProductVariant = variant,
            UnitId = orderedUnit.Id, Unit = orderedUnit, Factor = 0.5m,
            IsBaseUnit = true, IsActive = true
        };
        variant.UnitConversions.Add(orderedConversion);
        var line = new PurchaseOrderLine
        {
            Id = 31, StoreId = 1, LineNo = 1,
            ProductVariantId = variant.Id, ProductVariant = variant,
            ProductUnitConversionId = orderedConversion.Id,
            ProductUnitConversion = orderedConversion,
            UnitId = orderedUnit.Id,
            UnitNameSnapshot = orderedUnit.Name,
            ProductNameSnapshot = product.Name,
            ConversionFactor = 0.5m,
            OrderedQuantity = PurchaseReceiptQuantityConversionPolicy.MaximumStoredQuantity,
            ReceivedQuantity = PurchaseReceiptQuantityConversionPolicy.MaximumStoredQuantity - 0.001m
        };
        var map = typeof(PurchaseOrderService).GetMethod(
            "MapDetailLine",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("MapDetailLine not found.");

        var dto = Assert.IsType<PurchaseOrderLineDto>(map.Invoke(null, [line, false, 0m]));

        Assert.Equal(0.001m, dto.AllowedReceiptUnits.Single(x => x.UnitId == orderedUnit.Id)
            .MaximumEntryQuantity);
    }

    [Fact]
    public async Task Create_accepts_same_unit_fractional_boundary_and_rejects_next_quantity_step()
    {
        static (StockDocumentService Service, Func<StockDocument?> AddedDocument, Func<int> Rollbacks)
            CreateBoundaryService()
        {
            var order = CreateSameUnitBoundaryOrder();
            StockDocument? addedDocument = null;
            var rollbacks = 0;
            var line = order.Lines.Single();
            var repository = Proxy<IStockDocumentRepository>((method, args) => method.Name switch
            {
                nameof(IStockDocumentRepository.LockPurchasePriceHistoryVariantsAsync) => Task.FromResult(true),
                nameof(IStockDocumentRepository.BeginTransactionAsync) => Task.CompletedTask,
                nameof(IStockDocumentRepository.CommitTransactionAsync) => Task.CompletedTask,
                nameof(IStockDocumentRepository.RollbackTransactionAsync) => Rollback(() => rollbacks++),
                nameof(IStockDocumentRepository.LockPurchaseOrderForReceiptAsync) =>
                    Task.FromResult<PurchaseOrderReceiptState?>(new(
                        order.Id, order.StoreId, order.Status, order.SupplierId,
                        order.ExpectedWarehouseId, order.LegalEntityId)),
                nameof(IStockDocumentRepository.LockPurchaseOrderLinesAsync) =>
                    Task.FromResult<IReadOnlyDictionary<int, PurchaseOrderLineAllocationState>>(
                        new Dictionary<int, PurchaseOrderLineAllocationState>
                        {
                            [line.Id] = new(
                                line.Id,
                                order.StoreId,
                                line.OrderedQuantity,
                                line.ReceivedQuantity,
                                line.ShortClosedQuantity,
                                line.ConversionFactor)
                        }),
                nameof(IStockDocumentRepository.GetPurchaseOrderForReceiptAsync) =>
                    Task.FromResult<PurchaseOrder?>(order),
                nameof(IStockDocumentRepository.AddAsync) =>
                    CaptureAdded((StockDocument)args![0]!),
                nameof(IStockDocumentRepository.SaveChangesAsync) => Task.CompletedTask,
                nameof(IStockDocumentRepository.HasOtherActiveReceivingDraftAsync) => Task.FromResult(false),
                _ => throw new NotSupportedException(method.Name)
            });
            var sequence = Proxy<IDocumentNumberSequenceRepository>((method, _) =>
                method.Name == nameof(IDocumentNumberSequenceRepository.GetNextNumberAsync)
                    ? Task.FromResult(1)
                    : throw new NotSupportedException(method.Name));
            return (
                CreateServiceForOrder(repository, new RecordingMovementService(), sequence),
                () => addedDocument,
                () => rollbacks);

            Task CaptureAdded(StockDocument document)
            {
                addedDocument = document;
                return Task.CompletedTask;
            }
        }

        static CreatePurchaseReceiptRequest Request(decimal quantity) => new()
        {
            Lines =
            [
                new CreatePurchaseReceiptLineRequest
                {
                    PurchaseOrderLineId = 31,
                    ReceiptUnitId = 50,
                    Quantity = quantity
                }
            ]
        };

        var accepted = CreateBoundaryService();
        await accepted.Service.CreateReceiptFromPurchaseOrderAsync(30, Request(0.001m));

        var acceptedLine = Assert.Single(accepted.AddedDocument()!.Lines);
        Assert.Equal(0.001m, acceptedLine.Quantity);
        Assert.Equal(0.001m, acceptedLine.BaseQuantity);
        Assert.Equal(0, accepted.Rollbacks());

        var rejected = CreateBoundaryService();
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            rejected.Service.CreateReceiptFromPurchaseOrderAsync(30, Request(0.002m)));

        Assert.Contains("vượt giới hạn", error.Message);
        Assert.Null(rejected.AddedDocument());
        Assert.Equal(1, rejected.Rollbacks());
    }

    [Fact]
    public async Task Legacy_partial_receipt_policy_failure_is_safe_and_persists_nothing()
    {
        var order = CreateOrderWithAlternateCandidates(0);
        var line = order.Lines.Single();
        var addCalls = 0;
        var saveCalls = 0;
        var commitCalls = 0;
        var rollbackCalls = 0;
        var repository = Proxy<IStockDocumentRepository>((method, _) => method.Name switch
        {
            nameof(IStockDocumentRepository.LockPurchasePriceHistoryVariantsAsync) => Task.FromResult(true),
            nameof(IStockDocumentRepository.BeginTransactionAsync) => Task.CompletedTask,
            nameof(IStockDocumentRepository.CommitTransactionAsync) => Count(() => commitCalls++),
            nameof(IStockDocumentRepository.RollbackTransactionAsync) => Count(() => rollbackCalls++),
            nameof(IStockDocumentRepository.LockPurchaseOrderForReceiptAsync) =>
                Task.FromResult<PurchaseOrderReceiptState?>(new(
                    order.Id, order.StoreId, order.Status, order.SupplierId,
                    order.ExpectedWarehouseId, order.LegalEntityId)),
            nameof(IStockDocumentRepository.LockPurchaseOrderLinesAsync) =>
                Task.FromResult<IReadOnlyDictionary<int, PurchaseOrderLineAllocationState>>(
                    new Dictionary<int, PurchaseOrderLineAllocationState>
                    {
                        [line.Id] = new(
                            line.Id, order.StoreId, line.OrderedQuantity,
                            line.ReceivedQuantity, line.ShortClosedQuantity,
                            line.ConversionFactor)
                    }),
            nameof(IStockDocumentRepository.GetPurchaseOrderForReceiptAsync) =>
                Task.FromResult<PurchaseOrder?>(order),
            nameof(IStockDocumentRepository.AddAsync) => Count(() => addCalls++),
            nameof(IStockDocumentRepository.SaveChangesAsync) => Count(() => saveCalls++),
            nameof(IStockDocumentRepository.HasOtherActiveReceivingDraftAsync) => Task.FromResult(false),
            _ => throw new NotSupportedException(method.Name)
        });
        var sequence = Proxy<IDocumentNumberSequenceRepository>((method, _) =>
            method.Name == nameof(IDocumentNumberSequenceRepository.GetNextNumberAsync)
                ? Task.FromResult(1)
                : throw new NotSupportedException(method.Name));
        var movements = new RecordingMovementService();
        var service = CreateServiceForOrder(repository, movements, sequence);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.CreateReceiptFromPurchaseOrderAsync(order.Id, new CreatePurchaseReceiptRequest
            {
                Lines =
                [
                    new CreatePurchaseReceiptLineRequest
                    {
                        PurchaseOrderLineId = line.Id,
                        ReceiptUnitId = line.UnitId,
                        Quantity = 4m,
                        ShortageDisposition = PurchaseShortageDisposition.None
                    }
                ]
            }));

        Assert.Contains("phải chọn chờ giao bù hoặc đóng phần thiếu", error.Message);
        Assert.Equal(0, addCalls);
        Assert.Equal(0, saveCalls);
        Assert.Equal(0, commitCalls);
        Assert.Equal(1, rollbackCalls);
        Assert.Equal(0, movements.PreLockCalls);
        Assert.Equal(0, movements.CreateCalls);

        static Task Count(Action increment)
        {
            increment();
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Confirm_rejects_current_cancelled_po_under_parent_lock_before_posting()
    {
        var document = CreatePurchaseOrderReceipt(0m, 1m, 1m, 1m);
        var movements = new RecordingMovementService();
        var rollbackCalls = 0;
        var repository = CreateRepository(document,
            new PurchaseOrderReceiptState(
                document.PurchaseOrderId!.Value, 1, PurchaseOrderStatus.Cancelled,
                document.SupplierId!.Value, document.WarehouseId, document.Warehouse!.LegalEntityId),
            () => rollbackCalls++);
        var service = CreateService(document, repository, movements);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => service.ApproveAsync(
            document.Id, null, Convert.ToBase64String(document.RowVersion)));

        Assert.Contains("không còn ở trạng thái", error.Message);
        Assert.Equal(0, movements.PreLockCalls);
        Assert.Equal(0, movements.CreateCalls);
        Assert.Equal(1, rollbackCalls);
    }

    [Fact]
    public async Task Confirm_rejects_cumulative_fractional_drift_before_posting()
    {
        // Simulate a receipt loaded before another confirmation committed.
        // The tracked PO says zero, while the durable line lock returns 0.001.
        var document = CreatePurchaseOrderReceipt(0m, 0.001m, 1.5m, 0.002m);
        var movements = new RecordingMovementService();
        var rollbackCalls = 0;
        var repository = CreateRepository(document,
            new PurchaseOrderReceiptState(
                document.PurchaseOrderId!.Value, 1, PurchaseOrderStatus.PartiallyReceived,
                document.SupplierId!.Value, document.WarehouseId, document.Warehouse!.LegalEntityId),
            () => rollbackCalls++,
            lockedReceivedQuantity: 0.001m);
        var service = CreateService(document, repository, movements);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => service.ApproveAsync(
            document.Id, null, Convert.ToBase64String(document.RowVersion)));

        Assert.Contains("tích lũy", error.Message);
        Assert.Equal(0, movements.PreLockCalls);
        Assert.Equal(0, movements.CreateCalls);
        Assert.Equal(1, rollbackCalls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(1)]
    public async Task Create_fails_closed_for_missing_ambiguous_or_foreign_conversion(int mode)
    {
        var order = CreateOrderWithAlternateCandidates(mode);
        var repository = Proxy<IStockDocumentRepository>((method, _) => method.Name switch
        {
            nameof(IStockDocumentRepository.LockPurchasePriceHistoryVariantsAsync) => Task.FromResult(true),
            nameof(IStockDocumentRepository.BeginTransactionAsync) => Task.CompletedTask,
            nameof(IStockDocumentRepository.RollbackTransactionAsync) => Task.CompletedTask,
            nameof(IStockDocumentRepository.LockPurchaseOrderForReceiptAsync) =>
                Task.FromResult<PurchaseOrderReceiptState?>(new(
                    order.Id, order.StoreId, order.Status, order.SupplierId,
                    order.ExpectedWarehouseId, order.LegalEntityId)),
            nameof(IStockDocumentRepository.LockPurchaseOrderLinesAsync) =>
                Task.FromResult<IReadOnlyDictionary<int, PurchaseOrderLineAllocationState>>(
                    new Dictionary<int, PurchaseOrderLineAllocationState>
                    {
                        [order.Lines.Single().Id] = new(
                            order.Lines.Single().Id, 1, 10m, 0m, 0m, 12m)
                    }),
            nameof(IStockDocumentRepository.GetPurchaseOrderForReceiptAsync) =>
                Task.FromResult<PurchaseOrder?>(order),
            nameof(IStockDocumentRepository.GetInFlightPurchaseReceiptQuantitiesAsync) =>
                Task.FromResult<IReadOnlyDictionary<int, decimal>>(new Dictionary<int, decimal>()),
            nameof(IStockDocumentRepository.HasOtherActiveReceivingDraftAsync) => Task.FromResult(false),
            _ => throw new NotSupportedException(method.Name)
        });
        var sequence = Proxy<IDocumentNumberSequenceRepository>((method, _) =>
            method.Name == nameof(IDocumentNumberSequenceRepository.GetNextNumberAsync)
                ? Task.FromResult(1)
                : throw new NotSupportedException(method.Name));
        var movements = new RecordingMovementService();
        var service = CreateServiceForOrder(repository, movements, sequence);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.CreateReceiptFromPurchaseOrderAsync(order.Id, new CreatePurchaseReceiptRequest
            {
                Lines =
                [
                    new CreatePurchaseReceiptLineRequest
                    {
                        PurchaseOrderLineId = order.Lines.Single().Id,
                        ReceiptUnitId = 51,
                        Quantity = 12m,
                        ShortageDisposition = PurchaseShortageDisposition.WaitForBackorder
                    }
                ]
            }));

        Assert.Equal("Không tìm thấy quy đổi hợp lệ cho đơn vị nhận.", error.Message);
        Assert.Equal(0, movements.CreateCalls);
    }

    [Fact]
    public async Task Confirm_uses_saved_alternate_snapshot_and_retry_is_no_op()
    {
        var document = CreateAlternateSnapshotReceipt();
        var movements = new RecordingMovementService();
        var repository = Proxy<IStockDocumentRepository>((method, _) => method.Name switch
        {
            nameof(IStockDocumentRepository.GetForConfirmAsync) => Task.FromResult<StockDocument?>(document),
            nameof(IStockDocumentRepository.GetSupplierAsync) => Task.FromResult<Supplier?>(document.Supplier),
            nameof(IStockDocumentRepository.LockPurchasePriceHistoryVariantsAsync) => Task.FromResult(true),
            nameof(IStockDocumentRepository.BeginTransactionAsync) => Task.CompletedTask,
            nameof(IStockDocumentRepository.CommitTransactionAsync) => Task.CompletedTask,
            nameof(IStockDocumentRepository.RollbackTransactionAsync) => Task.CompletedTask,
            nameof(IStockDocumentRepository.LockPurchaseOrderForReceiptAsync) =>
                Task.FromResult<PurchaseOrderReceiptState?>(new(
                    document.PurchaseOrderId!.Value, 1, PurchaseOrderStatus.Approved,
                    document.SupplierId!.Value, document.WarehouseId, document.Warehouse!.LegalEntityId)),
            nameof(IStockDocumentRepository.LockPurchaseOrderLinesAsync) =>
                Task.FromResult<IReadOnlyDictionary<int, PurchaseOrderLineAllocationState>>(
                    new Dictionary<int, PurchaseOrderLineAllocationState>
                    {
                        [31] = new(31, 1, 10m, 0m, 0m, 12m)
                    }),
            nameof(IStockDocumentRepository.GetInFlightPurchaseReceiptQuantitiesAsync) =>
                Task.FromResult<IReadOnlyDictionary<int, decimal>>(new Dictionary<int, decimal>()),
            nameof(IStockDocumentRepository.PurchaseReceiptLineSnapshotsBelongToStoreAsync) => Task.FromResult(true),
            nameof(IStockDocumentRepository.MarkVariantsHasInputInvoiceAsync) => Task.CompletedTask,
            nameof(IStockDocumentRepository.PurchasePayableExistsAsync) => Task.FromResult(false),
            nameof(IStockDocumentRepository.AddPurchasePayableAsync) => Task.CompletedTask,
            nameof(IStockDocumentRepository.SaveChangesAsync) => Task.CompletedTask,
            nameof(IStockDocumentRepository.HasUnresolvedProvisionalItemsAsync) => Task.FromResult(false),
            _ => throw new NotSupportedException(method.Name)
        });
        var valuation = Proxy<IInventoryValuationEntryRepository>((method, _) =>
            method.Name == nameof(IInventoryValuationEntryRepository.GetByReferenceAsync)
                ? Task.FromResult(new List<InventoryValuationEntry>())
                : throw new NotSupportedException(method.Name));
        var service = CreateService(document, repository, movements, valuation);

        await service.ApproveAsync(document.Id, null, Convert.ToBase64String(document.RowVersion));
        await service.ApproveAsync(document.Id, "retry", null);

        Assert.Equal(StockDocumentStatus.Confirmed, document.Status);
        Assert.Equal(3m, document.PurchaseOrder!.Lines.Single().ReceivedQuantity);
        Assert.Single(movements.Requests);
        Assert.Equal(36m, movements.Requests.Single().QuantityChange);
    }

    [Fact]
    public async Task Same_unit_fractional_factor_preserves_saved_ordered_quantity()
    {
        var document = CreatePurchaseOrderReceipt(0m, 0.001m, 0.5m, 0.001m);
        var movements = new RecordingMovementService();
        var repository = CreateSuccessfulConfirmRepository(document);
        var valuation = Proxy<IInventoryValuationEntryRepository>((method, _) =>
            method.Name == nameof(IInventoryValuationEntryRepository.GetByReferenceAsync)
                ? Task.FromResult(new List<InventoryValuationEntry>())
                : throw new NotSupportedException(method.Name));
        var service = CreateService(document, repository, movements, valuation);

        await service.ApproveAsync(document.Id, null, Convert.ToBase64String(document.RowVersion));

        Assert.Equal(0.001m, document.PurchaseOrder!.Lines.Single().ReceivedQuantity);
        Assert.Single(movements.Requests);
        Assert.Equal(0.001m, movements.Requests.Single().QuantityChange);
    }

    [Fact]
    public async Task Confirm_rejects_overdelivery_without_explicit_manager_acceptance_before_posting()
    {
        var document = CreatePurchaseOrderReceipt(9m, 2m, 1m, 2m);
        var movements = new RecordingMovementService();
        var repository = CreateSuccessfulConfirmRepository(document);
        var service = CreateService(document, repository, movements);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => service.ApproveAsync(
            document.Id,
            null,
            Convert.ToBase64String(document.RowVersion)));

        Assert.Contains("phải xác nhận chấp nhận nhận vượt", error.Message);
        Assert.Equal(0, movements.PreLockCalls);
        Assert.Equal(0, movements.CreateCalls);
        Assert.Equal(9m, document.PurchaseOrder!.Lines.Single().ReceivedQuantity);
        Assert.Equal(StockDocumentStatus.PendingApproval, document.Status);
    }

    [Fact]
    public async Task Confirm_posts_overdelivery_after_explicit_acceptance_and_records_exact_audit_evidence()
    {
        var document = CreatePurchaseOrderReceipt(9m, 2m, 1m, 2m);
        var movements = new RecordingMovementService();
        var repository = CreateSuccessfulConfirmRepository(document);
        var valuation = Proxy<IInventoryValuationEntryRepository>((method, _) =>
            method.Name == nameof(IInventoryValuationEntryRepository.GetByReferenceAsync)
                ? Task.FromResult(new List<InventoryValuationEntry>())
                : throw new NotSupportedException(method.Name));
        var service = CreateService(document, repository, movements, valuation);

        await service.ApproveAsync(
            document.Id,
            "Đã đối chiếu",
            Convert.ToBase64String(document.RowVersion),
            acceptOverdelivery: true,
            overdeliveryNote: null);

        Assert.Equal(StockDocumentStatus.Confirmed, document.Status);
        Assert.Equal(11m, document.PurchaseOrder!.Lines.Single().ReceivedQuantity);
        Assert.Single(movements.Requests);
        Assert.Equal(2m, movements.Requests.Single().QuantityChange);
        var audit = PurchaseReceiptAuditEvidence.GetWorkflowIntent(document);
        Assert.NotNull(audit);
        Assert.Null(audit.Reason);
        Assert.Contains("lượng vượt tăng 1 base", audit.Note);
        Assert.Contains("tổng lượng vượt sau duyệt 1 base", audit.Note);
    }

    [Fact]
    public async Task Alternate_receipt_unit_overdelivery_updates_ordered_unit_equivalent_only_after_acceptance()
    {
        var document = CreateAlternateSnapshotReceipt();
        var orderLine = document.PurchaseOrder!.Lines.Single();
        orderLine.ReceivedQuantity = 9m;
        var receiptLine = document.Lines.Single();
        receiptLine.Quantity = 24m;
        receiptLine.BaseQuantity = 24m;
        receiptLine.LineTotal = 240m;
        var movements = new RecordingMovementService();
        var repository = CreateSuccessfulConfirmRepository(document);
        var valuation = Proxy<IInventoryValuationEntryRepository>((method, _) =>
            method.Name == nameof(IInventoryValuationEntryRepository.GetByReferenceAsync)
                ? Task.FromResult(new List<InventoryValuationEntry>())
                : throw new NotSupportedException(method.Name));
        var service = CreateService(document, repository, movements, valuation);

        await service.ApproveAsync(
            document.Id,
            null,
            Convert.ToBase64String(document.RowVersion),
            acceptOverdelivery: true,
            overdeliveryNote: "NCC giao nguyên kiện");

        Assert.Equal(11m, orderLine.ReceivedQuantity);
        Assert.Equal(24m, movements.Requests.Single().QuantityChange);
        var audit = PurchaseReceiptAuditEvidence.GetWorkflowIntent(document);
        Assert.Equal("NCC giao nguyên kiện", audit?.Reason);
        Assert.Contains("12 base", audit?.Note);
    }

    [Fact]
    public async Task Existing_draft_can_be_confirmed_as_overdelivery_after_another_receipt_completed_the_order()
    {
        var document = CreatePurchaseOrderReceipt(10m, 1m, 1m, 1m);
        document.PurchaseOrder!.Status = PurchaseOrderStatus.FullyReceived;
        var movements = new RecordingMovementService();
        var repository = CreateSuccessfulConfirmRepository(document);
        var valuation = Proxy<IInventoryValuationEntryRepository>((method, _) =>
            method.Name == nameof(IInventoryValuationEntryRepository.GetByReferenceAsync)
                ? Task.FromResult(new List<InventoryValuationEntry>())
                : throw new NotSupportedException(method.Name));
        var service = CreateService(document, repository, movements, valuation);

        await service.ApproveAsync(
            document.Id,
            null,
            Convert.ToBase64String(document.RowVersion),
            acceptOverdelivery: true,
            overdeliveryNote: null);

        Assert.Equal(11m, document.PurchaseOrder.Lines.Single().ReceivedQuantity);
        Assert.Equal(PurchaseOrderStatus.FullyReceived, document.PurchaseOrder.Status);
        Assert.Single(movements.Requests);
    }

    [Fact]
    public async Task Confirm_recomputes_overdelivery_from_locked_state_instead_of_stale_tracked_quantity()
    {
        // The browser-loaded graph sees 9 + 1 = 10 (no overdelivery). Another
        // confirmation commits first, so the durable lock now reports 10.
        var document = CreatePurchaseOrderReceipt(9m, 1m, 1m, 1m);
        var movements = new RecordingMovementService();
        var repository = CreateSuccessfulConfirmRepository(
            document,
            lockedReceivedQuantity: 10m,
            lockedStatus: PurchaseOrderStatus.FullyReceived);
        var valuation = Proxy<IInventoryValuationEntryRepository>((method, _) =>
            method.Name == nameof(IInventoryValuationEntryRepository.GetByReferenceAsync)
                ? Task.FromResult(new List<InventoryValuationEntry>())
                : throw new NotSupportedException(method.Name));
        var service = CreateService(document, repository, movements, valuation);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => service.ApproveAsync(
            document.Id,
            null,
            Convert.ToBase64String(document.RowVersion)));

        Assert.Contains("phải xác nhận chấp nhận nhận vượt", error.Message);
        Assert.Empty(movements.Requests);

        await service.ApproveAsync(
            document.Id,
            null,
            Convert.ToBase64String(document.RowVersion),
            acceptOverdelivery: true,
            overdeliveryNote: null);

        Assert.Equal(11m, document.PurchaseOrder!.Lines.Single().ReceivedQuantity);
        Assert.Single(movements.Requests);
    }

    private static StockDocument CreatePurchaseOrderReceipt(
        decimal receivedQuantity,
        decimal receiptQuantity,
        decimal factor,
        decimal canonicalQuantity)
    {
        var legalEntity = new LegalEntity
        {
            Id = 5, StoreId = 1, Code = "LE", Name = "HKD", LegalName = "HKD", IsActive = true
        };
        var warehouse = new Warehouse
        {
            Id = 10, StoreId = 1, Code = "WH", Name = "Kho", IsActive = true,
            LegalEntityId = legalEntity.Id, LegalEntity = legalEntity
        };
        var supplier = new Supplier
        {
            Id = 20, StoreId = 1, Code = "SUP", Name = "NCC", IsActive = true
        };
        var order = new PurchaseOrder
        {
            Id = 30, StoreId = 1, OrderNumber = "PO-C2", Status = PurchaseOrderStatus.Approved,
            SupplierId = supplier.Id, Supplier = supplier, ExpectedWarehouseId = warehouse.Id,
            ExpectedWarehouse = warehouse, LegalEntityId = legalEntity.Id, LegalEntity = legalEntity
        };
        var orderLine = new PurchaseOrderLine
        {
            Id = 31, StoreId = 1, PurchaseOrderId = order.Id, PurchaseOrder = order,
            LineNo = 1, ProductVariantId = 40, ProductNameSnapshot = "Gạo",
            UnitId = 50, ProductUnitConversionId = 60, UnitNameSnapshot = "thùng",
            ConversionFactor = factor, OrderedQuantity = 10m, ReceivedQuantity = receivedQuantity
        };
        order.Lines.Add(orderLine);
        var document = new StockDocument
        {
            Id = 70, StoreId = 1, DocumentNo = "NK-C2", Type = StockDocumentType.Receipt,
            Status = StockDocumentStatus.PendingApproval, ReceiptSource = PurchaseReceiptSource.PurchaseOrder,
            PurchaseOrderId = order.Id, PurchaseOrder = order, WarehouseId = warehouse.Id,
            Warehouse = warehouse, SupplierId = supplier.Id, Supplier = supplier,
            RowVersion = [1, 2, 3]
        };
        document.Lines.Add(new StockDocumentLine
        {
            Id = 71, StockDocumentId = document.Id, StockDocument = document, LineNo = 1,
            ProductVariantId = 40, PurchaseOrderLineId = orderLine.Id, PurchaseOrderLine = orderLine,
            UnitId = 50, ProductUnitConversionId = 60, UnitNameSnapshot = "thùng",
            Quantity = receiptQuantity, Factor = factor, BaseQuantity = canonicalQuantity,
            UnitCost = 10m, UnitPriceBeforeVat = 10m, UnitPriceAfterVat = 10m,
            LineTotal = PurchasePricingPolicy.RoundMoney(receiptQuantity * 10m),
            ProductNameSnapshot = "Gạo", ShortageDisposition = PurchaseShortageDisposition.WaitForBackorder
        });
        return document;
    }

    private static PurchaseOrder CreateOrderWithAlternateCandidates(int mode)
    {
        var orderedUnit = new Unit { Id = 50, StoreId = 1, Code = "BOX", Name = "thùng", IsActive = true };
        var alternateUnit = new Unit { Id = 51, StoreId = 1, Code = "BOTTLE", Name = "chai", IsActive = true };
        var product = new Product
        {
            Id = 80, StoreId = 1, Name = "Gạo", Alias = "gao", BaseUnitId = alternateUnit.Id,
            BaseUnit = alternateUnit, IsActive = true
        };
        var variant = new ProductVariant
        {
            Id = 40, StoreId = 1, ProductId = product.Id, Product = product,
            Sku = "SKU-C2", IsActive = true
        };
        var orderedConversion = new ProductUnitConversion
        {
            Id = 60, StoreId = 1, ProductVariantId = variant.Id, ProductVariant = variant,
            UnitId = orderedUnit.Id, Unit = orderedUnit, Factor = 12m, IsActive = true
        };
        variant.UnitConversions.Add(orderedConversion);
        var candidateCount = mode == 2 ? 2 : mode == 1 ? 1 : 0;
        for (var index = 0; index < candidateCount; index++)
        {
            variant.UnitConversions.Add(new ProductUnitConversion
            {
                Id = 61 + index, StoreId = mode == 1 ? 2 : 1,
                ProductVariantId = variant.Id, ProductVariant = variant,
                UnitId = alternateUnit.Id, Unit = alternateUnit, Factor = 1m, IsActive = true
            });
        }
        var order = new PurchaseOrder
        {
            Id = 30, StoreId = 1, OrderNumber = "PO-ALT", Status = PurchaseOrderStatus.Approved,
            SupplierId = 20, ExpectedWarehouseId = 10, LegalEntityId = 5
        };
        order.Lines.Add(new PurchaseOrderLine
        {
            Id = 31, StoreId = 1, PurchaseOrderId = order.Id, PurchaseOrder = order,
            LineNo = 1, ProductVariantId = variant.Id, ProductVariant = variant,
            ProductUnitConversionId = orderedConversion.Id, ProductUnitConversion = orderedConversion,
            UnitId = orderedUnit.Id, UnitNameSnapshot = orderedUnit.Name,
            ConversionFactor = 12m, OrderedQuantity = 10m, ProductNameSnapshot = product.Name
        });
        return order;
    }

    private static PurchaseOrder CreateSameUnitBoundaryOrder()
    {
        var orderedUnit = new Unit
        {
            Id = 50, StoreId = 1, Code = "HALF", Name = "nửa đơn vị gốc", IsActive = true
        };
        var product = new Product
        {
            Id = 80, StoreId = 1, Name = "Gạo", Alias = "gao",
            BaseUnitId = orderedUnit.Id, BaseUnit = orderedUnit, IsActive = true
        };
        var variant = new ProductVariant
        {
            Id = 40, StoreId = 1, ProductId = product.Id, Product = product,
            Sku = "SKU-C3-SAME-CREATE", IsActive = true
        };
        var conversion = new ProductUnitConversion
        {
            Id = 60, StoreId = 1, ProductVariantId = variant.Id, ProductVariant = variant,
            UnitId = orderedUnit.Id, Unit = orderedUnit, Factor = 0.5m,
            IsBaseUnit = true, IsActive = true
        };
        variant.UnitConversions.Add(conversion);
        var order = new PurchaseOrder
        {
            Id = 30, StoreId = 1, OrderNumber = "PO-C3-SAME-LIMIT",
            Status = PurchaseOrderStatus.PartiallyReceived,
            SupplierId = 20, ExpectedWarehouseId = 10, LegalEntityId = 5
        };
        order.Lines.Add(new PurchaseOrderLine
        {
            Id = 31, StoreId = 1, PurchaseOrderId = order.Id, PurchaseOrder = order,
            LineNo = 1, ProductVariantId = variant.Id, ProductVariant = variant,
            ProductUnitConversionId = conversion.Id, ProductUnitConversion = conversion,
            UnitId = orderedUnit.Id, UnitNameSnapshot = orderedUnit.Name,
            ConversionFactor = conversion.Factor,
            OrderedQuantity = PurchaseReceiptQuantityConversionPolicy.MaximumStoredQuantity,
            ReceivedQuantity = PurchaseReceiptQuantityConversionPolicy.MaximumStoredQuantity - 0.001m,
            ProductNameSnapshot = product.Name
        });
        return order;
    }

    private static StockDocument CreateAlternateSnapshotReceipt()
    {
        var document = CreatePurchaseOrderReceipt(0m, 36m, 1m, 36m);
        var orderLine = document.PurchaseOrder!.Lines.Single();
        orderLine.ConversionFactor = 12m;
        orderLine.UnitId = 50;
        orderLine.ProductUnitConversionId = 60;
        var line = document.Lines.Single();
        line.UnitId = 51;
        line.ProductUnitConversionId = 61;
        line.Factor = 1m;
        line.Quantity = 36m;
        line.BaseQuantity = 36m;
        line.UnitCost = 10m;
        line.UnitPriceBeforeVat = 10m;
        line.UnitPriceAfterVat = 10m;
        line.LineTotal = 360m;
        // The current master may now say something else; confirmation must use
        // the durable Factor/BaseQuantity snapshot above.
        line.ProductUnitConversion = new ProductUnitConversion
        {
            Id = 61, StoreId = 1, ProductVariantId = line.ProductVariantId,
            UnitId = 51, Factor = 99m, IsActive = true
        };
        return document;
    }

    private static IStockDocumentRepository CreateRepository(
        StockDocument document,
        PurchaseOrderReceiptState orderState,
        Action rollback,
        decimal? lockedReceivedQuantity = null)
    {
        var orderLine = document.PurchaseOrder!.Lines.Single();
        var proxy = DispatchProxy.Create<IStockDocumentRepository, DelegateProxy>();
        ((DelegateProxy)(object)proxy).Handler = (method, _) => method.Name switch
        {
            nameof(IStockDocumentRepository.GetForConfirmAsync) => Task.FromResult<StockDocument?>(document),
            nameof(IStockDocumentRepository.GetSupplierAsync) => Task.FromResult<Supplier?>(document.Supplier),
            nameof(IStockDocumentRepository.LockPurchasePriceHistoryVariantsAsync) => Task.FromResult(true),
            nameof(IStockDocumentRepository.BeginTransactionAsync) => Task.CompletedTask,
            nameof(IStockDocumentRepository.RollbackTransactionAsync) => Rollback(rollback),
            nameof(IStockDocumentRepository.LockPurchaseOrderForReceiptAsync) =>
                Task.FromResult<PurchaseOrderReceiptState?>(orderState),
            nameof(IStockDocumentRepository.LockPurchaseOrderLinesAsync) =>
                Task.FromResult<IReadOnlyDictionary<int, PurchaseOrderLineAllocationState>>(
                    new Dictionary<int, PurchaseOrderLineAllocationState>
                    {
                        [31] = new(31, 1, 10m,
                            lockedReceivedQuantity ?? orderLine.ReceivedQuantity,
                            0m, orderLine.ConversionFactor)
                    }),
            nameof(IStockDocumentRepository.GetInFlightPurchaseReceiptQuantitiesAsync) =>
                Task.FromResult<IReadOnlyDictionary<int, decimal>>(new Dictionary<int, decimal>()),
            nameof(IStockDocumentRepository.PurchaseReceiptLineSnapshotsBelongToStoreAsync) =>
                Task.FromResult(true),
            nameof(IStockDocumentRepository.HasUnresolvedProvisionalItemsAsync) => Task.FromResult(false),
            _ => throw new NotSupportedException(method.Name)
        };
        return proxy;
    }

    private static IStockDocumentRepository CreateSuccessfulConfirmRepository(
        StockDocument document,
        decimal? lockedReceivedQuantity = null,
        PurchaseOrderStatus? lockedStatus = null)
    {
        var order = document.PurchaseOrder!;
        var orderLine = order.Lines.Single();
        return Proxy<IStockDocumentRepository>((method, _) => method.Name switch
        {
            nameof(IStockDocumentRepository.GetForConfirmAsync) => Task.FromResult<StockDocument?>(document),
            nameof(IStockDocumentRepository.GetSupplierAsync) => Task.FromResult<Supplier?>(document.Supplier),
            nameof(IStockDocumentRepository.LockPurchasePriceHistoryVariantsAsync) => Task.FromResult(true),
            nameof(IStockDocumentRepository.BeginTransactionAsync) => Task.CompletedTask,
            nameof(IStockDocumentRepository.CommitTransactionAsync) => Task.CompletedTask,
            nameof(IStockDocumentRepository.RollbackTransactionAsync) => Task.CompletedTask,
            nameof(IStockDocumentRepository.LockPurchaseOrderForReceiptAsync) =>
                Task.FromResult<PurchaseOrderReceiptState?>(new(
                    order.Id, order.StoreId, lockedStatus ?? order.Status, order.SupplierId,
                    order.ExpectedWarehouseId, order.LegalEntityId)),
            nameof(IStockDocumentRepository.LockPurchaseOrderLinesAsync) =>
                Task.FromResult<IReadOnlyDictionary<int, PurchaseOrderLineAllocationState>>(
                    new Dictionary<int, PurchaseOrderLineAllocationState>
                    {
                        [orderLine.Id] = new(
                            orderLine.Id, orderLine.LineNo, orderLine.OrderedQuantity,
                            lockedReceivedQuantity ?? orderLine.ReceivedQuantity,
                            orderLine.ShortClosedQuantity,
                            orderLine.ConversionFactor)
                    }),
            nameof(IStockDocumentRepository.GetInFlightPurchaseReceiptQuantitiesAsync) =>
                Task.FromResult<IReadOnlyDictionary<int, decimal>>(new Dictionary<int, decimal>()),
            nameof(IStockDocumentRepository.PurchaseReceiptLineSnapshotsBelongToStoreAsync) => Task.FromResult(true),
            nameof(IStockDocumentRepository.MarkVariantsHasInputInvoiceAsync) => Task.CompletedTask,
            nameof(IStockDocumentRepository.PurchasePayableExistsAsync) => Task.FromResult(false),
            nameof(IStockDocumentRepository.AddPurchasePayableAsync) => Task.CompletedTask,
            nameof(IStockDocumentRepository.SaveChangesAsync) => Task.CompletedTask,
            nameof(IStockDocumentRepository.HasUnresolvedProvisionalItemsAsync) => Task.FromResult(false),
            _ => throw new NotSupportedException(method.Name)
        });
    }

    private static Task Rollback(Action rollback)
    {
        rollback();
        return Task.CompletedTask;
    }

    private static StockDocumentService CreateService(
        StockDocument document,
        IStockDocumentRepository repository,
        RecordingMovementService movements,
        IInventoryValuationEntryRepository? valuation = null)
        => new(
            repository,
            Unused<ILegalEntityRepository>(),
            Proxy<IWarehouseRepository>((method, _) => method.Name == nameof(IWarehouseRepository.GetByIdAsync)
                ? Task.FromResult<Warehouse?>(document.Warehouse)
                : throw new NotSupportedException(method.Name)),
            Unused<IBarcodeLookupService>(),
            Unused<IInventoryUnitResolver>(),
            movements,
            new InventoryMovementFactory(),
            Unused<IInventoryRevaluationService>(),
            Unused<IDocumentNumberSequenceRepository>(),
            new TenantStub(),
            valuation ?? Unused<IInventoryValuationEntryRepository>(),
            new CurrentUserStub());

    private static StockDocumentService CreateServiceForOrder(
        IStockDocumentRepository repository,
        RecordingMovementService movements,
        IDocumentNumberSequenceRepository sequence)
        => new(
            repository,
            Unused<ILegalEntityRepository>(),
            Unused<IWarehouseRepository>(),
            Unused<IBarcodeLookupService>(),
            Unused<IInventoryUnitResolver>(),
            movements,
            new InventoryMovementFactory(),
            Unused<IInventoryRevaluationService>(),
            sequence,
            new TenantStub(),
            Unused<IInventoryValuationEntryRepository>(),
            new CurrentUserStub());

    private static T Unused<T>() where T : class
        => Proxy<T>((method, _) => throw new NotSupportedException(method.Name));

    private static T Proxy<T>(Func<MethodInfo, object?[]?, object?> handler) where T : class
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

    private sealed class RecordingMovementService : IInventoryMovementService
    {
        public int PreLockCalls { get; private set; }
        public int CreateCalls { get; private set; }
        public List<CreateInventoryMovementRequest> Requests { get; } = [];
        public Task PreLockBalancesAsync(IEnumerable<InventoryPostingLockKey> keys, CancellationToken ct = default)
        {
            PreLockCalls++;
            return Task.CompletedTask;
        }
        public Task<InventoryMovementResultDto> CreateAsync(CreateInventoryMovementRequest request, CancellationToken ct = default)
        {
            CreateCalls++;
            Requests.Add(request);
            return Task.FromResult(new InventoryMovementResultDto { IsCreated = true });
        }
        public Task<decimal> PeekOutboundUnitCostAsync(int warehouseId, int productVariantId, decimal quantity, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class TenantStub : ITenantContext
    {
        public int? StoreId => 1;
        public bool IsHostAdmin => false;
        public string? Subdomain => "c2";
    }

    private sealed class CurrentUserStub : ICurrentUser
    {
        public int? UserId => 9;
        public string? UserName => "c2";
        public int? TerminalId => null;
        public string? TerminalCode => null;
        public bool IsAuthenticated => true;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "GaoApp.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
