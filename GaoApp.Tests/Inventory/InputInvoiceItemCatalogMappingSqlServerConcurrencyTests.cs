using FluentAssertions;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Services.Inventory;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Repositories.Inventory;
using GaoApp.Infrastructure.Tenant;
using GaoApp.Tests.Configuration;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Inventory;

[Collection("R1FinalDatabasePreflight")]
public sealed class InputInvoiceItemCatalogMappingSqlServerConcurrencyTests
{
    [Fact]
    public async Task Active_mapping_key_is_unique_within_store_and_supplier()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync();
        await using var db = database.CreateHostContext();

        var entity = db.Model.FindEntityType(typeof(InputInvoiceItemCatalogMap));
        entity.Should().NotBeNull();
        entity!.GetIndexes().Should().Contain(x => x.IsUnique &&
            x.GetFilter()!.Contains("NormalizedSupplierItemCode", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Concurrent_same_key_same_target_is_idempotent_through_central_service()
    {
        await using var database = new InventoryPostingLocalDb();
        var seed = await SeedAsync(database);

        var outcomes = await Task.WhenAll(
            ConfirmAsync(database, seed, seed.FirstLineId,
                seed.FirstVariantId, seed.FirstConversionId),
            ConfirmAsync(database, seed, seed.FirstLineId,
                seed.FirstVariantId, seed.FirstConversionId));

        outcomes.Should().OnlyContain(x => x == null);
        await using var verify = database.CreateTenantContext(seed.StoreId);
        (await verify.InputInvoiceItemCatalogMaps.CountAsync()).Should().Be(1);
        (await verify.PurchaseReceiptAuditEvents.CountAsync(x =>
            x.EventType == PurchaseReceiptAuditEventType.InputInvoiceItemMappingConfirmed))
            .Should().Be(1);
    }

    [Fact]
    public async Task Concurrent_same_key_different_target_has_one_winner_and_one_conflict()
    {
        await using var database = new InventoryPostingLocalDb();
        var seed = await SeedAsync(database);

        var outcomes = await Task.WhenAll(
            ConfirmAsync(database, seed, seed.FirstLineId,
                seed.FirstVariantId, seed.FirstConversionId),
            ConfirmAsync(database, seed, seed.SecondLineId,
                seed.SecondVariantId, seed.SecondConversionId));

        outcomes.Count(x => x is null).Should().Be(1);
        outcomes.Count(x => x is BusinessRuleException).Should().Be(1);
        await using var verify = database.CreateTenantContext(seed.StoreId);
        (await verify.InputInvoiceItemCatalogMaps.CountAsync()).Should().Be(1);
        (await verify.PurchaseReceiptAuditEvents.CountAsync(x =>
            x.EventType == PurchaseReceiptAuditEventType.InputInvoiceItemMappingConfirmed))
            .Should().Be(1);
    }

    [Fact]
    public async Task Concurrent_distinct_units_for_same_code_create_independent_durable_rows()
    {
        await using var database = new InventoryPostingLocalDb();
        var seed = await SeedMultiUnitAsync(database);

        var outcomes = await Task.WhenAll(seed.Targets.Select(target =>
            ConfirmAsync(database, seed.StoreId, seed.ReceiptId,
                target.LineId, target.DetailId, seed.VariantId,
                target.ConversionId)));

        outcomes.Should().OnlyContain(x => x == null);
        await using var verify = database.CreateTenantContext(seed.StoreId);
        var mappings = await verify.InputInvoiceItemCatalogMaps
            .Where(x => x.NormalizedSupplierItemCode == "SQL-MULTI-UNIT")
            .OrderBy(x => x.NormalizedSupplierUnitName)
            .ToListAsync();
        mappings.Should().HaveCount(3);
        mappings.Select(x => x.NormalizedSupplierUnitName)
            .Should().BeEquivalentTo("HỘP", "LỐC", "THÙNG");
        mappings.Select(x => x.ProductUnitConversionId)
            .Should().BeEquivalentTo(seed.Targets.Select(x => x.ConversionId));
    }

    private static async Task<Exception?> ConfirmAsync(
        InventoryPostingLocalDb database,
        Seed seed,
        int lineId,
        int variantId,
        int conversionId)
        => await ConfirmAsync(database, seed.StoreId, seed.ReceiptId,
            lineId, seed.DetailId, variantId, conversionId);

    private static async Task<Exception?> ConfirmAsync(
        InventoryPostingLocalDb database,
        int storeId,
        int receiptId,
        int lineId,
        int detailId,
        int variantId,
        int conversionId)
    {
        await using var context = CreateContext(database, storeId);
        var repository = new InputInvoiceRepository(context);
        var service = new InputInvoiceItemCatalogMappingService(
            repository, new UserStub());
        await repository.BeginSupplierResolutionTransactionAsync();
        try
        {
            await service.ConfirmWithinTransactionAsync(
                storeId, receiptId, lineId, detailId,
                variantId, conversionId, null);
            await repository.SaveChangesAsync();
            await repository.CommitSupplierResolutionTransactionAsync();
            return null;
        }
        catch (Exception exception)
        {
            await repository.RollbackSupplierResolutionTransactionAsync();
            return exception;
        }
    }

    private static async Task<MultiUnitSeed> SeedMultiUnitAsync(
        InventoryPostingLocalDb database)
    {
        await database.MigrateAsync();
        var catalog = await database.SeedInventoryCatalogAsync();
        await using var db = database.CreateTenantContext(catalog.StoreId);
        var product = await db.Products.SingleAsync();
        var supplier = await db.Suppliers.SingleAsync();
        var variant = await db.ProductVariants.SingleAsync();
        var baseUnit = await db.Units.SingleAsync();
        baseUnit.Name = "Hộp";
        baseUnit.Code = $"HOP-{Guid.NewGuid():N}"[..20];
        var packUnit = new Unit
        {
            StoreId = catalog.StoreId,
            Code = $"LOC-{Guid.NewGuid():N}"[..20],
            Name = "Lốc",
            IsActive = true
        };
        var cartonUnit = new Unit
        {
            StoreId = catalog.StoreId,
            Code = $"THUNG-{Guid.NewGuid():N}"[..20],
            Name = "Thùng",
            IsActive = true
        };
        db.Units.AddRange(packUnit, cartonUnit);
        await db.SaveChangesAsync();
        var conversions = new[]
        {
            new ProductUnitConversion
            {
                StoreId = catalog.StoreId, ProductVariantId = variant.Id,
                UnitId = baseUnit.Id, Factor = 1m, IsBaseUnit = true, IsActive = true
            },
            new ProductUnitConversion
            {
                StoreId = catalog.StoreId, ProductVariantId = variant.Id,
                UnitId = packUnit.Id, Factor = 4m, IsActive = true
            },
            new ProductUnitConversion
            {
                StoreId = catalog.StoreId, ProductVariantId = variant.Id,
                UnitId = cartonUnit.Id, Factor = 48m, IsActive = true
            }
        };
        db.ProductUnitConversions.AddRange(conversions);
        await db.SaveChangesAsync();
        product.BaseUnitId = baseUnit.Id;

        var receipt = new StockDocument
        {
            StoreId = catalog.StoreId,
            DocumentNo = $"PN-MU-{Guid.NewGuid():N}"[..24],
            Type = StockDocumentType.Receipt,
            Status = StockDocumentStatus.PendingApproval,
            WarehouseId = catalog.WarehouseId,
            SupplierId = supplier.Id
        };
        var units = new[]
        {
            (Unit: baseUnit, Conversion: conversions[0], Name: "Hộp"),
            (Unit: packUnit, Conversion: conversions[1], Name: "Lốc"),
            (Unit: cartonUnit, Conversion: conversions[2], Name: "Thùng")
        };
        foreach (var (entry, index) in units.Select((value, index) => (value, index)))
        {
            receipt.Lines.Add(new StockDocumentLine
            {
                LineNo = index + 1,
                ProductVariantId = variant.Id,
                ProductUnitConversionId = entry.Conversion.Id,
                UnitId = entry.Unit.Id,
                UnitNameSnapshot = entry.Name,
                Factor = entry.Conversion.Factor,
                Quantity = 1m,
                BaseQuantity = entry.Conversion.Factor,
                UnitCost = 1m,
                LineTotal = 1m,
                ProductNameSnapshot = "Multi-unit target"
            });
        }
        var invoice = new InputInvoiceHead
        {
            StoreId = catalog.StoreId,
            InvoiceNumber = $"HD-MU-{Guid.NewGuid():N}"[..24],
            ResolvedSupplierId = supplier.Id,
            SupplierResolutionStatus = InputInvoiceSupplierResolutionStatus.Resolved
        };
        foreach (var (entry, index) in units.Select((value, index) => (value, index)))
        {
            invoice.Details.Add(new InputInvoiceDetail
            {
                LineNo = index + 1,
                SupplierItemCode = "SQL-MULTI-UNIT",
                NormalizedSupplierItemCode = "SQL-MULTI-UNIT",
                ItemName = "SQL Multi Unit",
                NormalizedItemName = "SQL MULTI UNIT",
                UnitName = entry.Name,
                NormalizedUnitName = entry.Name.ToUpperInvariant(),
                Quantity = 1m
            });
        }
        db.AddRange(receipt, invoice);
        await db.SaveChangesAsync();
        db.StockDocumentInputInvoiceMaps.Add(new StockDocumentInputInvoiceMap
        {
            StoreId = catalog.StoreId,
            StockDocumentId = receipt.Id,
            InputInvoiceHeadId = invoice.Id
        });
        await db.SaveChangesAsync();

        var targets = receipt.Lines.OrderBy(x => x.LineNo)
            .Zip(invoice.Details.OrderBy(x => x.LineNo), (line, detail) =>
                new MultiUnitTarget(line.Id, detail.Id,
                    line.ProductUnitConversionId!.Value))
            .ToArray();
        return new MultiUnitSeed(catalog.StoreId, receipt.Id, variant.Id, targets);
    }

    private static async Task<Seed> SeedAsync(InventoryPostingLocalDb database)
    {
        await database.MigrateAsync();
        var catalog = await database.SeedInventoryCatalogAsync();
        await using var db = database.CreateTenantContext(catalog.StoreId);
        var product = await db.Products.SingleAsync();
        var supplier = await db.Suppliers.SingleAsync();
        var baseUnit = await db.Units.SingleAsync();
        var firstVariant = await db.ProductVariants.SingleAsync();
        var firstConversion = new ProductUnitConversion
        {
            StoreId = catalog.StoreId,
            ProductVariantId = firstVariant.Id,
            UnitId = baseUnit.Id,
            Factor = 1m,
            IsBaseUnit = true,
            IsActive = true
        };
        var secondVariant = new ProductVariant
        {
            StoreId = catalog.StoreId,
            ProductId = product.Id,
            Sku = $"ITEM-MAP-2-{Guid.NewGuid():N}",
            ProductVariantName = "Second target",
            IsActive = true
        };
        db.AddRange(firstConversion, secondVariant);
        await db.SaveChangesAsync();
        var secondConversion = new ProductUnitConversion
        {
            StoreId = catalog.StoreId,
            ProductVariantId = secondVariant.Id,
            UnitId = baseUnit.Id,
            Factor = 1m,
            IsBaseUnit = true,
            IsActive = true
        };
        db.ProductUnitConversions.Add(secondConversion);
        await db.SaveChangesAsync();

        var receipt = new StockDocument
        {
            StoreId = catalog.StoreId,
            DocumentNo = $"PN-IM-{Guid.NewGuid():N}"[..24],
            Type = StockDocumentType.Receipt,
            Status = StockDocumentStatus.PendingApproval,
            WarehouseId = catalog.WarehouseId,
            SupplierId = supplier.Id
        };
        var firstLine = NewLine(firstVariant.Id, firstConversion.Id, baseUnit.Id, 1);
        var secondLine = NewLine(secondVariant.Id, secondConversion.Id, baseUnit.Id, 2);
        receipt.Lines.Add(firstLine);
        receipt.Lines.Add(secondLine);
        var invoice = new InputInvoiceHead
        {
            StoreId = catalog.StoreId,
            InvoiceNumber = $"HD-IM-{Guid.NewGuid():N}"[..24],
            ResolvedSupplierId = supplier.Id,
            SupplierResolutionStatus = InputInvoiceSupplierResolutionStatus.Resolved
        };
        var detail = new InputInvoiceDetail
        {
            LineNo = 1,
            SupplierItemCode = "SQL-CONCURRENT-ITEM",
            NormalizedSupplierItemCode = "SQL-CONCURRENT-ITEM",
            ItemName = "SQL Concurrent Item",
            NormalizedItemName = "SQL CONCURRENT ITEM",
            UnitName = baseUnit.Name,
            NormalizedUnitName = baseUnit.Name.ToUpperInvariant(),
            Quantity = 1m
        };
        invoice.Details.Add(detail);
        db.AddRange(receipt, invoice);
        await db.SaveChangesAsync();
        db.StockDocumentInputInvoiceMaps.Add(new StockDocumentInputInvoiceMap
        {
            StoreId = catalog.StoreId,
            StockDocumentId = receipt.Id,
            InputInvoiceHeadId = invoice.Id
        });
        await db.SaveChangesAsync();
        return new Seed(catalog.StoreId, receipt.Id, detail.Id,
            firstLine.Id, firstVariant.Id, firstConversion.Id,
            secondLine.Id, secondVariant.Id, secondConversion.Id);
    }

    private static StockDocumentLine NewLine(
        int variantId, int conversionId, int unitId, int lineNo)
        => new()
        {
            LineNo = lineNo,
            ProductVariantId = variantId,
            ProductUnitConversionId = conversionId,
            UnitId = unitId,
            UnitNameSnapshot = "R2 Unit",
            Factor = 1m,
            Quantity = 1m,
            BaseQuantity = 1m,
            UnitCost = 1m,
            LineTotal = 1m,
            ProductNameSnapshot = $"Target {lineNo}"
        };

    private static AppDbContext CreateContext(
        InventoryPostingLocalDb database,
        int storeId)
    {
        var tenant = new TenantContext();
        tenant.SetStore(storeId, "item-map-sql");
        return new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlServer(database.ConnectionString)
                .Options,
            tenant,
            new UserStub());
    }

    private sealed record Seed(
        int StoreId,
        int ReceiptId,
        int DetailId,
        int FirstLineId,
        int FirstVariantId,
        int FirstConversionId,
        int SecondLineId,
        int SecondVariantId,
        int SecondConversionId);

    private sealed record MultiUnitSeed(
        int StoreId,
        int ReceiptId,
        int VariantId,
        IReadOnlyList<MultiUnitTarget> Targets);

    private sealed record MultiUnitTarget(
        int LineId,
        int DetailId,
        int ConversionId);

    private sealed class UserStub : ICurrentUser
    {
        public int? UserId => 17;
        public string? UserName => "item-map-sql";
        public int? TerminalId => null;
        public string? TerminalCode => null;
        public bool IsAuthenticated => true;
    }
}
