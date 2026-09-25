using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Application.Interfaces.Services.Inventory;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Services.Inventory;

public sealed class InventoryLedgerIndexReadService : IInventoryLedgerIndexReadService
{
    private static readonly int[] AllowedPageSizes = [10, 20, 50, 100, 200];

    private readonly IInventoryLedgerIndexReadRepository _repository;
    private readonly ICurrentStore _currentStore;
    private readonly ICurrentUser _currentUser;

    public InventoryLedgerIndexReadService(
        IInventoryLedgerIndexReadRepository repository,
        ICurrentStore currentStore,
        ICurrentUser currentUser)
    {
        _repository = repository;
        _currentStore = currentStore;
        _currentUser = currentUser;
    }

    public async Task<InventoryLedgerIndexPageDto> GetPageAsync(
        InventoryLedgerIndexQueryRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var fromDate = request.FromDate?.Date;
        var toDate = request.ToDate?.Date;

        if (fromDate.HasValue && toDate.HasValue && fromDate > toDate)
            (fromDate, toDate) = (toDate, fromDate);

        var normalized = new InventoryLedgerIndexQueryRequest
        {
            WarehouseId = request.WarehouseId is > 0 ? request.WarehouseId : null,
            Keyword = NormalizeText(request.Keyword, 200),
            TransactionType = request.TransactionType.HasValue
                && Enum.IsDefined(request.TransactionType.Value)
                ? request.TransactionType
                : null,
            ReferenceType = request.ReferenceType.HasValue
                && Enum.IsDefined(request.ReferenceType.Value)
                ? request.ReferenceType
                : null,
            FromDate = fromDate,
            ToDate = toDate,
            ReferenceCode = NormalizeText(request.ReferenceCode, 100),
            State = NormalizeState(request.State),
            SortBy = NormalizeSortBy(request.SortBy),
            SortDirection = string.Equals(
                request.SortDirection,
                "asc",
                StringComparison.OrdinalIgnoreCase)
                    ? "asc"
                    : "desc",
            Page = Math.Max(1, request.Page),
            PageSize = AllowedPageSizes.Contains(request.PageSize)
                ? request.PageSize
                : 20,
            IncludeSummary = request.IncludeSummary
        };

        var result = await _repository.QueryAsync(
            _currentStore.StoreId,
            normalized,
            _currentUser.IsAuthenticated ? _currentUser.UserId : null,
            ct);

        foreach (var item in result.Items)
            ApplyVietnameseLabels(item);

        return result;
    }

    public async Task<InventoryLedgerIndexQuickViewDto?> GetQuickViewAsync(
        int transactionId,
        CancellationToken ct = default)
    {
        if (transactionId <= 0)
            return null;

        var result = await _repository.GetQuickViewAsync(
            _currentStore.StoreId,
            transactionId,
            _currentUser.IsAuthenticated ? _currentUser.UserId : null,
            ct);

        if (result is not null)
            ApplyVietnameseLabels(result.Item);

        return result;
    }

    private static string? NormalizeText(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private static string? NormalizeState(string? value)
        => value?.Trim().ToLowerInvariant() switch
        {
            InventoryLedgerIndexStates.Increase => InventoryLedgerIndexStates.Increase,
            InventoryLedgerIndexStates.Decrease => InventoryLedgerIndexStates.Decrease,
            InventoryLedgerIndexStates.Negative => InventoryLedgerIndexStates.Negative,
            _ => null
        };

    private static string NormalizeSortBy(string? value)
        => value?.Trim().ToLowerInvariant() switch
        {
            "product" => "product",
            "warehouse" => "warehouse",
            "change" => "change",
            "after" => "after",
            _ => "date"
        };

    private static void ApplyVietnameseLabels(InventoryLedgerIndexItemDto item)
    {
        item.TransactionTypeLabel = item.TransactionType switch
        {
            InventoryTransactionType.OpeningBalance => "Số dư đầu kỳ",
            InventoryTransactionType.PurchaseReceipt => "Nhập mua hàng",
            InventoryTransactionType.PurchaseReturn => "Trả hàng nhà cung cấp",
            InventoryTransactionType.SaleIssue => "Xuất bán hàng",
            InventoryTransactionType.SaleReturn => "Nhập lại hàng bán",
            InventoryTransactionType.SaleVoidIn => "Hoàn kho do hủy đơn",
            InventoryTransactionType.CustomerReturnIn => "Khách trả hàng",
            InventoryTransactionType.AdjustmentIncrease => "Điều chỉnh tăng",
            InventoryTransactionType.AdjustmentDecrease => "Điều chỉnh giảm",
            InventoryTransactionType.TransferIn => "Nhập chuyển kho",
            InventoryTransactionType.TransferOut => "Xuất chuyển kho",
            InventoryTransactionType.StockCountGain => "Kiểm kê tăng",
            InventoryTransactionType.StockCountLoss => "Kiểm kê giảm",
            InventoryTransactionType.Hold => "Giữ hàng",
            InventoryTransactionType.ReleaseHold => "Nhả giữ hàng",
            InventoryTransactionType.Revaluation => "Định giá lại",
            _ => "Giao dịch kho"
        };

        item.ReferenceTypeLabel = item.ReferenceType switch
        {
            InventoryReferenceType.None => "Không có nguồn",
            InventoryReferenceType.Order => "Đơn bán hàng",
            InventoryReferenceType.PurchaseReceipt => "Phiếu nhập mua",
            InventoryReferenceType.PurchaseReturn => "Phiếu trả nhà cung cấp",
            InventoryReferenceType.Refund => "Hoàn trả",
            InventoryReferenceType.Adjustment => "Phiếu điều chỉnh",
            InventoryReferenceType.StockTransfer => "Phiếu chuyển kho",
            InventoryReferenceType.StockCount => "Phiếu kiểm kê",
            InventoryReferenceType.Hold => "Giữ hàng",
            InventoryReferenceType.StockDocument => "Chứng từ kho",
            _ => "Nguồn khác"
        };
    }
}
