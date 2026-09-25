using GaoApp.Application.DTOs.Inventory;

namespace GaoApp.Application.Interfaces.Repositories.Inventory;

public interface IInventoryLedgerIndexReadRepository
{
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
