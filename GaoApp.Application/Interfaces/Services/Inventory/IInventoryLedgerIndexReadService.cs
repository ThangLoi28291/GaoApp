using GaoApp.Application.DTOs.Inventory;

namespace GaoApp.Application.Interfaces.Services.Inventory;

public interface IInventoryLedgerIndexReadService
{
    Task<InventoryLedgerIndexPageDto> GetPageAsync(
        InventoryLedgerIndexQueryRequest request,
        CancellationToken ct = default);

    Task<InventoryLedgerIndexQuickViewDto?> GetQuickViewAsync(
        int transactionId,
        CancellationToken ct = default);
}
