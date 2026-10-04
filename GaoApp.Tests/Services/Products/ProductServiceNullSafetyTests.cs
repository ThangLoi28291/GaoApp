using GaoApp.Application.Common;
using GaoApp.Application.DTOs.Products;
using GaoApp.Application.Interfaces.Repositories.Products;
using GaoApp.Application.Interfaces.Services.Media;
using GaoApp.Application.Services.Products;
using GaoApp.Domain.Entities;

namespace GaoApp.Tests.Services.Products;

public sealed class ProductServiceNullSafetyTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task CreateAsync_should_reject_missing_name_without_writing(
        string? name)
    {
        var productRepo = new FakeProductRepository();
        var imageService = new FakeProductImageService();
        var variantRepo = new FakeProductVariantRepository();
        var service = CreateService(
            productRepo,
            imageService,
            variantRepo);

        var dto = new ProductCreateDto
        {
            Name = name,
            Alias = null
        };

        var result = await service.CreateAsync(
            storeId: 1,
            dto,
            userId: 99);

        Assert.True(result.IsFailure);
        Assert.True(result.HasValidationErrors);
        Assert.Single(result.ValidationErrors);

        Assert.Null(productRepo.AddedProduct);
        Assert.Equal(0, productRepo.SaveChangesCalls);
        Assert.Null(variantRepo.AddedVariant);
        Assert.Equal(0, imageService.CommitTempImagesCalls);
    }

    [Fact]
    public async Task CreateAsync_should_generate_alias_from_name_when_alias_is_blank()
    {
        var productRepo = new FakeProductRepository();
        var imageService = new FakeProductImageService();
        var variantRepo = new FakeProductVariantRepository();
        var service = CreateService(
            productRepo,
            imageService,
            variantRepo);

        var dto = new ProductCreateDto
        {
            Name = "  Sữa Chua Dâu  ",
            Alias = "   ",
            CategoryId = 1,
            SupplierId = 2,
            BaseUnitId = 3,
            BasePrice = 10_000m
        };

        var result = await service.CreateAsync(
            storeId: 1,
            dto,
            userId: 99);

        Assert.True(result.IsSuccess);
        Assert.Equal(101, result.Value);

        Assert.NotNull(productRepo.AddedProduct);
        Assert.Equal(
            "Sữa Chua Dâu",
            productRepo.AddedProduct!.Name);
        Assert.Equal(
            "sua-chua-dau",
            productRepo.AddedProduct.Alias);

        Assert.NotNull(variantRepo.AddedVariant);
        Assert.Equal(
            "sua-chua-dau",
            variantRepo.AddedVariant!.Sku);

        Assert.NotNull(productRepo.AddedConversion);
        Assert.NotNull(productRepo.AddedBarcode);
        Assert.StartsWith(
            "20",
            productRepo.AddedBarcode!.Barcode);

        Assert.Equal(1, imageService.CommitTempImagesCalls);
        Assert.Equal(101, imageService.LastCommittedProductId);
    }

    [Fact]
    public async Task UpdateAsync_should_return_not_found_before_input_validation()
    {
        var productRepo = new FakeProductRepository
        {
            DetailResult = null
        };

        var imageService = new FakeProductImageService();
        var variantRepo = new FakeProductVariantRepository();
        var service = CreateService(
            productRepo,
            imageService,
            variantRepo);

        var dto = NewUpdateRequest(
            name: null,
            alias: null);

        var result = await service.UpdateAsync(
            storeId: 1,
            dto,
            userId: 99);

        Assert.True(result.IsFailure);
        Assert.False(result.HasValidationErrors);
        Assert.Contains(
            "Không tìm thấy sản phẩm",
            result.Error.Message);

        Assert.Equal(1, productRepo.GetDetailCalls);
        Assert.Equal(0, productRepo.SaveChangesCalls);
        Assert.Equal(0, imageService.SyncEditCalls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task UpdateAsync_should_reject_missing_name_without_writing(
        string? name)
    {
        var productRepo = new FakeProductRepository
        {
            DetailResult = NewExistingProduct()
        };

        var imageService = new FakeProductImageService();
        var variantRepo = new FakeProductVariantRepository();
        var service = CreateService(
            productRepo,
            imageService,
            variantRepo);

        var dto = NewUpdateRequest(
            name,
            alias: "valid-alias");

        var result = await service.UpdateAsync(
            storeId: 1,
            dto,
            userId: 99);

        Assert.True(result.IsFailure);
        Assert.True(result.HasValidationErrors);
        Assert.Single(result.ValidationErrors);

        Assert.Equal(0, productRepo.SaveChangesCalls);
        Assert.Equal(0, imageService.SyncEditCalls);
        Assert.Equal(0, imageService.SetPrimaryCalls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task UpdateAsync_should_reject_missing_alias_without_writing(
        string? alias)
    {
        var productRepo = new FakeProductRepository
        {
            DetailResult = NewExistingProduct()
        };

        var imageService = new FakeProductImageService();
        var variantRepo = new FakeProductVariantRepository();
        var service = CreateService(
            productRepo,
            imageService,
            variantRepo);

        var dto = NewUpdateRequest(
            name: "Tên sản phẩm mới",
            alias);

        var result = await service.UpdateAsync(
            storeId: 1,
            dto,
            userId: 99);

        Assert.True(result.IsFailure);
        Assert.True(result.HasValidationErrors);
        Assert.Single(result.ValidationErrors);

        Assert.Equal(0, productRepo.SaveChangesCalls);
        Assert.Equal(0, imageService.SyncEditCalls);
        Assert.Equal(0, imageService.SetPrimaryCalls);
    }

    [Fact]
    public async Task CreateAsync_should_propagate_unexpected_repository_exception()
    {
        var expected = new IOException("synthetic infrastructure failure");
        var productRepo = new FakeProductRepository
        {
            SaveChangesException = expected
        };
        var service = CreateService(
            productRepo,
            new FakeProductImageService(),
            new FakeProductVariantRepository());

        var actual = await Assert.ThrowsAsync<IOException>(
            () => service.CreateAsync(
                storeId: 1,
                new ProductCreateDto
                {
                    Name = "Sản phẩm",
                    Alias = "san-pham",
                    CategoryId = 1,
                    SupplierId = 2,
                    BaseUnitId = 3,
                    BasePrice = 10_000m
                },
                userId: 99));

        Assert.Same(expected, actual);
    }

    [Fact]
    public async Task CreateAsync_should_propagate_technical_invalid_operation()
    {
        const string technicalDetail =
            "Server=private-sql;Password=synthetic-secret";
        var expected = new InvalidOperationException(technicalDetail);
        var productRepo = new FakeProductRepository
        {
            SaveChangesException = expected
        };
        var service = CreateService(
            productRepo,
            new FakeProductImageService(),
            new FakeProductVariantRepository());

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CreateAsync(
                storeId: 1,
                new ProductCreateDto
                {
                    Name = "Sản phẩm",
                    Alias = "san-pham",
                    CategoryId = 1,
                    SupplierId = 2,
                    BaseUnitId = 3,
                    BasePrice = 10_000m
                },
                userId: 99));

        Assert.Same(expected, actual);
    }

    [Fact]
    public async Task CreateAsync_should_propagate_caller_cancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var productRepo = new FakeProductRepository
        {
            SaveChangesException = new OperationCanceledException(cts.Token)
        };
        var service = CreateService(
            productRepo,
            new FakeProductImageService(),
            new FakeProductVariantRepository());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.CreateAsync(
                storeId: 1,
                new ProductCreateDto
                {
                    Name = "Sản phẩm",
                    Alias = "san-pham",
                    CategoryId = 1,
                    SupplierId = 2,
                    BaseUnitId = 3,
                    BasePrice = 10_000m
                },
                userId: 99,
                ct: cts.Token));
    }

    [Fact]
    public async Task UpdateAsync_should_reject_malformed_image_state_without_empty_success()
    {
        var productRepo = new FakeProductRepository
        {
            DetailResult = NewExistingProduct()
        };
        var imageService = new FakeProductImageService();
        var service = CreateService(
            productRepo,
            imageService,
            new FakeProductVariantRepository());
        var request = NewUpdateRequest("Tên sản phẩm", "ten-san-pham");
        request.ImagesStateJson = "{malformed-json";

        var result = await service.UpdateAsync(
            storeId: 1,
            request,
            userId: 99);

        Assert.True(result.IsFailure);
        Assert.Contains(
            "trạng thái ảnh không hợp lệ",
            result.Error.Message,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Product.BusinessRule", result.Error.Code);
        Assert.Equal(0, imageService.SyncEditCalls);
        Assert.Equal(0, productRepo.SaveChangesCalls);
    }

    private static ProductService CreateService(
        FakeProductRepository productRepo,
        FakeProductImageService imageService,
        FakeProductVariantRepository variantRepo)
    {
        return new ProductService(
            productRepo,
            imageService,
            variantRepo,
            new GaoApp.Tests.Media.MediaTestUnitOfWork());
    }

    private static UpdateProductRequest NewUpdateRequest(
        string? name,
        string? alias)
    {
        return new UpdateProductRequest
        {
            Id = 10,
            Name = name,
            Alias = alias,
            CategoryId = 1,
            SupplierId = 2,
            BaseUnitId = 3,
            BasePrice = 10_000m
        };
    }

    private static Product NewExistingProduct()
    {
        return new Product
        {
            Id = 10,
            StoreId = 1,
            Name = "Tên cũ",
            Alias = "ten-cu",
            CategoryId = 1,
            SupplierId = 2,
            BaseUnitId = 3,
            BasePrice = 10_000m
        };
    }

    private sealed class FakeProductRepository : IProductRepository
    {
        public Task SynchronizeBaseUnitAsync(int storeId, int productId, int unitId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<PagedResult<ProductListItemDto>> SearchCatalogAsync(int storeId, string? search, int? categoryId,
        bool? isActive, bool? isSellable, int page, int pageSize, ProductListFilters filters, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<List<ProductFilterOptionDto>> FilterOptionsAsync(int storeId, string kind, string? term, int? selectedId, CancellationToken ct = default) => throw new NotSupportedException();


        public Product? DetailResult { get; set; }

        public Product? AddedProduct { get; private set; }

        public ProductUnitConversion? AddedConversion { get; private set; }

        public ProductVariantUnitBarcode? AddedBarcode { get; private set; }

        public int GetDetailCalls { get; private set; }

        public int SaveChangesCalls { get; private set; }

        public bool AliasExists { get; set; }

        public Exception? SaveChangesException { get; set; }

        public Task<PagedResult<ProductListItemDto>> GetPagedAsync(
            int storeId,
            string? search,
            int page,
            int pageSize,
            CancellationToken ct = default)
        {
            throw new NotSupportedException();
        }

        public Task<PagedResult<ProductListItemDto>> GetPagedAsync(
            int storeId,
            string? search,
            int? categoryId,
            bool? isActive,
            bool? isSellable,
            int page,
            int pageSize,
            CancellationToken ct = default)
        {
            throw new NotSupportedException();
        }

        public Task<(int TotalItems, int PosAllowedItems, int NotForPosItems, int InactiveItems)> GetSummaryAsync(
            int storeId,
            CancellationToken ct = default)
        {
            throw new NotSupportedException();
        }

        public Task<Product?> GetDetailAsync(
            int storeId,
            int id,
            CancellationToken ct = default)
        {
            GetDetailCalls++;
            return Task.FromResult(DetailResult);
        }

        public Task<bool> ExistsAliasAsync(
            int storeId,
            string alias,
            int? excludeId,
            CancellationToken ct = default)
        {
            return Task.FromResult(AliasExists);
        }

        public Task AddAsync(
            Product entity,
            CancellationToken ct = default)
        {
            entity.Id = 101;
            AddedProduct = entity;
            return Task.CompletedTask;
        }

        public Task AddProductUnitConversionAsync(
            ProductUnitConversion entity,
            CancellationToken ct = default)
        {
            entity.Id = 301;
            AddedConversion = entity;
            return Task.CompletedTask;
        }

        public Task AddProductVariantUnitBarcodeAsync(
            ProductVariantUnitBarcode entity,
            CancellationToken ct = default)
        {
            entity.Id = 401;
            AddedBarcode = entity;
            return Task.CompletedTask;
        }

        public Task<int> SaveChangesAsync(
            CancellationToken ct = default)
        {
            SaveChangesCalls++;

            if (SaveChangesException is not null)
                return Task.FromException<int>(SaveChangesException);

            return Task.FromResult(SaveChangesCalls);
        }

        public Task<bool> ToggleStatusAsync(
            int storeId,
            int id,
            int? userId,
            CancellationToken ct = default)
        {
            throw new NotSupportedException();
        }

        public Task<bool> SoftDeleteAsync(
            int storeId,
            int id,
            int? userId,
            CancellationToken ct = default)
        {
            throw new NotSupportedException();
        }

        public Task<bool> ExistsVariantUnitBarcodeAsync(
            int storeId,
            string barcode,
            CancellationToken ct = default)
        {
            return Task.FromResult(false);
        }
    }

    private sealed class FakeProductImageService : IProductImageService
    {
        public int CommitTempImagesCalls { get; private set; }

        public int SyncEditCalls { get; private set; }

        public int SetPrimaryCalls { get; private set; }

        public int? LastCommittedProductId { get; private set; }

        public Task CommitTempImagesAsync(
            int storeId,
            int productId,
            List<string> tempTokens,
            string? primaryToken,
            int? userId,
            CancellationToken ct = default)
        {
            CommitTempImagesCalls++;
            LastCommittedProductId = productId;
            return Task.CompletedTask;
        }

        public Task SyncEditAsync(
            int storeId,
            int productId,
            List<ProductImageStateDto> orderedItems,
            string? primaryKey,
            int? userId,
            CancellationToken ct = default)
        {
            SyncEditCalls++;
            return Task.CompletedTask;
        }

        public Task SetPrimaryAsync(
            int storeId,
            int productId,
            string? primaryKey,
            CancellationToken ct = default)
        {
            SetPrimaryCalls++;
            return Task.CompletedTask;
        }

        public Task ClearImageTrackingAsync(
            int storeId,
            int productId)
        {
            return Task.CompletedTask;
        }

        public Task<List<ProductImageListItemDto>>
            GetImagesForVariantAsync(
                int storeId,
                int productId,
                CancellationToken ct = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FakeProductVariantRepository
        : IProductVariantRepository
    {
        public ProductVariant? AddedVariant { get; private set; }

        public int SaveChangesCalls { get; private set; }

        public Task<List<AttributeWithValuesDto>>
            GetAttributesWithValuesAsync(
                int storeId,
                CancellationToken ct)
        {
            throw new NotSupportedException();
        }

        public Task<List<ProductVariant>> GetByProductAsync(
            int storeId,
            int productId,
            CancellationToken ct)
        {
            throw new NotSupportedException();
        }

        public Task<bool> ExistsSkuAsync(
            int storeId,
            string sku,
            int? excludeVariantId,
            CancellationToken ct)
        {
            throw new NotSupportedException();
        }

        public Task AddAsync(
            ProductVariant entity,
            CancellationToken ct)
        {
            entity.Id = 201;
            AddedVariant = entity;
            return Task.CompletedTask;
        }

        public Task RemoveRangeAsync(
            IEnumerable<ProductVariant> entities,
            CancellationToken ct)
        {
            throw new NotSupportedException();
        }

        public Task RemoveMappingsByVariantAsync(
            int storeId,
            int variantId,
            CancellationToken ct)
        {
            throw new NotSupportedException();
        }

        public Task AddMappingsAsync(
            IEnumerable<ProductVariantAttributeValue> mappings,
            CancellationToken ct)
        {
            throw new NotSupportedException();
        }

        public Task SaveChangesAsync(
            CancellationToken ct)
        {
            SaveChangesCalls++;
            return Task.CompletedTask;
        }

        public Task<Dictionary<int, int>>
            MapValueIdsToAttributeIdsAsync(
                int storeId,
                IEnumerable<int> valueIds,
                CancellationToken ct = default)
        {
            throw new NotSupportedException();
        }

        public Task SaveVariantsAsync(
            int storeId,
            int productId,
            List<ProductVariantRowDto> variants,
            int? userId,
            CancellationToken ct = default)
        {
            throw new NotSupportedException();
        }

        public Task<bool> ToggleStatusAsync(
            int storeId,
            int variantId,
            int? userId,
            CancellationToken ct)
        {
            throw new NotSupportedException();
        }

        public Task<bool> SoftDeleteVariantAsync(
            int storeId,
            int variantId,
            int? userId,
            CancellationToken ct)
        {
            throw new NotSupportedException();
        }

        public Task<bool> SetVariantImageAsync(
            int storeId,
            int variantId,
            int? primaryProductImageId,
            int? userId,
            CancellationToken ct)
        {
            throw new NotSupportedException();
        }

        public Task<ProductVariant?> GetActiveWithProductAsync(
            int variantId,
            CancellationToken ct = default)
        {
            throw new NotSupportedException();
        }

        public Task<int?> ResolveVariantIdByBarcodeHistoryAsync(
            string barcode,
            CancellationToken ct = default)
        {
            throw new NotSupportedException();
        }

        public Task<List<ProductVariant>> SearchForPOSAsync(
            string keyword,
            int take = 20,
            CancellationToken ct = default)
        {
            throw new NotSupportedException();
        }

        public Task<List<ProductVariant>>
            SearchForStockDocumentAsync(
                string keyword,
                int take = 20,
                CancellationToken ct = default)
        {
            throw new NotSupportedException();
        }
    }
}
