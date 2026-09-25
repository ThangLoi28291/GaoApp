using System.Reflection;
using FluentAssertions;
using GaoApp.Application.Common;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.DTOs.Purchases;
using GaoApp.Application.Interfaces.Services.Products;
using GaoApp.Application.Services.Inventory;
using GaoApp.Application.Services.Purchases;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Repositories.Inventory;
using GaoApp.Infrastructure.Repositories.LegalEntities;
using GaoApp.Infrastructure.Repositories.Purchases;
using GaoApp.Tests.Configuration;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Purchases;

[Collection("R1FinalDatabasePreflight")]
public sealed class PurchaseToReceiptFinalCoverageSqlServerTests
{
    [Fact]
    public async Task T1_three_receipts_complete_one_two_line_purchase_order_without_double_counting()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync();
        var catalog = await SeedCatalogAsync(database);
        var order = await CreateApprovedPurchaseOrderAsync(
            database,
            catalog,
            new PurchaseOrderInput(catalog.FirstVariantId, catalog.FirstConversionId, 100m),
            new PurchaseOrderInput(catalog.SecondVariantId, catalog.SecondConversionId, 50m));

        var receipt1 = await ConfirmReceiptAsync(
            database,
            catalog,
            order.PurchaseOrderId,
            expectedLastPrice: null,
            new ReceiptInput(order.LineIds[0], 30m, PurchaseShortageDisposition.WaitForBackorder),
            new ReceiptInput(order.LineIds[1], 20m, PurchaseShortageDisposition.WaitForBackorder));

        await AssertPurchaseOrderStateAsync(
            database,
            catalog.StoreId,
            order.PurchaseOrderId,
            PurchaseOrderStatus.PartiallyReceived,
            new ExpectedOrderLine(100m, 30m, 70m, PurchaseOrderLineReceiptStatus.PartiallyReceived),
            new ExpectedOrderLine(50m, 20m, 30m, PurchaseOrderLineReceiptStatus.PartiallyReceived));
        var immutableReceipt1 = await CaptureReceiptAsync(database, catalog.StoreId, receipt1.ReceiptId);

        var receipt2 = await ConfirmReceiptAsync(
            database,
            catalog,
            order.PurchaseOrderId,
            expectedLastPrice: 10m,
            new ReceiptInput(order.LineIds[0], 40m, PurchaseShortageDisposition.WaitForBackorder),
            new ReceiptInput(order.LineIds[1], 30m, PurchaseShortageDisposition.None));

        await AssertPurchaseOrderStateAsync(
            database,
            catalog.StoreId,
            order.PurchaseOrderId,
            PurchaseOrderStatus.PartiallyReceived,
            new ExpectedOrderLine(100m, 70m, 30m, PurchaseOrderLineReceiptStatus.PartiallyReceived),
            new ExpectedOrderLine(50m, 50m, 0m, PurchaseOrderLineReceiptStatus.FullyReceived));
        (await CaptureReceiptAsync(database, catalog.StoreId, receipt1.ReceiptId))
            .Should().BeEquivalentTo(immutableReceipt1, options => options.WithStrictOrdering());
        var immutableReceipt2 = await CaptureReceiptAsync(database, catalog.StoreId, receipt2.ReceiptId);

        var receipt3 = await ConfirmReceiptAsync(
            database,
            catalog,
            order.PurchaseOrderId,
            expectedLastPrice: 10m,
            new ReceiptInput(order.LineIds[0], 30m, PurchaseShortageDisposition.None));

        await AssertPurchaseOrderStateAsync(
            database,
            catalog.StoreId,
            order.PurchaseOrderId,
            PurchaseOrderStatus.FullyReceived,
            new ExpectedOrderLine(100m, 100m, 0m, PurchaseOrderLineReceiptStatus.FullyReceived),
            new ExpectedOrderLine(50m, 50m, 0m, PurchaseOrderLineReceiptStatus.FullyReceived));
        (await CaptureReceiptAsync(database, catalog.StoreId, receipt1.ReceiptId))
            .Should().BeEquivalentTo(immutableReceipt1, options => options.WithStrictOrdering());
        (await CaptureReceiptAsync(database, catalog.StoreId, receipt2.ReceiptId))
            .Should().BeEquivalentTo(immutableReceipt2, options => options.WithStrictOrdering());

        await using var verify = CreateContext(database, catalog.StoreId);
        var receiptIds = new[] { receipt1.ReceiptId, receipt2.ReceiptId, receipt3.ReceiptId };
        receiptIds.Should().OnlyHaveUniqueItems();
        var receipts = await verify.StockDocuments
            .AsNoTracking()
            .Where(x => receiptIds.Contains(x.Id))
            .OrderBy(x => x.Id)
            .ToListAsync();
        receipts.Should().HaveCount(3);
        receipts.Should().OnlyContain(x =>
            x.PurchaseOrderId == order.PurchaseOrderId &&
            x.ReceiptSource == PurchaseReceiptSource.PurchaseOrder &&
            x.Status == StockDocumentStatus.Confirmed);

