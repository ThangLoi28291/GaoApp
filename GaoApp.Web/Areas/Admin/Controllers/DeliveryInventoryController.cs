using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Delivery;
using GaoApp.Application.Interfaces.Services.Delivery;
using GaoApp.Domain.Delivery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin"), ApiController, Route("admin/api/deliveries/{id:int}/inventory")]
[Authorize(Policy = PermissionCodes.Delivery.View)]
public sealed class DeliveryInventoryController(IDeliveryInventoryService inventory) : ControllerBase
{
    [HttpGet("")]
    public Task<IActionResult> Get(int id, CancellationToken ct) => Execute(() => inventory.GetAsync(id, ct));

    [HttpPost("cancel"), Authorize(Policy = PermissionCodes.Delivery.Cancel), ValidateAntiForgeryToken]
    public Task<IActionResult> Cancel(int id, [FromBody] DeliveryInventoryCancelRequest request, CancellationToken ct)
        => Execute(() => inventory.CancelBeforeHandoverAsync(id, request, ct));

    private async Task<IActionResult> Execute<T>(Func<Task<T>> operation)
    {
        Response.Headers.CacheControl = "no-store";
        try { return Ok(await operation()); }
        catch (DeliveryFoundationException ex) { return StatusCode(ex.StatusCode, new { code = ex.Code, message = ex.Message }); }
        catch (DeliveryRuleException ex) { return BadRequest(new { code = ex.Code, message = ex.Message }); }
    }
}
