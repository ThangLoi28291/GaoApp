using GaoApp.Application.Common.Security;
using GaoApp.Application.Interfaces.Services.Invoices;
using GaoApp.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

public partial class InvoiceController
{
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(
        Policy = PermissionCodes.System.Invoice.Route)]
    public async Task<IActionResult> ChangeIssuanceRoute(
        int id,
        InvoiceIssuanceRoute route,
        [FromServices]
        IInvoiceIssuanceRouteService routeService,
        CancellationToken ct)
    {
        if (route is not
            (InvoiceIssuanceRoute.Automatic or
             InvoiceIssuanceRoute.Manual))
        {
            FlashError(
                "Phương thức phát hành hóa đơn không hợp lệ.");

            return RedirectToInvoiceDetailPage(id);
        }

        var invoiceResult =
            await _invoiceService
                .GetInvoiceDetailAsync(
                    id,
                    ct);

        if (!invoiceResult.IsSuccess)
        {
            FlashError(
                invoiceResult.Error?.Message,
                "Không tìm thấy hóa đơn.");

            return RedirectToInvoiceIndexPage();
        }

        var invoice =
            invoiceResult.Value;

        if (!invoice.OrderId.HasValue)
        {
            FlashError(
                "Hóa đơn lịch sử không liên kết Order " +
                "không hỗ trợ chuyển phương thức phát hành.");

            return RedirectToInvoiceDetailPage(id);
        }

        var result =
            await routeService.ChangeRouteAsync(
                invoice.OrderId.Value,
                route,
                ct);

        if (!result.IsSuccess)
        {
            FlashError(
                result.Error?.Message,
                "Không thể chuyển phương thức phát hành.");

            return RedirectToInvoiceDetailPage(id);
        }

        FlashSuccess(
            route ==
            InvoiceIssuanceRoute.Manual
                ? "Đã chuyển hóa đơn sang luồng phát hành thủ công."
                : "Đã chuyển hóa đơn sang luồng phát hành tự động.");

        return RedirectToInvoiceDetailPage(id);
    }
}