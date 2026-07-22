using GaoApp.Application.Common.Interfaces;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Repositories.Products;
using GaoApp.Infrastructure.Tenant;
using GaoApp.Tests.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Products;

public sealed class ProductVariantUnitBarcodeRepositoryTests
{
    [Fact]
    public async Task FindByBarcodeAsync_should_trim_input_and_load_active_graph()
    {
        var options = CreateOptions();

        await using (var seedContext = CreateContext(options, storeId: 1))
        {
            SeedBarcodeGraph(
                seedContext,
                storeId: 1,
                barcode: "8930000000001");

            await seedContext.SaveChangesAsync();
        }

        await using var readContext = CreateContext(options, storeId: 1);
        var repository =
            new ProductVariantUnitBarcodeRepository(readContext);

        var result = await repository.FindByBarcodeAsync(
            storeId: 1,
            barcode: "  8930000000001  ");

        Assert.NotNull(result);
        Assert.Equal("8930000000001", result!.Barcode);
        Assert.Equal(1, result.StoreId);
        Assert.True(result.IsActive);
        Assert.False(result.IsDeleted);

        Assert.NotNull(result.ProductUnitConversion);
        Assert.NotNull(result.ProductUnitConversion.Unit);
        Assert.NotNull(result.ProductUnitConversion.ProductVariant);
        Assert.NotNull(
            result.ProductUnitConversion.ProductVariant.Product);
        Assert.NotNull(
            result.ProductUnitConversion.ProductVariant.Product.BaseUnit);
    }

    [Fact]
    public async Task Lookup_methods_should_return_empty_result_for_blank_or_missing_barcode()
    {
        var options = CreateOptions();

        await using var context = CreateContext(options, storeId: 1);
        var repository =
            new ProductVariantUnitBarcodeRepository(context);

        var blankLookup = await repository.FindByBarcodeAsync(
            storeId: 1,
            barcode: "   ");

        var blankExists = await repository.ExistsBarcodeAsync(
            storeId: 1,
            barcode: "   ");

        var blankDuplicate =
            await repository.FindDuplicateWithDetailsAsync(
                storeId: 1,
                barcode: "   ");

        var missingLookup = await repository.FindByBarcodeAsync(
            storeId: 1,
            barcode: "NOT-FOUND");

        var missingExists = await repository.ExistsBarcodeAsync(
            storeId: 1,
            barcode: "NOT-FOUND");

        Assert.Null(blankLookup);
        Assert.False(blankExists);
        Assert.Null(blankDuplicate);
        Assert.Null(missingLookup);
        Assert.False(missingExists);
    }

    [Fact]
    public async Task Soft_deleted_barcode_should_be_hidden_from_active_lookup_but_remain_duplicate()
    {
        var options = CreateOptions();
        int deletedBarcodeId;

        await using (var seedContext = CreateContext(
                         options,
                         storeId: 1))
        {
            // Bước 1: lưu barcode active bình thường.
            var deletedBarcode = SeedBarcodeGraph(
                seedContext,
                storeId: 1,
                barcode: "SOFT-DELETED");

            await seedContext.SaveChangesAsync();

            deletedBarcodeId = deletedBarcode.Id;

            // Bước 2: xóa qua đúng flow DbContext.
            // AppDbContext sẽ chuyển Deleted thành soft-delete.
            seedContext.ProductVariantUnitBarcodes.Remove(
                deletedBarcode);

            await seedContext.SaveChangesAsync();

            // Xác nhận dữ liệu test đã thực sự soft-delete.
            Assert.True(deletedBarcode.IsDeleted);
            Assert.NotNull(deletedBarcode.DeletedAtUtc);
        }

        await using var readContext = CreateContext(
            options,
            storeId: 1);

        var repository =
            new ProductVariantUnitBarcodeRepository(readContext);

        var activeLookup = await repository.FindByBarcodeAsync(
            storeId: 1,
            barcode: "SOFT-DELETED");

        var exists = await repository.ExistsBarcodeAsync(
            storeId: 1,
            barcode: "SOFT-DELETED");

        var duplicate =
            await repository.FindDuplicateWithDetailsAsync(
                storeId: 1,
                barcode: "SOFT-DELETED");

        var existsWhenExcluded =
            await repository.ExistsBarcodeAsync(
                storeId: 1,
                barcode: "SOFT-DELETED",
                excludeId: deletedBarcodeId);

        // Lookup active phải ẩn barcode đã soft-delete.
        Assert.Null(activeLookup);

        // Duplicate check dùng IgnoreQueryFilters,
        // nên vẫn phải phát hiện barcode cũ.
        Assert.True(exists);

        Assert.NotNull(duplicate);
        Assert.Equal(deletedBarcodeId, duplicate!.Id);
        Assert.True(duplicate.IsDeleted);

        // Khi exclude chính bản ghi đó thì không còn duplicate.
        Assert.False(existsWhenExcluded);
    }

