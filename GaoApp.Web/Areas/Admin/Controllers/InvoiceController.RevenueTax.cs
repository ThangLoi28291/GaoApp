using GaoApp.Application.DTOs.Invoices;
using GaoApp.Application.Common.Security;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

public partial class InvoiceController
{
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> RevenueTaxPartial(int id, CancellationToken ct)
    {
        var result = await _invoiceService.GetInvoiceDetailAsync(id, ct);
        return result.IsSuccess ? PartialView("_InvoiceRevenueTax", result.Value) : BadRequest("Không tải được thông tin giảm thuế.");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateRevenueTax(UpdateInvoiceRevenueTaxRequest request, bool returnToPayload, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            FlashError("Dữ liệu giảm thuế không hợp lệ. Tải lại trang và chọn lại tỷ lệ.");
        else
        {
            var result = await _invoiceService.UpdateRevenueTaxAsync(request, ct);
            if (result.IsSuccess) FlashSuccess(request.Enabled ? "Đã lưu giảm 20% mức tỷ lệ thuế theo doanh thu." : "Đã tắt giảm thuế theo doanh thu.");
            else FlashError(InvoiceErrorReadSanitizer.UserMessage(result.Error));
        }
        return returnToPayload ? RedirectToViettelPayloadPage(request.InvoiceHeadId) : RedirectToInvoiceDetailPage(request.InvoiceHeadId);
    }
}
