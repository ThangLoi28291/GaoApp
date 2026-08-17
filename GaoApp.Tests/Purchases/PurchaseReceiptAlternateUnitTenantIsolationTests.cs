using FluentAssertions;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Repositories.Inventory;
using GaoApp.Tests.Configuration;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Purchases;

public sealed class PurchaseReceiptAlternateUnitTenantIsolationTests
{
    [Fact]
    public async Task Saved_snapshot_survives_master_factor_change_but_rejects_wrong_variant()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync();
        var seed = await database.SeedInventoryCatalogAsync();
        await using var db = database.CreateTenantContext(seed.StoreId);
        var variant = await db.ProductVariants.Include(x => x.Product).SingleAsync(x => x.Id == seed.ProductVariantId);
        var unitId = variant.Product.BaseUnitId;
        var conversion = new ProductUnitConversion
        {
            StoreId = seed.StoreId, ProductVariantId = variant.Id, UnitId = unitId,
            Factor = 1m, IsBaseUnit = true, IsActive = true
        };
        db.ProductUnitConversions.Add(conversion);
        await db.SaveChangesAsync();
        var document = CreateDocument(seed, variant.Id, unitId, conversion.Id);
        db.StockDocuments.Add(document);
        await db.SaveChangesAsync();
        var repository = new StockDocumentRepository(db);

        conversion.Factor = 2m;
        await db.SaveChangesAsync();
        (await repository.PurchaseReceiptLineSnapshotsBelongToStoreAsync(
            seed.StoreId, document.Id, [document.Lines.Single().Id])).Should().BeTrue();

        var wrongVariant = new ProductVariant
        {
            StoreId = seed.StoreId, ProductId = variant.ProductId,
            Sku = $"WRONG-{Guid.NewGuid():N}", CostPrice = 1m, Price = 1m, IsActive = true
        };
        db.ProductVariants.Add(wrongVariant);
        await db.SaveChangesAsync();
        var wrongConversion = new ProductUnitConversion
        {
            StoreId = seed.StoreId, ProductVariantId = wrongVariant.Id, UnitId = unitId,
            Factor = 1m, IsBaseUnit = true, IsActive = true
        };
        db.ProductUnitConversions.Add(wrongConversion);
        await db.SaveChangesAsync();
        document.Lines.Single().ProductUnitConversionId = wrongConversion.Id;
        await db.SaveChangesAsync();

        (await repository.PurchaseReceiptLineSnapshotsBelongToStoreAsync(
            seed.StoreId, document.Id, [document.Lines.Single().Id])).Should().BeFalse();
    }

    [Fact]
    public async Task Foreign_store_unit_and_conversion_fail_snapshot_ownership_check()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync();
        var local = await database.SeedInventoryCatalogAsync();
        var foreign = await database.SeedInventoryCatalogAsync();
        int documentId;
        int lineId;
        await using (var localDb = database.CreateTenantContext(local.StoreId))
        {
            var variant = await localDb.ProductVariants.Include(x => x.Product)
                .SingleAsync(x => x.Id == local.ProductVariantId);
            var conversion = new ProductUnitConversion
            {
                StoreId = local.StoreId, ProductVariantId = variant.Id,
                UnitId = variant.Product.BaseUnitId, Factor = 1m, IsBaseUnit = true, IsActive = true
            };
            localDb.ProductUnitConversions.Add(conversion);
            await localDb.SaveChangesAsync();
            var document = CreateDocument(local, variant.Id, variant.Product.BaseUnitId, conversion.Id);
            localDb.StockDocuments.Add(document);
            await localDb.SaveChangesAsync();
            documentId = document.Id;
            lineId = document.Lines.Single().Id;
        }

        int foreignUnitId;
        int foreignConversionId;
        await using (var foreignDb = database.CreateTenantContext(foreign.StoreId))
        {
            var variant = await foreignDb.ProductVariants.Include(x => x.Product)
                .SingleAsync(x => x.Id == foreign.ProductVariantId);
            var conversion = new ProductUnitConversion
            {
                StoreId = foreign.StoreId, ProductVariantId = variant.Id,
                UnitId = variant.Product.BaseUnitId, Factor = 1m, IsBaseUnit = true, IsActive = true
            };
            foreignDb.ProductUnitConversions.Add(conversion);
            await foreignDb.SaveChangesAsync();
            foreignUnitId = variant.Product.BaseUnitId;
            foreignConversionId = conversion.Id;
        }

        await database.ExecuteAsync(
            $"UPDATE [StockDocumentLine] SET [UnitId] = {foreignUnitId}, [ProductUnitConversionId] = {foreignConversionId} WHERE [Id] = {lineId}");
        await using var verifyDb = database.CreateTenantContext(local.StoreId);
        var repository = new StockDocumentRepository(verifyDb);
        (await repository.PurchaseReceiptLineSnapshotsBelongToStoreAsync(
            local.StoreId, documentId, [lineId])).Should().BeFalse();
    }

    [Fact]
    public void Conversion_resolution_is_store_variant_and_unit_scoped_with_safe_errors()
    {
        var root = FindRepositoryRoot();
        var service = File.ReadAllText(Path.Combine(root, "GaoApp.Application", "Services", "Inventory", "StockDocumentService.cs"));

        Assert.Contains("x.ProductVariantId == variant.Id", service);
        Assert.Contains("x.StoreId == order.StoreId", service);
        Assert.Contains("x.Unit.StoreId == order.StoreId", service);
        Assert.Contains("x.UnitId == unitId", service);
        Assert.Contains("candidates.Length != 1", service);
        Assert.Contains("Không tìm thấy quy đổi hợp lệ cho đơn vị nhận.", service);
        Assert.DoesNotContain("foreign", service, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "GaoApp.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static StockDocument CreateDocument(
        InventoryPostingSeed seed,
        int variantId,
        int unitId,
        int conversionId)
    {
        var document = new StockDocument
        {
            StoreId = seed.StoreId, DocumentNo = $"NK-{Guid.NewGuid():N}"[..20],
            Type = StockDocumentType.Receipt, Status = StockDocumentStatus.Draft,
            ReceiptSource = PurchaseReceiptSource.PurchaseOrder,
            WarehouseId = seed.WarehouseId, DocumentDate = DateTime.UtcNow
        };
        document.Lines.Add(new StockDocumentLine
        {
            LineNo = 1, ProductVariantId = variantId, UnitId = unitId,
            ProductUnitConversionId = conversionId, UnitNameSnapshot = "base",
            Factor = 1m, Quantity = 1m, BaseQuantity = 1m,
            ProductNameSnapshot = "Tenant-safe product"
        });
        return document;
    }
}
