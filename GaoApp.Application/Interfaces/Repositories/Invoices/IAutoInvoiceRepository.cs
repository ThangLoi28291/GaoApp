using GaoApp.Domain.Entities;

namespace GaoApp.Application.Interfaces.Repositories.Invoices;

public interface IAutoInvoiceRepository
{
    Task<AutoInvoiceSettings?> GetSettingsAsync(int storeId, CancellationToken ct = default);
    Task AddSettingsAsync(AutoInvoiceSettings settings, CancellationToken ct = default);
    Task<List<int>> GetActiveStoreIdsAsync(CancellationToken ct = default);
    Task<AutoInvoiceWorkerState?> GetWorkerStateAsync(int storeId, string workerName, CancellationToken ct = default);
    Task AddWorkerStateAsync(AutoInvoiceWorkerState state, CancellationToken ct = default);
    Task<List<InvoiceHead>> GetCandidateInvoicesAsync(int storeId, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default);
    Task<int> ClearRecoverableCredentialErrorsAsync(int storeId, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default);
    Task MarkInvoiceErrorsAsync(int storeId, IReadOnlyCollection<int> invoiceHeadIds, string errorCode, string? errorMessage, CancellationToken ct = default);
    Task ClearInvoiceErrorsAsync(
    int storeId,
    IReadOnlyCollection<int> invoiceHeadIds,
    CancellationToken ct = default);

    Task<int> RepairMissingUnitNamesAsync(
        int storeId,
        int invoiceHeadId,
        CancellationToken ct = default);
    Task<List<AutoInvoiceOperation>> GetActiveOperationsAsync(int storeId, CancellationToken ct = default);
    Task<InvoiceHead?> GetInvoiceHeadForAutomaticIssueAsync(int storeId, int invoiceHeadId, CancellationToken ct = default);
    Task<InvoiceHead?> GetInvoiceHeadForClaimAsync(
    int storeId,
    int invoiceHeadId,
    CancellationToken ct = default);

    Task<InvoiceHead?> GetInvoiceHeadForManualClaimAsync(
        int storeId,
        int invoiceHeadId,
        CancellationToken ct = default);

    Task<List<InvoiceHead>> GetInvoiceHeadsForClaimAsync(
        int storeId,
        IReadOnlyCollection<int> invoiceHeadIds,
        CancellationToken ct = default);

    Task<bool> HasSuccessfulSourceAsync(
        int storeId,
        int invoiceHeadId,
        CancellationToken ct = default);
    Task<List<AutoInvoiceOperation>> GetOperationsAsync(int storeId, int take, CancellationToken ct = default);
    Task<AutoInvoiceOperation?> GetOperationAsync(int storeId, int operationId, CancellationToken ct = default);
    Task<bool> HasActiveSourceAsync(int storeId, int invoiceHeadId, CancellationToken ct = default);
    Task AddOperationAsync(AutoInvoiceOperation operation, CancellationToken ct = default);
    Task AddInvoiceHeadAsync(InvoiceHead invoice, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);

    /// <summary>
    /// Bỏ các entity của một lần phát hành tự động đã rollback/failed khỏi
    /// ChangeTracker để lần quét kế tiếp không thử insert lại dữ liệu mồ côi.
    /// WorkerState và Settings được giữ lại để vẫn ghi được heartbeat/lỗi.
    /// </summary>
    void DiscardFailedAutoInvoiceChanges(bool includeWorkerState = false);
}
