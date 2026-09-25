using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.DTOs.Purchases;
using GaoApp.Application.Interfaces.Repositories.Purchases;
using GaoApp.Application.Interfaces.Services.Purchases;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Services.Purchases;

public sealed class PurchaseOrderIndexReadService : IPurchaseOrderIndexReadService
{
    private static readonly int[] AllowedPageSizes = [10, 20, 50, 100];

    private readonly IPurchaseOrderIndexReadRepository _repository;
    private readonly ICurrentStore _currentStore;

    public PurchaseOrderIndexReadService(
        IPurchaseOrderIndexReadRepository repository,
        ICurrentStore currentStore)
    {
        _repository = repository;
        _currentStore = currentStore;
    }

    public async Task<PurchaseOrderIndexPageDto> GetPageAsync(
        PurchaseOrderIndexQueryRequest request,
        bool includeCost,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var fromDate = request.FromDate?.Date;
        var toDate = request.ToDate?.Date;
        if (fromDate.HasValue && toDate.HasValue && fromDate > toDate)
            (fromDate, toDate) = (toDate, fromDate);

        var normalized = new PurchaseOrderIndexQueryRequest
        {
            Keyword = NormalizeText(request.Keyword, 200),
            LegalEntityId = request.LegalEntityId is > 0 ? request.LegalEntityId : null,
            SupplierId = request.SupplierId is > 0 ? request.SupplierId : null,
            WarehouseId = request.WarehouseId is > 0 ? request.WarehouseId : null,
            Source = NormalizeSource(request.Source),
            State = NormalizeState(request.State),
            FromDate = fromDate,
            ToDate = toDate,
            Page = Math.Max(1, request.Page),
            PageSize = AllowedPageSizes.Contains(request.PageSize)
                ? request.PageSize
                : 20
        };

        var result = await _repository.QueryAsync(
            _currentStore.StoreId,
            normalized,
            includeCost,
            ct);

        result.CanViewCost = includeCost;
        foreach (var item in result.Items)
        {
            item.StatusLabel = GetStatusLabel(item.Status);
            item.StatusHint = GetStatusHint(item.Status);
        }

        return result;
    }

    public Task<PurchaseOrderIndexFilterOptionsDto> GetFilterOptionsAsync(
        CancellationToken ct = default)
        => _repository.GetFilterOptionsAsync(_currentStore.StoreId, ct);

    public async Task<PurchaseOrderIndexQuickViewDto?> GetQuickViewAsync(
        int orderId,
        bool includeCost,
        CancellationToken ct = default)
    {
        if (orderId <= 0)
            return null;

        var result = await _repository.GetQuickViewAsync(
            _currentStore.StoreId,
            orderId,
            includeCost,
            ct);

        if (result is not null)
        {
            result.CanViewCost = includeCost;
            result.StatusLabel = GetStatusLabel(result.Status);
            result.StatusHint = GetStatusHint(result.Status);
        }

        return result;
    }

    private static string? NormalizeText(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private static string NormalizeSource(string? value)
        => value?.Trim().ToLowerInvariant() switch
        {
            PurchaseOrderIndexSources.Request => PurchaseOrderIndexSources.Request,
            PurchaseOrderIndexSources.Direct => PurchaseOrderIndexSources.Direct,
            _ => PurchaseOrderIndexSources.All
        };

    private static string NormalizeState(string? value)
        => value?.Trim().ToLowerInvariant() switch
        {
            PurchaseOrderIndexStates.All => PurchaseOrderIndexStates.All,
            PurchaseOrderIndexStates.NeedsAction => PurchaseOrderIndexStates.NeedsAction,
            PurchaseOrderIndexStates.InProgress => PurchaseOrderIndexStates.InProgress,
            PurchaseOrderIndexStates.Completed => PurchaseOrderIndexStates.Completed,
            PurchaseOrderIndexStates.Draft => PurchaseOrderIndexStates.Draft,
            PurchaseOrderIndexStates.Pending => PurchaseOrderIndexStates.Pending,
            PurchaseOrderIndexStates.Returned => PurchaseOrderIndexStates.Returned,
            PurchaseOrderIndexStates.Rejected => PurchaseOrderIndexStates.Rejected,
            PurchaseOrderIndexStates.Approved => PurchaseOrderIndexStates.Approved,
            PurchaseOrderIndexStates.Sent => PurchaseOrderIndexStates.Sent,
            PurchaseOrderIndexStates.Receiving => PurchaseOrderIndexStates.Receiving,
            PurchaseOrderIndexStates.FullyReceived => PurchaseOrderIndexStates.FullyReceived,
            PurchaseOrderIndexStates.ShortClosed => PurchaseOrderIndexStates.ShortClosed,
            PurchaseOrderIndexStates.Cancelled => PurchaseOrderIndexStates.Cancelled,
            PurchaseOrderIndexStates.Open => PurchaseOrderIndexStates.Open,
            _ => PurchaseOrderIndexStates.Open
        };

    private static string GetStatusLabel(PurchaseOrderStatus status)
        => status switch
        {
            PurchaseOrderStatus.Draft => "Đơn nháp",
            PurchaseOrderStatus.PendingApproval => "Chờ duyệt",
            PurchaseOrderStatus.ReturnedForRevision => "Cần chỉnh sửa",
            PurchaseOrderStatus.Rejected => "Đã từ chối",
            PurchaseOrderStatus.Approved => "Đã duyệt",
            PurchaseOrderStatus.SentToSupplier => "Đã gửi NCC",
            PurchaseOrderStatus.PartiallyReceived => "Đang nhận hàng",
            PurchaseOrderStatus.FullyReceived => "Đã nhận đủ",
            PurchaseOrderStatus.ShortClosed => "Đã đóng thiếu",
            PurchaseOrderStatus.Cancelled => "Đã hủy",
            _ => "Không rõ"
        };

    private static string GetStatusHint(PurchaseOrderStatus status)
        => status switch
        {
            PurchaseOrderStatus.Draft => "Cần hoàn thiện và gửi duyệt",
            PurchaseOrderStatus.PendingApproval => "Đang chờ người có quyền duyệt",
            PurchaseOrderStatus.ReturnedForRevision => "Cần chỉnh sửa theo phản hồi",
            PurchaseOrderStatus.Rejected => "Đơn đã bị từ chối",
            PurchaseOrderStatus.Approved => "Sẵn sàng gửi nhà cung cấp",
            PurchaseOrderStatus.SentToSupplier => "Đang chờ giao hàng",
            PurchaseOrderStatus.PartiallyReceived => "Đã nhận một phần",
            PurchaseOrderStatus.FullyReceived => "Đã nhận đủ hàng",
            PurchaseOrderStatus.ShortClosed => "Đã đóng phần giao thiếu",
            PurchaseOrderStatus.Cancelled => "Đơn đã hủy",
            _ => string.Empty
        };
}
