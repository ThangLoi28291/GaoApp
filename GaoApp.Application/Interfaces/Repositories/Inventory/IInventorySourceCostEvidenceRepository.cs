using GaoApp.Application.DTOs.Inventory;
using GaoApp.Domain.Entities;

namespace GaoApp.Application.Interfaces.Repositories.Inventory;

public interface IInventorySourceCostEvidenceRepository
{
    Task EnsurePromotedAsync(InventoryBalance balance, CancellationToken ct);
    Task PromoteLayerAsync(InventoryCostLayer layer, CancellationToken ct);
    Task RejectUnsupportedMirrorAsync(CreateInventoryMovementRequest request, int storeId, CancellationToken ct);
    Task<InventoryDeliveryPostingProof> GetDeliveryDispatchProofAsync(InventoryDeliveryDispatchPosting request, CancellationToken ct);
    Task<InventoryDeliveryPostingProof> GetDeliveryReturnProofAsync(InventoryDeliveryReturnPosting request, CancellationToken ct);
    Task<InventoryTransferPostingProof> GetTransferProofAsync(InventorySourcePreservingTransfer request, CancellationToken ct);
    Task<IReadOnlyList<InventorySourceCostMirrorPart>> GetPostingPartsAsync(int storeId, int transactionId, CancellationToken ct);
    Task AddDebitAsync(InventoryCostBudgetDebit debit, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}
