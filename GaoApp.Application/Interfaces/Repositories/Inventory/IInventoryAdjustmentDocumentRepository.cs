using GaoApp.Application.Common;
using GaoApp.Application.DTOs.Inventory;
using GaoApp.Domain.Entities;

namespace GaoApp.Application.Interfaces.Repositories.Inventory;

/// <summary>
/// Repository cho phiếu điều chỉnh kho.
/// Chỉ làm việc với Document/Line, không tự tạo InventoryTransaction.
/// </summary>
public interface IInventoryAdjustmentDocumentRepository
{
    Task<InventoryAdjustmentDocument?> GetByIdAsync(
        int id,
        CancellationToken ct = default);

    Task<InventoryAdjustmentDocument?> GetDetailEntityAsync(
        int id,
        CancellationToken ct = default);

    Task<InventoryAdjustmentDocumentDetailDto?> GetDetailDtoAsync(
        int id,
        CancellationToken ct = default);

    Task<PagedResult<InventoryAdjustmentDocumentListItemDto>> GetPagedAsync(
        InventoryAdjustmentDocumentFilterDto filter,
        CancellationToken ct = default);

    Task<bool> ExistsDocumentNoAsync(
        string documentNo,
        int? excludeId = null,
        CancellationToken ct = default);

    Task AddAsync(
        InventoryAdjustmentDocument document,
        CancellationToken ct = default);

    void Update(InventoryAdjustmentDocument document);

    void Remove(InventoryAdjustmentDocument document);
}