using GaoApp.Domain.Entities;

namespace GaoApp.Application.Interfaces.Repositories.LegalEntities;

public interface ILegalEntityRepository
{
    Task<List<LegalEntity>> GetAllAsync(CancellationToken ct = default);

    Task<LegalEntity?> GetByIdAsync(int id, CancellationToken ct = default);

    Task<LegalEntity?> GetDefaultForPurchaseAsync(CancellationToken ct = default);

    Task<LegalEntity?> GetFirstActiveByPriorityAsync(CancellationToken ct = default);

    Task<bool> ExistsCodeAsync(
        string code,
        int? excludeId = null,
        CancellationToken ct = default);

    Task<bool> ExistsSalePriorityAsync(
        int salePriority,
        int? excludeId = null,
        CancellationToken ct = default);

    Task<bool> ExistsTaxCodeAsync(
        string taxCode,
        int? excludeId = null,
        CancellationToken ct = default);

    Task<bool> ExistsDefaultWarehouseAssignmentAsync(
        int warehouseId,
        int? excludeId = null,
        CancellationToken ct = default);

    Task<bool> ExistsInvoiceSettingAssignmentAsync(
        int invoiceProviderSettingId,
        int? excludeId = null,
        CancellationToken ct = default);

    Task<bool> HasActiveWarehousesAsync(
        int legalEntityId,
        CancellationToken ct = default);

    Task<Store?> GetStoreAsync(int storeId, CancellationToken ct = default);

    Task<Store?> GetStoreForUpdateAsync(int storeId, CancellationToken ct = default);

    Task ClearDefaultForPurchaseAsync(
        int? exceptLegalEntityId = null,
        CancellationToken ct = default);

    Task AddAsync(LegalEntity entity, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
