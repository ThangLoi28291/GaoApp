using FluentAssertions;
using GaoApp.Application.Common;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.DTOs.Products;
using GaoApp.Application.DTOs.Purchases;
using GaoApp.Application.DTOs.Units;
using GaoApp.Application.Interfaces.Services.Products;
using GaoApp.Application.Interfaces.Services.Purchases;
using GaoApp.Application.Services.Purchases;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Repositories.Inventory;
using GaoApp.Infrastructure.Repositories.Purchases;
using GaoApp.Tests.Configuration;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Purchases;

[Collection("R1FinalDatabasePreflight")]
public sealed class ProvisionalReceivingSqlServerTests
{
    [Fact]
    public async Task Capture_accumulates_only_exact_barcode_unit_and_undo_restores_complete_state()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync();
        var seed = await SeedAsync(database);
        const int userId = 1701;

        await using var db = CreateContext(database, seed.StoreId, userId);
        var workbench = CreateWorkbench(db, seed.StoreId, userId, seed.PoConversionId);
        var started = await workbench.StartOrResumeAsync(seed.PurchaseOrderId);
        var service = CreateProvisional(db, seed.StoreId, userId);

        var firstCommand = Guid.NewGuid();
        var first = await service.CaptureAsync(started.StockDocumentId, new()
        {
            CommandId = firstCommand,
            LeaseToken = started.LeaseToken,
            DocumentRowVersion = started.RowVersion,
            Name = "Hàng lạ A",
            RawBarcode = "unknown-01",
            UnitId = seed.BaseUnitId,
            Quantity = 1m
        });
        first.Items.Should().ContainSingle(x => x.Quantity == 1m);

        var retry = await service.CaptureAsync(started.StockDocumentId, new()
        {
            CommandId = firstCommand,
            LeaseToken = Guid.NewGuid(), // same employee resumes regardless of tab token
            DocumentRowVersion = started.RowVersion, // retry may carry stale RowVersion
            Name = "Hàng lạ A",
            RawBarcode = "unknown-01",
            UnitId = seed.BaseUnitId,
            Quantity = 1m
        });
        retry.Items.Should().ContainSingle(x => x.Quantity == 1m);

        var conflictingRetry = () => service.CaptureAsync(started.StockDocumentId, new()
        {
            CommandId = firstCommand,
            DocumentRowVersion = retry.DocumentRowVersion,
            Name = "Hàng lạ A",
            RawBarcode = "unknown-01",
            UnitId = seed.BaseUnitId,
            Quantity = 2m
        });
        (await conflictingRetry.Should().ThrowAsync<BusinessRuleException>())
            .Which.SafeMessage.Should().Contain("IDEMPOTENCY_CONFLICT");

        var accumulated = await service.CaptureAsync(started.StockDocumentId, new()
        {
            CommandId = Guid.NewGuid(),
            DocumentRowVersion = retry.DocumentRowVersion,
            Name = "Tên nhập khác không quyết định merge",
            RawBarcode = "UNKNOWN-01",
            UnitId = seed.BaseUnitId,
            Quantity = 2m
        });
        accumulated.Items.Should().ContainSingle(x => x.Quantity == 3m);

        var unknownUnitOne = await service.CaptureAsync(started.StockDocumentId, new()
        {
            CommandId = Guid.NewGuid(), DocumentRowVersion = accumulated.DocumentRowVersion,
            Name = "Không barcode không đơn vị", Quantity = 1m
        });
        var unknownUnitTwo = await service.CaptureAsync(started.StockDocumentId, new()
        {
            CommandId = Guid.NewGuid(), DocumentRowVersion = unknownUnitOne.DocumentRowVersion,
            Name = "Không barcode không đơn vị", Quantity = 1m
        });
        unknownUnitTwo.Items.Count(x => x.Name == "Không barcode không đơn vị").Should().Be(2);

        var target = unknownUnitTwo.Items.Single(x => x.RawBarcode == "unknown-01");
        var edited = await service.EditAsync(started.StockDocumentId, target.Id, new()
        {
            CommandId = Guid.NewGuid(), DocumentRowVersion = unknownUnitTwo.DocumentRowVersion,
            ItemRowVersion = target.RowVersion, Name = "Tên sau sửa", UnitName = "Bao",
            Quantity = 7m, Note = "sau sửa"
        });
        edited.Items.Single(x => x.Id == target.Id).Should().Match<StockDocumentProvisionalItemDto>(x =>
            x.Name == "Tên sau sửa" && x.UnitId == null && x.UnitName == "Bao" && x.Quantity == 7m);

        var undone = await service.UndoAsync(started.StockDocumentId, new()
        {
            CommandId = Guid.NewGuid(), DocumentRowVersion = edited.DocumentRowVersion
        });
        undone.Items.Single(x => x.Id == target.Id).Should().Match<StockDocumentProvisionalItemDto>(x =>
            x.Name == "Hàng lạ A" && x.UnitId == seed.BaseUnitId && x.Quantity == 3m && x.Note == null);

