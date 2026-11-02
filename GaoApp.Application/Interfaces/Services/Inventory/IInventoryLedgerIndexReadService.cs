using GaoApp.Application.DTOs.Inventory;

namespace GaoApp.Application.Interfaces.Services.Inventory;

public interface IInventoryLedgerIndexReadService
{
    Task<InventoryLedgerPeriodDto?> GetPeriodAsync(int warehouseId, int productVariantId,
        DateTime? fromDate, DateTime? toDate, CancellationToken ct = default);
    Task<InventoryLedgerIndexPageDto> GetPageAsync(
        InventoryLedgerIndexQueryRequest request,
        CancellationToken ct = default);

    Task<InventoryLedgerIndexQuickViewDto?> GetQuickViewAsync(
        int transactionId,
        CancellationToken ct = default);
}
