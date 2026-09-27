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
      CancellationToken ct)
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

        string? selfServiceUrl = null;
        DateTime? selfServiceExpiresAtUtc = null;

        if (order.InvoiceIssuanceRoute ==
            InvoiceIssuanceRoute.Manual)
        {
            var link =
                await _buyerSelfService.CreateLinkAsync(
                    orderId,
                    ct);

            if (!link.IsSuccess)
            {
                return Conflict(
                    link.Error?.Message ??
                    "Không thể tạo mã QR nhập thông tin hóa đơn.");
            }

            selfServiceUrl =
                links.GetUriByName(
                    HttpContext,
                    "invoice-buyer-self-service",
                    new
                    {
                        token = link.Value.Token
                    });

            if (string.IsNullOrWhiteSpace(
                    selfServiceUrl))
            {
                return StatusCode(
                    StatusCodes
                        .Status500InternalServerError,
                    "Không thể tạo liên kết QR hóa đơn.");
            }

            selfServiceExpiresAtUtc =
                link.Value.ExpiresAtUtc;
        }

        ViewData["Title"] =
            $"Phiếu in #{orderId}";

        return View(
            "~/Areas/Admin/Views/ReceiptTemplates/Print.cshtml",
            new GaoApp.Web.Services.Printing.ReceiptPrintModel(
                receipt,
                await templates.ListAsync(ct),
                CurrentStoreId,
                runtime.TerminalId,
                true,
                Size: null,
                InvoiceBuyerSelfServiceUrl:
                    selfServiceUrl,
                InvoiceBuyerSelfServiceExpiresAtUtc:
                    selfServiceExpiresAtUtc));
    }
}
