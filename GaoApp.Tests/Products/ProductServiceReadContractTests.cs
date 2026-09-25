using System.Reflection;
using FluentAssertions;
using GaoApp.Application.Common;
using GaoApp.Application.DTOs.Products;
using GaoApp.Application.Interfaces.Repositories.Products;
using GaoApp.Application.Services.Products;
using GaoApp.Domain.Entities;

namespace GaoApp.Tests.Products;

public sealed class ProductServiceReadContractTests
{
    [Fact]
    public async Task Existing_GetPagedAsync_should_preserve_current_read_contract()
    {
        var repository = new FakeProductRepository();
        var service = CreateService(repository);

        await service.GetPagedAsync(7, "milk", 2, 50, CancellationToken.None);

        repository.LastStoreId.Should().Be(7);
        repository.LastSearch.Should().Be("milk");
        repository.LastPage.Should().Be(2);
        repository.LastPageSize.Should().Be(50);
        repository.FilteredReadCalls.Should().Be(0);
    }

    [Fact]
    public async Task Filtered_GetPagedAsync_should_forward_category_lifecycle_and_cancellation()
    {
        var repository = new FakeProductRepository();
        var service = CreateService(repository);
        using var cts = new CancellationTokenSource();

        var method = typeof(ProductService).GetMethod(
            nameof(ProductService.GetPagedAsync),
            BindingFlags.Public | BindingFlags.Instance,
            binder: null,
            types:
            [
                typeof(int),
                typeof(string),
                typeof(int?),
                typeof(bool?),
                typeof(bool?),
                typeof(int),
                typeof(int),
                typeof(CancellationToken)
            ],
            modifiers: null);

        Assert.NotNull(method);

        var invocation = method.Invoke(
            service,
            [11, "SKU-A", 4, true, false, 3, 10, cts.Token]);

        var task = Assert.IsAssignableFrom<Task>(invocation);
        await task;

        repository.FilteredReadCalls.Should().Be(1);
        repository.LastStoreId.Should().Be(11);
        repository.LastSearch.Should().Be("SKU-A");
        repository.LastCategoryId.Should().Be(4);
        repository.LastIsActive.Should().BeTrue();
        repository.LastIsSellable.Should().BeFalse();
        repository.LastPage.Should().Be(3);
        repository.LastPageSize.Should().Be(10);
        repository.LastCancellationToken.Should().Be(cts.Token);
    }

    [Fact]
    public async Task GetSummaryAsync_should_forward_same_store_and_cancellation()
    {
        var repository = new FakeProductRepository
        {
            Summary = (TotalItems: 8, PosAllowedItems: 4, NotForPosItems: 3, InactiveItems: 1)
        };
        var service = CreateService(repository);
        using var cts = new CancellationTokenSource();

        var method = typeof(ProductService).GetMethod(
            "GetSummaryAsync",
            BindingFlags.Public | BindingFlags.Instance,
            binder: null,
            types: [typeof(int), typeof(CancellationToken)],
            modifiers: null);

        Assert.NotNull(method);

        var invocation = method.Invoke(service, [9, cts.Token]);
        var task = Assert.IsAssignableFrom<Task>(invocation);
        await task;

        var result = task.GetType().GetProperty("Result")?.GetValue(task);
        var summary = Assert.IsType<(
            int TotalItems,
            int PosAllowedItems,
            int NotForPosItems,
            int InactiveItems)>(result);

        summary.Should().Be((8, 4, 3, 1));
        repository.LastStoreId.Should().Be(9);
        repository.LastCancellationToken.Should().Be(cts.Token);
    }

    private static ProductService CreateService(FakeProductRepository repository)
        => new(
            repository,
            new FakeProductImageService(),
            new FakeProductVariantRepository(),
            new GaoApp.Tests.Media.MediaTestUnitOfWork());

    private sealed class FakeProductRepository : IProductRepository
    {
        public (int TotalItems, int PosAllowedItems, int NotForPosItems, int InactiveItems) Summary { get; init; }
        public int? LastStoreId { get; private set; }
        public string? LastSearch { get; private set; }
        public int? LastCategoryId { get; private set; }
        public bool? LastIsActive { get; private set; }
        public bool? LastIsSellable { get; private set; }
        public int? LastPage { get; private set; }
        public int? LastPageSize { get; private set; }
        public CancellationToken LastCancellationToken { get; private set; }
        public int FilteredReadCalls { get; private set; }

        public Task<PagedResult<ProductListItemDto>> GetPagedAsync(
            int storeId,
            string? search,
            int page,
            int pageSize,
            CancellationToken ct = default)
        {
            LastStoreId = storeId;
            LastSearch = search;
            LastPage = page;
            LastPageSize = pageSize;
            LastCancellationToken = ct;
            return Task.FromResult(EmptyPage(page, pageSize));
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
            FilteredReadCalls++;
            LastStoreId = storeId;
            LastSearch = search;
            LastCategoryId = categoryId;
            LastIsActive = isActive;
            LastIsSellable = isSellable;
            LastPage = page;
            LastPageSize = pageSize;
            LastCancellationToken = ct;
            return Task.FromResult(EmptyPage(page, pageSize));
        }