        var restoredTarget = undone.Items.Single(x => x.Id == target.Id);
        var removed = await service.RemoveAsync(started.StockDocumentId, target.Id, new()
        {
            CommandId = Guid.NewGuid(), DocumentRowVersion = undone.DocumentRowVersion,
            ItemRowVersion = restoredTarget.RowVersion
        }, managerRemoval: false);
        removed.Items.Single(x => x.Id == target.Id).Status
            .Should().Be(StockDocumentProvisionalItemStatus.Removed);

        var removeUndone = await service.UndoAsync(started.StockDocumentId, new()
        {
            CommandId = Guid.NewGuid(), DocumentRowVersion = removed.DocumentRowVersion
        });
        removeUndone.Items.Single(x => x.Id == target.Id).Should().Match<StockDocumentProvisionalItemDto>(x =>
            x.Status == StockDocumentProvisionalItemStatus.Unresolved &&
            x.Name == "Hàng lạ A" && x.UnitId == seed.BaseUnitId && x.Quantity == 3m);

        var finished = await workbench.FinishAsync(started.StockDocumentId, new()
        {
            LeaseToken = started.LeaseToken,
            RowVersion = removeUndone.DocumentRowVersion
        });
        finished.Status.Should().Be(StockDocumentStatus.PendingApproval);
        finished.UnresolvedProvisionalCount.Should().BeGreaterThan(0);