    [Fact]
    public async Task Duplicate_lookup_should_find_same_store_barcode_and_honor_exclude_id()
    {
        var options = CreateOptions();
        int barcodeId;

        await using (var seedContext = CreateContext(options, storeId: 1))
        {
            var barcode = SeedBarcodeGraph(
                seedContext,
                storeId: 1,
                barcode: "DUPLICATE-001");

            await seedContext.SaveChangesAsync();
            barcodeId = barcode.Id;
        }

        await using var readContext = CreateContext(options, storeId: 1);
        var repository =
            new ProductVariantUnitBarcodeRepository(readContext);

        var duplicate =
            await repository.FindDuplicateWithDetailsAsync(
                storeId: 1,
                barcode: "  DUPLICATE-001  ");

        var excludedDuplicate =
            await repository.FindDuplicateWithDetailsAsync(
                storeId: 1,
                barcode: "DUPLICATE-001",
                excludeId: barcodeId);

        Assert.NotNull(duplicate);
        Assert.Equal(barcodeId, duplicate!.Id);
        Assert.NotNull(duplicate.ProductUnitConversion);
        Assert.NotNull(duplicate.ProductUnitConversion.Unit);
        Assert.NotNull(
            duplicate.ProductUnitConversion.ProductVariant.Product);

        Assert.Null(excludedDuplicate);
    }

    [Fact]
    public async Task Barcode_lookup_should_not_cross_store_boundary()
    {
        var options = CreateOptions();

        await using (var storeOneContext =
                     CreateContext(options, storeId: 1))
        {
            SeedBarcodeGraph(
                storeOneContext,
                storeId: 1,
                barcode: "SHARED-BARCODE");

            await storeOneContext.SaveChangesAsync();
        }

        await using (var storeTwoContext =
                     CreateContext(options, storeId: 2))
        {
            SeedBarcodeGraph(
                storeTwoContext,
                storeId: 2,
                barcode: "SHARED-BARCODE");

            SeedBarcodeGraph(
                storeTwoContext,
                storeId: 2,
                barcode: "STORE-TWO-ONLY");

            await storeTwoContext.SaveChangesAsync();
        }

        await using var readContext = CreateContext(options, storeId: 1);
        var repository =
            new ProductVariantUnitBarcodeRepository(readContext);

        var sameStore = await repository.FindByBarcodeAsync(
            storeId: 1,
            barcode: "SHARED-BARCODE");

        var otherStoreLookup = await repository.FindByBarcodeAsync(
            storeId: 1,
            barcode: "STORE-TWO-ONLY");

        var otherStoreExists = await repository.ExistsBarcodeAsync(
            storeId: 1,
            barcode: "STORE-TWO-ONLY");

        var otherStoreDuplicate =
            await repository.FindDuplicateWithDetailsAsync(
                storeId: 1,
                barcode: "STORE-TWO-ONLY");

        Assert.NotNull(sameStore);
        Assert.Equal(1, sameStore!.StoreId);

        Assert.Null(otherStoreLookup);
        Assert.False(otherStoreExists);
        Assert.Null(otherStoreDuplicate);
    }

    private static ProductVariantUnitBarcode SeedBarcodeGraph(
     InMemoryAppDbContext context,
     int storeId,
     string barcode)
    {
        var unit = new Unit
        {
            StoreId = storeId,
            Code = $"UNIT-{storeId}-{Guid.NewGuid():N}",
            Name = $"Unit {storeId}",
            IsActive = true,
            IsBase = true,
            RowVersion = new byte[8]
        };

        var product = new Product
        {
            StoreId = storeId,
            Name = $"Product {storeId}",
            Alias = $"product-{storeId}-{Guid.NewGuid():N}",
            CategoryId = 1,
            SupplierId = 1,
            BaseUnit = unit,
            BasePrice = 10_000m,
            IsActive = true,
            RowVersion = new byte[8]
        };

        var variant = new ProductVariant
        {
            StoreId = storeId,
            Product = product,
            Sku = $"SKU-{storeId}-{Guid.NewGuid():N}",
            ProductVariantName = $"Variant {storeId}",
            IsActive = true,
            RowVersion = new byte[8]
        };

        var conversion = new ProductUnitConversion
        {
            StoreId = storeId,
            ProductVariant = variant,
            Unit = unit,
            Factor = 1m,
            IsBaseUnit = true,
            IsDefaultForSale = true,
            IsActive = true,
            RowVersion = new byte[8]
        };

        var barcodeEntity = new ProductVariantUnitBarcode
        {
            StoreId = storeId,
            ProductUnitConversion = conversion,
            Barcode = barcode,
            IsPrimary = true,
            IsActive = true,   
            RowVersion = new byte[8]
        };

        conversion.Barcodes.Add(barcodeEntity);
        variant.UnitConversions.Add(conversion);
        product.Variants.Add(variant);

        context.Products.Add(product);

        return barcodeEntity;
    }

    private static DbContextOptions<InMemoryAppDbContext> CreateOptions()
    {
        return new DbContextOptionsBuilder<InMemoryAppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
    }

    private static InMemoryAppDbContext CreateContext(
        DbContextOptions<InMemoryAppDbContext> options,
        int storeId)
    {
        var tenant = new TenantContext();
        tenant.SetStore(storeId, $"store-{storeId}");

        var context = new InMemoryAppDbContext(
            options,
            tenant,
            new TestCurrentUser());

        context.VerifyRowVersionConfiguration();

        return context;
    }

    private sealed class TestCurrentUser : ICurrentUser
    {
        public int? UserId => 99;

        public string? UserName => "barcode-repository-test";

        public int? TerminalId => null;

        public string? TerminalCode => null;

        public bool IsAuthenticated => true;
    }
}