        public Task<(int TotalItems, int PosAllowedItems, int NotForPosItems, int InactiveItems)> GetSummaryAsync(
            int storeId,
            CancellationToken ct = default)
        {
            LastStoreId = storeId;
            LastCancellationToken = ct;
            return Task.FromResult(Summary);
        }

        public Task<Product?> GetDetailAsync(int storeId, int id, CancellationToken ct = default) =>
            Task.FromResult<Product?>(null);

        public Task<bool> ExistsAliasAsync(int storeId, string alias, int? excludeId, CancellationToken ct = default) =>
            Task.FromResult(false);

        public Task AddAsync(Product entity, CancellationToken ct = default) => Task.CompletedTask;
        public Task AddProductUnitConversionAsync(ProductUnitConversion entity, CancellationToken ct = default) => Task.CompletedTask;
        public Task AddProductVariantUnitBarcodeAsync(ProductVariantUnitBarcode entity, CancellationToken ct = default) => Task.CompletedTask;
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
        public Task<bool> ToggleStatusAsync(int storeId, int id, int? userId, CancellationToken ct = default) => Task.FromResult(false);
        public Task<bool> SoftDeleteAsync(int storeId, int id, int? userId, CancellationToken ct = default) => Task.FromResult(false);
        public Task<bool> ExistsVariantUnitBarcodeAsync(int storeId, string barcode, CancellationToken ct = default) => Task.FromResult(false);

        private static PagedResult<ProductListItemDto> EmptyPage(int page, int pageSize) => new()
        {
            Page = page,
            PageSize = pageSize,
            TotalItems = 0,
            Items = []
        };
    }

    private sealed class FakeProductImageService : GaoApp.Application.Interfaces.Services.Media.IProductImageService
    {
        public Task CommitTempImagesAsync(int storeId, int productId, List<string> tempTokens, string? primaryToken, int? userId, CancellationToken ct = default) => Task.CompletedTask;
        public Task SyncEditAsync(int storeId, int productId, List<ProductImageStateDto> orderedItems, string? primaryKey, int? userId, CancellationToken ct = default) => Task.CompletedTask;
        public Task SetPrimaryAsync(int storeId, int productId, string? primaryKey, CancellationToken ct = default) => Task.CompletedTask;
        public Task ClearImageTrackingAsync(int storeId, int productId) => Task.CompletedTask;
        public Task<List<ProductImageListItemDto>> GetImagesForVariantAsync(int storeId, int productId, CancellationToken ct = default) => Task.FromResult(new List<ProductImageListItemDto>());
    }

    private sealed class FakeProductVariantRepository : GaoApp.Application.Interfaces.Repositories.Products.IProductVariantRepository
    {
        public Task<List<AttributeWithValuesDto>> GetAttributesWithValuesAsync(int storeId, CancellationToken ct) => throw new NotSupportedException();
        public Task<List<ProductVariant>> GetByProductAsync(int storeId, int productId, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> ExistsSkuAsync(int storeId, string sku, int? excludeVariantId, CancellationToken ct) => throw new NotSupportedException();
        public Task AddAsync(ProductVariant entity, CancellationToken ct) => Task.CompletedTask;
        public Task RemoveRangeAsync(IEnumerable<ProductVariant> entities, CancellationToken ct) => throw new NotSupportedException();
        public Task RemoveMappingsByVariantAsync(int storeId, int variantId, CancellationToken ct) => throw new NotSupportedException();
        public Task AddMappingsAsync(IEnumerable<ProductVariantAttributeValue> mappings, CancellationToken ct) => throw new NotSupportedException();
        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
        public Task<Dictionary<int, int>> MapValueIdsToAttributeIdsAsync(int storeId, IEnumerable<int> valueIds, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveVariantsAsync(int storeId, int productId, List<ProductVariantRowDto> variants, int? userId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> ToggleStatusAsync(int storeId, int variantId, int? userId, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> SoftDeleteVariantAsync(int storeId, int variantId, int? userId, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> SetVariantImageAsync(int storeId, int variantId, int? primaryProductImageId, int? userId, CancellationToken ct) => throw new NotSupportedException();
        public Task<ProductVariant?> GetActiveWithProductAsync(int variantId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<int?> ResolveVariantIdByBarcodeHistoryAsync(string barcode, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<List<ProductVariant>> SearchForPOSAsync(string keyword, int take = 20, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<List<ProductVariant>> SearchForStockDocumentAsync(string keyword, int take = 20, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
