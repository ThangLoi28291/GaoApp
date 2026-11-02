using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Invoices;
using GaoApp.Web.Areas.Admin.ViewModels.Invoices;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

public partial class InvoiceController
{
    [HttpGet]
    public async Task<IActionResult> AutoLinesPartial(int id, CancellationToken ct)
    {
        var result = await _invoiceService.GetInvoiceDetailAsync(id, ct);
        if (!result.IsSuccess)
            return BadRequest(InvoiceErrorReadSanitizer.UserMessage(result.Error));

        return PartialView("_InvoiceAutoLinesTable", new InvoiceDetailViewModel { Invoice = result.Value });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateDetail(UpdateManualInvoiceDetailRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return AjaxBadRequest("Dữ liệu dòng hóa đơn không hợp lệ.", "Không cập nhật được dòng hóa đơn.");

        var result = await _invoiceService.UpdateDetailAsync(request, ct);
        if (!result.IsSuccess)
            return AjaxBadRequest(InvoiceErrorReadSanitizer.UserMessage(result.Error), "Không cập nhật được dòng hóa đơn.");

        return AjaxInvoiceChanged("Đã cập nhật dòng hóa đơn.", result.Value);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteDetail(int invoiceDetailId, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return AjaxBadRequest("Dữ liệu dòng hóa đơn không hợp lệ.", "Không xóa được dòng hóa đơn.");

        var result = await _invoiceService.DeleteDetailAsync(invoiceDetailId, ct);
        if (!result.IsSuccess)
            return AjaxBadRequest(InvoiceErrorReadSanitizer.UserMessage(result.Error), "Không xóa được dòng hóa đơn.");

        return AjaxInvoiceChanged("Đã xóa dòng hóa đơn.", result.Value);
    }
}
