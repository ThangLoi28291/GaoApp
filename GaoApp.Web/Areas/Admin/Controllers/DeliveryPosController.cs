using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Delivery;
using GaoApp.Domain.Delivery;
using GaoApp.Web.Services.Delivery;
using GaoApp.Web.Common.POS;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin"), ApiController, Route("admin/api/deliveries")]
[Authorize(Policy = PermissionCodes.Delivery.View), Authorize(Policy = PermissionCodes.Delivery.Create)]
[Authorize(Policy = PermissionCodes.Pos.Order.View), Authorize(Policy = PermissionCodes.Pos.Order.Create)]
public sealed class DeliveryPosController(DeliveryPosService service, IPosRealtimeNotifier notifier,
    IPOSRuntimeContextAccessor runtime, ILogger<DeliveryPosController> logger) : ControllerBase
{
    [HttpGet("current-cart")]
    public Task<IActionResult> CurrentCart(CancellationToken ct) => Execute(() => service.SnapshotAsync(ct));
    [HttpPost("from-current-cart"), ValidateAntiForgeryToken]
    public Task<IActionResult> Create([FromBody] DeliveryPosCreateRequest request, CancellationToken ct)
        => Execute(async () =>
        {
            if (Request.Headers["X-POS-Offline"] == "1")
                throw new DeliveryFoundationException(409, "ONLINE_REQUIRED", "Tạo đơn giao cần kết nối; thao tác chưa được xếp hàng offline.");
            var result = await service.CreateAsync(request, ct);
            // This is a POS cart refresh only. Delivery monitor events remain owned by the D02 outbox/D10.
            try { await notifier.NotifyTerminalAsync(runtime.StoreId!.Value, runtime.TerminalId!.Value.ToString(),
                "current_cart_switched", orderId: result.NextCartId, relatedOrderId: request.SourceCartId,
                cartChanged: true, summaryChanged: true, paymentsChanged: true, customerChanged: true, ct: ct); }
            catch (Exception ex) { logger.LogWarning(ex, "Delivery committed; POS cart refresh notification failed."); }
            return result;
        });
    private async Task<IActionResult> Execute<T>(Func<Task<T>> work)
    {
        try { return Ok(await work()); }
        catch (DeliveryFoundationException ex) { return StatusCode(ex.StatusCode, new { code = ex.Code, message = ex.Message }); }
        catch (DeliveryRuleException ex) { return BadRequest(new { code = ex.Code, message = ex.Message }); }
    }
}
