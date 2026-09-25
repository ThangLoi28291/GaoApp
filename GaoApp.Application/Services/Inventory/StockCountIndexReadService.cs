using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Application.Interfaces.Services.Inventory;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Services.Inventory;

public sealed class StockCountIndexReadService : IStockCountIndexReadService
{
    private static readonly int[] AllowedPageSizes = [10, 20, 50, 100];

    private readonly IStockCountIndexReadRepository _repository;
    private readonly ICurrentStore _currentStore;

    public StockCountIndexReadService(
        IStockCountIndexReadRepository repository,
        ICurrentStore currentStore)
    {
        _repository = repository;
        _currentStore = currentStore;
    }

    public async Task<StockCountIndexPageDto> GetPageAsync(
        StockCountIndexQueryRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var fromDate = request.FromDate?.Date;
        var toDate = request.ToDate?.Date;
        if (fromDate.HasValue && toDate.HasValue && fromDate > toDate)
            (fromDate, toDate) = (toDate, fromDate);

        var normalized = new StockCountIndexQueryRequest
        {
            Keyword = NormalizeText(request.Keyword, 200),
            WarehouseId = request.WarehouseId is > 0 ? request.WarehouseId : null,
            FromDate = fromDate,
            ToDate = toDate,
            State = NormalizeState(request.State),
            Page = Math.Max(1, request.Page),
            PageSize = AllowedPageSizes.Contains(request.PageSize)
                ? request.PageSize
                : 20
        };

        var result = await _repository.QueryAsync(
            _currentStore.StoreId,
            normalized,
            ct);

        foreach (var item in result.Items)
            item.StatusLabel = GetStatusLabel(item.Status);

        return result;
    }

    public Task<IReadOnlyList<StockCountIndexWarehouseOptionDto>> GetWarehouseOptionsAsync(
        CancellationToken ct = default)
        => _repository.GetWarehouseOptionsAsync(_currentStore.StoreId, ct);

    public async Task<StockCountIndexQuickViewDto?> GetQuickViewAsync(
        int documentId,
        CancellationToken ct = default)
    {
        if (documentId <= 0)
            return null;

        var result = await _repository.GetQuickViewAsync(
            _currentStore.StoreId,
            documentId,
            ct);

        if (result is not null)
            result.StatusLabel = GetStatusLabel(result.Status);

        return result;
    }

    private static string? NormalizeText(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private static string NormalizeState(string? value)
        => value?.Trim().ToLowerInvariant() switch
        {
            StockCountIndexStates.All => StockCountIndexStates.All,
            StockCountIndexStates.Working => StockCountIndexStates.Working,
            StockCountIndexStates.Draft => StockCountIndexStates.Draft,
            StockCountIndexStates.Pending => StockCountIndexStates.Pending,
            StockCountIndexStates.Rejected => StockCountIndexStates.Rejected,
            StockCountIndexStates.Confirmed => StockCountIndexStates.Confirmed,
            StockCountIndexStates.Cancelled => StockCountIndexStates.Cancelled,
            _ => StockCountIndexStates.Open
        };

    private static string GetStatusLabel(StockCountDocumentStatus status)
        => status switch
        {
            StockCountDocumentStatus.Draft => "Nháp",
            StockCountDocumentStatus.PendingApproval => "Chờ duyệt",
            StockCountDocumentStatus.Confirmed => "Đã xác nhận",
            StockCountDocumentStatus.Rejected => "Từ chối",
            StockCountDocumentStatus.Cancelled => "Đã hủy",
            _ => "Không rõ"
        };
}
