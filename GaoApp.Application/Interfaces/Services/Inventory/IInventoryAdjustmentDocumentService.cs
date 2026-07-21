using GaoApp.Application.Common;
using GaoApp.Application.DTOs.Inventory;

namespace GaoApp.Application.Interfaces.Services.Inventory;

/// <summary>
/// Service quản lý phiếu điều chỉnh kho.
/// Phase ADJ.3 chỉ xử lý tạo/sửa/xem/gửi duyệt.
/// Chưa xử lý approve tác động tồn kho.
/// </summary>
public interface IInventoryAdjustmentDocumentService
{
    Task<InventoryAdjustmentDocumentDetailDto> CreateAsync(
        CreateInventoryAdjustmentDocumentRequest request,
        CancellationToken ct = default);

    Task<InventoryAdjustmentDocumentDetailDto> UpdateAsync(
        UpdateInventoryAdjustmentDocumentRequest request,
        CancellationToken ct = default);

    Task<InventoryAdjustmentDocumentDetailDto> SubmitAsync(
        SubmitInventoryAdjustmentDocumentRequest request,
        CancellationToken ct = default);

    Task DeleteDraftAsync(
        int id,
        CancellationToken ct = default);

    Task<InventoryAdjustmentDocumentDetailDto?> GetDetailAsync(
        int id,
        CancellationToken ct = default);

    Task<PagedResult<InventoryAdjustmentDocumentListItemDto>> GetPagedAsync(
        InventoryAdjustmentDocumentFilterDto filter,
        CancellationToken ct = default);
    Task<InventoryAdjustmentDocumentDetailDto> ApproveAsync(
    ApproveInventoryAdjustmentDocumentRequest request,
    CancellationToken ct = default);

    Task<InventoryAdjustmentDocumentDetailDto> RejectAsync(
        RejectInventoryAdjustmentDocumentRequest request,
        CancellationToken ct = default);

    Task<InventoryAdjustmentDocumentDetailDto> CancelAsync(
        CancelInventoryAdjustmentDocumentRequest request,
        CancellationToken ct = default);
}