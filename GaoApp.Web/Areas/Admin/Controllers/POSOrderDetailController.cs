using GaoApp.Application.Interfaces.Services.Orders;
using GaoApp.Application.Interfaces.Services.POSShifts;
using GaoApp.Web.Common.POS;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Route("admin/pos/order-detail")]
public class POSOrderDetailController : BasePOSPageController
{
    private readonly IPOSService _pos;

    public POSOrderDetailController(
        IPOSService pos,
        IPOSRuntimeContextAccessor runtimeContext,
        IPOSShiftService posShiftService)
        : base(runtimeContext, posShiftService)
    {
        _pos = pos;
    }

    [HttpGet("{orderId:int}")]
    public async Task<IActionResult> Index(int orderId, CancellationToken ct)
    {
        await BindPOSHeaderContextAsync(ct);

        var receipt = await _pos.GetReceiptAsync(orderId, ct);
        ViewData["Title"] = $"Chi tiết đơn #{orderId}";
        return View(receipt);
    }
}