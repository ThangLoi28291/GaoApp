using GaoApp.Application.DTOs.Inventory;

namespace GaoApp.Application.Interfaces.Services.Inventory;

public interface IStockDocumentService
{
    Task<List<StockDocumentListItemDto>> GetReceiptListAsync(CancellationToken ct = default);

    Task<StockReceiptFormOptionsDto> GetReceiptFormOptionsAsync(CancellationToken ct = default);

    Task<int> CreateReceiptAsync(CreateStockDocumentRequest request, CancellationToken ct = default);

    Task<int> CreateReceiptFromPurchaseOrderAsync(
        int purchaseOrderId,
        GaoApp.Application.DTOs.Purchases.CreatePurchaseReceiptRequest request,
        CancellationToken ct = default);

    Task UpdatePurchaseReceiptApprovalAsync(
        int documentId,
        UpdatePurchaseReceiptApprovalRequest request,
        CancellationToken ct = default);

    Task<StockDocumentDto?> GetDetailAsync(int id, CancellationToken ct = default);

    Task<PurchaseReceiptAuditTimelineDto?> GetAuditTimelineAsync(
        int id,
        CancellationToken ct = default);

  

    Task<int> AddLineAsync(int documentId, AddStockDocumentLineRequest request, CancellationToken ct = default);
    Task<int> AddLineByBarcodeAsync(int documentId, AddStockDocumentLineByBarcodeRequest request, CancellationToken ct = default);

    Task UpdateLineAsync(int lineId, UpdateStockDocumentLineRequest request, CancellationToken ct = default);

    Task DeleteLineAsync(int lineId, CancellationToken ct = default);

    Task SubmitForApprovalAsync(int documentId, string? approvalNote, string? rowVersion, CancellationToken ct = default);

    Task ApproveAsync(int documentId, string? approvalNote, string? rowVersion, CancellationToken ct = default);

    Task ApproveAsync(
        int documentId,
        string? approvalNote,
        string? rowVersion,
        bool acceptOverdelivery,
        string? overdeliveryNote,
        CancellationToken ct = default);

    Task ApproveCommercialAsync(
        int documentId,
        ApprovePurchaseReceiptCommercialRequest request,
        CancellationToken ct = default);

    Task RejectAsync(int documentId, string? approvalNote, string? rowVersion, CancellationToken ct = default);

    Task UpdateHeaderAsync(UpdateStockDocumentHeaderRequest request, CancellationToken ct = default);
    Task RequestRevisionAsync(
        int documentId,
        string note,
        string? rowVersion,
        CancellationToken ct = default);

    Task ResolveRevisionRequestAsync(
        int documentId,
        bool returnToEdit,
        string? rowVersion,
        string? approvalNote = null,
        CancellationToken ct = default);


}