        (await db.InventoryTransactions.CountAsync()).Should().Be(0);
        (await db.InventoryCostLayers.CountAsync()).Should().Be(0);
        (await db.InventoryValuationEntries.CountAsync()).Should().Be(0);
        (await db.PurchasePayables.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Manager_link_classifies_po_and_outside_and_preserves_physical_evidence()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync();
        var seed = await SeedAsync(database);
        int receiptId;
        int poItemId;
        int outsideItemId;

        await using (var warehouseDb = CreateContext(database, seed.StoreId, 1801))
        {
            var workbench = CreateWorkbench(warehouseDb, seed.StoreId, 1801, seed.PoConversionId);
            var started = await workbench.StartOrResumeAsync(seed.PurchaseOrderId);
            receiptId = started.StockDocumentId;
            var service = CreateProvisional(warehouseDb, seed.StoreId, 1801);
            var poItem = await service.CaptureAsync(receiptId, new()
            {
                CommandId = Guid.NewGuid(), DocumentRowVersion = started.RowVersion,
                Name = "Sữa thực nhận", RawBarcode = "RAW-PO", UnitId = seed.BaseUnitId,
                Quantity = 2m
            });
            poItemId = poItem.Items.Single().Id;
            var outsideItem = await service.CaptureAsync(receiptId, new()
            {
                CommandId = Guid.NewGuid(), DocumentRowVersion = poItem.DocumentRowVersion,
                Name = "Ngoài đơn", RawBarcode = "RAW-OUT", UnitId = seed.BaseUnitId,
                Quantity = 4m
            });
            outsideItemId = outsideItem.Items.Single(x => x.Name == "Ngoài đơn").Id;
            await workbench.FinishAsync(receiptId, new()
            {
                LeaseToken = started.LeaseToken, RowVersion = outsideItem.DocumentRowVersion
            });
        }

        await using (var managerDb = CreateContext(database, seed.StoreId, 1802))
        {
            var service = CreateProvisional(managerDb, seed.StoreId, 1802);
            var state = await service.GetAsync(receiptId);
            var poItem = state.Items.Single(x => x.Id == poItemId);
            var linkedPo = await service.LinkExistingAsync(receiptId, poItemId, new()
            {
                CommandId = Guid.NewGuid(), DocumentRowVersion = state.DocumentRowVersion,
                ItemRowVersion = poItem.RowVersion, ProductVariantId = seed.PoVariantId,
                ProductUnitConversionId = seed.PoConversionId, RememberRawBarcode = false
            }, canCreateBarcode: false);

            var outsideItem = linkedPo.Items.Single(x => x.Id == outsideItemId);
            await service.LinkExistingAsync(receiptId, outsideItemId, new()
            {
                CommandId = Guid.NewGuid(), DocumentRowVersion = linkedPo.DocumentRowVersion,
                ItemRowVersion = outsideItem.RowVersion, ProductVariantId = seed.OutsideVariantId,
                ProductUnitConversionId = seed.OutsideConversionId, RememberRawBarcode = false
            }, canCreateBarcode: false);
        }

        await using var verify = CreateContext(database, seed.StoreId, 1803);
        var items = await verify.StockDocumentProvisionalItems.AsNoTracking()
            .Where(x => x.StockDocumentId == receiptId).OrderBy(x => x.Id).ToListAsync();
        items.Should().OnlyContain(x => x.Status == StockDocumentProvisionalItemStatus.Resolved);
        items.Single(x => x.Id == poItemId).NameSnapshot.Should().Be("Sữa thực nhận");
        items.Single(x => x.Id == outsideItemId).NameSnapshot.Should().Be("Ngoài đơn");
        var lines = await verify.StockDocumentLines.AsNoTracking()
            .Where(x => x.StockDocumentId == receiptId).ToListAsync();
        lines.Should().ContainSingle(x => x.ProductVariantId == seed.PoVariantId &&
            x.PurchaseOrderLineId == seed.PurchaseOrderLineId &&
            x.ReceiptAllocationKind == ReceiptAllocationKind.PurchaseOrder && x.Quantity == 2m);
        lines.Should().ContainSingle(x => x.ProductVariantId == seed.OutsideVariantId &&
            x.PurchaseOrderLineId == null && x.ReceiptAllocationKind == ReceiptAllocationKind.OutsidePo &&
            x.OutsidePoDecisionStatus == OutsidePoDecisionStatus.Pending && x.Quantity == 4m);
        (await verify.PurchaseReceivingActions.CountAsync(x =>
            x.StockDocumentId == receiptId && x.StockDocumentProvisionalItemId != null)).Should().Be(4);
        (await verify.InventoryTransactions.CountAsync()).Should().Be(0);
        (await verify.PurchasePayables.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Direct_resolution_is_direct_and_oversize_barcode_is_snapshot_only()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync();
        var seed = await SeedAsync(database);
        var longBarcode = new string('X', 100);
        int receiptId;

        await using (var warehouseDb = CreateContext(database, seed.StoreId, 1901))
        {
            var document = new StockDocument
            {
                StoreId = seed.StoreId, DocumentNo = $"DIRECT-{Guid.NewGuid():N}"[..28],
                Type = StockDocumentType.Receipt, Status = StockDocumentStatus.Draft,
                ReceiptSource = PurchaseReceiptSource.Direct, DirectReceiptReason = "Khác",
                WarehouseId = seed.WarehouseId, SupplierId = seed.SupplierId,
                DocumentDate = DateTime.UtcNow
            };
            warehouseDb.StockDocuments.Add(document);
            await warehouseDb.SaveChangesAsync();
            receiptId = document.Id;
            var service = CreateProvisional(warehouseDb, seed.StoreId, 1901);
            var captured = await service.CaptureAsync(receiptId, new()
            {
                CommandId = Guid.NewGuid(),
                DocumentRowVersion = Convert.ToBase64String(document.RowVersion),
                Name = "Direct chưa có", RawBarcode = longBarcode,
                UnitId = seed.BaseUnitId, Quantity = 5m
            });
            document.Status = StockDocumentStatus.PendingApproval;
            await warehouseDb.SaveChangesAsync();
        }

        await using (var managerDb = CreateContext(database, seed.StoreId, 1902))
        {
            var service = CreateProvisional(managerDb, seed.StoreId, 1902);
            var state = await service.GetAsync(receiptId);
            var item = state.Items.Single();
            await service.LinkExistingAsync(receiptId, item.Id, new()
            {
                CommandId = Guid.NewGuid(), DocumentRowVersion = state.DocumentRowVersion,
                ItemRowVersion = item.RowVersion, ProductVariantId = seed.OutsideVariantId,
                ProductUnitConversionId = seed.OutsideConversionId, RememberRawBarcode = true
            }, canCreateBarcode: true);
        }

        await using var verify = CreateContext(database, seed.StoreId, 1903);
        var itemRow = await verify.StockDocumentProvisionalItems.AsNoTracking().SingleAsync(x => x.StockDocumentId == receiptId);
        itemRow.RawBarcodeSnapshot.Should().Be(longBarcode);
        itemRow.RawBarcodeRemembered.Should().BeFalse();
        itemRow.CreatedBarcodeId.Should().BeNull();
        var line = await verify.StockDocumentLines.AsNoTracking().SingleAsync(x => x.StockDocumentId == receiptId);
        line.ReceiptAllocationKind.Should().Be(ReceiptAllocationKind.Direct);
        line.PurchaseOrderLineId.Should().BeNull();
        line.OutsidePoDecisionStatus.Should().Be(OutsidePoDecisionStatus.NotApplicable);
    }

    [Fact]
    public async Task Quick_create_resolution_rolls_back_catalog_line_and_evidence_when_barcode_save_fails()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync();
        var seed = await SeedAsync(database);
        int receiptId;

        await using (var warehouseDb = CreateContext(database, seed.StoreId, 2001))
        {
            var workbench = CreateWorkbench(warehouseDb, seed.StoreId, 2001, seed.PoConversionId);
            var started = await workbench.StartOrResumeAsync(seed.PurchaseOrderId);
            receiptId = started.StockDocumentId;
            var service = CreateProvisional(warehouseDb, seed.StoreId, 2001);
            var captured = await service.CaptureAsync(receiptId, new()
            {
                CommandId = Guid.NewGuid(), DocumentRowVersion = started.RowVersion,
                Name = "Sản phẩm rollback", RawBarcode = "ROLLBACK-RAW",
                UnitId = seed.BaseUnitId, Quantity = 3m
            });
            await workbench.FinishAsync(receiptId, new()
            {
                LeaseToken = started.LeaseToken, RowVersion = captured.DocumentRowVersion
            });
        }

        int productCountBefore;
        int lineCountBefore;
        int actionCountBefore;
        await using (var before = CreateContext(database, seed.StoreId, 2002))
        {
            productCountBefore = await before.Products.CountAsync();
            lineCountBefore = await before.StockDocumentLines.CountAsync(x => x.StockDocumentId == receiptId);
            actionCountBefore = await before.PurchaseReceivingActions.CountAsync(x => x.StockDocumentId == receiptId);
        }

        await using (var managerDb = CreateContext(database, seed.StoreId, 2002))
        {
            var catalog = new CreatingCatalogStub(managerDb, seed.StoreId, seed.SupplierId, seed.BaseUnitId);
            var service = CreateProvisional(managerDb, seed.StoreId, 2002,
                catalog, new FailingProductUnitStub(), new BarcodeStub(null));
            var state = await service.GetAsync(receiptId);
            var item = state.Items.Single();
            var action = () => service.QuickCreateAndResolveAsync(receiptId, item.Id, new()
            {
                CommandId = Guid.NewGuid(), CategoryId = 1,
                UnitId = seed.BaseUnitId, ProductName = "Sản phẩm rollback",
                RememberRawBarcode = true,
                DocumentRowVersion = state.DocumentRowVersion,
                ItemRowVersion = item.RowVersion
            }, canCreateUnit: false, canCreateBarcode: true);

            (await action.Should().ThrowAsync<InvalidOperationException>())
                .Which.Message.Should().Contain("simulated barcode failure");
        }

        await using var verify = CreateContext(database, seed.StoreId, 2003);
        (await verify.Products.CountAsync()).Should().Be(productCountBefore);
        (await verify.StockDocumentLines.CountAsync(x => x.StockDocumentId == receiptId))
            .Should().Be(lineCountBefore);
        (await verify.PurchaseReceivingActions.CountAsync(x => x.StockDocumentId == receiptId))
            .Should().Be(actionCountBefore);
        var unchanged = await verify.StockDocumentProvisionalItems.AsNoTracking()
            .SingleAsync(x => x.StockDocumentId == receiptId);
        unchanged.Status.Should().Be(StockDocumentProvisionalItemStatus.Unresolved);
        unchanged.ResolvedStockDocumentLineId.Should().BeNull();
    }

