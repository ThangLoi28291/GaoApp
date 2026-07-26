using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.DTOs.Products;
using GaoApp.Application.Interfaces.Repositories.Products;
using GaoApp.Application.Services.Products;
using GaoApp.Domain.Entities;

namespace GaoApp.Tests.Observability;

public sealed class ProductVariantExceptionBoundaryTests
{
    [Fact]
    public async Task Technical_invalid_operation_from_repository_should_propagate()
    {
        var expected = new InvalidOperationException(
            "Server=private-sql;Password=synthetic-secret");
        var service = new ProductVariantService(
            new ThrowingRepository(expected));

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SaveVariantsAsync(
                storeId: 1,
                productId: 2,
                variants: [],
                userId: 3,
                ct: default));

        Assert.Same(expected, actual);
    }

    [Fact]
    public async Task Expected_business_guard_should_return_stable_safe_result()
    {
        var service = new ProductVariantService(
            new ThrowingRepository(
                new BusinessRuleException("SKU đã tồn tại.")));

        var result = await service.SaveVariantsAsync(
            storeId: 1,
            productId: 2,
            variants: [],
            userId: 3,
            ct: default);

        Assert.True(result.IsFailure);
        Assert.Equal("ProductVariant.BusinessRule", result.Error.Code);
        Assert.Equal("SKU đã tồn tại.", result.Error.Message);
    }

    private sealed class ThrowingRepository(Exception saveException)
        : IProductVariantRepository
    {
        public Task<List<AttributeWithValuesDto>> GetAttributesWithValuesAsync(
            int storeId,
            CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<List<ProductVariant>> GetByProductAsync(
            int storeId,
            int productId,
            CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<bool> ExistsSkuAsync(
            int storeId,
            string sku,
            int? excludeVariantId,
            CancellationToken ct) =>
            throw new NotSupportedException();

        public Task AddAsync(
            ProductVariant entity,
            CancellationToken ct) =>
            throw new NotSupportedException();

        public Task RemoveRangeAsync(
            IEnumerable<ProductVariant> entities,
            CancellationToken ct) =>
            throw new NotSupportedException();

        public Task RemoveMappingsByVariantAsync(
            int storeId,
            int variantId,
            CancellationToken ct) =>
            throw new NotSupportedException();

        public Task AddMappingsAsync(
            IEnumerable<ProductVariantAttributeValue> mappings,
            CancellationToken ct) =>
            throw new NotSupportedException();

        public Task SaveChangesAsync(CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<Dictionary<int, int>> MapValueIdsToAttributeIdsAsync(
            int storeId,
            IEnumerable<int> valueIds,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task SaveVariantsAsync(
            int storeId,
            int productId,
            List<ProductVariantRowDto> variants,
            int? userId,
            CancellationToken ct = default) =>
            Task.FromException(saveException);

        public Task<bool> ToggleStatusAsync(
            int storeId,
            int variantId,
            int? userId,
            CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<bool> SoftDeleteVariantAsync(
            int storeId,
            int variantId,
            int? userId,
            CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<bool> SetVariantImageAsync(
            int storeId,
            int variantId,
            int? primaryProductImageId,
            int? userId,
            CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<ProductVariant?> GetActiveWithProductAsync(
            int variantId,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<int?> ResolveVariantIdByBarcodeHistoryAsync(
            string barcode,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<List<ProductVariant>> SearchForPOSAsync(
            string keyword,
            int take = 20,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<List<ProductVariant>> SearchForStockDocumentAsync(
            string keyword,
            int take = 20,
            CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
