using FluentAssertions;
using GaoApp.Application.Common;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.DTOs.Products;
using GaoApp.Application.DTOs.Purchases;
using GaoApp.Application.Interfaces.Services.Products;
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
public sealed class ReceivingWorkbenchSqlServerTests
{
    [Fact]
    public async Task Same_product_in_multiple_ordered_units_is_allocated_by_exact_conversion()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync();
        var catalog = await SeedAsync(database);
        int boxLineId;
        int caseLineId;

        await using (var setup = database.CreateTenantContext(catalog.StoreId))
        {
            var order = await setup.PurchaseOrders.Include(x => x.Lines)
                .SingleAsync(x => x.Id == catalog.PurchaseOrderId);
            var variantId = order.Lines.Single().ProductVariantId!.Value;
            boxLineId = order.Lines.Single().Id;
            var caseConversion = await setup.ProductUnitConversions
                .Include(x => x.Unit)
                .SingleAsync(x => x.Id == catalog.CaseConversionId);
            var caseLine = new PurchaseOrderLine
            {
                StoreId = catalog.StoreId,
                PurchaseOrderId = order.Id,
                LineNo = 2,
                ProductVariantId = variantId,
                ProductUnitConversionId = caseConversion.Id,
                UnitId = caseConversion.UnitId,
                ProductNameSnapshot = "Sữa",
                UnitNameSnapshot = caseConversion.Unit.Name,
                ConversionFactor = caseConversion.Factor,
                OrderedQuantity = 2m
            };
            order.Lines.Add(caseLine);
            await setup.SaveChangesAsync();
            caseLineId = caseLine.Id;
        }

        await using var db = CreateContext(database, catalog.StoreId, 710);
        var service = CreateService(db, catalog.StoreId, 710, catalog.BoxConversionId);
        var started = await service.StartOrResumeAsync(catalog.PurchaseOrderId);
        var boxReceived = await service.AddAsync(started.StockDocumentId,
            new PurchaseReceivingAddRequest
            {
                CommandId = Guid.NewGuid(),
                ProductUnitConversionId = catalog.BoxConversionId,
                Quantity = 3m,
                LeaseToken = started.LeaseToken,
                RowVersion = started.RowVersion
            }, bulk: true);
        var caseReceived = await service.AddAsync(started.StockDocumentId,
            new PurchaseReceivingAddRequest
            {
                CommandId = Guid.NewGuid(),
                ProductUnitConversionId = catalog.CaseConversionId,
                Quantity = 1m,
                LeaseToken = boxReceived.LeaseToken,
                RowVersion = boxReceived.RowVersion
            }, bulk: false);