    [Fact]
    public async Task Quick_create_commits_catalog_resolution_outside_pending_and_explicit_barcode_memory()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync();
        var seed = await SeedAsync(database);
        int receiptId;

        await using (var warehouseDb = CreateContext(database, seed.StoreId, 2051))
        {
            var workbench = CreateWorkbench(warehouseDb, seed.StoreId, 2051, seed.PoConversionId);
            var started = await workbench.StartOrResumeAsync(seed.PurchaseOrderId);
            receiptId = started.StockDocumentId;
            var service = CreateProvisional(warehouseDb, seed.StoreId, 2051);
            var captured = await service.CaptureAsync(receiptId, new()
            {
                CommandId = Guid.NewGuid(), DocumentRowVersion = started.RowVersion,
                Name = "Sản phẩm tạo nhanh", RawBarcode = "QUICK-RAW-01",
                UnitId = seed.BaseUnitId, Quantity = 6m
            });
            await workbench.FinishAsync(receiptId, new()
            {
                LeaseToken = started.LeaseToken, RowVersion = captured.DocumentRowVersion
            });
        }

        int createdProductId;
        await using (var managerDb = CreateContext(database, seed.StoreId, 2052))
        {
            var categoryId = await managerDb.Categories.Select(x => x.Id).SingleAsync();
            var catalog = new CreatingCatalogStub(managerDb, seed.StoreId, seed.SupplierId, seed.BaseUnitId);
            var service = CreateProvisional(managerDb, seed.StoreId, 2052,
                catalog, new PersistingProductUnitStub(managerDb, seed.StoreId), new BarcodeStub(null));
            var state = await service.GetAsync(receiptId);
            var item = state.Items.Single();
            var resolved = await service.QuickCreateAndResolveAsync(receiptId, item.Id, new()
            {
                CommandId = Guid.NewGuid(), CategoryId = categoryId,
                UnitId = seed.BaseUnitId, ProductName = "Sản phẩm tạo nhanh",
                RememberRawBarcode = true,
                DocumentRowVersion = state.DocumentRowVersion,
                ItemRowVersion = item.RowVersion
            }, canCreateUnit: false, canCreateBarcode: true);
            resolved.Items.Single().RawBarcodeRemembered.Should().BeTrue();
            createdProductId = await managerDb.Products.OrderByDescending(x => x.Id)
                .Select(x => x.Id).FirstAsync();
        }

