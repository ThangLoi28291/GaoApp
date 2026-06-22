using GaoApp.Application.DTOs.Invoices;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

public partial class InvoiceController
{
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Lock(
        int invoiceHeadId,
        string? reason,
        CancellationToken ct)
    {
        var result = await _invoiceService.LockInvoiceAsync(
            new LockInvoiceRequest
            {
                InvoiceHeadId = invoiceHeadId,
                Reason = reason,
                UserId = null
            },
            ct);

        if (!result.IsSuccess)
        {
            return AjaxBadRequest(
     result.Error?.Message,
     "Không khóa được hóa đơn.");
        }

        return AjaxInvoiceLockChanged(
     "Đã khóa hóa đơn.",
     result.Value);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Unlock(
        int invoiceHeadId,
        string? reason,
        CancellationToken ct)
    {
        var result = await _invoiceService.UnlockInvoiceAsync(
            new UnlockInvoiceRequest
            {
                InvoiceHeadId = invoiceHeadId,
                Reason = reason,
                UserId = null
            },
            ct);

        if (!result.IsSuccess)
        {
            return AjaxBadRequest(
     result.Error?.Message,
     "Không mở khóa được hóa đơn.");
        }

        return AjaxInvoiceLockChanged(
       "Đã mở khóa hóa đơn.",
       result.Value);
    }
}