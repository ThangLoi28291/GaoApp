using FluentAssertions;
using GaoApp.Application.Common.Helpers;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Repositories.Products;
using GaoApp.Infrastructure.Tenant;
using GaoApp.Tests.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Products;

public class ProductVariantRepositorySearchTests
{
    [Fact]
    public async Task SearchForPOSAsync_should_limit_first_preserve_order_and_load_required_graph()
    {
        var options = CreateOptions();

        await using (var seedContext = CreateContext(options))
        {
            SeedSearchGraph(seedContext);
            await seedContext.SaveChangesAsync();
        }

        await using var searchContext = CreateContext(options);
        var repository = new ProductVariantRepository(searchContext);

        var results = await repository.SearchForPOSAsync("sua ch", take: 2);

        results.Should().HaveCount(2);
        results.Select(x => x.ProductVariantName).Should().Equal(
            "Sữa chua chanh dây 200 ml",
            "Sữa chua dâu 200 ml");

        var first = results[0];
        first.Product.Name.Should().Be("Nhóm hàng kiểm thử");
        first.Product.BaseUnit.Name.Should().Be("Cái");
        first.Product.ProductImages.Should().ContainSingle();
        first.Product.ProductImages.Single().MediaAsset.StoragePath.Should().Be("products/test.png");
        first.PrimaryProductImage.Should().NotBeNull();
        first.PrimaryProductImage!.MediaAsset.StoragePath.Should().Be("products/test.png");

        first.UnitConversions.Should().ContainSingle();
        var activeConversion = first.UnitConversions.Single();
        activeConversion.Unit.Name.Should().Be("Cái");
        activeConversion.Barcodes.Should().ContainSingle();
        activeConversion.Barcodes.Single().Barcode.Should().Be("8930000000001");
    }

    [Fact]
    public async Task SearchForPOSAsync_should_cap_requested_take_at_fifty()
    {
        var options = CreateOptions();

        await using (var seedContext = CreateContext(options))
        {
            var unit = NewUnit();
            var product = NewProduct(unit);

            for (var index = 1; index <= 55; index++)
            {
                product.Variants.Add(NewVariant(
                    product,
                    $"Sữa chua kiểm thử {index:00}",
                    $"SKU-{index:00}"));
            }

            seedContext.Products.Add(product);
            await seedContext.SaveChangesAsync();
        }

        await using var searchContext = CreateContext(options);
        var repository = new ProductVariantRepository(searchContext);

        var results = await repository.SearchForPOSAsync("sua chua kiem thu", take: 999);

        results.Should().HaveCount(50);
        results.First().Sku.Should().Be("SKU-01");
        results.Last().Sku.Should().Be("SKU-50");
    }

    [Fact]
    public async Task Pending_catalog_product_should_be_searchable_for_stock_but_hidden_from_pos()
    {
        var options = CreateOptions();

        await using (var seedContext = CreateContext(options))
        {
            var unit = NewUnit();
            var product = NewProduct(unit);
            product.Name = "Hàng mới nhập thử";
            product.Alias = "hang-moi-nhap-thu";
            product.IsSellable = false;
            product.Variants.Add(NewVariant(product, "Hàng mới nhập thử", "PENDING-001"));
            seedContext.Products.Add(product);
            await seedContext.SaveChangesAsync();
        }

        await using var searchContext = CreateContext(options);
        var repository = new ProductVariantRepository(searchContext);

        var posResults = await repository.SearchForPOSAsync("hang moi nhap thu", 20);
        var stockResults = await repository.SearchForStockDocumentAsync("hang moi nhap thu", 20);

        posResults.Should().BeEmpty();
        stockResults.Should().ContainSingle();
        stockResults.Single().Sku.Should().Be("PENDING-001");
        stockResults.Single().Product.IsSellable.Should().BeFalse();
    }

    [Fact]
    public async Task GetActiveWithProductAsync_should_return_variant_without_primary_image()
    {
        var options = CreateOptions();
        int variantId;

        await using (var seedContext = CreateContext(options))
        {
            var unit = NewUnit();
            var product = NewProduct(unit);

            var variant = NewVariant(
                product,
                "Sản phẩm không có ảnh riêng",
                "NO-IMAGE-001");

            product.Variants.Add(variant);
            seedContext.Products.Add(product);

            await seedContext.SaveChangesAsync();

            variantId = variant.Id;
        }

        await using var readContext = CreateContext(options);

        var repository =
            new ProductVariantRepository(readContext);

        var result = await repository.GetActiveWithProductAsync(
            variantId);

        result.Should().NotBeNull();
        result!.PrimaryProductImage.Should().BeNull();

        result.Product.Should().NotBeNull();
        result.Product.BaseUnit.Should().NotBeNull();
        result.Product.BaseUnit.Name.Should().Be("Cái");
    }