        await using var verify = CreateContext(database, seed.StoreId, 2053);
        var product = await verify.Products.AsNoTracking().SingleAsync(x => x.Id == createdProductId);
        product.Name.Should().Be("Sản phẩm tạo nhanh");
        product.IsSellable.Should().BeFalse();
        var itemRow = await verify.StockDocumentProvisionalItems.AsNoTracking()
            .SingleAsync(x => x.StockDocumentId == receiptId);
        itemRow.Status.Should().Be(StockDocumentProvisionalItemStatus.Resolved);
        itemRow.RawBarcodeRemembered.Should().BeTrue();
        itemRow.CreatedBarcodeId.Should().NotBeNull();
        var line = await verify.StockDocumentLines.AsNoTracking()
            .SingleAsync(x => x.Id == itemRow.ResolvedStockDocumentLineId);
        line.ReceiptAllocationKind.Should().Be(ReceiptAllocationKind.OutsidePo);
        line.PurchaseOrderLineId.Should().BeNull();
        line.OutsidePoDecisionStatus.Should().Be(OutsidePoDecisionStatus.Pending);
        line.Quantity.Should().Be(6m);
        (await verify.ProductVariantUnitBarcodes.CountAsync(x => x.Barcode == "QUICK-RAW-01"))
            .Should().Be(1);
    }

    [Fact]
    public async Task Cross_workflow_guard_allows_only_one_editable_purchase_receipt_per_order()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync();
        var seed = await SeedAsync(database);

        await using var db = CreateContext(database, seed.StoreId, 2101);
        db.StockDocuments.Add(new StockDocument
        {
            StoreId = seed.StoreId, DocumentNo = $"GUARD-A-{Guid.NewGuid():N}"[..28],
            Type = StockDocumentType.Receipt, Status = StockDocumentStatus.Draft,
            ReceiptSource = PurchaseReceiptSource.PurchaseOrder,
            PurchaseOrderId = seed.PurchaseOrderId, WarehouseId = seed.WarehouseId,
            SupplierId = seed.SupplierId, DocumentDate = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        db.StockDocuments.Add(new StockDocument
        {
            StoreId = seed.StoreId, DocumentNo = $"GUARD-B-{Guid.NewGuid():N}"[..28],
            Type = StockDocumentType.Receipt, Status = StockDocumentStatus.Rejected,
            ReceiptSource = PurchaseReceiptSource.PurchaseOrder,
            PurchaseOrderId = seed.PurchaseOrderId, WarehouseId = seed.WarehouseId,
            SupplierId = seed.SupplierId, DocumentDate = DateTime.UtcNow
        });

        await FluentActions.Invoking(() => db.SaveChangesAsync())
            .Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Link_existing_rejects_incompatible_physical_unit_without_partial_resolution()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync();
        var seed = await SeedAsync(database);
        int receiptId;

        await using (var warehouseDb = CreateContext(database, seed.StoreId, 2151))
        {
            var workbench = CreateWorkbench(warehouseDb, seed.StoreId, 2151, seed.PoConversionId);
            var started = await workbench.StartOrResumeAsync(seed.PurchaseOrderId);
            receiptId = started.StockDocumentId;
            var service = CreateProvisional(warehouseDb, seed.StoreId, 2151);
            var captured = await service.CaptureAsync(receiptId, new()
            {
                CommandId = Guid.NewGuid(), DocumentRowVersion = started.RowVersion,
                Name = "Sai đơn vị", UnitName = "Đơn vị khác hoàn toàn", Quantity = 2m
            });
            await workbench.FinishAsync(receiptId, new()
            {
                LeaseToken = started.LeaseToken, RowVersion = captured.DocumentRowVersion
            });
        }

        await using (var managerDb = CreateContext(database, seed.StoreId, 2152))
        {
            var service = CreateProvisional(managerDb, seed.StoreId, 2152);
            var state = await service.GetAsync(receiptId);
            var item = state.Items.Single();
            var action = () => service.LinkExistingAsync(receiptId, item.Id, new()
            {
                CommandId = Guid.NewGuid(), DocumentRowVersion = state.DocumentRowVersion,
                ItemRowVersion = item.RowVersion, ProductVariantId = seed.PoVariantId,
                ProductUnitConversionId = seed.PoConversionId
            }, canCreateBarcode: false);
            (await action.Should().ThrowAsync<BusinessRuleException>())
                .Which.SafeMessage.Should().Contain("không khớp đơn vị vật lý");
        }

        await using var verify = CreateContext(database, seed.StoreId, 2153);
        (await verify.StockDocumentLines.CountAsync(x => x.StockDocumentId == receiptId))
            .Should().Be(0);
        (await verify.StockDocumentProvisionalItems.SingleAsync(x => x.StockDocumentId == receiptId))
            .Status.Should().Be(StockDocumentProvisionalItemStatus.Unresolved);
    }

    [Fact]
    public async Task Retired_barcode_is_advisory_and_does_not_block_product_resolution()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync();
        var seed = await SeedAsync(database);
        int receiptId;

        await using (var warehouseDb = CreateContext(database, seed.StoreId, 2201))
        {
            warehouseDb.ProductVariantBarcodeHistories.Add(new ProductVariantBarcodeHistory
            {
                StoreId = seed.StoreId, ProductVariantId = seed.PoVariantId,
                ProductUnitConversionId = seed.PoConversionId,
                OldBarcode = "RETIRED-RAW", ActionType = BarcodeHistoryActionType.Deactivated,
                Reason = "test retired evidence", ChangedByUserId = 2201,
                ChangedByUserName = "test", ChangedAtUtc = DateTime.UtcNow
            });
            await warehouseDb.SaveChangesAsync();
            var workbench = CreateWorkbench(warehouseDb, seed.StoreId, 2201, seed.PoConversionId);
            var started = await workbench.StartOrResumeAsync(seed.PurchaseOrderId);
            receiptId = started.StockDocumentId;
            var service = CreateProvisional(warehouseDb, seed.StoreId, 2201);
            var captured = await service.CaptureAsync(receiptId, new()
            {
                CommandId = Guid.NewGuid(), DocumentRowVersion = started.RowVersion,
                Name = "Barcode lịch sử", RawBarcode = "RETIRED-RAW",
                UnitId = seed.BaseUnitId, Quantity = 1m
            });
            await workbench.FinishAsync(receiptId, new()
            {
                LeaseToken = started.LeaseToken, RowVersion = captured.DocumentRowVersion
            });
        }

        await using (var managerDb = CreateContext(database, seed.StoreId, 2202))
        {
            var service = CreateProvisional(managerDb, seed.StoreId, 2202);
            var state = await service.GetAsync(receiptId);
            var item = state.Items.Single();
            var resolved = await service.LinkExistingAsync(receiptId, item.Id, new()
            {
                CommandId = Guid.NewGuid(), DocumentRowVersion = state.DocumentRowVersion,
                ItemRowVersion = item.RowVersion, ProductVariantId = seed.PoVariantId,
                ProductUnitConversionId = seed.PoConversionId, RememberRawBarcode = true
            }, canCreateBarcode: true);
            resolved.Items.Single().Status.Should().Be(StockDocumentProvisionalItemStatus.Resolved);
            resolved.Items.Single().RawBarcodeRemembered.Should().BeFalse();
            resolved.Items.Single().BarcodeState.Should().Be("SnapshotOnly");
        }

        await using var verify = CreateContext(database, seed.StoreId, 2203);
        (await verify.ProductVariantUnitBarcodes.CountAsync(x => x.Barcode == "RETIRED-RAW"))
            .Should().Be(0);
    }

    private static PurchaseReceivingWorkbenchService CreateWorkbench(
        AppDbContext db, int storeId, int userId, int conversionId) => new(
            new PurchaseReceivingWorkbenchRepository(db), new StockDocumentRepository(db),
            new DocumentNumberSequenceRepository(db), new BarcodeStub(conversionId),
            new TenantStub(storeId), new UserStub(userId));

    private static StockDocumentProvisionalItemService CreateProvisional(
        AppDbContext db, int storeId, int userId,
        IProcurementCatalogService? catalog = null,
        IProductUnitConversionService? productUnits = null,
        IBarcodeLookupService? barcodes = null) => new(
            new StockDocumentProvisionalItemRepository(db), new AppUnitOfWork(db),
            catalog ?? new CatalogStub(), productUnits ?? new ProductUnitStub(),
            barcodes ?? new BarcodeStub(null),
            new TenantStub(storeId), new UserStub(userId), null!);

    private static AppDbContext CreateContext(
        InventoryPostingLocalDb database, int storeId, int userId) => new(
            new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(database.ConnectionString).Options,
            new TenantStub(storeId), new UserStub(userId));

    private static async Task<Seed> SeedAsync(InventoryPostingLocalDb database)
    {
        var inventory = await database.SeedInventoryCatalogAsync();
        await using var db = database.CreateTenantContext(inventory.StoreId);
        var variant = await db.ProductVariants.Include(x => x.Product).SingleAsync(x => x.Id == inventory.ProductVariantId);
        var unit = await db.Units.SingleAsync(x => x.Id == variant.Product.BaseUnitId);
        var conversion = new ProductUnitConversion
        {
            StoreId = inventory.StoreId, ProductVariantId = variant.Id, UnitId = unit.Id,
            Factor = 1m, IsBaseUnit = true, IsActive = true
        };
        db.ProductUnitConversions.Add(conversion);
        var categoryId = await db.Categories.Select(x => x.Id).SingleAsync();
        var supplierId = await db.Suppliers.Select(x => x.Id).SingleAsync();
        var outsideProduct = new Product
        {
            StoreId = inventory.StoreId, Name = "Outside provisional",
            Alias = $"outside-{Guid.NewGuid():N}", CategoryId = categoryId,
            SupplierId = supplierId, BaseUnitId = unit.Id, BasePrice = 0m,
            IsActive = true, IsSellable = false
        };
        db.Products.Add(outsideProduct);
        await db.SaveChangesAsync();
        var outsideVariant = new ProductVariant
        {
            StoreId = inventory.StoreId, ProductId = outsideProduct.Id,
            Sku = $"PROV-{Guid.NewGuid():N}", IsActive = true
        };
        db.ProductVariants.Add(outsideVariant);
        await db.SaveChangesAsync();
        var outsideConversion = new ProductUnitConversion
        {
            StoreId = inventory.StoreId, ProductVariantId = outsideVariant.Id,
            UnitId = unit.Id, Factor = 1m, IsBaseUnit = true, IsActive = true
        };
        db.ProductUnitConversions.Add(outsideConversion);
        await db.SaveChangesAsync();

        var warehouse = await db.Warehouses.SingleAsync(x => x.Id == inventory.WarehouseId);
        var order = new PurchaseOrder
        {
            StoreId = inventory.StoreId, OrderNumber = $"PROV-{Guid.NewGuid():N}"[..25],
            SupplierId = supplierId, ExpectedWarehouseId = warehouse.Id,
            LegalEntityId = warehouse.LegalEntityId, Status = PurchaseOrderStatus.Approved
        };
        var orderLine = new PurchaseOrderLine
        {
            StoreId = inventory.StoreId, LineNo = 1, ProductVariantId = variant.Id,
            ProductUnitConversionId = conversion.Id, UnitId = unit.Id,
            ProductNameSnapshot = "PO product", UnitNameSnapshot = unit.Name,
            ConversionFactor = 1m, OrderedQuantity = 20m
        };
        order.Lines.Add(orderLine);
        db.PurchaseOrders.Add(order);
        await db.SaveChangesAsync();
        return new Seed(inventory.StoreId, warehouse.Id, supplierId, unit.Id,
            order.Id, orderLine.Id, variant.Id, conversion.Id,
            outsideVariant.Id, outsideConversion.Id);
    }

    private sealed class CatalogStub : IProcurementCatalogService
    {
        public Task<ProcurementQuickCreateOptionsDto> GetQuickCreateOptionsAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PurchaseLookupPageDto<PurchaseSupplierLookupDto>> SearchSuppliersAsync(string? term, int page, CancellationToken ct = default) => throw new NotSupportedException();
        public Task ResolvePurchaseOrderLineAsync(int purchaseOrderId, int purchaseOrderLineId, ResolvePurchaseOrderLineRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ProcurementCreatedProductDto> QuickCreateProductAsync(QuickCreateProcurementProductRequest request, bool canCreateUnit, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ProcurementCreatedProductDto> CreateProductWithinTransactionAsync(QuickCreateProcurementProductRequest request, bool canCreateUnit, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ProcurementCreatedProductDto> QuickCreateAndResolvePurchaseOrderLineAsync(int purchaseOrderId, int purchaseOrderLineId, QuickCreateAndResolvePurchaseOrderLineRequest request, bool canCreateUnit, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class ProductUnitStub : IProductUnitConversionService
    {
        public Task<List<UpsertProductUnitConversionRequest>> GetByVariantIdAsync(int productVariantId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<int> SaveConversionAsync(ProductUnitConversionUpsertDto dto, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<int> SaveBarcodeAsync(UpsertProductVariantUnitBarcodeRequest dto, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ProductUnitConversion> CreateAsync(CreateProductUnitConversionRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<List<ProductVariantBarcodeHistoryRowDto>> GetBarcodeHistoryByConversionIdAsync(int productUnitConversionId, int take = 20, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FailingProductUnitStub : IProductUnitConversionService
    {
        public Task<List<UpsertProductUnitConversionRequest>> GetByVariantIdAsync(int productVariantId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<int> SaveConversionAsync(ProductUnitConversionUpsertDto dto, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<int> SaveBarcodeAsync(UpsertProductVariantUnitBarcodeRequest dto, CancellationToken ct = default)
            => throw new InvalidOperationException("simulated barcode failure");
        public Task<ProductUnitConversion> CreateAsync(CreateProductUnitConversionRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<List<ProductVariantBarcodeHistoryRowDto>> GetBarcodeHistoryByConversionIdAsync(int productUnitConversionId, int take = 20, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class PersistingProductUnitStub(AppDbContext db, int storeId)
        : IProductUnitConversionService
    {
        public Task<List<UpsertProductUnitConversionRequest>> GetByVariantIdAsync(int productVariantId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<int> SaveConversionAsync(ProductUnitConversionUpsertDto dto, CancellationToken ct = default) => throw new NotSupportedException();
        public async Task<int> SaveBarcodeAsync(UpsertProductVariantUnitBarcodeRequest dto, CancellationToken ct = default)
        {
            var barcode = new ProductVariantUnitBarcode
            {
                StoreId = storeId,
                ProductUnitConversionId = dto.ProductUnitConversionId,
                Barcode = dto.Barcode ?? throw new InvalidOperationException("barcode required"),
                BarcodeType = dto.BarcodeType,
                IsPrimary = dto.IsPrimary,
                IsActive = dto.IsActive,
                Note = dto.Note
            };
            db.ProductVariantUnitBarcodes.Add(barcode);
            await db.SaveChangesAsync(ct);
            return barcode.Id;
        }
        public Task<ProductUnitConversion> CreateAsync(CreateProductUnitConversionRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<List<ProductVariantBarcodeHistoryRowDto>> GetBarcodeHistoryByConversionIdAsync(int productUnitConversionId, int take = 20, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class CreatingCatalogStub(
        AppDbContext db, int storeId, int supplierId, int unitId) : IProcurementCatalogService
    {
        public Task<ProcurementQuickCreateOptionsDto> GetQuickCreateOptionsAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PurchaseLookupPageDto<PurchaseSupplierLookupDto>> SearchSuppliersAsync(string? term, int page, CancellationToken ct = default) => throw new NotSupportedException();
        public Task ResolvePurchaseOrderLineAsync(int purchaseOrderId, int purchaseOrderLineId, ResolvePurchaseOrderLineRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ProcurementCreatedProductDto> QuickCreateProductAsync(QuickCreateProcurementProductRequest request, bool canCreateUnit, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ProcurementCreatedProductDto> QuickCreateAndResolvePurchaseOrderLineAsync(int purchaseOrderId, int purchaseOrderLineId, QuickCreateAndResolvePurchaseOrderLineRequest request, bool canCreateUnit, CancellationToken ct = default) => throw new NotSupportedException();

        public async Task<ProcurementCreatedProductDto> CreateProductWithinTransactionAsync(
            QuickCreateProcurementProductRequest request, bool canCreateUnit,
            CancellationToken ct = default)
        {
            var product = new Product
            {
                StoreId = storeId, Name = request.Name ?? "Quick product",
                Alias = $"quick-{Guid.NewGuid():N}", CategoryId = request.CategoryId,
                SupplierId = supplierId, BaseUnitId = unitId,
                BasePrice = 0m, IsActive = true, IsSellable = false
            };
            db.Products.Add(product);
            await db.SaveChangesAsync(ct);
            var variant = new ProductVariant
            {
                StoreId = storeId, ProductId = product.Id,
                Sku = $"QUICK-{Guid.NewGuid():N}", IsActive = true
            };
            db.ProductVariants.Add(variant);
            await db.SaveChangesAsync(ct);
            var conversion = new ProductUnitConversion
            {
                StoreId = storeId, ProductVariantId = variant.Id, UnitId = unitId,
                Factor = 1m, IsBaseUnit = true, IsActive = true
            };
            db.ProductUnitConversions.Add(conversion);
            await db.SaveChangesAsync(ct);
            return new ProcurementCreatedProductDto
            {
                ProductId = product.Id, ProductVariantId = variant.Id,
                ProductUnitConversionId = conversion.Id, UnitId = unitId,
                ProductName = product.Name, Sku = variant.Sku,
                UnitName = "Base", Factor = 1m, IsSellable = false
            };
        }
    }

    private sealed class BarcodeStub(int? conversionId) : IBarcodeLookupService
    {
        public Task<BarcodeLookupResultDto?> FindAsync(string barcode, CancellationToken ct = default)
            => Task.FromResult(conversionId.HasValue ? new BarcodeLookupResultDto
            {
                Found = true, InputBarcode = barcode,
                ProductUnitConversionId = conversionId
            } : null);
        public Task<List<StockDocumentLookupSelect2ItemDto>> SearchForStockDocumentSelect2Async(string keyword, int take = 20, CancellationToken ct = default) => Task.FromResult(new List<StockDocumentLookupSelect2ItemDto>());
        public Task<List<InventoryAdjustmentLookupSelect2ItemDto>> SearchForInventoryAdjustmentSelect2Async(string keyword, int take = 20, CancellationToken ct = default) => Task.FromResult(new List<InventoryAdjustmentLookupSelect2ItemDto>());
        public Task<BarcodeLookupResultDto> LookupAsync(string barcode, CancellationToken ct = default) => Task.FromResult(new BarcodeLookupResultDto { Found = conversionId.HasValue, ProductUnitConversionId = conversionId });
    }

    private sealed class TenantStub(int id) : ITenantContext
    {
        public int? StoreId => id;
        public bool IsHostAdmin => false;
        public string? Subdomain => "provisional";
    }

    private sealed class UserStub(int id) : ICurrentUser
    {
        public int? UserId => id;
        public string? UserName => $"provisional-{id}";
        public int? TerminalId => null;
        public string? TerminalCode => null;
        public bool IsAuthenticated => true;
    }

    private sealed record Seed(
        int StoreId, int WarehouseId, int SupplierId, int BaseUnitId,
        int PurchaseOrderId, int PurchaseOrderLineId,
        int PoVariantId, int PoConversionId,
        int OutsideVariantId, int OutsideConversionId);
}
