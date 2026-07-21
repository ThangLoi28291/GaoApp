using GaoApp.Application.Interfaces.Services.Orders;
using GaoApp.Application.Interfaces.Services.POSShifts;
using GaoApp.Web.Common.POS;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Route("admin/pos/receipt")]
public class POSReceiptController : BasePOSPageController
{
    private readonly IPOSService _pos;

    public POSReceiptController(
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
        ViewData["Title"] = $"Phiếu in #{orderId}";
        return View(receipt);
    }
}