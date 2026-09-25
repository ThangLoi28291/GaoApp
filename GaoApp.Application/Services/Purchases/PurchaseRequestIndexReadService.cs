using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.DTOs.Purchases;
using GaoApp.Application.Interfaces.Repositories.Purchases;
using GaoApp.Application.Interfaces.Services.Purchases;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Services.Purchases;

public sealed class PurchaseRequestIndexReadService : IPurchaseRequestIndexReadService
{
    private static readonly int[] AllowedPageSizes = [10, 20, 50, 100];

    private readonly IPurchaseRequestIndexReadRepository _repository;
    private readonly ICurrentStore _currentStore;

    public PurchaseRequestIndexReadService(
        IPurchaseRequestIndexReadRepository repository,
        ICurrentStore currentStore)
    {
        _repository = repository;
        _currentStore = currentStore;
    }

    public async Task<PurchaseRequestIndexPageDto> GetPageAsync(
        PurchaseRequestIndexQueryRequest request,
        int? requestedByUserId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var fromDate = request.FromDate?.Date;
        var toDate = request.ToDate?.Date;
        if (fromDate.HasValue && toDate.HasValue && fromDate > toDate)
            (fromDate, toDate) = (toDate, fromDate);

        var normalized = new PurchaseRequestIndexQueryRequest
        {
            Scope = requestedByUserId.HasValue
                ? PurchaseRequestIndexScopes.Mine
                : PurchaseRequestIndexScopes.Store,
            Keyword = NormalizeText(request.Keyword, 200),
            RequesterUserId = requestedByUserId.HasValue
                ? null
                : request.RequesterUserId is > 0
                    ? request.RequesterUserId
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
            requestedByUserId,
            normalized,
            ct);

        foreach (var item in result.Items)
        {
            item.StatusLabel = GetStatusLabel(item.Status);
            item.StatusHint = GetStatusHint(item.Status);
        }

        return result;
    }

    public Task<List<PurchaseRequestIndexRequesterOptionDto>> GetRequesterOptionsAsync(
        CancellationToken ct = default)
        => _repository.GetRequesterOptionsAsync(_currentStore.StoreId, ct);

    public async Task<PurchaseRequestIndexQuickViewDto?> GetQuickViewAsync(
        int requestId,
        int? requestedByUserId,
        CancellationToken ct = default)
    {
        if (requestId <= 0)
            return null;

        var result = await _repository.GetQuickViewAsync(
            _currentStore.StoreId,
            requestId,
            requestedByUserId,
            ct);

        if (result is not null)
        {
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

    private static string NormalizeState(string? value)
        => value?.Trim().ToLowerInvariant() switch
        {
            PurchaseRequestIndexStates.Working => PurchaseRequestIndexStates.Working,
            PurchaseRequestIndexStates.OrderReady => PurchaseRequestIndexStates.OrderReady,
            PurchaseRequestIndexStates.Converted => PurchaseRequestIndexStates.Converted,
            PurchaseRequestIndexStates.Draft => PurchaseRequestIndexStates.Draft,
            PurchaseRequestIndexStates.Pending => PurchaseRequestIndexStates.Pending,
            PurchaseRequestIndexStates.Returned => PurchaseRequestIndexStates.Returned,
            PurchaseRequestIndexStates.Rejected => PurchaseRequestIndexStates.Rejected,
            PurchaseRequestIndexStates.Approved => PurchaseRequestIndexStates.Approved,
            PurchaseRequestIndexStates.Partial => PurchaseRequestIndexStates.Partial,
            PurchaseRequestIndexStates.Cancelled => PurchaseRequestIndexStates.Cancelled,
            _ => PurchaseRequestIndexStates.All
        };

    private static string GetStatusLabel(PurchaseRequestStatus status)
        => status switch
        {
            PurchaseRequestStatus.Draft => "Nháp",
            PurchaseRequestStatus.PendingApproval => "Chờ quản lý chốt",
            PurchaseRequestStatus.ReturnedForRevision => "Trả sửa",
            PurchaseRequestStatus.Rejected => "Từ chối",
            PurchaseRequestStatus.Approved => "Đã chốt mua",
            PurchaseRequestStatus.PartiallyConverted => "Đang tạo đơn",
            PurchaseRequestStatus.Converted => "Đã tạo đủ đơn",
            PurchaseRequestStatus.Cancelled => "Đã hủy",
            _ => "Không rõ"
        };

    private static string GetStatusHint(PurchaseRequestStatus status)
        => status switch
        {
            PurchaseRequestStatus.Draft => "Cần hoàn thiện và gửi duyệt",
            PurchaseRequestStatus.PendingApproval => "Đang chờ quản lý xử lý",
            PurchaseRequestStatus.ReturnedForRevision => "Cần chỉnh sửa theo phản hồi",
            PurchaseRequestStatus.Rejected => "Yêu cầu đã bị từ chối",
            PurchaseRequestStatus.Approved => "Sẵn sàng lập đơn đặt hàng",
            PurchaseRequestStatus.PartiallyConverted => "Còn mặt hàng cần lập đơn",
            PurchaseRequestStatus.Converted => "Đã chuyển đủ sang đơn đặt hàng",
            PurchaseRequestStatus.Cancelled => "Yêu cầu đã hủy",
            _ => string.Empty
        };
}
