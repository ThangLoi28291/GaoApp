using GaoApp.Application.DTOs.Invoices;

namespace GaoApp.Web.Areas.Admin.Controllers;

public partial class InvoiceController
{
    private async Task LoadCorrectionHistoryViewBagAsync(
        int invoiceHeadId,
        CancellationToken ct)
    {
        var historyResult = await _invoiceCorrectionService.GetHistoryAsync(
            invoiceHeadId,
            ct);

        ViewBag.CorrectionHistory = historyResult.IsSuccess
            ? historyResult.Value
            : BuildDefaultCorrectionHistory(invoiceHeadId);
    }

    private static InvoiceCorrectionHistoryDto BuildDefaultCorrectionHistory(
        int invoiceHeadId)
    {
        return new InvoiceCorrectionHistoryDto
        {
            CurrentInvoiceHeadId = invoiceHeadId,
            OriginalInvoiceHeadId = invoiceHeadId,
            IsCurrentOriginal = true
        };
    }
}