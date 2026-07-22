using GaoApp.Application.Common.Interfaces;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Repositories.Purchases;
using GaoApp.Infrastructure.Tenant;
using GaoApp.Tests.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Purchases;

public sealed class PurchaseRequestRepositoryNullNavigationTests
{
    [Fact]
    public async Task GetDetailAsync_should_load_optional_purchase_line_graph_safely()
    {
        var options = CreateOptions();
        int requestId;

        await using (var seedContext = CreateContext(
                         options,
                         storeId: 1))
        {
            var request = NewRequestWithOptionalLineGraph(
                storeId: 1,
                requestNumber: "PR-W5-OPTIONAL");

            seedContext.PurchaseRequests.Add(request);
            await seedContext.SaveChangesAsync();

            requestId = request.Id;
        }

        await using var readContext = CreateContext(
            options,
            storeId: 1);

        var repository = new PurchaseRequestRepository(readContext);

        var result = await repository.GetDetailAsync(
            requestId,
            tracking: false);

        Assert.NotNull(result);
        Assert.Equal(2, result!.Lines.Count);

        var catalogLine = Assert.Single(
            result.Lines,
            line => line.LineNo == 1);

        Assert.NotNull(catalogLine.ProductVariant);
        Assert.NotNull(catalogLine.ProductVariant!.Product);
        Assert.NotNull(catalogLine.ProductVariant.Product.Supplier);
        Assert.Equal(
            "Nhà cung cấp W5",
            catalogLine.ProductVariant.Product.Supplier.Name);

        // Tax là navigation optional của Product.
        Assert.Null(catalogLine.ProductVariant.Product.Tax);

        Assert.NotNull(catalogLine.ProductUnitConversion);
        Assert.NotNull(catalogLine.ProductUnitConversion!.Unit);
        Assert.Equal(
            "Cái",
            catalogLine.ProductUnitConversion.Unit.Name);

        // Dòng snapshot/legacy có thể không còn liên kết catalog.
        var snapshotOnlyLine = Assert.Single(
            result.Lines,
            line => line.LineNo == 2);

        Assert.Null(snapshotOnlyLine.ProductVariant);
        Assert.Null(snapshotOnlyLine.ProductUnitConversion);
    }

    [Fact]
    public async Task GetDetailAsync_should_not_read_purchase_request_from_other_store()
    {
        var options = CreateOptions();
        int storeTwoRequestId;

        await using (var storeTwoSeedContext = CreateContext(
                         options,
                         storeId: 2))
        {
            var request = new PurchaseRequest
            {
                StoreId = 2,
                RequestNumber = "PR-W5-STORE-2",
                Title = "Yêu cầu mua của cửa hàng 2",
                RequestedByUserId = 99,
                RowVersion = new byte[8]
            };

            storeTwoSeedContext.PurchaseRequests.Add(request);
            await storeTwoSeedContext.SaveChangesAsync();

            storeTwoRequestId = request.Id;
        }

        await using var storeOneReadContext = CreateContext(
            options,
            storeId: 1);

        var repository =
            new PurchaseRequestRepository(storeOneReadContext);

        var result = await repository.GetDetailAsync(
            storeTwoRequestId,
            tracking: false);

        Assert.Null(result);
    }

    private static PurchaseRequest NewRequestWithOptionalLineGraph(
        int storeId,
        string requestNumber)
    {
        var supplier = new Supplier
        {
            StoreId = storeId,
            Code = "NCC-W5",
            Name = "Nhà cung cấp W5",
            IsActive = true,
            RowVersion = new byte[8]
        };

        var unit = new Unit
        {
            StoreId = storeId,
            Code = "CAI-W5",
            Name = "Cái",
            IsActive = true,
            IsBase = true,
            RowVersion = new byte[8]
        };

        var product = new Product
        {
            StoreId = storeId,
            Name = "Sản phẩm W5",
            Alias = "san-pham-w5",
            CategoryId = 1,
            Supplier = supplier,
            BaseUnit = unit,
            BasePrice = 10_000m,
            IsActive = true,
            RowVersion = new byte[8]
        };

        var variant = new ProductVariant
        {
            StoreId = storeId,
            Product = product,
            Sku = "SKU-W5",
            ProductVariantName = "Sản phẩm W5",
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

        var request = new PurchaseRequest
        {
            StoreId = storeId,
            RequestNumber = requestNumber,
            Title = "Yêu cầu mua W5",
            RequestedByUserId = 99,
            RowVersion = new byte[8]
        };

        request.Lines.Add(new PurchaseRequestLine
        {
            StoreId = storeId,
            LineNo = 1,
            ProductVariant = variant,
            Unit = unit,
            ProductUnitConversion = conversion,
            ProductNameSnapshot = product.Name,
            SkuSnapshot = variant.Sku,
            UnitNameSnapshot = unit.Name,
            ConversionFactor = 1m,
            RequestedQuantity = 5m,
            RowVersion = new byte[8]
        });

        request.Lines.Add(new PurchaseRequestLine
        {
            StoreId = storeId,
            LineNo = 2,
            ProductNameSnapshot = "Hàng snapshot không có catalog link",
            SkuSnapshot = null,
            UnitNameSnapshot = "Cái",
            ConversionFactor = 1m,
            RequestedQuantity = 2m,
            RowVersion = new byte[8]
        });

        return request;
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

        public string? UserName => "purchase-request-w5-test";

        public int? TerminalId => null;

        public string? TerminalCode => null;

        public bool IsAuthenticated => true;
    }
}
