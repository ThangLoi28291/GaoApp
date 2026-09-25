using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Application.Interfaces.Services.Inventory;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Services.Inventory;

public sealed class StockTransferIndexReadService : IStockTransferIndexReadService
{
    private static readonly int[] AllowedPageSizes = [10, 20, 50, 100];

    private readonly IStockTransferIndexReadRepository _repository;
    private readonly ICurrentStore _currentStore;

    public StockTransferIndexReadService(
        IStockTransferIndexReadRepository repository,
        ICurrentStore currentStore)
    {
        _repository = repository;
        _currentStore = currentStore;
    }

    public async Task<StockTransferIndexPageDto> GetPageAsync(
        StockTransferIndexQueryRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var fromDate = request.FromDate?.Date;
        var toDate = request.ToDate?.Date;

        if (fromDate.HasValue && toDate.HasValue && fromDate > toDate)
            (fromDate, toDate) = (toDate, fromDate);

        var normalized = new StockTransferIndexQueryRequest
        {
            Keyword = NormalizeText(request.Keyword, 200),
            FromWarehouseId = request.FromWarehouseId is > 0
                ? request.FromWarehouseId
                : null,
            ToWarehouseId = request.ToWarehouseId is > 0
                ? request.ToWarehouseId
                : null,
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

    public async Task<StockTransferIndexQuickViewDto?> GetQuickViewAsync(
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
            StockTransferIndexStates.All => StockTransferIndexStates.All,
            StockTransferIndexStates.Working => StockTransferIndexStates.Working,
            StockTransferIndexStates.Draft => StockTransferIndexStates.Draft,
            StockTransferIndexStates.Pending => StockTransferIndexStates.Pending,
            StockTransferIndexStates.Rejected => StockTransferIndexStates.Rejected,
            StockTransferIndexStates.Confirmed => StockTransferIndexStates.Confirmed,
            _ => StockTransferIndexStates.Open
        };

    private static string GetStatusLabel(StockTransferDocumentStatus status)
        => status switch
        {
            StockTransferDocumentStatus.Draft => "Nháp",
            StockTransferDocumentStatus.PendingApproval => "Chờ duyệt",
            StockTransferDocumentStatus.Rejected => "Từ chối",
            StockTransferDocumentStatus.Confirmed => "Đã xác nhận",
            _ => "Không rõ"
        };
}
