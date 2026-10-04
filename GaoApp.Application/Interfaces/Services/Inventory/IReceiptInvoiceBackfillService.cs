using GaoApp.Application.DTOs.Inventory;

namespace GaoApp.Application.Interfaces.Services.Inventory;

public interface IReceiptInvoiceBackfillService
{
    Task<ReceiptInvoiceBackfillPage> PreviewAsync(ReceiptInvoiceBackfillQuery query,
        bool canApproveDirect, bool canApprovePurchaseOrder, CancellationToken ct);
    Task<IReadOnlyList<ReceiptInvoiceBackfillResult>> ConfirmAsync(ReceiptInvoiceBackfillRequest request,
        bool canApproveDirect, bool canApprovePurchaseOrder, CancellationToken ct);
}
