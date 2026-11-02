using GaoApp.Application.DTOs.Inventory;

namespace GaoApp.Application.Interfaces.Repositories.Inventory;

public interface IInventoryLedgerIndexReadRepository
{
    Task<InventoryLedgerPeriodDto?> GetPeriodAsync(int storeId, int warehouseId, int productVariantId,
        DateTime? fromDate, DateTime? toDate, CancellationToken ct = default);
    Task<InventoryLedgerIndexPageDto> QueryAsync(
        int storeId,
        InventoryLedgerIndexQueryRequest request,
        int? costViewerUserId,
        CancellationToken ct = default);

    Task<InventoryLedgerIndexQuickViewDto?> GetQuickViewAsync(
        int storeId,
        int transactionId,
        int? costViewerUserId,
        CancellationToken ct = default);
}