    private static void SeedSearchGraph(InMemoryAppDbContext context)
    {
        var unit = NewUnit();
        var product = NewProduct(unit);
        var mediaAsset = new MediaAsset
        {
            StoreId = 1,
            StoragePath = "products/test.png",
            OriginalFileName = "test.png",
            ContentType = "image/png",
            RowVersion = new byte[8]
        };
        var image = new ProductImage
        {
            StoreId = 1,
            Product = product,
            MediaAsset = mediaAsset,
            IsPrimary = true,
            RowVersion = new byte[8]
        };

        product.ProductImages.Add(image);

        var variants = new[]
        {
            NewVariant(product, "Sữa chua nha đam 200 ml", "SKU-03"),
            NewVariant(product, "Sữa chua dâu 200 ml", "SKU-02"),
            NewVariant(product, "Sữa chua chanh dây 200 ml", "SKU-01")
        };

        foreach (var variant in variants)
        {
            variant.PrimaryProductImage = image;

            var activeConversion = new ProductUnitConversion
            {
                StoreId = 1,
                ProductVariant = variant,
                Unit = unit,
                Factor = 1m,
                IsBaseUnit = true,
                IsDefaultForSale = true,
                IsActive = true,
                RowVersion = new byte[8]
            };
            activeConversion.Barcodes.Add(new ProductVariantUnitBarcode
            {
                StoreId = 1,
                Barcode = "8930000000001",
                IsPrimary = true,
                IsActive = true,
                RowVersion = new byte[8]
            });
            activeConversion.Barcodes.Add(new ProductVariantUnitBarcode
            {
                StoreId = 1,
                Barcode = "8930000000099",
                IsPrimary = false,
                IsActive = false,
                RowVersion = new byte[8]
            });

            variant.UnitConversions.Add(activeConversion);
            variant.UnitConversions.Add(new ProductUnitConversion
            {
                StoreId = 1,
                ProductVariant = variant,
                Unit = unit,
                Factor = 10m,
                IsActive = false,
                RowVersion = new byte[8]
            });
            product.Variants.Add(variant);
        }

        context.Products.Add(product);
    }

    private static Product NewProduct(Unit unit)
        => new()
        {
            StoreId = 1,
            Name = "Nhóm hàng kiểm thử",
            Alias = "nhom-hang-kiem-thu",
            CategoryId = 1,
            SupplierId = 1,
            BaseUnit = unit,
            BasePrice = 10_000m,
            IsActive = true,
            RowVersion = new byte[8]
        };

    private static ProductVariant NewVariant(Product product, string name, string sku)
        => new()
        {
            StoreId = 1,
            Product = product,
            ProductVariantName = name,
            ProductVariantNameNormalized = ProductVariantNameHelper.NormalizeForSearch(name),
            Sku = sku,
            Price = 10_000m,
            IsActive = true,
            RowVersion = new byte[8]
        };

    private static Unit NewUnit()
        => new()
        {
            StoreId = 1,
            Code = "CAI",
            Name = "Cái",
            IsActive = true,
            IsBase = true,
            RowVersion = new byte[8]
        };

    private static DbContextOptions<InMemoryAppDbContext> CreateOptions()
        => new DbContextOptionsBuilder<InMemoryAppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

    private static InMemoryAppDbContext CreateContext(
        DbContextOptions<InMemoryAppDbContext> options)
    {
        var tenant = new TenantContext();
        tenant.SetStore(1, "store-one");
        var context = new InMemoryAppDbContext(options, tenant, new TestCurrentUser());
        context.VerifyRowVersionConfiguration();
        return context;
    }

    private sealed class TestCurrentUser : ICurrentUser
    {
        public int? UserId => 99;
        public string? UserName => "pos-search-test";
        public int? TerminalId => null;
        public string? TerminalCode => null;
        public bool IsAuthenticated => true;
    }
}
