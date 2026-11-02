using Microsoft.AspNetCore.Authorization;
using GaoApp.Web.Security;
using GaoApp.Application.Common.Security;
using GaoApp.Application.Interfaces.Services.Orders;
using GaoApp.Application.Interfaces.Services.POSShifts;
using GaoApp.Web.Common.POS;
using Microsoft.AspNetCore.Mvc;
using GaoApp.Application.Interfaces.Repositories.Orders;
using GaoApp.Application.Interfaces.Services.Invoices;
using GaoApp.Domain.Enums;
using Microsoft.AspNetCore.Routing;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Route("admin/pos/receipt")]
[Authorize(Policy = PermissionCodes.Pos.Order.View)]
[Authorize(Policy = PermissionCodes.Pos.Order.Reprint)]
public class POSReceiptController : BasePOSPageController
{
    private readonly IPOSService _pos;
    private readonly IOrderRepository _orders;
    private readonly IInvoiceBuyerSelfServiceService _buyerSelfService;

    public POSReceiptController(
        IPOSService pos,
        IOrderRepository orders,
        IInvoiceBuyerSelfServiceService buyerSelfService,
        IPOSRuntimeContextAccessor runtimeContext,
        IPOSShiftService posShiftService)
        : base(runtimeContext, posShiftService)
    {
        _pos = pos;
        _orders = orders;
        _buyerSelfService = buyerSelfService;
    }

    [HttpGet("{orderId:int}")]
    public async Task<IActionResult> Index(
      int orderId,
      [FromServices]
    GaoApp.Web.Services.Printing.ReceiptTemplateService templates,
      [FromServices]
    IPOSRuntimeContextAccessor runtime,
      [FromServices]
    LinkGenerator links,
      [FromServices] GaoApp.Application.Interfaces.Services.Security.IStoreAdminAccess receiptAdmin,
      CancellationToken ct,
      [FromQuery] bool data = false)
    {
        await BindPOSHeaderContextAsync(ct);

        var receipt =
            await _pos.GetReceiptAsync(
                orderId,
                ct);

        var order =
            await _orders.GetByIdAsync(
                orderId,
                ct);

        if (order == null ||
            order.StoreId != CurrentStoreId)
        {
            return NotFound();
        }
        if (receipt.Delivery is { IsSettlementSale: false })
            return Redirect(receipt.Delivery.BillUrl);

        string? selfServiceUrl = null;
        DateTime? selfServiceExpiresAtUtc = null;

        if (order.InvoiceIssuanceRoute ==
            InvoiceIssuanceRoute.Manual)
        {
            var link =
                await _buyerSelfService.CreateLinkAsync(
                    orderId,
                    ct);

            if (!link.IsSuccess && link.Error.Code != "Closed")
            {
                return Conflict(
                    link.Error?.Message ??
                    "Không thể tạo mã QR nhập thông tin hóa đơn.");
            }

            if (link.IsSuccess)
            {
                selfServiceUrl = links.GetUriByName(
                    HttpContext, "invoice-buyer-self-service", new { token = link.Value.Token });
                if (string.IsNullOrWhiteSpace(selfServiceUrl))
                    return StatusCode(StatusCodes.Status500InternalServerError,
                        "Không thể tạo liên kết QR hóa đơn.");
                selfServiceExpiresAtUtc = link.Value.ExpiresAtUtc;
            }
        }

        ViewData["Title"] =
            $"Phiếu in #{orderId}";

        var printModel = new GaoApp.Web.Services.Printing.ReceiptPrintModel(
                receipt,
                [],
                CurrentStoreId,
                runtime.TerminalId,
                true,
                Size: null,
                InvoiceBuyerSelfServiceUrl:
                    selfServiceUrl,
                InvoiceBuyerSelfServiceExpiresAtUtc:
                    selfServiceExpiresAtUtc,
                ReceiptDefault: await templates.GetDefaultAsync(ct),
                CanManageTemplates: await receiptAdmin.IsAdminAsync(ct));
        return data
            ? new JsonResult(printModel, GaoApp.Web.Services.Printing.ReceiptTemplateService.Json)
            : View("~/Areas/Admin/Views/ReceiptTemplates/Print.cshtml", printModel);
    }

    [HttpPost("{orderId:int}/buyer-link/replace")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReplaceBuyerLink(int orderId, [FromServices] LinkGenerator links, CancellationToken ct)
    {
        await BindPOSHeaderContextAsync(ct);
        var order = await _orders.GetByIdAsync(orderId, ct);
        if (order == null || order.StoreId != CurrentStoreId) return NotFound();
        var result = await _buyerSelfService.ReplaceLinkAsync(orderId, ct);
        if (!result.IsSuccess)
            return result.Error.Code == "NotFound" ? NotFound() : Conflict(result.Error.Message);
        var url = links.GetUriByName(HttpContext, "invoice-buyer-self-service", new { token = result.Value.Token });
        return Json(new { url, expiresAtUtc = result.Value.ExpiresAtUtc });
    }
}
