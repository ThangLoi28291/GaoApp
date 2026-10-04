using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Application.Interfaces.Services.Inventory;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Services.Inventory;

public sealed class InventoryAdjustmentIndexReadService
    : IInventoryAdjustmentIndexReadService
{
    private static readonly int[] AllowedPageSizes = [10, 20, 50, 100];

    private readonly IInventoryAdjustmentIndexReadRepository _repository;
    private readonly ICurrentStore _currentStore;

    public InventoryAdjustmentIndexReadService(
        IInventoryAdjustmentIndexReadRepository repository,
        ICurrentStore currentStore)
    {
        _repository = repository;
        _currentStore = currentStore;
    }

    public async Task<InventoryAdjustmentIndexPageDto> GetPageAsync(
        InventoryAdjustmentIndexQueryRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var fromDate = request.FromDate?.Date;
        var toDate = request.ToDate?.Date;
        if (fromDate.HasValue && toDate.HasValue && fromDate > toDate)
            (fromDate, toDate) = (toDate, fromDate);

        var normalized = new InventoryAdjustmentIndexQueryRequest
        {
            Keyword = NormalizeText(request.Keyword, 200),
            WarehouseId = request.WarehouseId is > 0 ? request.WarehouseId : null,
            AdjustmentType = IsAdjustmentType(request.AdjustmentType)
                ? request.AdjustmentType
                : null,
            ReasonType = Enum.IsDefined(request.ReasonType ?? (InventoryAdjustmentReasonType)(-1))
                ? request.ReasonType
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
            ApplyLabels(item);

        return result;
    }

    public Task<IReadOnlyList<InventoryAdjustmentIndexWarehouseOptionDto>> GetWarehouseOptionsAsync(
        CancellationToken ct = default)
        => _repository.GetWarehouseOptionsAsync(_currentStore.StoreId, ct);

    public async Task<InventoryAdjustmentIndexQuickViewDto?> GetQuickViewAsync(
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
        {
            result.StatusLabel = GetStatusLabel(result.Status);
            result.AdjustmentTypeLabel = GetAdjustmentTypeLabel(result.AdjustmentType);
            result.ReasonTypeLabel = GetReasonLabel(result.ReasonType);
        }

        return result;
    }

    private static bool IsAdjustmentType(InventoryTransactionType? value)
        => value is InventoryTransactionType.AdjustmentIncrease
            or InventoryTransactionType.AdjustmentDecrease
            or InventoryTransactionType.Revaluation;

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
            InventoryAdjustmentIndexStates.All => InventoryAdjustmentIndexStates.All,
            InventoryAdjustmentIndexStates.Working => InventoryAdjustmentIndexStates.Working,
            InventoryAdjustmentIndexStates.Draft => InventoryAdjustmentIndexStates.Draft,
            InventoryAdjustmentIndexStates.Pending => InventoryAdjustmentIndexStates.Pending,
            InventoryAdjustmentIndexStates.Rejected => InventoryAdjustmentIndexStates.Rejected,
            InventoryAdjustmentIndexStates.Approved => InventoryAdjustmentIndexStates.Approved,
            InventoryAdjustmentIndexStates.Cancelled => InventoryAdjustmentIndexStates.Cancelled,
            _ => InventoryAdjustmentIndexStates.Open
        };

    private static void ApplyLabels(InventoryAdjustmentIndexItemDto item)
    {
        item.StatusLabel = GetStatusLabel(item.Status);
        item.AdjustmentTypeLabel = GetAdjustmentTypeLabel(item.AdjustmentType);
        item.ReasonTypeLabel = GetReasonLabel(item.ReasonType);
    }

    private static string GetStatusLabel(InventoryAdjustmentDocumentStatus status)
        => status switch
        {
            InventoryAdjustmentDocumentStatus.Draft => "Nháp",
            InventoryAdjustmentDocumentStatus.PendingApproval => "Chờ duyệt",
            InventoryAdjustmentDocumentStatus.Approved => "Đã duyệt",
            InventoryAdjustmentDocumentStatus.Rejected => "Từ chối",
            InventoryAdjustmentDocumentStatus.Cancelled => "Đã hủy",
            _ => "Không rõ"
        };

    private static string GetAdjustmentTypeLabel(InventoryTransactionType type)
        => type switch
        {
            InventoryTransactionType.AdjustmentIncrease => "Điều chỉnh tăng",
            InventoryTransactionType.AdjustmentDecrease => "Điều chỉnh giảm",
            InventoryTransactionType.Revaluation => "Điều chỉnh giá vốn",
            _ => "Không rõ"
        };

    private static string GetReasonLabel(InventoryAdjustmentReasonType reason)
        => reason switch
        {
            InventoryAdjustmentReasonType.Other => "Khác",
            InventoryAdjustmentReasonType.StockTakingDifference => "Chênh lệch kiểm kê",
            InventoryAdjustmentReasonType.Damaged => "Hàng hư hỏng",
            InventoryAdjustmentReasonType.Expired => "Hàng hết hạn",
            InventoryAdjustmentReasonType.Lost => "Thất thoát",
            InventoryAdjustmentReasonType.Found => "Tìm thấy hàng",
            InventoryAdjustmentReasonType.OpeningCorrection => "Điều chỉnh tồn đầu kỳ",
            InventoryAdjustmentReasonType.CostCorrection => "Điều chỉnh giá vốn",
            _ => "Không rõ"
        };
}
