using GaoApp.Application.DTOs.Inventory.InputInvoices;

namespace GaoApp.Application.Interfaces.Services.Inventory;

public interface IInputInvoiceReconciliationService
{
    Task<InputInvoiceReconciliationDto> CalculateAsync(int storeId, int stockDocumentId,
        CancellationToken ct = default);
    Task<InputInvoiceReconciliationDto> RefreshWithinTransactionAsync(int storeId,
        int stockDocumentId, CancellationToken ct = default);
    Task<InputInvoiceReconciliationDto> GetForReceiptAsync(int storeId, int stockDocumentId,
        CancellationToken ct = default);
    Task<InputInvoiceReconciliationDto> PreviewCommercialAsync(int storeId,
        int stockDocumentId, InputInvoiceCommercialPreviewRequest request,
        CancellationToken ct = default);
    Task<InputInvoiceReconciliationDto> IgnoreXmlDetailWithinTransactionAsync(int storeId,
        int stockDocumentId, int inputInvoiceDetailId, bool ignored, string? reason,
        CancellationToken ct = default);
    Task<InputInvoiceReconciliationDto> SetReceiptLineExcludedWithinTransactionAsync(int storeId,
        int stockDocumentId, int stockDocumentLineId, bool excluded, string? reason,
        CancellationToken ct = default);
    Task<InputInvoiceReconciliationDto> AcceptMismatchWithinTransactionAsync(int storeId,
        int stockDocumentId, string? reason, string? expectedEvidenceFingerprint,
        CancellationToken ct = default);
    Task<InputInvoiceReconciliationDto> EnsureConfirmableWithinTransactionAsync(int storeId,
        int stockDocumentId, CancellationToken ct = default);
    Task InvalidateWithinTransactionAsync(int storeId, int stockDocumentId, string reason,
        CancellationToken ct = default);
}
