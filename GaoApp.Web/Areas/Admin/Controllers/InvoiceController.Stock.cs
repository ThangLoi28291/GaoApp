using GaoApp.Application.Common.Security;
using GaoApp.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

public partial class InvoiceController
{
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> StockAvailability(
        int id,
        [FromServices] IAuthorizationService authorization,
        CancellationToken ct)
    {
        // Kiểm tra hóa đơn trong cửa hàng hiện tại trước khi đọc tồn.
        var result = await _invoiceService.GetInvoiceDetailAsync(id, ct);
        if (!result.IsSuccess)
            return AjaxNotFound(InvoiceErrorReadSanitizer.UserMessage(result.Error), "Không tìm thấy hóa đơn.");

        var invoice = result.Value;
        if (invoice.IsCorrectionInvoice)
            return Json(new { success = true, checkRequired = false, message = "Hóa đơn điều chỉnh/thay thế không tiêu tồn lần nữa." });

        if (!string.IsNullOrWhiteSpace(invoice.ProviderInvoiceNo)
            || invoice.ProviderStatus is InvoiceProviderStatus.Issuing
                or InvoiceProviderStatus.Issued or InvoiceProviderStatus.IssuedWaitingNumber
                or InvoiceProviderStatus.PdfDownloaded or InvoiceProviderStatus.ZipDownloaded
                or InvoiceProviderStatus.EmailSent or InvoiceProviderStatus.Cancelled)
            return Json(new { success = true, checkRequired = false, message = "Hóa đơn đã gửi phát hành hoặc đã hủy." });

        var availability = await _inputStockRepository.GetAvailabilityAsync(id, ct);
        var canViewStock = (await authorization.AuthorizeAsync(
            User, PermissionCodes.Inventory.Transaction.View)).Succeeded;
        return Json(new
        {
            success = true,
            checkRequired = true,
            canViewStock,
            availability,
            checkedAtUtc = DateTime.UtcNow
        });
    }
}
