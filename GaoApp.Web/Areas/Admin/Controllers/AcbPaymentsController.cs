using GaoApp.Application.Common.Security;
using GaoApp.Web.Services.Acb;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Route("admin/acb/payments")]
[Authorize(Policy = PermissionCodes.Pos.Payment.View)]
public sealed class AcbPaymentsController(AcbPaymentService service) : BaseAdminController
{
    [HttpGet("orders/{orderId:int}/qrs")]
    public async Task<IActionResult> QrHistory(int orderId, CancellationToken ct) => Ok(await service.QrHistoryAsync(orderId, ct));
    [HttpGet("orders/{orderId:int}/qrs/{qrId:int}")]
    public async Task<IActionResult> ReopenQr(int orderId, int qrId, CancellationToken ct) => Ok(await service.ReopenQrAsync(orderId, qrId, ct));
    [HttpPost("{qrId:int}/status"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Status(int qrId, bool refresh, bool manual, CancellationToken ct) => Ok(await service.StatusAsync(qrId, refresh, ct, manual));
    [HttpPost("{qrId:int}/complete"), ValidateAntiForgeryToken]
    [Authorize(Policy = PermissionCodes.Pos.Order.Finalize)]
    public async Task<IActionResult> Complete(int qrId, CancellationToken ct) => Ok(await service.CompleteAsync(qrId, ct));
    [HttpPost("{qrId:int}/cancel"), ValidateAntiForgeryToken]
    [Authorize(Policy = PermissionCodes.Pos.Payment.Create)]
    public async Task<IActionResult> Cancel(int qrId, CancellationToken ct) => Ok(new { success = await service.CancelAsync(qrId, ct) });
    [HttpGet("terminal-pending")]
    public async Task<IActionResult> Pending(CancellationToken ct) => Ok(await service.PendingTerminalAsync(ct));
    [HttpGet("orders/{orderId:int}")]
    public IActionResult Lookup(int orderId, bool embedded = false) { ViewBag.OrderId = orderId; return View(embedded ? "Embedded" : "Lookup"); }
    [HttpGet("orders/{orderId:int}/data")]
    public async Task<IActionResult> Data(int orderId, CancellationToken ct) => Ok(await service.LookupAsync(orderId, false, ct));
    [HttpPost("orders/{orderId:int}/refresh"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Refresh(int orderId, CancellationToken ct) => Ok(await service.LookupAsync(orderId, true, ct));
}