        foreach (var receiptId in receiptIds)
        {
            var auditTypes = await verify.PurchaseReceiptAuditEvents
                .AsNoTracking()
                .Where(x => x.StockDocumentId == receiptId)
                .Select(x => x.EventType)
                .ToListAsync();
            auditTypes.Should().Contain(PurchaseReceiptAuditEventType.ReceiptCreated);
            auditTypes.Should().Contain(PurchaseReceiptAuditEventType.SubmittedForApproval);
            auditTypes.Should().Contain(PurchaseReceiptAuditEventType.CommercialApprovalConfirmed);
        }

        var postingIdentities = await verify.InventoryTransactions
            .AsNoTracking()
            .Where(x =>
                x.ReferenceType == InventoryReferenceType.StockDocument &&
                receiptIds.Select(id => id.ToString()).Contains(x.ReferenceId!))
            .Select(x => new { x.ReferenceId, x.ReferenceLineId, x.IdempotencyKey })
            .ToListAsync();
        postingIdentities.Should().HaveCount(5);
        postingIdentities.Should().OnlyContain(x =>
            x.ReferenceLineId.HasValue && x.IdempotencyKey != null && x.IdempotencyKey.Length > 0);
        postingIdentities
            .Select(x => $"{x.ReferenceId}:{x.ReferenceLineId}")
            .Should().OnlyHaveUniqueItems();
        postingIdentities.Select(x => x.ReferenceId).Distinct().Should().HaveCount(3);
    }

    [Fact]
    public async Task T2_cross_purchase_order_line_is_rejected_without_partial_receipt_or_posting()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync();
        var catalog = await SeedCatalogAsync(database);
        var orderA = await CreateApprovedPurchaseOrderAsync(
            database,
            catalog,
            new PurchaseOrderInput(catalog.FirstVariantId, catalog.FirstConversionId, 10m));
        var orderB = await CreateApprovedPurchaseOrderAsync(
            database,
            catalog,
            new PurchaseOrderInput(catalog.SecondVariantId, catalog.SecondConversionId, 10m));

        var receiptAId = await CreateDraftReceiptAsync(
            database,
            catalog.StoreId,
            orderA.PurchaseOrderId,
            new ReceiptInput(orderA.LineIds.Single(), 1m, PurchaseShortageDisposition.WaitForBackorder));
        var before = await CaptureCardinalityStateAsync(database, catalog.StoreId);

        Func<Task> injectForeignLine = async () =>
            await CreateDraftReceiptAsync(
                database,
                catalog.StoreId,
                orderA.PurchaseOrderId,
                new ReceiptInput(orderB.LineIds.Single(), 1m, PurchaseShortageDisposition.WaitForBackorder));

        var failure = await injectForeignLine.Should().ThrowAsync<BusinessRuleException>();
        failure.Which.Message.Should().Be("Đơn đặt hàng hoặc dòng nhận hàng không hợp lệ.");

        var after = await CaptureCardinalityStateAsync(database, catalog.StoreId);
        after.Should().BeEquivalentTo(before, options => options.WithStrictOrdering());

        await using var verify = CreateContext(database, catalog.StoreId);
        var receipt = await verify.StockDocuments
            .AsNoTracking()
            .SingleAsync(x => x.Id == receiptAId);
        receipt.PurchaseOrderId.Should().Be(orderA.PurchaseOrderId);
        receipt.Status.Should().Be(StockDocumentStatus.Draft);
        var receiptLine = await verify.StockDocumentLines
            .AsNoTracking()
            .SingleAsync(x => x.StockDocumentId == receiptAId);
        receiptLine.PurchaseOrderLineId.Should().Be(orderA.LineIds.Single());
        receiptLine.PurchaseOrderLineId.Should().NotBe(orderB.LineIds.Single());
        (await verify.InventoryTransactions.CountAsync()).Should().Be(0);
        (await verify.PurchasePayables.CountAsync()).Should().Be(0);
        (await verify.PurchaseOrderLines
                .Where(x => x.PurchaseOrderId == orderA.PurchaseOrderId ||
                            x.PurchaseOrderId == orderB.PurchaseOrderId)
                .SumAsync(x => x.ReceivedQuantity))
            .Should().Be(0m);
    }

    [Fact]
    public async Task T3_confirm_posts_purchase_inventory_cost_payable_and_retry_exactly_once()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync();
        var catalog = await SeedCatalogAsync(database);
        var order = await CreateApprovedPurchaseOrderAsync(
            database,
            catalog,
            new PurchaseOrderInput(catalog.FirstVariantId, catalog.FirstConversionId, 10m));

        var receipt = await ConfirmReceiptAsync(
            database,
            catalog,
            order.PurchaseOrderId,
            expectedLastPrice: null,
            new ReceiptInput(order.LineIds.Single(), 4m, PurchaseShortageDisposition.WaitForBackorder));

        await AssertPurchaseOrderStateAsync(
            database,
            catalog.StoreId,
            order.PurchaseOrderId,
            PurchaseOrderStatus.PartiallyReceived,
            new ExpectedOrderLine(10m, 4m, 6m, PurchaseOrderLineReceiptStatus.PartiallyReceived));

        await using (var verify = CreateContext(database, catalog.StoreId))
        {
            var confirmed = await verify.StockDocuments
                .AsNoTracking()
                .SingleAsync(x => x.Id == receipt.ReceiptId);
            confirmed.Status.Should().Be(StockDocumentStatus.Confirmed);
            confirmed.ConfirmedLegalEntityId.Should().Be(catalog.LegalEntityId);
            confirmed.TotalAmount.Should().Be(40m);

            var transaction = await verify.InventoryTransactions
                .AsNoTracking()
                .SingleAsync(x =>
                    x.ReferenceType == InventoryReferenceType.StockDocument &&
                    x.ReferenceId == receipt.ReceiptId.ToString());
            transaction.TransactionType.Should().Be(InventoryTransactionType.PurchaseReceipt);
            transaction.ProductVariantId.Should().Be(catalog.FirstVariantId);
            transaction.WarehouseId.Should().Be(catalog.WarehouseId);
            transaction.QuantityChange.Should().Be(4m);
            transaction.UnitCostSnapshot.Should().Be(10m);
            transaction.TotalCost.Should().Be(40m);
            transaction.IdempotencyKey.Should().NotBeNullOrEmpty();

            var balance = await verify.InventoryBalances
                .AsNoTracking()
                .SingleAsync(x =>
                    x.WarehouseId == catalog.WarehouseId &&
                    x.ProductVariantId == catalog.FirstVariantId);
            balance.OnHandQty.Should().Be(4m);
            balance.InventoryValue.Should().Be(40m);
            balance.AverageUnitCost.Should().Be(10m);
            balance.LastInboundUnitCost.Should().Be(10m);

            var valuation = await verify.InventoryValuationEntries
                .AsNoTracking()
                .SingleAsync(x =>
                    x.ReferenceType == InventoryReferenceType.StockDocument &&
                    x.ReferenceId == receipt.ReceiptId.ToString());
            valuation.EntryType.Should().Be(InventoryValuationEntryType.Inbound);
            valuation.Quantity.Should().Be(4m);
            valuation.UnitCost.Should().Be(10m);
            valuation.Amount.Should().Be(40m);
            valuation.InventoryCostLayerId.Should().NotBeNull();

            var layer = await verify.InventoryCostLayers
                .AsNoTracking()
                .SingleAsync(x =>
                    x.ReferenceType == InventoryReferenceType.StockDocument &&
                    x.ReferenceId == receipt.ReceiptId.ToString());
            layer.InventoryTransactionId.Should().Be(transaction.Id);
            layer.InventoryValuationEntryId.Should().Be(valuation.Id);
            layer.OriginalQuantity.Should().Be(4m);
            layer.RemainingQuantity.Should().Be(4m);
            layer.UnitCost.Should().Be(10m);

            var payable = await verify.PurchasePayables
                .AsNoTracking()
                .SingleAsync(x => x.StockDocumentId == receipt.ReceiptId);
            payable.PurchaseOrderId.Should().Be(order.PurchaseOrderId);
            payable.Type.Should().Be(PurchasePayableType.Merchandise);
            payable.SourceKey.Should().Be(PurchasePostingIdentity.MerchandisePayable(receipt.ReceiptId));
            payable.SupplierId.Should().Be(catalog.SupplierId);
            payable.Amount.Should().Be(40m);
            payable.Status.Should().Be(PurchasePayableStatus.Outstanding);

            var auditTypes = await verify.PurchaseReceiptAuditEvents
                .AsNoTracking()
                .Where(x => x.StockDocumentId == receipt.ReceiptId)
                .Select(x => x.EventType)
                .ToListAsync();
            auditTypes.Should().Contain(PurchaseReceiptAuditEventType.ReceiptCreated);
            auditTypes.Should().Contain(PurchaseReceiptAuditEventType.SubmittedForApproval);
            auditTypes.Should().Contain(PurchaseReceiptAuditEventType.CommercialApprovalConfirmed);
        }

        var beforeRetry = await CapturePostingStateAsync(database, catalog.StoreId, receipt.ReceiptId);
        await using (var retryDb = CreateContext(database, catalog.StoreId))
        {
            var retryService = CreateStockDocumentService(retryDb, catalog.StoreId);
            await retryService.ApproveCommercialAsync(receipt.ReceiptId, receipt.ApprovalRequest);
        }
        var afterRetry = await CapturePostingStateAsync(database, catalog.StoreId, receipt.ReceiptId);
        afterRetry.Should().BeEquivalentTo(beforeRetry, options => options.WithStrictOrdering());
    }

    [Fact]
    public async Task Mixed_physical_components_confirm_as_one_aggregate_allocation_and_post_each_component_once()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync();
        var catalog = await SeedCatalogAsync(database);
        var order = await CreateApprovedPurchaseOrderAsync(
            database,
            catalog,
            new PurchaseOrderInput(catalog.FirstVariantId, catalog.FirstConversionId, 98m));
        int caseConversionId;
        int receiptId;
        int caseLineId;
        int boxLineId;

        await using (var setup = CreateContext(database, catalog.StoreId))
        {
            var boxConversion = await setup.ProductUnitConversions
                .Include(x => x.Unit)
                .SingleAsync(x => x.Id == catalog.FirstConversionId);
            var caseUnit = new Unit
            {
                StoreId = catalog.StoreId,
                Code = $"P2R-CASE-{Guid.NewGuid():N}"[..20],
                Name = "Thùng",
                IsActive = true
            };
            var caseConversion = new ProductUnitConversion
            {
                StoreId = catalog.StoreId,
                ProductVariantId = catalog.FirstVariantId,
                Unit = caseUnit,
                Factor = 48m,
                IsActive = true
            };
            setup.ProductUnitConversions.Add(caseConversion);
            await setup.SaveChangesAsync();
            caseConversionId = caseConversion.Id;
        }

        receiptId = await CreateDraftReceiptAsync(
            database,
            catalog.StoreId,
            order.PurchaseOrderId,
            new ReceiptInput(order.LineIds.Single(), 98m, PurchaseShortageDisposition.None));

        await using (var setup = CreateContext(database, catalog.StoreId))
        {
            var document = await setup.StockDocuments.SingleAsync(x => x.Id == receiptId);
            var box = await setup.StockDocumentLines.SingleAsync(x => x.StockDocumentId == receiptId);
            var caseConversion = await setup.ProductUnitConversions
                .Include(x => x.Unit)
                .SingleAsync(x => x.Id == caseConversionId);
            box.Quantity = 2m;
            box.BaseQuantity = 2m;
            box.LineTotal = 0m;
            boxLineId = box.Id;
            var cases = new StockDocumentLine
            {
                StockDocument = document,
                StockDocumentId = document.Id,
                LineNo = 2,
                ProductVariantId = box.ProductVariantId,
                ProductUnitConversionId = caseConversion.Id,
                PurchaseOrderLineId = box.PurchaseOrderLineId,
                ReceiptAllocationKind = ReceiptAllocationKind.PurchaseOrder,
                OutsidePoDecisionStatus = OutsidePoDecisionStatus.NotApplicable,
                UnitId = caseConversion.UnitId,
                UnitNameSnapshot = caseConversion.Unit.Name,
                Factor = caseConversion.Factor,
                Quantity = 2m,
                BaseQuantity = 96m,
                ProductNameSnapshot = box.ProductNameSnapshot,
                SkuSnapshot = box.SkuSnapshot,
                ShortageDisposition = PurchaseShortageDisposition.None
            };
            setup.StockDocumentLines.Add(cases);
            await setup.SaveChangesAsync();
            caseLineId = cases.Id;
        }

        ApprovePurchaseReceiptCommercialRequest approval;
        await using (var approve = CreateContext(database, catalog.StoreId))
        {
            var service = CreateStockDocumentService(approve, catalog.StoreId);
            var rowVersion = await GetStockDocumentRowVersionAsync(approve, receiptId);
            await service.SubmitForApprovalAsync(receiptId, "Submit mixed receipt", rowVersion);
            rowVersion = await GetStockDocumentRowVersionAsync(approve, receiptId);
            approval = new ApprovePurchaseReceiptCommercialRequest
            {
                RowVersion = rowVersion,
                ApprovalNote = "Confirm mixed receipt",
                SupplierId = catalog.SupplierId,
                HasVat = false,
                IncludeVatInInventoryCost = false,
                IsMerchandisePaid = false,
                HasFreight = false,
                CapitalizeFreightInInventoryCost = false,
                Lines =
                [
                    new PurchaseReceiptFinancialLineInputDto
                    {
                        StockDocumentLineId = boxLineId,
                        UnitPriceBeforeVat = 10m
                    },
                    new PurchaseReceiptFinancialLineInputDto
                    {
                        StockDocumentLineId = caseLineId,
                        UnitPriceBeforeVat = 480m
                    }
                ]
            };
            await service.ApproveCommercialAsync(receiptId, approval);
        }

        await using (var verify = CreateContext(database, catalog.StoreId))
        {
            var poLine = await verify.PurchaseOrderLines.SingleAsync(x => x.Id == order.LineIds.Single());
            poLine.ReceivedQuantity.Should().Be(98m);
            poLine.PendingQuantity.Should().Be(0m);
            var transactions = await verify.InventoryTransactions
                .Where(x => x.ReferenceType == InventoryReferenceType.StockDocument &&
                            x.ReferenceId == receiptId.ToString())
                .ToListAsync();
            transactions.Should().HaveCount(2);
            transactions.Sum(x => x.QuantityChange).Should().Be(98m);
            transactions.Sum(x => x.TotalCost).Should().Be(980m);
            (await verify.InventoryValuationEntries
                .Where(x => x.ReferenceType == InventoryReferenceType.StockDocument &&
                            x.ReferenceId == receiptId.ToString())
                .SumAsync(x => x.Amount)).Should().Be(980m);
            (await verify.InventoryCostLayers
                .Where(x => x.ReferenceType == InventoryReferenceType.StockDocument &&
                            x.ReferenceId == receiptId.ToString())
                .SumAsync(x => x.OriginalQuantity)).Should().Be(98m);
            var payable = await verify.PurchasePayables.SingleAsync(x => x.StockDocumentId == receiptId);
            payable.Amount.Should().Be(980m);
        }

        var beforeRetry = await CapturePostingStateAsync(database, catalog.StoreId, receiptId);
        await using (var retry = CreateContext(database, catalog.StoreId))
            await CreateStockDocumentService(retry, catalog.StoreId)
                .ApproveCommercialAsync(receiptId, approval);
        var afterRetry = await CapturePostingStateAsync(database, catalog.StoreId, receiptId);
        afterRetry.Should().BeEquivalentTo(beforeRetry, options => options.WithStrictOrdering());
    }

    private static async Task<CatalogFixture> SeedCatalogAsync(InventoryPostingLocalDb database)
    {
        var seed = await database.SeedInventoryCatalogAsync();
        await using var db = CreateContext(database, seed.StoreId);
        var warehouse = await db.Warehouses.AsNoTracking().SingleAsync(x => x.Id == seed.WarehouseId);
        var firstVariant = await db.ProductVariants
            .Include(x => x.Product)
            .SingleAsync(x => x.Id == seed.ProductVariantId);
        var unit = await db.Units.SingleAsync();
        var supplier = await db.Suppliers.SingleAsync();
        var category = await db.Categories.SingleAsync();

        var firstConversion = new ProductUnitConversion
        {
            StoreId = seed.StoreId,
            ProductVariantId = firstVariant.Id,
            UnitId = unit.Id,
            Factor = 1m,
            IsBaseUnit = true,
            IsDefaultForSale = true,
            IsActive = true
        };
        var unique = Guid.NewGuid().ToString("N");
        var secondProduct = new Product
        {
            StoreId = seed.StoreId,
            Name = "P2R second product",
            Alias = $"p2r-second-{unique}",
            CategoryId = category.Id,
            SupplierId = supplier.Id,
            BaseUnitId = unit.Id,
            BasePrice = 20m,
            IsActive = true,
            IsSellable = true
        };
        db.AddRange(firstConversion, secondProduct);
        await db.SaveChangesAsync();

        var secondVariant = new ProductVariant
        {
            StoreId = seed.StoreId,
            ProductId = secondProduct.Id,
            Sku = $"P2R-B-{unique}",
            ProductVariantName = "P2R second variant",
            CostPrice = 10m,
            Price = 20m,
            IsActive = true
        };
        db.ProductVariants.Add(secondVariant);
        await db.SaveChangesAsync();

        var secondConversion = new ProductUnitConversion
        {
            StoreId = seed.StoreId,
            ProductVariantId = secondVariant.Id,
            UnitId = unit.Id,
            Factor = 1m,
            IsBaseUnit = true,
            IsDefaultForSale = true,
            IsActive = true
        };
        db.ProductUnitConversions.Add(secondConversion);
        await db.SaveChangesAsync();

        return new CatalogFixture(
            seed.StoreId,
            seed.WarehouseId,
            warehouse.LegalEntityId,
            supplier.Id,
            firstVariant.Id,
            firstConversion.Id,
            secondVariant.Id,
            secondConversion.Id);
    }

    private static async Task<PurchaseOrderFixture> CreateApprovedPurchaseOrderAsync(
        InventoryPostingLocalDb database,
        CatalogFixture catalog,
        params PurchaseOrderInput[] inputs)
    {
        await using var db = CreateContext(database, catalog.StoreId);
        var tenant = new TenantStub(catalog.StoreId);
        var user = new UserStub();
        var service = new PurchaseOrderService(
            new PurchaseOrderRepository(db),
            new DocumentNumberSequenceRepository(db),
            tenant,
            user,
            new AppUnitOfWork(db));
        var orderId = await service.SaveAsync(new SavePurchaseOrderRequest
        {
            Title = "P2R final automated coverage",
            SupplierId = catalog.SupplierId,
            ExpectedWarehouseId = catalog.WarehouseId,
            LegalEntityId = catalog.LegalEntityId,
            OrderDate = DateTime.UtcNow.Date,
            OutsideRequestReason = "Automated purchase-to-receipt coverage",
            Lines = inputs.Select(input => new PurchaseOrderLineInputDto
            {
                ItemKind = PurchaseItemKind.Catalog,
                ProductVariantId = input.ProductVariantId,
                ProductUnitConversionId = input.ProductUnitConversionId,
                Quantity = input.Quantity,
                UnitPriceBeforeVat = 0m
            }).ToList()
        });

        var rowVersion = await GetPurchaseOrderRowVersionAsync(db, orderId);
        await service.SubmitAsync(orderId, new PurchaseWorkflowRequest
        {
            Note = "Submit P2R test order",
            RowVersion = rowVersion
        });
        rowVersion = await GetPurchaseOrderRowVersionAsync(db, orderId);
        await service.ApproveAsync(orderId, new ApprovePurchaseOrderRequest
        {
            SupplierId = catalog.SupplierId,
            Note = "Approve P2R test order",
            RowVersion = rowVersion
        });

        var lines = await db.PurchaseOrderLines
            .AsNoTracking()
            .Where(x => x.PurchaseOrderId == orderId)
            .OrderBy(x => x.LineNo)
            .Select(x => x.Id)
            .ToArrayAsync();
        lines.Should().HaveCount(inputs.Length);
        return new PurchaseOrderFixture(orderId, lines);
    }

    private static async Task<int> CreateDraftReceiptAsync(
        InventoryPostingLocalDb database,
        int storeId,
        int purchaseOrderId,
        params ReceiptInput[] inputs)
    {
        await using var db = CreateContext(database, storeId);
        var service = CreateStockDocumentService(db, storeId);
        return await service.CreateReceiptFromPurchaseOrderAsync(
            purchaseOrderId,
            new CreatePurchaseReceiptRequest
            {
                DocumentDate = DateTime.UtcNow.Date,
                Note = "P2R SQL coverage receipt",
                Lines = inputs.Select(input => new CreatePurchaseReceiptLineRequest
                {
                    PurchaseOrderLineId = input.PurchaseOrderLineId,
                    Quantity = input.Quantity,
                    ShortageDisposition = input.ShortageDisposition
                }).ToList()
            });
    }

    private static async Task<ConfirmedReceipt> ConfirmReceiptAsync(
        InventoryPostingLocalDb database,
        CatalogFixture catalog,
        int purchaseOrderId,
        decimal? expectedLastPrice,
        params ReceiptInput[] inputs)
    {
        await using var db = CreateContext(database, catalog.StoreId);
        var service = CreateStockDocumentService(db, catalog.StoreId);
        var receiptId = await service.CreateReceiptFromPurchaseOrderAsync(
            purchaseOrderId,
            new CreatePurchaseReceiptRequest
            {
                DocumentDate = DateTime.UtcNow.Date,
                Note = "P2R SQL coverage receipt",
                Lines = inputs.Select(input => new CreatePurchaseReceiptLineRequest
                {
                    PurchaseOrderLineId = input.PurchaseOrderLineId,
                    Quantity = input.Quantity,
                    ShortageDisposition = input.ShortageDisposition
                }).ToList()
            });

        var rowVersion = await GetStockDocumentRowVersionAsync(db, receiptId);
        await service.SubmitForApprovalAsync(
            receiptId,
            "Submit P2R SQL receipt",
            rowVersion);
        rowVersion = await GetStockDocumentRowVersionAsync(db, receiptId);
        var lines = await db.StockDocumentLines
            .AsNoTracking()
            .Where(x => x.StockDocumentId == receiptId)
            .OrderBy(x => x.LineNo)
            .Select(x => x.Id)
            .ToArrayAsync();
        var request = new ApprovePurchaseReceiptCommercialRequest
        {
            RowVersion = rowVersion,
            ApprovalNote = "Confirm P2R SQL receipt",
            SupplierId = catalog.SupplierId,
            HasVat = false,
            IncludeVatInInventoryCost = false,
            IsMerchandisePaid = false,
            HasFreight = false,
            CapitalizeFreightInInventoryCost = false,
            Lines = lines.Select(lineId => new PurchaseReceiptFinancialLineInputDto
            {
                StockDocumentLineId = lineId,
                UnitPriceBeforeVat = 10m,
                ExpectedLastPurchaseUnitPriceBeforeVat = expectedLastPrice
            }).ToList()
        };
        await service.ApproveCommercialAsync(receiptId, request);
        return new ConfirmedReceipt(receiptId, request);
    }

    private static async Task AssertPurchaseOrderStateAsync(
        InventoryPostingLocalDb database,
        int storeId,
        int purchaseOrderId,
        PurchaseOrderStatus expectedStatus,
        params ExpectedOrderLine[] expectedLines)
    {
        await using var db = CreateContext(database, storeId);
        var order = await db.PurchaseOrders
            .AsNoTracking()
            .Include(x => x.Lines)
            .SingleAsync(x => x.Id == purchaseOrderId);
        order.Status.Should().Be(expectedStatus);
        var actualLines = order.Lines.Where(x => !x.IsDeleted).OrderBy(x => x.LineNo).ToArray();
        actualLines.Should().HaveCount(expectedLines.Length);
        for (var index = 0; index < expectedLines.Length; index++)
        {
            var expected = expectedLines[index];
            var actual = actualLines[index];
            actual.OrderedQuantity.Should().Be(expected.Ordered);
            actual.ReceivedQuantity.Should().Be(expected.Received);
            actual.PendingQuantity.Should().Be(expected.Remaining);
            actual.PendingQuantity.Should().BeGreaterThanOrEqualTo(0m);
            actual.ReceiptStatus.Should().Be(expected.ReceiptStatus);
        }
    }

    private static async Task<ReceiptSnapshot> CaptureReceiptAsync(
        InventoryPostingLocalDb database,
        int storeId,
        int receiptId)
    {
        await using var db = CreateContext(database, storeId);
        var document = await db.StockDocuments
            .AsNoTracking()
            .SingleAsync(x => x.Id == receiptId);
        var lines = await db.StockDocumentLines
            .AsNoTracking()
            .Where(x => x.StockDocumentId == receiptId)
            .OrderBy(x => x.LineNo)
            .Select(x => new ReceiptLineSnapshot(
                x.Id,
                x.PurchaseOrderLineId,
                x.Quantity,
                x.BaseQuantity,
                x.UnitCost,
                x.LineTotal,
                x.ShortageDisposition,
                x.ShortageReason,
                Convert.ToHexString(x.RowVersion)))
            .ToArrayAsync();
        return new ReceiptSnapshot(
            document.Id,
            document.PurchaseOrderId,
            document.Status,
            document.TotalAmount,
            document.ConfirmedLegalEntityId,
            document.ConfirmedAtUtc,
            document.ConfirmedByUserId,
            document.ApprovalNote,
            Convert.ToHexString(document.RowVersion),
            lines);
    }

    private static async Task<CardinalityState> CaptureCardinalityStateAsync(
        InventoryPostingLocalDb database,
        int storeId)
    {
        await using var db = CreateContext(database, storeId);
        return new CardinalityState(
            await db.StockDocuments.CountAsync(),
            await db.StockDocumentLines.CountAsync(),
            await db.PurchaseReceiptAuditEvents.CountAsync(),
            await db.InventoryTransactions.CountAsync(),
            await db.InventoryValuationEntries.CountAsync(),
            await db.InventoryCostLayers.CountAsync(),
            await db.PurchasePayables.CountAsync(),
            await db.PurchaseOrderLines
                .OrderBy(x => x.PurchaseOrderId)
                .ThenBy(x => x.LineNo)
                .Select(x => new OrderLineQuantityState(x.Id, x.ReceivedQuantity, x.ShortClosedQuantity))
                .ToArrayAsync());
    }

    private static async Task<PostingState> CapturePostingStateAsync(
        InventoryPostingLocalDb database,
        int storeId,
        int receiptId)
    {
        await using var db = CreateContext(database, storeId);
        var referenceId = receiptId.ToString();
        var orderLineId = await db.StockDocumentLines
            .Where(x => x.StockDocumentId == receiptId)
            .Select(x => x.PurchaseOrderLineId!.Value)
            .Distinct()
            .SingleAsync();
        var orderLine = await db.PurchaseOrderLines.SingleAsync(x => x.Id == orderLineId);
        return new PostingState(
            await db.InventoryTransactions.CountAsync(x =>
                x.ReferenceType == InventoryReferenceType.StockDocument && x.ReferenceId == referenceId),
            await db.InventoryValuationEntries.CountAsync(x =>
                x.ReferenceType == InventoryReferenceType.StockDocument && x.ReferenceId == referenceId),
            await db.InventoryCostLayers.CountAsync(x =>
                x.ReferenceType == InventoryReferenceType.StockDocument && x.ReferenceId == referenceId),
            await db.PurchasePayables.CountAsync(x => x.StockDocumentId == receiptId),
            await db.PurchaseReceiptAuditEvents.CountAsync(x => x.StockDocumentId == receiptId),
            orderLine.ReceivedQuantity,
            orderLine.PendingQuantity,
            await db.InventoryBalances
                .Where(x =>
                    x.WarehouseId == (db.StockDocuments
                        .Where(d => d.Id == receiptId)
                        .Select(d => d.WarehouseId)
                        .Single()) &&
                    x.ProductVariantId == orderLine.ProductVariantId)
                .Select(x => x.OnHandQty)
                .SingleAsync());
    }

    private static StockDocumentService CreateStockDocumentService(AppDbContext db, int storeId)
    {
        var tenant = new TenantStub(storeId);
        var user = new UserStub();
        var stockDocuments = new StockDocumentRepository(db);
        var balances = new InventoryBalanceRepository(db);
        var transactions = new InventoryTransactionRepository(db);
        var valuations = new InventoryValuationEntryRepository(db);
        var layers = new InventoryCostLayerRepository(db);
        var allocations = new InventoryCostLayerAllocationRepository(db);
        var warehouses = new WarehouseRepository(db);
        var movement = new InventoryMovementService(
            balances,
            transactions,
            valuations,
            layers,
            allocations,
            warehouses,
            new InventoryPostingTransactionCoordinator(db));
        var revaluation = new InventoryRevaluationService(
            layers,
            allocations,
            valuations,
            transactions,
            balances,
            movement);
        var inputInvoices = new InputInvoiceRepository(db);
        var supplierResolution = new InputInvoiceSupplierResolutionService(inputInvoices, user);

        return new StockDocumentService(
            stockDocuments,
            new LegalEntityRepository(db),
            warehouses,
            Unused<IBarcodeLookupService>(),
            new InventoryUnitResolver(stockDocuments),
            movement,
            new InventoryMovementFactory(),
            revaluation,
            new DocumentNumberSequenceRepository(db),
            tenant,
            valuations,
            user,
            supplierResolution,
            inputInvoices,
            inputInvoiceReconciliationService: null);
    }

    private static AppDbContext CreateContext(
        InventoryPostingLocalDb database,
        int storeId)
        => new(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlServer(database.ConnectionString)
                .Options,
            new TenantStub(storeId),
            new UserStub());

    private static async Task<string> GetPurchaseOrderRowVersionAsync(AppDbContext db, int id)
        => Convert.ToBase64String(await db.PurchaseOrders
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => x.RowVersion)
            .SingleAsync());

    private static async Task<string> GetStockDocumentRowVersionAsync(AppDbContext db, int id)
        => Convert.ToBase64String(await db.StockDocuments
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => x.RowVersion)
            .SingleAsync());

    private static T Unused<T>() where T : class
        => DispatchProxy.Create<T, ThrowingDependencyProxy>();

    private class ThrowingDependencyProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            => throw new NotSupportedException(targetMethod?.Name);
    }

    private sealed class TenantStub(int storeId) : ITenantContext
    {
        public int? StoreId => storeId;
        public bool IsHostAdmin => false;
        public string? Subdomain => "p2r-final-coverage";
    }

    private sealed class UserStub : ICurrentUser
    {
        public int? UserId => 501;
        public string? UserName => "p2r-final-coverage";
        public int? TerminalId => null;
        public string? TerminalCode => null;
        public bool IsAuthenticated => true;
    }

    private sealed record CatalogFixture(
        int StoreId,
        int WarehouseId,
        int LegalEntityId,
        int SupplierId,
        int FirstVariantId,
        int FirstConversionId,
        int SecondVariantId,
        int SecondConversionId);

    private sealed record PurchaseOrderInput(
        int ProductVariantId,
        int ProductUnitConversionId,
        decimal Quantity);

    private sealed record PurchaseOrderFixture(int PurchaseOrderId, int[] LineIds);

    private sealed record ReceiptInput(
        int PurchaseOrderLineId,
        decimal Quantity,
        PurchaseShortageDisposition ShortageDisposition);

    private sealed record ConfirmedReceipt(
        int ReceiptId,
        ApprovePurchaseReceiptCommercialRequest ApprovalRequest);

    private sealed record ExpectedOrderLine(
        decimal Ordered,
        decimal Received,
        decimal Remaining,
        PurchaseOrderLineReceiptStatus ReceiptStatus);

    private sealed record ReceiptLineSnapshot(
        int Id,
        int? PurchaseOrderLineId,
        decimal Quantity,
        decimal BaseQuantity,
        decimal UnitCost,
        decimal LineTotal,
        PurchaseShortageDisposition ShortageDisposition,
        string? ShortageReason,
        string RowVersion);

    private sealed record ReceiptSnapshot(
        int Id,
        int? PurchaseOrderId,
        StockDocumentStatus Status,
        decimal TotalAmount,
        int? ConfirmedLegalEntityId,
        DateTime? ConfirmedAtUtc,
        int? ConfirmedByUserId,
        string? ApprovalNote,
        string RowVersion,
        ReceiptLineSnapshot[] Lines);

    private sealed record OrderLineQuantityState(
        int Id,
        decimal ReceivedQuantity,
        decimal ShortClosedQuantity);

    private sealed record CardinalityState(
        int ReceiptCount,
        int ReceiptLineCount,
        int AuditCount,
        int TransactionCount,
        int ValuationCount,
        int CostLayerCount,
        int PayableCount,
        OrderLineQuantityState[] PurchaseOrderLines);

    private sealed record PostingState(
        int TransactionCount,
        int ValuationCount,
        int CostLayerCount,
        int PayableCount,
        int AuditCount,
        decimal ReceivedQuantity,
        decimal RemainingQuantity,
        decimal OnHandQuantity);
}
