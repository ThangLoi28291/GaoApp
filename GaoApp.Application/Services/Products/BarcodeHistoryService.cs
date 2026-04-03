using GaoApp.Application.Common;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.DTOs.Products;
using GaoApp.Application.Interfaces.Repositories.Products;
using GaoApp.Application.Interfaces.Services.Products;

namespace GaoApp.Application.Services.Products;

/// <summary>
/// Service đọc lịch sử barcode theo store hiện tại.
/// </summary>
public sealed class BarcodeHistoryService : IBarcodeHistoryService
{
    private readonly IProductVariantBarcodeHistoryRepository _repository;
    private readonly ICurrentStore _currentStore;

    public BarcodeHistoryService(
        IProductVariantBarcodeHistoryRepository repository,
        ICurrentStore currentStore)
    {
        _repository = repository;
        _currentStore = currentStore;
    }

    public async Task<PagedResult<BarcodeHistoryRowDto>> GetPagedAsync(
        BarcodeHistoryQueryRequest request,
        CancellationToken ct = default)
    {
        var storeId = _currentStore.StoreId;
        return await _repository.GetPagedAsync(storeId, request, ct);
    }
}