        caseReceived.Components.Should().ContainSingle(x =>
            x.ProductUnitConversionId == catalog.BoxConversionId &&
            x.PurchaseOrderLineId == boxLineId);
        caseReceived.Components.Should().ContainSingle(x =>
            x.ProductUnitConversionId == catalog.CaseConversionId &&
            x.PurchaseOrderLineId == caseLineId);
        caseReceived.Components.Should().OnlyContain(x =>
            x.AllocationKind == ReceiptAllocationKind.PurchaseOrder);
        caseReceived.Progress.Should().HaveCount(2);
        caseReceived.Progress.Single(x => x.PurchaseOrderLineId == boxLineId)
            .ProductUnitConversionId.Should().Be(catalog.BoxConversionId);
        caseReceived.Progress.Single(x => x.PurchaseOrderLineId == caseLineId)
            .ProductUnitConversionId.Should().Be(catalog.CaseConversionId);
    }

    [Fact]
    public async Task Session_mixed_units_idempotency_undo_outside_and_finish_are_persisted()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync();
        var catalog = await SeedAsync(database);

        await using var db = CreateContext(database, catalog.StoreId, 701);
        var service = CreateService(db, catalog.StoreId, 701, catalog.BoxConversionId);
        var started = await service.StartOrResumeAsync(catalog.PurchaseOrderId);
        started.LeaseToken.Should().NotBeNull();
        started.SessionState.Should().Be(ReceivingSessionState.Active);

        var boxCommand = Guid.NewGuid();
        var boxes = await service.ScanAsync(started.StockDocumentId, new PurchaseReceivingScanRequest
        {
            CommandId = boxCommand,
            Barcode = "BOX-CODE",
            LeaseToken = started.LeaseToken,
            RowVersion = started.RowVersion
        });
        boxes.Components.Should().ContainSingle(x =>
            x.ProductUnitConversionId == catalog.BoxConversionId && x.Quantity == 1m);

        var retry = await service.ScanAsync(started.StockDocumentId, new PurchaseReceivingScanRequest
        {
            CommandId = boxCommand,
            Barcode = "BOX-CODE",
            LeaseToken = boxes.LeaseToken,
            RowVersion = started.RowVersion // intentionally stale: command ID is authoritative for retry
        });
        retry.Components.Single(x => x.ProductUnitConversionId == catalog.BoxConversionId)
            .Quantity.Should().Be(1m);

        var mixed = await service.AddAsync(started.StockDocumentId, new PurchaseReceivingAddRequest
        {
            CommandId = Guid.NewGuid(),
            ProductUnitConversionId = catalog.CaseConversionId,
            Quantity = 2m,
            LeaseToken = retry.LeaseToken,
            RowVersion = retry.RowVersion
        }, bulk: true);
        mixed.Components.Should().HaveCount(2);
        mixed.Components.Sum(x => x.BaseQuantity).Should().Be(97m);
        mixed.Components.Should().OnlyContain(x => x.BaseUnitName == "Hộp");
        mixed.Components.Single(x => x.ProductUnitConversionId == catalog.BoxConversionId)
            .IsBaseUnit.Should().BeTrue();
        mixed.Components.Single(x => x.ProductUnitConversionId == catalog.CaseConversionId)
            .IsBaseUnit.Should().BeFalse();
        mixed.Progress.Single().BaseUnitName.Should().Be("Hộp");
        mixed.Progress.Single().ProjectedOverdeliveryBaseQuantity.Should().Be(1m);

        var undone = await service.UndoAsync(started.StockDocumentId, new PurchaseReceivingCommandRequest
        {
            CommandId = Guid.NewGuid(),
            LeaseToken = mixed.LeaseToken,
            RowVersion = mixed.RowVersion
        });
        undone.Components.Should().ContainSingle();
        undone.Components.Single().ProductUnitConversionId.Should().Be(catalog.BoxConversionId);

        var outside = await service.AddAsync(started.StockDocumentId, new PurchaseReceivingAddRequest
        {
            CommandId = Guid.NewGuid(),
            ProductUnitConversionId = catalog.OutsideConversionId,
            Quantity = 3m,
            LeaseToken = undone.LeaseToken,
            RowVersion = undone.RowVersion
        }, bulk: false);
        outside.HasPendingOutsidePo.Should().BeTrue();
        var outsideBaseComponent = outside.Components.Single(x =>
            x.ProductUnitConversionId == catalog.OutsideConversionId);
        outsideBaseComponent.OutsideStatus.Should().Be(OutsidePoDecisionStatus.Pending);
        outsideBaseComponent.BaseUnitName.Should().Be("Chai");
        outsideBaseComponent.IsBaseUnit.Should().BeTrue();
        var outsideTwo = await service.AddAsync(started.StockDocumentId, new PurchaseReceivingAddRequest
        {
            CommandId = Guid.NewGuid(),
            ProductUnitConversionId = catalog.SecondOutsideConversionId,
            Quantity = 1m,
            LeaseToken = outside.LeaseToken,
            RowVersion = outside.RowVersion
        }, bulk: false);
        outsideTwo.Components.Single(x =>
            x.ProductUnitConversionId == catalog.SecondOutsideConversionId)
            .BaseUnitName.Should().Be("Chai");

        var finished = await service.FinishAsync(started.StockDocumentId, new PurchaseReceivingLeaseRequest
        {
            LeaseToken = outsideTwo.LeaseToken,
            RowVersion = outsideTwo.RowVersion
        });
        finished.Status.Should().Be(StockDocumentStatus.PendingApproval);
        finished.SessionState.Should().Be(ReceivingSessionState.Frozen);
        finished.LeaseToken.Should().BeNull();

        var firstOutside = finished.Components.Single(x =>
            x.ProductUnitConversionId == catalog.OutsideConversionId);
        var accepted = await service.DecideOutsideAsync(started.StockDocumentId, firstOutside.Id,
            new PurchaseReceivingOutsideDecisionRequest
            {
                Accept = true,
                Note = "Manager accepts this exception",
                RowVersion = finished.RowVersion
            });
        accepted.Components.Single(x => x.Id == firstOutside.Id).OutsideStatus
            .Should().Be(OutsidePoDecisionStatus.Accepted);
        var secondOutside = accepted.Components.Single(x =>
            x.ProductUnitConversionId == catalog.SecondOutsideConversionId);
        var staleDecision = () => service.DecideOutsideAsync(started.StockDocumentId, secondOutside.Id,
            new PurchaseReceivingOutsideDecisionRequest
            {
                Accept = false,
                Note = "Stale but valid manager decision",
                RowVersion = finished.RowVersion
            });
        (await staleDecision.Should().ThrowAsync<BusinessRuleException>()).Which.Message
            .Should().Contain("đã được cập nhật ở nơi khác");
        var rejected = await service.DecideOutsideAsync(started.StockDocumentId, secondOutside.Id,
            new PurchaseReceivingOutsideDecisionRequest
            {
                Accept = false,
                Note = "Manager rejects this exception",
                RowVersion = accepted.RowVersion
            });
        rejected.Components.Should().NotContain(x => x.Id == secondOutside.Id);
        rejected.HasPendingOutsidePo.Should().BeFalse();

        await using var verify = database.CreateTenantContext(catalog.StoreId);
        (await verify.PurchaseReceivingActions.CountAsync(x => x.StockDocumentId == started.StockDocumentId))
            .Should().Be(5); // scan, case add, undo, two outside adds; retry created nothing
        (await verify.StockDocumentLines.CountAsync(x => x.StockDocumentId == started.StockDocumentId && !x.IsDeleted))
            .Should().Be(2);
    }

    [Fact]
    public async Task Outside_add_after_last_component_is_removed_in_previous_request_uses_durable_next_line_number()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync();
        var catalog = await SeedAsync(database);
        const int userId = 702;
        int receiptId;
        PurchaseReceivingWorkbenchDto removed;

        await using (var firstRequest = CreateContext(database, catalog.StoreId, userId))
        {
            var firstService = CreateService(
                firstRequest, catalog.StoreId, userId, catalog.BoxConversionId);
            var started = await firstService.StartOrResumeAsync(catalog.PurchaseOrderId);
            receiptId = started.StockDocumentId;
            var added = await firstService.AddAsync(receiptId, new PurchaseReceivingAddRequest
            {
                CommandId = Guid.NewGuid(),
                ProductUnitConversionId = catalog.BoxConversionId,
                Quantity = 1m,
                LeaseToken = started.LeaseToken,
                RowVersion = started.RowVersion
            }, bulk: false);
            var line = added.Components.Should().ContainSingle().Subject;

            removed = await firstService.RemoveAsync(receiptId, line.Id,
                new PurchaseReceivingCommandRequest
                {
                    CommandId = Guid.NewGuid(),
                    LeaseToken = added.LeaseToken,
                    RowVersion = added.RowVersion
                });
            removed.Components.Should().BeEmpty();
        }

        int actionsBefore;
        decimal[] receivedBefore;
        int inventoryBefore;
        int valuationBefore;
        int fifoBefore;
        int payableBefore;
        await using (var before = CreateContext(database, catalog.StoreId, userId))
        {
            var deleted = await before.StockDocumentLines.IgnoreQueryFilters()
                .SingleAsync(x => x.StockDocumentId == receiptId);
            deleted.LineNo.Should().Be(1);
            deleted.IsDeleted.Should().BeTrue();
            actionsBefore = await before.PurchaseReceivingActions
                .CountAsync(x => x.StockDocumentId == receiptId);
            receivedBefore = await before.PurchaseOrderLines
                .Where(x => x.PurchaseOrderId == catalog.PurchaseOrderId)
                .OrderBy(x => x.Id)
                .Select(x => x.ReceivedQuantity)
                .ToArrayAsync();
            inventoryBefore = await before.InventoryTransactions.CountAsync();
            valuationBefore = await before.InventoryValuationEntries.CountAsync();
            fifoBefore = await before.InventoryCostLayers.CountAsync();
            payableBefore = await before.PurchasePayables.CountAsync();
        }

        var outsideCommand = Guid.NewGuid();
        await using (var secondRequest = CreateContext(database, catalog.StoreId, userId))
        {
            var secondService = CreateService(
                secondRequest, catalog.StoreId, userId, catalog.BoxConversionId);
            var view = await secondService.GetAsync(receiptId);
            var outside = await secondService.AddAsync(receiptId, new PurchaseReceivingAddRequest
            {
                CommandId = outsideCommand,
                ProductUnitConversionId = catalog.OutsideConversionId,
                Quantity = 2m,
                LeaseToken = removed.LeaseToken,
                RowVersion = view.RowVersion
            }, bulk: true);

            outside.Components.Should().ContainSingle(x =>
                x.ProductUnitConversionId == catalog.OutsideConversionId &&
                x.Quantity == 2m && x.BaseQuantity == 2m);
        }

        await using var verify = CreateContext(database, catalog.StoreId, userId);
        var target = await verify.ProductUnitConversions.AsNoTracking()
            .SingleAsync(x => x.Id == catalog.OutsideConversionId);
        var persisted = await verify.StockDocumentLines.AsNoTracking()
            .SingleAsync(x => x.StockDocumentId == receiptId);
        persisted.LineNo.Should().Be(2);
        persisted.PurchaseOrderLineId.Should().BeNull();
        persisted.ReceiptAllocationKind.Should().Be(ReceiptAllocationKind.OutsidePo);
        persisted.OutsidePoDecisionStatus.Should().Be(OutsidePoDecisionStatus.Pending);
        persisted.ProductVariantId.Should().Be(target.ProductVariantId);
        persisted.ProductUnitConversionId.Should().Be(target.Id);
        persisted.UnitId.Should().Be(target.UnitId);
        persisted.Quantity.Should().Be(2m);
        persisted.BaseQuantity.Should().Be(2m);
        (await verify.PurchaseReceivingActions.CountAsync(x =>
            x.StockDocumentId == receiptId && x.CommandId == outsideCommand)).Should().Be(1);
        (await verify.PurchaseReceivingActions.CountAsync(x => x.StockDocumentId == receiptId))
            .Should().Be(actionsBefore + 1);
        (await verify.PurchaseOrderLines
            .Where(x => x.PurchaseOrderId == catalog.PurchaseOrderId)
            .OrderBy(x => x.Id)
            .Select(x => x.ReceivedQuantity)
            .ToArrayAsync()).Should().Equal(receivedBefore);
        (await verify.InventoryTransactions.CountAsync()).Should().Be(inventoryBefore);
        (await verify.InventoryValuationEntries.CountAsync()).Should().Be(valuationBefore);
        (await verify.InventoryCostLayers.CountAsync()).Should().Be(fifoBefore);
        (await verify.PurchasePayables.CountAsync()).Should().Be(payableBefore);
    }

    [Fact]
    public async Task Active_lease_rejects_a_second_user_and_unique_indexes_fail_closed()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync();
        var catalog = await SeedAsync(database);
        int receiptId;

        await using (var firstDb = CreateContext(database, catalog.StoreId, 801))
        {
            var first = CreateService(firstDb, catalog.StoreId, 801, catalog.BoxConversionId);
            var started = await first.StartOrResumeAsync(catalog.PurchaseOrderId);
            receiptId = started.StockDocumentId;
        }

        await using (var secondDb = CreateContext(database, catalog.StoreId, 802))
        {
            var second = CreateService(secondDb, catalog.StoreId, 802, catalog.BoxConversionId);
            var view = await second.StartOrResumeAsync(catalog.PurchaseOrderId);
            view.LeaseToken.Should().BeNull();
            var acquire = () => second.AcquireAsync(receiptId, new PurchaseReceivingLeaseRequest
            {
                RowVersion = view.RowVersion
            });
            (await acquire.Should().ThrowAsync<BusinessRuleException>()).Which.Message
                .Should().Contain("tab hoặc tài khoản khác");
        }

        await using var db = database.CreateTenantContext(catalog.StoreId);
        db.StockDocuments.Add(new StockDocument
        {
            StoreId = catalog.StoreId, DocumentNo = "RW-DUP-ACTIVE", Type = StockDocumentType.Receipt,
            Status = StockDocumentStatus.Draft, ReceiptSource = PurchaseReceiptSource.PurchaseOrder,
            PurchaseOrderId = catalog.PurchaseOrderId, WarehouseId = catalog.WarehouseId,
            ReceivingSessionState = ReceivingSessionState.Active
        });
        var uniqueFailure = () => db.SaveChangesAsync();
        await uniqueFailure.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Same_employee_with_missing_token_reacquires_the_canonical_live_lease()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync();
        var catalog = await SeedAsync(database);
        int receiptId;
        Guid canonicalToken;

        await using (var firstDb = CreateContext(database, catalog.StoreId, 811))
        {
            var first = CreateService(firstDb, catalog.StoreId, 811, catalog.BoxConversionId);
            var started = await first.StartOrResumeAsync(catalog.PurchaseOrderId);
            receiptId = started.StockDocumentId;
            started.LeaseToken.Should().NotBeNull();
            canonicalToken = started.LeaseToken!.Value;
        }

        await using var resumedDb = CreateContext(database, catalog.StoreId, 811);
        var resumedService = CreateService(resumedDb, catalog.StoreId, 811, catalog.BoxConversionId);
        var view = await resumedService.GetAsync(receiptId);
        var acquired = await resumedService.AcquireAsync(receiptId, new PurchaseReceivingLeaseRequest
        {
            RowVersion = view.RowVersion
        });

        acquired.LeaseToken.Should().Be(canonicalToken);
        acquired.OwnerUserId.Should().Be(811);
    }

    [Fact]
    public async Task Same_employee_with_different_token_reacquires_the_canonical_live_lease()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync();
        var catalog = await SeedAsync(database);
        int receiptId;
        Guid canonicalToken;

        await using (var firstDb = CreateContext(database, catalog.StoreId, 812))
        {
            var first = CreateService(firstDb, catalog.StoreId, 812, catalog.BoxConversionId);
            var started = await first.StartOrResumeAsync(catalog.PurchaseOrderId);
            receiptId = started.StockDocumentId;
            started.LeaseToken.Should().NotBeNull();
            canonicalToken = started.LeaseToken!.Value;
        }

        await using var resumedDb = CreateContext(database, catalog.StoreId, 812);
        var resumedService = CreateService(resumedDb, catalog.StoreId, 812, catalog.BoxConversionId);
        var view = await resumedService.GetAsync(receiptId);
        var acquired = await resumedService.AcquireAsync(receiptId, new PurchaseReceivingLeaseRequest
        {
            LeaseToken = Guid.NewGuid(),
            RowVersion = view.RowVersion
        });

        acquired.LeaseToken.Should().Be(canonicalToken);
        acquired.OwnerUserId.Should().Be(812);
    }

    [Fact]
    public async Task Start_or_resume_by_same_employee_returns_the_canonical_live_lease()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync();
        var catalog = await SeedAsync(database);
        int receiptId;
        Guid canonicalToken;

        await using (var firstDb = CreateContext(database, catalog.StoreId, 813))
        {
            var first = CreateService(firstDb, catalog.StoreId, 813, catalog.BoxConversionId);
            var started = await first.StartOrResumeAsync(catalog.PurchaseOrderId);
            receiptId = started.StockDocumentId;
            started.LeaseToken.Should().NotBeNull();
            canonicalToken = started.LeaseToken!.Value;
        }

        await using var resumedDb = CreateContext(database, catalog.StoreId, 813);
        var resumedService = CreateService(resumedDb, catalog.StoreId, 813, catalog.BoxConversionId);
        var resumed = await resumedService.StartOrResumeAsync(catalog.PurchaseOrderId);

        resumed.StockDocumentId.Should().Be(receiptId);
        resumed.LeaseToken.Should().Be(canonicalToken);
        resumed.OwnerUserId.Should().Be(813);
    }

    [Fact]
    public async Task Expired_lease_can_be_acquired_by_another_authorized_employee()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync();
        var catalog = await SeedAsync(database);
        int receiptId;
        Guid expiredToken;

        await using (var firstDb = CreateContext(database, catalog.StoreId, 814))
        {
            var first = CreateService(firstDb, catalog.StoreId, 814, catalog.BoxConversionId);
            var started = await first.StartOrResumeAsync(catalog.PurchaseOrderId);
            receiptId = started.StockDocumentId;
            started.LeaseToken.Should().NotBeNull();
            expiredToken = started.LeaseToken!.Value;
        }

        await using (var expireDb = database.CreateTenantContext(catalog.StoreId))
        {
            var document = await expireDb.StockDocuments.SingleAsync(x => x.Id == receiptId);
            document.ReceivingLeaseExpiresAtUtc = DateTime.UtcNow.AddSeconds(-1);
            await expireDb.SaveChangesAsync();
        }

        await using var takeoverDb = CreateContext(database, catalog.StoreId, 815);
        var takeoverService = CreateService(takeoverDb, catalog.StoreId, 815, catalog.BoxConversionId);
        var view = await takeoverService.GetAsync(receiptId);
        var acquired = await takeoverService.AcquireAsync(receiptId, new PurchaseReceivingLeaseRequest
        {
            RowVersion = view.RowVersion
        });

        acquired.OwnerUserId.Should().Be(815);
        acquired.LeaseToken.Should().NotBeNull().And.NotBe(expiredToken);
    }

    [Fact]
    public async Task Same_account_new_command_with_stale_rowversion_remains_fail_closed()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync();
        var catalog = await SeedAsync(database);

        await using var db = CreateContext(database, catalog.StoreId, 816);
        var service = CreateService(db, catalog.StoreId, 816, catalog.BoxConversionId);
        var started = await service.StartOrResumeAsync(catalog.PurchaseOrderId);
        await service.ScanAsync(started.StockDocumentId, new PurchaseReceivingScanRequest
        {
            CommandId = Guid.NewGuid(),
            Barcode = "BOX-CODE",
            LeaseToken = started.LeaseToken,
            RowVersion = started.RowVersion
        });

        var staleMutation = () => service.ScanAsync(started.StockDocumentId, new PurchaseReceivingScanRequest
        {
            CommandId = Guid.NewGuid(),
            Barcode = "BOX-CODE",
            LeaseToken = started.LeaseToken,
            RowVersion = started.RowVersion
        });

        (await staleMutation.Should().ThrowAsync<BusinessRuleException>()).Which.Message
            .Should().Contain("đã được cập nhật ở nơi khác");
    }

    [Fact]
    public async Task Migration_detects_duplicate_product_lines_and_does_not_remediate_history()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync("20260828150000_EnforceSingleActiveInputInvoicePerReceipt");
        var seed = await database.SeedInventoryCatalogAsync();
        int orderId;
        await using (var db = database.CreateTenantContext(seed.StoreId))
        {
            var warehouse = await db.Warehouses.SingleAsync(x => x.Id == seed.WarehouseId);
            var supplierId = await db.Suppliers.Select(x => x.Id).SingleAsync();
            var order = new PurchaseOrder
            {
                StoreId = seed.StoreId, OrderNumber = $"RW-DUP-{Guid.NewGuid():N}"[..25],
                SupplierId = supplierId, ExpectedWarehouseId = seed.WarehouseId,
                LegalEntityId = warehouse.LegalEntityId, Status = PurchaseOrderStatus.Approved
            };
            order.Lines.Add(NewOrderLine(seed.StoreId, seed.ProductVariantId, 1));
            order.Lines.Add(NewOrderLine(seed.StoreId, seed.ProductVariantId, 2));
            db.PurchaseOrders.Add(order);
            await db.SaveChangesAsync();
            orderId = order.Id;
        }

        var migrate = () => database.MigrateAsync();
        (await migrate.Should().ThrowAsync<Exception>()).Which.Message
            .Should().Contain("duplicate active ProductVariant");
        await using var verify = database.CreateTenantContext(seed.StoreId);
        (await verify.PurchaseOrderLines.IgnoreQueryFilters()
            .CountAsync(x => x.PurchaseOrderId == orderId && !x.IsDeleted)).Should().Be(2);
    }

    [Fact]
    public async Task Database_rejects_duplicate_physical_component_identity_and_invalid_outside_state()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync();
        var catalog = await SeedAsync(database);
        int receiptId;
        int poLineId;
        await using (var setup = database.CreateTenantContext(catalog.StoreId))
        {
            poLineId = await setup.PurchaseOrderLines.Where(x => x.PurchaseOrderId == catalog.PurchaseOrderId)
                .Select(x => x.Id).SingleAsync();
            var receipt = new StockDocument
            {
                StoreId = catalog.StoreId, DocumentNo = "RW-COMPONENT", Type = StockDocumentType.Receipt,
                Status = StockDocumentStatus.Draft, ReceiptSource = PurchaseReceiptSource.PurchaseOrder,
                PurchaseOrderId = catalog.PurchaseOrderId, WarehouseId = catalog.WarehouseId,
                ReceivingSessionState = ReceivingSessionState.None
            };
            receipt.Lines.Add(Component(receipt, poLineId, catalog.BoxConversionId,
                catalog.ProductVariantId, catalog.BaseUnitId, 1));
            setup.StockDocuments.Add(receipt);
            await setup.SaveChangesAsync();
            receiptId = receipt.Id;
        }

        await using (var duplicate = database.CreateTenantContext(catalog.StoreId))
        {
            var document = await duplicate.StockDocuments.SingleAsync(x => x.Id == receiptId);
            duplicate.StockDocumentLines.Add(Component(document, poLineId, catalog.BoxConversionId,
                catalog.ProductVariantId, catalog.BaseUnitId, 2));
            Func<Task> saveDuplicate = () => duplicate.SaveChangesAsync();
            await saveDuplicate.Should().ThrowAsync<DbUpdateException>();
        }

        var invalid = () => database.ExecuteAsync($"""
            UPDATE [StockDocumentLine]
            SET [ReceiptAllocationKind] = 2, [OutsidePoDecisionStatus] = 0, [PurchaseOrderLineId] = NULL
            WHERE [StockDocumentId] = {receiptId};
            """);
        await invalid.Should().ThrowAsync<Exception>();
    }

    private static StockDocumentLine Component(
        StockDocument document, int poLineId, int conversionId,
        int productVariantId, int unitId, int lineNo) => new()
    {
        StockDocument = document, StockDocumentId = document.Id, LineNo = lineNo,
        ProductVariantId = productVariantId,
        PurchaseOrderLineId = poLineId, ProductUnitConversionId = conversionId,
        ReceiptAllocationKind = ReceiptAllocationKind.PurchaseOrder,
        OutsidePoDecisionStatus = OutsidePoDecisionStatus.NotApplicable,
        UnitId = unitId, UnitNameSnapshot = "Hộp", Factor = 1m, Quantity = 1m,
        BaseQuantity = 1m, ProductNameSnapshot = "Sữa",
        ShortageDisposition = PurchaseShortageDisposition.WaitForBackorder
    };

    private static PurchaseOrderLine NewOrderLine(int storeId, int variantId, int lineNo) => new()
    {
        StoreId = storeId, LineNo = lineNo, ProductVariantId = variantId,
        ProductNameSnapshot = "Duplicate", UnitNameSnapshot = "Base",
        ConversionFactor = 1m, OrderedQuantity = 1m
    };

    private static PurchaseReceivingWorkbenchService CreateService(
        AppDbContext db, int storeId, int userId, int barcodeConversionId)
        => new(
            new PurchaseReceivingWorkbenchRepository(db),
            new StockDocumentRepository(db),
            new DocumentNumberSequenceRepository(db),
            new BarcodeStub(barcodeConversionId),
            new TenantStub(storeId),
            new UserStub(userId));

    private static AppDbContext CreateContext(
        InventoryPostingLocalDb database, int storeId, int userId)
        => new(new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlServer(database.ConnectionString).Options,
            new TenantStub(storeId), new UserStub(userId));

    private static async Task<Seed> SeedAsync(InventoryPostingLocalDb database)
    {
        var seed = await database.SeedInventoryCatalogAsync();
        await using var db = database.CreateTenantContext(seed.StoreId);
        var variant = await db.ProductVariants.Include(x => x.Product).SingleAsync(x => x.Id == seed.ProductVariantId);
        var baseUnit = await db.Units.SingleAsync(x => x.Id == variant.Product.BaseUnitId);
        baseUnit.Name = "Hộp";
        var caseUnit = new Unit { StoreId = seed.StoreId, Code = $"CASE-{Guid.NewGuid():N}"[..20], Name = "Thùng", IsActive = true };
        var outsideBaseUnit = new Unit
        {
            StoreId = seed.StoreId, Code = $"BOTTLE-{Guid.NewGuid():N}"[..20],
            Name = "Chai", IsActive = true
        };
        db.Units.AddRange(caseUnit, outsideBaseUnit);
        var box = new ProductUnitConversion
        {
            StoreId = seed.StoreId, ProductVariantId = variant.Id, UnitId = baseUnit.Id,
            Factor = 1m, IsBaseUnit = true, IsActive = true
        };
        var cases = new ProductUnitConversion
        {
            StoreId = seed.StoreId, ProductVariantId = variant.Id, Unit = caseUnit,
            Factor = 48m, IsActive = true
        };
        db.ProductUnitConversions.AddRange(box, cases);

        var categoryId = await db.Categories.Select(x => x.Id).SingleAsync();
        var supplierId = await db.Suppliers.Select(x => x.Id).SingleAsync();
        var outsideProduct = new Product
        {
            StoreId = seed.StoreId, Name = "Ngoài PO", Alias = $"outside-{Guid.NewGuid():N}",
            CategoryId = categoryId, SupplierId = supplierId, BaseUnit = outsideBaseUnit,
            BasePrice = 1m, IsActive = true, IsSellable = true
        };
        db.Products.Add(outsideProduct);
        await db.SaveChangesAsync();
        var outsideVariant = new ProductVariant
        {
            StoreId = seed.StoreId, ProductId = outsideProduct.Id,
            Sku = $"OUT-{Guid.NewGuid():N}", CostPrice = 1m, Price = 1m, IsActive = true
        };
        db.ProductVariants.Add(outsideVariant);
        await db.SaveChangesAsync();
        var outsideConversion = new ProductUnitConversion
        {
            StoreId = seed.StoreId, ProductVariantId = outsideVariant.Id, UnitId = outsideBaseUnit.Id,
            Factor = 1m, IsBaseUnit = true, IsActive = true
        };
        var secondOutsideUnit = new Unit
        {
            StoreId = seed.StoreId, Code = $"OUT2-{Guid.NewGuid():N}"[..20],
            Name = "Gói", IsActive = true
        };
        var secondOutsideConversion = new ProductUnitConversion
        {
            StoreId = seed.StoreId, ProductVariantId = outsideVariant.Id,
            Unit = secondOutsideUnit, Factor = 2m, IsActive = true
        };
        db.ProductUnitConversions.AddRange(outsideConversion, secondOutsideConversion);
        await db.SaveChangesAsync();

        var warehouse = await db.Warehouses.SingleAsync(x => x.Id == seed.WarehouseId);
        var order = new PurchaseOrder
        {
            StoreId = seed.StoreId, OrderNumber = $"RW-{Guid.NewGuid():N}"[..20],
            SupplierId = supplierId, ExpectedWarehouseId = seed.WarehouseId,
            LegalEntityId = warehouse.LegalEntityId, Status = PurchaseOrderStatus.Approved
        };
        order.Lines.Add(new PurchaseOrderLine
        {
            StoreId = seed.StoreId, LineNo = 1, ProductVariantId = variant.Id,
            ProductUnitConversionId = box.Id, UnitId = baseUnit.Id,
            ProductNameSnapshot = "Sữa", UnitNameSnapshot = "Hộp",
            ConversionFactor = 1m, OrderedQuantity = 96m
        });
        db.PurchaseOrders.Add(order);
        await db.SaveChangesAsync();
        return new Seed(seed.StoreId, seed.WarehouseId, order.Id, variant.Id, baseUnit.Id,
            box.Id, cases.Id, outsideConversion.Id, secondOutsideConversion.Id);
    }

    private sealed class BarcodeStub(int conversionId) : IBarcodeLookupService
    {
        public Task<BarcodeLookupResultDto?> FindAsync(string barcode, CancellationToken ct = default)
            => Task.FromResult<BarcodeLookupResultDto?>(new()
            {
                Found = true, InputBarcode = barcode, ProductUnitConversionId = conversionId
            });
        public Task<List<StockDocumentLookupSelect2ItemDto>> SearchForStockDocumentSelect2Async(string keyword, int take = 20, CancellationToken ct = default)
            => Task.FromResult(new List<StockDocumentLookupSelect2ItemDto>());
        public Task<List<InventoryAdjustmentLookupSelect2ItemDto>> SearchForInventoryAdjustmentSelect2Async(string keyword, int take = 20, CancellationToken ct = default)
            => Task.FromResult(new List<InventoryAdjustmentLookupSelect2ItemDto>());
        public Task<BarcodeLookupResultDto> LookupAsync(string barcode, CancellationToken ct = default)
            => Task.FromResult(new BarcodeLookupResultDto { Found = true, ProductUnitConversionId = conversionId });
    }

    private sealed class TenantStub(int id) : ITenantContext
    {
        public int? StoreId => id;
        public bool IsHostAdmin => false;
        public string? Subdomain => "rw";
    }

    private sealed class UserStub(int id) : ICurrentUser
    {
        public int? UserId => id;
        public string? UserName => $"rw-{id}";
        public int? TerminalId => null;
        public string? TerminalCode => null;
        public bool IsAuthenticated => true;
    }

    private sealed record Seed(
        int StoreId, int WarehouseId, int PurchaseOrderId,
        int ProductVariantId, int BaseUnitId,
        int BoxConversionId, int CaseConversionId, int OutsideConversionId,
        int SecondOutsideConversionId);
}
